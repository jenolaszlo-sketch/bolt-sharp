using System.Buffers;
using System.Buffers.Binary;

namespace BoltSharp.Framing;

/// <summary>
/// Writes Bolt messages using the chunked transfer encoding. Each message is emitted as one
/// or more chunks — a 2-byte big-endian length followed by that many payload bytes — and
/// terminated by a zero-length chunk (<c>00 00</c>). All lengths are written big-endian.
/// <para>
/// A message is split into chunks of at most
/// <see cref="BoltFramingLimits.MaxChunkPayloadSize"/> (65,535) bytes, so the output is
/// deterministic for a given message and never emits more chunks than necessary.
/// </para>
/// <para>
/// <b>Empty messages.</b> The chunked encoding cannot represent an empty message distinctly
/// from a <c>NOOP</c> keep-alive chunk (both are a lone <c>00 00</c>), so
/// <see cref="WriteMessage"/> rejects an empty message. Use <see cref="WriteNoop"/> to emit a
/// keep-alive.
/// </para>
/// <para>
/// <b>Size limits (B2-A-R1, finding Q2).</b> The writer does not enforce the receiving peer's
/// <see cref="BoltFramingLimits.MaxMessageSize"/>; it emits whatever message it is given. The
/// receiving side applies its own limit.
/// </para>
/// <para>
/// This type performs no I/O and has no socket, TLS or ASP.NET dependency; it writes only to
/// the supplied <see cref="IBufferWriter{T}"/>. It does not implement the Bolt handshake.
/// </para>
/// </summary>
public sealed class BoltMessageWriter
{
    private readonly IBufferWriter<byte> _output;

    public BoltMessageWriter(IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    /// <summary>
    /// Writes one message as one or more chunks followed by the <c>00 00</c> end-of-message
    /// terminator.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="message"/> is empty.</exception>
    public void WriteMessage(ReadOnlySpan<byte> message)
    {
        if (message.IsEmpty)
        {
            throw new ArgumentException(
                "A Bolt message must contain at least one byte; an empty message cannot be " +
                "distinguished from a NOOP keep-alive chunk on the wire.",
                nameof(message));
        }

        var offset = 0;
        while (offset < message.Length)
        {
            var chunkLength = Math.Min(BoltFramingLimits.MaxChunkPayloadSize, message.Length - offset);

            var header = _output.GetSpan(2);
            BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)chunkLength);
            _output.Advance(2);

            _output.Write(message.Slice(offset, chunkLength));
            offset += chunkLength;
        }

        WriteTerminator();
    }

    /// <summary>
    /// Writes a <c>NOOP</c> keep-alive chunk (a lone <c>00 00</c>). A receiver skips it
    /// without ending the current message (Bolt 4.1+).
    /// </summary>
    public void WriteNoop() => WriteTerminator();

    private void WriteTerminator()
    {
        var terminator = _output.GetSpan(2);
        terminator[0] = 0;
        terminator[1] = 0;
        _output.Advance(2);
    }
}
