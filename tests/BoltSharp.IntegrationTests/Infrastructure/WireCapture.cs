using System.Text;
using BoltSharp.PackStream;

namespace BoltSharp.IntegrationTests.Infrastructure;

internal enum WireDirection
{
    ClientToServer,
    ServerToClient,
}

/// <summary>
/// A single captured wire frame. The <see cref="RawMessage"/> is retained in memory for assertions;
/// <see cref="SanitizedHex"/> / <see cref="Summary"/> are the only representations written to
/// persisted evidence. Authentication-bearing messages are always redacted.
/// </summary>
internal sealed class WireFrame
{
    public required long Sequence { get; init; }

    public required WireDirection Direction { get; init; }

    public required string Name { get; init; }

    /// <summary>Message payload (without chunk framing). Never persisted for sensitive messages.</summary>
    public required byte[] RawMessage { get; init; }

    /// <summary>Hex of <see cref="RawMessage"/>, or a redaction placeholder for sensitive messages.</summary>
    public required string SanitizedHex { get; init; }

    /// <summary>A short structural summary that never contains credential material.</summary>
    public required string Summary { get; init; }

    public required bool Sensitive { get; init; }
}

/// <summary>
/// A captured client request decoded from the wire.
/// </summary>
internal sealed class BoltRequest
{
    public required long Sequence { get; init; }

    public required byte Signature { get; init; }

    public required string Name { get; init; }

    public required byte[] RawMessage { get; init; }

    public required PackStreamStructure Structure { get; init; }

    public required bool Sensitive { get; init; }

    public required string SanitizedDescription { get; init; }

    /// <summary>True when no script rule matched this request.</summary>
    public required bool Unscripted { get; init; }
}

/// <summary>
/// The full transcript of one fake-server connection: handshake bytes, ordered frames, decoded
/// requests and diagnostic notes. Evidence serialisation redacts sensitive frames.
/// </summary>
internal sealed class WireCapture
{
    private readonly List<WireFrame> _frames = new();
    private readonly List<BoltRequest> _requests = new();
    private readonly List<string> _notes = new();
    private long _sequence;

    public byte[] ClientHandshake { get; set; } = Array.Empty<byte>();

    public byte[] ServerHandshake { get; set; } = Array.Empty<byte>();

    /// <summary>The negotiated Bolt version as "major.minor", or "closed" / "rejected".</summary>
    public string NegotiatedVersion { get; set; } = "NOT_NEGOTIATED";

    public IReadOnlyList<WireFrame> Frames => _frames;

    public IReadOnlyList<BoltRequest> Requests => _requests;

    public IReadOnlyList<string> Notes => _notes;

    public string RequestsSummary => string.Join(",", _requests.Select(r => r.Name));

    public void Note(string message) => _notes.Add(message);

    public WireFrame RecordFrame(
        WireDirection direction,
        string name,
        byte[] rawMessage,
        string summary,
        bool sensitive)
    {
        var frame = new WireFrame
        {
            Sequence = _sequence++,
            Direction = direction,
            Name = name,
            RawMessage = rawMessage,
            SanitizedHex = sensitive ? "<redacted>" : Convert.ToHexString(rawMessage),
            Summary = summary,
            Sensitive = sensitive,
        };
        _frames.Add(frame);
        return frame;
    }

    public void RecordRequest(BoltRequest request) => _requests.Add(request);

    public static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    public static string HexSpaced(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 3);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(bytes[i].ToString("X2"));
        }

        return sb.ToString();
    }
}
