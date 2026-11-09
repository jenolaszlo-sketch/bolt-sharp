using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using BoltSharp.Transport;

namespace B5AQ1Probe;

/// <summary>
/// Reviewer-owned Bolt client over real loopback sockets: real handshake bytes, real chunked
/// framing (own reader), own byte-level oracles. Shares no code with the producer's test client.
/// </summary>
internal sealed class ReviewClient : IDisposable
{
    private readonly TcpClient _tcp = new();
    private Stream _stream = null!;
    private bool _tlsAuthenticated;

    public string LastReplyHex { get; private set; } = string.Empty;
    public bool IsTlsAuthenticated => _tlsAuthenticated;
    public string NegotiatedTls { get; private set; } = string.Empty;
    public string NegotiatedCipher { get; private set; } = string.Empty;

    public static ReviewClient Connect(IPEndPoint endpoint, int receiveBufferSize = -1)
    {
        var client = new ReviewClient();
        if (receiveBufferSize > 0)
        {
            client._tcp.ReceiveBufferSize = receiveBufferSize;
        }

        client._tcp.Connect(endpoint.Address, endpoint.Port);
        client._stream = client._tcp.GetStream();
        return client;
    }

    public static ReviewClient ConnectTls(
        IPEndPoint endpoint,
        X509Certificate2? trustedCertificate,
        string targetHost = "localhost",
        bool strictChainValidation = false,
        SslProtocols enabledProtocols = SslProtocols.None)
    {
        var client = Connect(endpoint);
        var ssl = new SslStream(client._tcp.GetStream(), leaveInnerStreamOpen: false);
        ssl.AuthenticateAsClient(new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = enabledProtocols,
            RemoteCertificateValidationCallback = strictChainValidation
                ? null // platform default: full chain + name validation (rejects self-signed)
                : (_, cert, _, errors) =>
                {
                    // Reviewer trust model: accept only the explicitly supplied test thumbprint.
                    return trustedCertificate is not null
                        && cert is X509Certificate2 actual
                        && actual.Thumbprint == trustedCertificate.Thumbprint;
                },
        });
        client._stream = ssl;
        client._tlsAuthenticated = true;
        client.NegotiatedTls = ssl.SslProtocol.ToString();
        client.NegotiatedCipher = ssl.NegotiatedCipherSuite.ToString();
        return client;
    }

    /// <summary>Sends the legacy handshake with zero-padded proposal slots and reads the reply.</summary>
    public string HandshakeLegacy(params uint[] words)
    {
        var proposals = new uint[4];
        for (var i = 0; i < words.Length && i < 4; i++) { proposals[i] = words[i]; }
        var bytes = new byte[20];
        bytes[0] = 0x60; bytes[1] = 0x60; bytes[2] = 0xB0; bytes[3] = 0x17;
        for (var i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4 + (i * 4), 4), proposals[i]);
        }

        _stream.Write(bytes);
        var reply = new byte[4];
        if (!Framing.ReadExactly(_stream, reply)) { throw new EndOfStreamException("no handshake reply"); }
        LastReplyHex = Program.Hex(reply);
        return LastReplyHex;
    }

    /// <summary>Sends magic + arbitrary proposal words (exactly 16 bytes; no padding).</summary>
    public string HandshakeWords(uint w0, uint w1, uint w2, uint w3)
    {
        var bytes = new byte[20];
        bytes[0] = 0x60; bytes[1] = 0x60; bytes[2] = 0xB0; bytes[3] = 0x17;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), w0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), w1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12, 4), w2);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), w3);
        _stream.Write(bytes);
        var reply = new byte[4];
        if (!Framing.ReadExactly(_stream, reply)) { throw new EndOfStreamException("no handshake reply"); }
        LastReplyHex = Program.Hex(reply);
        return LastReplyHex;
    }

    /// <summary>
    /// Completes a manifest exchange after the handshake has been sent (via HandshakeWords) and
    /// the 4-byte marker reply consumed: reads the offer tail (VarInt count + ranges + VarInt
    /// capabilities), then sends the final selection, optionally pipelined with the first Bolt
    /// message in ONE write. Returns the offer tail bytes (the marker is in LastReplyHex).
    /// </summary>
    public byte[] CompleteManifest(byte[] selection, byte[]? pipelinedAfterSelection = null)
    {
        var offer = new List<byte>();
        var count = ReadVarIntFromStream(offer);
        for (var i = 0; i < (int)count; i++)
        {
            var rangeWord = new byte[4];
            if (!Framing.ReadExactly(_stream, rangeWord)) { throw new EndOfStreamException("no manifest range"); }
            offer.AddRange(rangeWord);
        }

        offer.AddRange(ReadVarIntBytesFromStream());
        var selectionBytes = pipelinedAfterSelection is null ? selection : selection.Concat(pipelinedAfterSelection).ToArray();
        _stream.Write(selectionBytes);
        return offer.ToArray();
    }

    private ulong ReadVarIntFromStream(List<byte> sink)
    {
        ulong value = 0;
        var shift = 0;
        while (true)
        {
            var b = _stream.ReadByte();
            if (b < 0) { throw new EndOfStreamException("EOF in VarInt"); }
            sink.Add((byte)b);
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) { return value; }
            shift += 7;
            if (shift > 63) { throw new InvalidOperationException("VarInt too long"); }
        }
    }

    private byte[] ReadVarIntBytesFromStream()
    {
        var bytes = new List<byte>();
        while (true)
        {
            var b = _stream.ReadByte();
            if (b < 0) { throw new EndOfStreamException("EOF in VarInt"); }
            bytes.Add((byte)b);
            if ((b & 0x80) == 0) { return bytes.ToArray(); }
        }
    }

    public void SendRaw(byte[] bytes) => _stream.Write(bytes);

    /// <summary>Reads exactly one byte into the buffer; returns false on EOF.</summary>
    public bool ReadRawByte(byte[] one)
    {
        var read = _stream.Read(one, 0, 1);
        return read == 1;
    }

    public void SendFramed(params byte[][] chunks) => _stream.Write(Framing.Frame(chunks));

    /// <summary>Sends one reviewer-encoded request as a single-chunk framed message.</summary>
    public void Send(byte[] request) => SendFramed(request);

    public byte[]? ReadMessage() => Framing.ReadMessage(_stream);

    public byte[] ReadMessageOrThrow()
    {
        var message = ReadMessage();
        if (message is null) { throw new EndOfStreamException("server closed before a response arrived"); }
        return message;
    }

    /// <summary>Waits for server closure; returns "closed" or "open".</summary>
    public string WaitForClose(int timeoutMs)
    {
        try
        {
            if (!_tcp.Client.Poll(timeoutMs * 1000, SelectMode.SelectRead)) { return "open"; }
            var probe = new byte[1];
            var read = _stream.Read(probe, 0, 1);
            return read == 0 ? "closed" : "open";
        }
        catch (Exception)
        {
            return "closed";
        }
    }

    /// <summary>
    /// Polls readability WITHOUT consuming bytes; returns "closed" only when the socket is
    /// readable with zero available bytes (true EOF), leaving pending data in the buffer.
    /// </summary>
    public string PollClosedOrReadable(int timeoutMs)
    {
        try
        {
            if (!_tcp.Client.Poll(timeoutMs * 1000, SelectMode.SelectRead))
            {
                return "open";
            }

            var available = _tcp.Client.Available;
            return available == 0 ? "closed" : "open";
        }
        catch (Exception)
        {
            return "closed";
        }
    }

    /// <summary>Drains every available byte from the socket, counting them.</summary>
    public long DrainAvailable(out bool sawEof)
    {
        long total = 0;
        sawEof = false;
        try
        {
            var buffer = new byte[64 * 1024];
            while (_tcp.Client.Poll(200_000, SelectMode.SelectRead))
            {
                if (_tcp.Client.Available == 0) { sawEof = true; break; }
                var n = _stream.Read(buffer, 0, buffer.Length);
                if (n == 0) { sawEof = true; break; }
                total += n;
            }
        }
        catch (Exception)
        {
            // closed socket
        }

        return total;
    }
    public void Dispose()
    {
        try { _stream?.Dispose(); } catch { }
        _tcp.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Golden byte oracles from the accepted B0-B/B3-A historical evidence.</summary>
internal static class Golden
{
    public const string RunScalarRequest = "B310D01352455455524E20343220415320616E73776572A0A0";
    public const string PullN1000 = "B13FA1816EC903E8";
    public const string Record42 = "B171912A";
    public const string Ignored = "B07E";
    public const string LogonSuccessEmpty = "B170A0";
    public const string GraphRecord =
        "B17193B44E019186506572736F6EA1846E616D6585416C696365836E3A31B852010102854B4E4F5753A18573696E6365C907E883723A31836E3A31836E3A32" +
        "B44E029186506572736F6EA1846E616D6583426F62836E3A32";
    public const string RunSuccessFieldsAnswer = "B170A1866669656C64739186616E73776572";
}

/// <summary>Minimal reviewer-owned PackStream parser for response assertions.</summary>
internal static class Parse
{
    public abstract record V;
    public sealed record I(long Value) : V;
    public sealed record S(string Value) : V;
    public sealed record Nul : V;
    public sealed record L(IReadOnlyList<V> Items) : V;
    public sealed record M(IReadOnlyList<(string Key, V? Value)> Entries) : V
    {
        public V? Get(string key)
        {
            foreach (var (k, v) in Entries)
            {
                if (k == key) { return v; }
            }

            return null;
        }
    }

    public sealed record St(byte Tag, IReadOnlyList<V> Fields) : V;
    public sealed record Node(long Id, IReadOnlyList<string> Labels, M Props, string ElementId) : V;
    public sealed record Rel(long Id, long Start, long End, string Type, M Props, string ElementId) : V;

    public static V Read(byte[] bytes)
    {
        var i = 0;
        return ParseValue(bytes, ref i);
    }

    private static V ParseValue(byte[] b, ref int i)
    {
        var marker = b[i++];
        if (marker <= 0x7F) { return new I(marker); }
        if (marker is >= 0xF0 and <= 0xFF) { return new I((sbyte)marker); }
        if (marker is >= 0x80 and <= 0x8F)
        {
            var len = marker & 0x0F;
            var s = Encoding.UTF8.GetString(b, i, len);
            i += len;
            return new S(s);
        }
        if (marker is >= 0x90 and <= 0x9F)
        {
            var n = marker & 0x0F;
            var items = new List<V>(n);
            for (var k = 0; k < n; k++) { items.Add(ParseValue(b, ref i)); }
            return new L(items);
        }

        if (marker is >= 0xA0 and <= 0xAF)
        {
            var n = marker & 0x0F;
            return new M(ParseEntries(b, ref i, n));
        }

        if (marker is >= 0xB0 and <= 0xBF)
        {
            var n = marker & 0x0F;
            var tag = b[i++];
            var fields = new List<V>(n);
            for (var k = 0; k < n; k++) { fields.Add(ParseValue(b, ref i)); }

            // Graph structures arrive as tiny struct markers carrying the graph tag (B4 4E, B8 52).
            if (tag == 0x4E && fields.Count == 4)
            {
                var id = fields[0].As<I>().Value;
                var labels = ParseLabels(fields[1]);
                var props = (M)fields[2];
                var elem = ((S)fields[3]).Value;
                return new Node(id, labels, props, elem);
            }

            if (tag == 0x52 && fields.Count == 8)
            {
                var id = fields[0].As<I>().Value;
                var start = fields[1].As<I>().Value;
                var end = fields[2].As<I>().Value;
                var type = ((S)fields[3]).Value;
                var props = (M)fields[4];
                var elem = ((S)fields[5]).Value;
                return new Rel(id, start, end, type, props, elem);
            }

            return new St(tag, fields);
        }

        switch (marker)
        {
            case 0xC0: return new Nul();
            case 0xC1: { var bits = 0L; for (var k = 0; k < 8; k++) { bits = (bits << 8) | b[i + k]; } i += 8; var d = BitConverter.Int64BitsToDouble(bits); return new S(d.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            case 0xC2: return new S("false");
            case 0xC3: return new S("true");
            case 0xC8: return new I((sbyte)b[i++]);
            case 0xC9: { var v = (short)((b[i] << 8) | b[i + 1]); i += 2; return new I(v); }
            case 0xCA: { long v = (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]; i += 4; return new I(v); }
            case 0xCB: { long v = 0; for (var k = 0; k < 8; k++) { v = (v << 8) | b[i + k]; } i += 8; return new I(v); }
            case 0xD0: { var len = b[i++]; var s = Encoding.UTF8.GetString(b, i, len); i += len; return new S(s); }
            case 0xD1: { var len = (b[i] << 8) | b[i + 1]; i += 2; var s = Encoding.UTF8.GetString(b, i, len); i += len; return new S(s); }
            case 0xD2: { var len = (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]; i += 4; var s = Encoding.UTF8.GetString(b, i, len); i += len; return new S(s); }
            case 0x4E: { var id = (long)ParseValue(b, ref i).As<I>().Value; var labels = ParseLabels(ParseValue(b, ref i)); var props = (M)ParseValue(b, ref i); var elem = ((S)ParseValue(b, ref i)).Value; return new Node(id, labels, props, elem); }
            case 0x52:
            {
                var id = (long)ParseValue(b, ref i).As<I>().Value;
                var start = (long)ParseValue(b, ref i).As<I>().Value;
                var end = (long)ParseValue(b, ref i).As<I>().Value;
                var type = ((S)ParseValue(b, ref i)).Value;
                var props = (M)ParseValue(b, ref i);
                var elem = ((S)ParseValue(b, ref i)).Value;
                ParseValue(b, ref i); // start element id
                ParseValue(b, ref i); // end element id
                return new Rel(id, start, end, type, props, elem);
            }
            default: throw new FormatException($"reviewer parser: unsupported marker 0x{marker:X2} at {i - 1}");
        }
    }

    private static (string, V?)[] ParseEntries(byte[] b, ref int i, int n)
    {
        var entries = new (string, V?)[n];
        for (var k = 0; k < n; k++)
        {
            var key = ((S)ParseValue(b, ref i)).Value;
            var value = ParseValue(b, ref i);
            entries[k] = (key, value);
        }

        return entries;
    }

    private static IReadOnlyList<string> ParseLabels(V v)
    {
        var list = (L)v;
        return list.Items.Select(item => ((S)item).Value).ToList();
    }
}

internal static class ParseExtensions
{
    public static T As<T>(this Parse.V v) where T : Parse.V => (T)v;
}
