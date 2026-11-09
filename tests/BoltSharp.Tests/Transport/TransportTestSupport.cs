using System.Buffers;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

using BoltSharp.Framing;
using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Tests.Sessions;
using BoltSharp.Transport;

namespace BoltSharp.Tests.Transport;

/// <summary>
/// Test-only helpers for the B5-A transport tests: handshake/framing byte builders, an in-memory
/// self-signed test certificate and a server-start helper. Everything here uses real loopback
/// sockets and ephemeral ports; no production code path is bypassed.
/// </summary>
internal static class TransportTestSupport
{
    /// <summary>The Bolt handshake identification bytes: 60 60 B0 17.</summary>
    public static byte[] Magic { get; } = { 0x60, 0x60, 0xB0, 0x17 };

    /// <summary>
    /// Builds a legacy handshake proposal from concrete versions. The legacy handshake always
    /// carries exactly four proposal slots; unused slots are zero-filled (protocol zero, never
    /// negotiable).
    /// </summary>
    public static byte[] HandshakeFromVersions(params BoltProtocolVersion[] versions)
    {
        var words = new uint[4];
        for (var i = 0; i < versions.Length && i < 4; i++)
        {
            words[i] = versions[i].ToUInt32();
        }

        return HandshakeWords(words);
    }

    /// <summary>Builds a legacy handshake proposal from raw big-endian version words.</summary>
    public static byte[] HandshakeWords(params uint[] proposalWords)
    {
        var bytes = new List<byte>(Magic);
        foreach (var word in proposalWords)
        {
            bytes.Add((byte)(word >> 24));
            bytes.Add((byte)(word >> 16));
            bytes.Add((byte)(word >> 8));
            bytes.Add((byte)word);
        }

        return bytes.ToArray();
    }

    /// <summary>
    /// Wraps chunk payloads in Bolt chunked framing: a 2-byte big-endian length per chunk and a
    /// zero-length terminator.
    /// </summary>
    public static byte[] Frame(params byte[][] chunks)
    {
        var bytes = new List<byte>();
        foreach (var chunk in chunks)
        {
            bytes.Add((byte)(chunk.Length >> 8));
            bytes.Add((byte)(chunk.Length & 0xFF));
            bytes.AddRange(chunk);
        }

        bytes.Add(0);
        bytes.Add(0);
        return bytes.ToArray();
    }

    /// <summary>Splits one message payload into wire chunks of at most <paramref name="chunkSize"/> bytes.</summary>
    public static byte[][] SplitForWire(byte[] message, int chunkSize)
    {
        var chunks = new List<byte[]>();
        for (var offset = 0; offset < message.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, message.Length - offset);
            var chunk = new byte[length];
            Array.Copy(message, offset, chunk, 0, length);
            chunks.Add(chunk);
        }

        return chunks.ToArray();
    }

    /// <summary>
    /// Generates a short-lived in-memory self-signed RSA certificate for TLS tests. Test-only:
    /// no key material is ever written to disk or committed.
    /// </summary>
    public static X509Certificate2 CreateSelfSignedCertificate(string subjectName = "CN=localhost")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
        // Re-import the PFX so the certificate owns a persisted private key usable by SslStream.
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
    }

    /// <summary>
    /// Starts a loopback BoltTcpServer on an ephemeral port. The factory builds the complete
    /// options object (init-only properties); without one, a default configuration with the
    /// deterministic test verifier injected is used (production defaults stay fail-closed).
    /// </summary>
    public static async Task<BoltTcpServer> StartServerAsync(Func<BoltTcpServerOptions>? factory = null)
    {
        var options = factory is null
            ? new BoltTcpServerOptions { CredentialVerifier = TestCredentialVerifier.AcceptAll() }
            : factory();
        var server = BoltTcpServer.Create(options);
        await server.StartAsync().ConfigureAwait(false);
        return server;
    }

    /// <summary>
    /// Polls a condition until it holds or the timeout expires. A bounded synchronization helper —
    /// never used to simulate fragmentation (tests use explicit write boundaries instead).
    /// </summary>
    public static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    /// <summary>
    /// Connects, handshakes (Bolt 5.8), completes HELLO and LOGON with the deterministic test
    /// credentials, optionally RUNs a query, and returns the ready client. Throws with a clear
    /// marker if any step does not return SUCCESS (this helper is not an assertion framework).
    /// </summary>
    public static TestBoltClient ConnectAndAuthorize(BoltTcpServer server, string? runQuery = null)
    {
        var client = TestBoltClient.Connect(server.BoundEndPoint!);
        try
        {
            client.Handshake(new BoltProtocolVersion(5, 8));
            client.SendRequest(SessionHarness.Hello());
            ExpectSuccess(client.ReadResponse(), "HELLO");
            client.SendRequest(SessionHarness.Logon());
            ExpectSuccess(client.ReadResponse(), "LOGON");
            if (runQuery is not null)
            {
                client.SendRequest(SessionHarness.Run(runQuery));
                ExpectSuccess(client.ReadResponse(), "RUN");
            }

            return client;
        }
        catch (Exception)
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Manual check helper (test support must not depend on xUnit).</summary>
    public static void ExpectSuccess(BoltResponse response, string step)
    {
        if (response is not SuccessResponse)
        {
            throw new InvalidOperationException($"expected SUCCESS for {step}, got: {response}");
        }
    }

    /// <summary>Test-side typed accessor (BoltMessageFields is internal to the production assembly).</summary>
    public static string? GetStringEntry(PackStreamMap map, string key)
    {
        foreach (var entry in map.Entries)
        {
            if (string.Equals(entry.Key, key, StringComparison.Ordinal) && entry.Value is PackStreamString text)
            {
                return text.Value;
            }
        }

        return null;
    }
}

