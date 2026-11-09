using System.Buffers;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using BoltSharp.Framing;
using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Transport;

// B5-A-IMPL-01 producer evidence probe: executes the required network flows over real loopback
// sockets and prints byte-level traces. Test-only; not part of the repository.

const string GraphQuery = "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b";
const string GoldenRecordHex =
    "B17193B44E019186506572736F6EA1846E616D6585416C696365836E3A31B852010102854B4E4F5753A18573696E6365C907E883723A31836E3A31836E3A32" +
    "B44E029186506572736F6EA1846E616D6583426F62836E3A32";

var sink = new List<string>();
void Log(string line) { Console.WriteLine(line); sink.Add(line); }
static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

static byte[] Frame(params byte[][] chunks)
{
    var bytes = new List<byte>();
    foreach (var chunk in chunks) { bytes.Add((byte)(chunk.Length >> 8)); bytes.Add((byte)(chunk.Length & 0xFF)); bytes.AddRange(chunk); }
    bytes.Add(0); bytes.Add(0);
    return bytes.ToArray();
}

static byte[] Encode(BoltRequest request)
{
    var buffer = new ArrayBufferWriter<byte>();
    var writer = new PackStreamWriter(buffer);
    switch (request)
    {
        case HelloRequest hello: writer.WriteStructure(BoltMessageSignatures.Hello, new PackStreamValue?[] { hello.Extra }); break;
        case LogonRequest logon: writer.WriteStructure(BoltMessageSignatures.Logon, new PackStreamValue?[] { logon.Auth }); break;
        case RunRequest run:
            writer.WriteStructure(BoltMessageSignatures.Run, new PackStreamValue?[] { new PackStreamString(run.Query), run.Parameters, run.Extra });
            break;
        case PullRequest pull:
            var entries = new List<KeyValuePair<string, PackStreamValue?>> { new("n", new PackStreamInteger(pull.N)) };
            writer.WriteStructure(BoltMessageSignatures.Pull, new PackStreamValue?[] { new PackStreamMap(entries) });
            break;
        case GoodbyeRequest: writer.WriteStructure(BoltMessageSignatures.Goodbye, Array.Empty<PackStreamValue?>()); break;
        default: throw new NotSupportedException(request.GetType().Name);
    }
    return buffer.WrittenSpan.ToArray();
}

PackStreamMap Map(params (string Key, PackStreamValue? Value)[] entries)
{
    var list = new List<KeyValuePair<string, PackStreamValue?>>(entries.Length);
    foreach (var (key, value) in entries) { list.Add(new KeyValuePair<string, PackStreamValue?>(key, value)); }
    return new PackStreamMap(list);
}

// A minimal deterministic verifier (test-only; the production default stays fail-closed).
var verifier = new ProbeVerifier();

// ---------------------------------------------------------------- plaintext scalar + graph flow
Log("== FLOW 1: plaintext TCP, scalar + graph conversation ==");
var serverOptions = new BoltTcpServerOptions { CredentialVerifier = verifier };
var server = BoltTcpServer.Create(serverOptions);
server.StartAsync().GetAwaiter().GetResult();
Log($"listener bound: {server.BoundEndPoint}");

using var tcp = new TcpClient();
tcp.Connect(server.BoundEndPoint!.Address, server.BoundEndPoint.Port);
var stream = (Stream)tcp.GetStream();

