namespace BoltSharp.Framing;

/// <summary>
/// Limits applied by the Bolt chunked-message framing codec.
/// <para>
/// The Bolt wire format fixes the maximum chunk payload at 65,535 bytes (a chunk length is a
/// 16-bit big-endian unsigned integer); that value is not configurable. The total size of a
/// single reassembled message, however, is not fixed by the protocol and is bounded here so
/// that a declared message length cannot drive unbounded allocation. The default is 64 MiB,
/// which comfortably carries the values permitted by the B1 PackStream per-value limits
/// (16 MiB strings and byte arrays) together with their message envelope.
/// </para>
/// <para>
/// <b>Scope (B2-A-R1, finding Q2).</b> <see cref="MaxMessageSize"/> is enforced by the
/// receiving side only, i.e. by <see cref="BoltMessageReader"/>. The writer
/// (<see cref="BoltMessageWriter"/>) emits whatever message it is given and does not enforce
/// the receiving peer's configured limit.
/// </para>
/// </summary>
public sealed class BoltFramingLimits
{
    /// <summary>The protocol-mandated maximum chunk payload size (65,535 bytes).</summary>
    public const int MaxChunkPayloadSize = 65_535;

    /// <summary>The default maximum size of a single reassembled message (64 MiB).</summary>
    public const int DefaultMaxMessageSize = 64 * 1024 * 1024;

    /// <summary>A shared instance using the default limits.</summary>
    public static BoltFramingLimits Default { get; } = new();

    public BoltFramingLimits(int maxMessageSize = DefaultMaxMessageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessageSize);
        MaxMessageSize = maxMessageSize;
    }

    /// <summary>
    /// Maximum permitted size, in bytes, of a single reassembled message. This is a
    /// receiver-side limit enforced by <see cref="BoltMessageReader"/>; the writer does not
    /// enforce the receiving peer's configured limit (B2-A-R1, finding Q2).
    /// </summary>
    public int MaxMessageSize { get; }
}