/// <summary>
/// A minimal real-socket Bolt client used to exercise the transport from outside the server
/// boundary: real TCP connect, real TLS authentication (when configured), real B2 handshake and
/// chunked framing, real B3-A response decoding. It never short-circuits the wire protocol.
/// </summary>
internal sealed class TestBoltClient : IDisposable
{
    private readonly TcpClient _tcp = new();
    private readonly BoltMessageCodec _codec = new();
    private NetworkStream _raw = null!;
    private Stream _protocol = null!;
    private BoltMessageReader? _reader;

    public BoltProtocolVersion? NegotiatedVersion { get; private set; }

    public static TestBoltClient Connect(IPEndPoint endpoint)
    {
        var client = new TestBoltClient();
        client._tcp.Connect(endpoint.Address, endpoint.Port);
        client._raw = client._tcp.GetStream();
        client._protocol = client._raw;
        return client;
    }

    /// <summary>
    /// Connects and authenticates TLS against the expected test certificate (explicit trust;
    /// no global validation bypass).
    /// </summary>
    public static TestBoltClient ConnectTls(
        IPEndPoint endpoint,
        X509Certificate2 expectedServerCertificate,
        string targetHost = "localhost")
    {
        var client = Connect(endpoint);
        var sslStream = new SslStream(client._raw, leaveInnerStreamOpen: false);
        sslStream.AuthenticateAsClient(new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            RemoteCertificateValidationCallback = (_, cert, _, _) =>
                cert as X509Certificate2 is { } actual && actual.Thumbprint == expectedServerCertificate.Thumbprint,
        });
        client._protocol = sslStream;
        return client;
    }

    /// <summary>
    /// Performs the legacy handshake (one write, four proposal slots zero-padded) and verifies
    /// the 4-byte selected-version reply.
    /// </summary>
    public void Handshake(params BoltProtocolVersion[] versions)
    {
        var words = new uint[4];
        for (var i = 0; i < versions.Length && i < 4; i++)
        {
            words[i] = versions[i].ToUInt32();
        }

        _protocol.Write(TransportTestSupport.HandshakeWords(words));
        var response = new byte[4];
        ReadExactly(response);
        var word = (uint)((response[0] << 24) | (response[1] << 16) | (response[2] << 8) | response[3]);
        NegotiatedVersion = BoltProtocolVersion.FromUInt32(word);
        AttachFraming();
    }

    /// <summary>Writes raw bytes (handshake fragments, invalid magic, arbitrary junk).</summary>
    public void SendRaw(byte[] bytes) => _protocol.Write(bytes);

    /// <summary>Sends one request encoded with the test-only request encoder, in one chunk.</summary>
    public void SendRequest(BoltRequest request)
    {
        var payload = TransportRequestEncoder.Encode(request);
        _protocol.Write(TransportTestSupport.Frame(payload));
    }

    /// <summary>Sends one request split into chunks of the given size (fragmentation probes).</summary>
    public void SendRequestChunked(BoltRequest request, int chunkSize)
    {
        var payload = TransportRequestEncoder.Encode(request);
        _protocol.Write(TransportTestSupport.Frame(TransportTestSupport.SplitForWire(payload, chunkSize)));
    }

    /// <summary>Sends pre-framed bytes (hand-built chunk sequences for edge-case probes).</summary>
    public void SendFramed(params byte[][] chunks) => _protocol.Write(TransportTestSupport.Frame(chunks));

    /// <summary>Reads exactly <paramref name="count"/> raw bytes from the underlying stream.</summary>
    public byte[] RawRead(int count)
    {
        var buffer = new byte[count];
        ReadExactly(buffer);
        return buffer;
    }

    /// <summary>Reads one framed message; <see langword="null"/> means clean EOF (server closed).</summary>
    public byte[]? ReadFramedMessage() => ReadMessage();

    /// <summary>Reads one framed message and decodes it through the accepted B3-A response decoder.</summary>
    public byte[]? ReadMessage()
    {
        ObjectDisposedException.ThrowIf(_reader is null, nameof(_reader));
        return _reader.ReadMessage();
    }

    /// <summary>Reads one framed message and decodes it through the accepted B3-A response decoder.</summary>
    public BoltResponse ReadResponse()
    {
        var message = ReadMessage();
        if (message is null)
        {
            throw new EndOfStreamException("connection closed before a response arrived");
        }

        return Decode(message);
    }

    /// <summary>Decodes a raw message payload through the accepted B3-A response decoder.</summary>
    public BoltResponse Decode(byte[] message) => _codec.DecodeResponse(message);

    /// <summary>
    /// Reads one PULL/DISCARD result pass: every RECORD plus the first SUCCESS summary. When the
    /// summary carries has_more=true the caller decides whether to PULL again (the client never
    /// invents requests). A FAILURE ends the pass as well.
    /// </summary>
    public BoltResponse[] ReadResult()
    {
        var responses = new List<BoltResponse>();
        while (true)
        {
            var response = ReadResponse();
            responses.Add(response);
            if (response is SuccessResponse or FailureResponse)
            {
                return responses.ToArray();
            }
        }
    }

    /// <summary>
    /// Waits up to <paramref name="timeoutMs"/> for the server to close the connection.
    /// Returns "closed" (clean EOF, reset, or local disposal) or "open" when the timeout expires.
    /// </summary>
    public string WaitForClose(int timeoutMs)
    {
        try
        {
            if (!_tcp.Client.Poll(timeoutMs * 1000, SelectMode.SelectRead))
            {
                return "open";
            }

            var buffer = new byte[1];
            return _raw.Read(buffer, 0, 1) == 0 ? "closed" : "open";
        }
        catch (Exception)
        {
            // A disposed/reset socket or a read fault during close is still closure.
            return "closed";
        }
    }

    public void Dispose()
    {
        _reader = null;
        _protocol.Dispose();
        _tcp.Dispose();
        GC.SuppressFinalize(this);
    }

    public void AttachFraming(BoltFramingLimits? limits = null) =>
        _reader = new BoltMessageReader(_protocol, limits ?? BoltFramingLimits.Default);

    private void ReadExactly(byte[] destination)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var count = _protocol.Read(destination, read, destination.Length - read);
            if (count == 0)
            {
                throw new EndOfStreamException("connection closed during read");
            }

            read += count;
        }
    }
}