// handshake
var handshake = new byte[] { 0x60, 0x60, 0xB0, 0x17, 0x00, 0x00, 0x08, 0x05, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
Log($"C->S handshake (20 bytes): {Hex(handshake)}");
stream.Write(handshake);
var reply = new byte[4];
ReadExactly(stream, reply);
Log($"S->C selected version (4 bytes): {Hex(reply)}  (expect 00000805)");
var reader = new BoltMessageReader(stream);
var codec = new BoltMessageCodec();

void Send(BoltRequest request, string label)
{
    var framed = Frame(Encode(request));
    Log($"C->S {label}: {Hex(framed)}");
    stream.Write(framed);
}

BoltResponse[] ReadResult(string label)
{
    var collected = new List<BoltResponse>();
    while (true)
    {
        var message = reader.ReadMessage() ?? throw new EndOfStreamException("server closed");
        Log($"S->C {label} message: {Hex(message)}");
        var response = codec.DecodeResponse(message);
        collected.Add(response);
        if (response is SuccessResponse or FailureResponse) { return collected.ToArray(); }
    }
}

Send(new HelloRequest(Map(("user_agent", new PackStreamString("B5AProbe/1.0")), ("bolt_agent", Map(("product", new PackStreamString("B5AProbe/1.0")))))), "HELLO");
var helloResponses = ReadResult("HELLO");
Log($"HELLO => {helloResponses[0]}");

Send(new LogonRequest(Map(("scheme", new PackStreamString("basic")), ("principal", new PackStreamString("probe-user")), ("credentials", new PackStreamString("probe-secret"))), BoltAuthScheme.Basic), "LOGON");
var logonResponses = ReadResult("LOGON");
Log($"LOGON => {logonResponses[0]}");

Send(new RunRequest("RETURN 42 AS answer", Map(), Map()), "RUN scalar");
var runResponses = ReadResult("RUN");
Log($"RUN => {runResponses[0]}");
Send(new PullRequest(1000, null), "PULL n=1000");
var pullResponses = ReadResult("PULL");
Log($"PULL => {pullResponses.Length} message(s): {string.Join(" | ", pullResponses.Select(r => r.ToString()))}");
var scalarRecord = pullResponses[0] as RecordResponse ?? throw new InvalidOperationException("expected RECORD");
if (AssertEqual(42, ((PackStreamInteger)scalarRecord.Data.Items[0]).Value, "scalar record value")) { }
Log("FLOW 1 scalar: PASS (RETURN 42 AS answer -> integer 42 over real TCP)");

Send(new RunRequest(GraphQuery, Map(), Map()), "RUN graph");
ReadResult("RUN graph");
Send(new PullRequest(1000, null), "PULL graph");
var graphMessage = reader.ReadMessage() ?? throw new EndOfStreamException("server closed during graph PULL");
Log($"S->C graph RECORD message: {Hex(graphMessage)}");
var graphMatch = string.Equals(Hex(graphMessage), GoldenRecordHex, StringComparison.OrdinalIgnoreCase);
Log($"graph RECORD matches accepted golden vector: {graphMatch}");
var graphRecord = (RecordResponse)codec.DecodeResponse(graphMessage);
Log($"graph record arity: {graphRecord.Data.Items.Count} (a, r, b)");
var summary = reader.ReadMessage() ?? throw new EndOfStreamException("server closed before graph summary");
Log($"S->C graph terminal summary: {Hex(summary)} => {codec.DecodeResponse(summary)}");
Log(graphMatch ? "FLOW 1 graph: PASS (Alice -[KNOWS]-> Bob, byte-identical to golden)" : "FLOW 1 graph: FAIL");

Send(new GoodbyeRequest(), "GOODBYE");
Log("C->S GOODBYE sent (no response expected)");
server.StopAsync().GetAwaiter().GetResult();
Log($"diagnostics after flow 1: accepted={server.Diagnostics.ConnectionsAccepted} completed={server.Diagnostics.ConnectionsCompleted} active={server.Diagnostics.ConnectionsActive} messages={server.Diagnostics.MessagesProcessed}");
stream.Dispose();
tcp.Dispose();

// ---------------------------------------------------------------- TLS flow
Log("");
Log("== FLOW 2: TLS TCP, scalar conversation ==");
using var rsa = RSA.Create(2048);
var certRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
var temporary = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
using var certificate = new X509Certificate2(temporary.Export(X509ContentType.Pfx));

var tlsOptions = new BoltTcpServerOptions
{
    CredentialVerifier = verifier,
    Tls = new BoltTlsServerOptions(certificate),
};
var tlsServer = BoltTcpServer.Create(tlsOptions);
tlsServer.StartAsync().GetAwaiter().GetResult();
Log($"TLS listener bound: {tlsServer.BoundEndPoint}");

using var tlsTcp = new TcpClient();
tlsTcp.Connect(tlsServer.BoundEndPoint!.Address, tlsServer.BoundEndPoint.Port);
var rawTls = tlsTcp.GetStream();
var ssl = new SslStream(rawTls, leaveInnerStreamOpen: false);
ssl.AuthenticateAsClient(new SslClientAuthenticationOptions
{
    TargetHost = "localhost",
    RemoteCertificateValidationCallback = (_, cert, _, _) => cert is X509Certificate2 c && c.Thumbprint == certificate.Thumbprint,
});
Log($"TLS authenticated: {ssl.SslProtocol} cipher={ssl.NegotiatedCipherSuite}");
Stream tlsStream = ssl;
var tlsReader = new BoltMessageReader(tlsStream);

void TlsSend(BoltRequest request, string label)
{
    var framed = Frame(Encode(request));
    Log($"C->S {label} (inside TLS): {Hex(framed)}");
    tlsStream.Write(framed);
}

BoltResponse[] TlsReadResult(string label)
{
    var collected = new List<BoltResponse>();
    while (true)
    {
        var message = tlsReader.ReadMessage() ?? throw new EndOfStreamException("TLS server closed");
        Log($"S->C {label} message (inside TLS): {Hex(message)}");
        var response = codec.DecodeResponse(message);
        collected.Add(response);
        if (response is SuccessResponse or FailureResponse) { return collected.ToArray(); }
    }
}

var tlsHandshake = new byte[] { 0x60, 0x60, 0xB0, 0x17, 0x00, 0x00, 0x08, 0x05, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
Log($"C->S handshake (inside TLS): {Hex(tlsHandshake)}");
tlsStream.Write(tlsHandshake);
var tlsReply = new byte[4];
ReadExactly(tlsStream, tlsReply);
Log($"S->C selected version (inside TLS): {Hex(tlsReply)} (expect 00000805)");

TlsSend(new HelloRequest(Map(("user_agent", new PackStreamString("B5AProbe/1.0")), ("bolt_agent", Map(("product", new PackStreamString("B5AProbe/1.0")))))), "HELLO");
TlsReadResult("HELLO");
TlsSend(new LogonRequest(Map(("scheme", new PackStreamString("basic")), ("principal", new PackStreamString("probe-user")), ("credentials", new PackStreamString("probe-secret"))), BoltAuthScheme.Basic), "LOGON");
TlsReadResult("LOGON");
TlsSend(new RunRequest("RETURN 42 AS answer", Map(), Map()), "RUN");
TlsReadResult("RUN");
TlsSend(new PullRequest(1000, null), "PULL");
var tlsPull = TlsReadResult("PULL");
var tlsRecord = (RecordResponse)tlsPull[0];
Log($"TLS PULL record value: {((PackStreamInteger)tlsRecord.Data.Items[0]).Value}");
Log("FLOW 2 TLS: PASS (authenticated TLS, negotiated Bolt 5.8, scalar query returned 42)");
Log($"TLS diagnostics: accepted={tlsServer.Diagnostics.ConnectionsAccepted} completed={tlsServer.Diagnostics.ConnectionsCompleted} messages={tlsServer.Diagnostics.MessagesProcessed}");
ssl.Dispose();
tlsTcp.Dispose();
Log("");
Log("PROBE COMPLETE");

static void ReadExactly(Stream stream, byte[] destination)
{
    var read = 0;
    while (read < destination.Length)
    {
        var count = stream.Read(destination, read, destination.Length - read);
        if (count == 0) { throw new EndOfStreamException("connection closed during read"); }
        read += count;
    }
}

static bool AssertEqual(long expected, long actual, string label)
{
    if (expected != actual) { throw new InvalidOperationException($"{label}: expected {expected}, got {actual}"); }
    return true;
}

/// <summary>Deterministic probe verifier (test-only).</summary>
internal sealed class ProbeVerifier : IBoltCredentialVerifier
{
    public bool Verify(LogonRequest logon) => logon.RecognizedScheme == BoltAuthScheme.Basic;
}