/// <summary>
/// Test-only wire encoder for client requests. B3-A deliberately exposes only
/// <c>EncodeResponse</c> publicly (the accepted B3-A scope), so the transport tests re-implement
/// the request encode for the supported Bolt 5.8 message shapes, matching the accepted B3-A
/// schemas exactly (including the authentic golden bytes from the B3-A/B0-B evidence).
/// This is a documented B5-A limitation, not a production path.
/// </summary>
internal static class TransportRequestEncoder
{
    public static byte[] Encode(BoltRequest request)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        switch (request)
        {
            case HelloRequest hello:
                writer.WriteStructure(BoltMessageSignatures.Hello, new PackStreamValue?[] { hello.Extra });
                break;
            case LogonRequest logon:
                writer.WriteStructure(BoltMessageSignatures.Logon, new PackStreamValue?[] { logon.Auth });
                break;
            case RunRequest run:
                writer.WriteStructure(
                    BoltMessageSignatures.Run,
                    new PackStreamValue?[] { new PackStreamString(run.Query), run.Parameters, run.Extra });
                break;
            case PullRequest pull:
                writer.WriteStructure(BoltMessageSignatures.Pull, new PackStreamValue?[] { NAndQid(pull.N, pull.Qid) });
                break;
            case DiscardRequest discard:
                writer.WriteStructure(BoltMessageSignatures.Discard, new PackStreamValue?[] { NAndQid(discard.N, discard.Qid) });
                break;
            case ResetRequest:
                writer.WriteStructure(BoltMessageSignatures.Reset, Array.Empty<PackStreamValue?>());
                break;
            case GoodbyeRequest:
                writer.WriteStructure(BoltMessageSignatures.Goodbye, Array.Empty<PackStreamValue?>());
                break;
            case LogoffRequest:
                writer.WriteStructure(BoltMessageSignatures.Logoff, Array.Empty<PackStreamValue?>());
                break;
            case TelemetryRequest telemetry:
                writer.WriteStructure(
                    BoltMessageSignatures.Telemetry,
                    new PackStreamValue?[] { new PackStreamInteger(telemetry.Api) });
                break;
            default:
                throw new NotSupportedException("test encoder does not support " + request.GetType().Name);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Builds the PULL/DISCARD <c>extra</c> map: required <c>n</c> plus optional <c>qid</c>.
    /// For PULL n=1000 this produces the authentic golden bytes B1 3F A1 81 6E C9 03 E8.
    /// </summary>
    private static PackStreamMap NAndQid(long n, long? qid)
    {
        var entries = new List<KeyValuePair<string, PackStreamValue?>> { new("n", new PackStreamInteger(n)) };
        if (qid is { } qidValue)
        {
            entries.Add(new KeyValuePair<string, PackStreamValue?>("qid", new PackStreamInteger(qidValue)));
        }

        return new PackStreamMap(entries);
    }
}
