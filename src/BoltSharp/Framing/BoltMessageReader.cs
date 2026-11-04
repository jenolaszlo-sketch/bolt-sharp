using System.Buffers;
using System.Buffers.Binary;

namespace BoltSharp.Framing;

/// <summary>
/// Reads Bolt messages from an ordered byte stream using the chunked transfer encoding. Each
/// message is a sequence of chunks — a 2-byte big-endian length followed by that many payload
/// bytes — terminated by a zero-length chunk (<c>00 00</c>). All lengths are interpreted
/// big-endian.
/// <para>
/// <b>NOOP handling.</b> A zero-length chunk that appears before any payload byte of the
/// current message is a <c>NOOP</c> keep-alive (Bolt 4.1+) and is skipped; a zero-length
/// chunk after payload bytes terminates the message. Empty messages are therefore never
/// returned. A clean end of stream at a chunk boundary between messages yields
/// <see langword="null"/> (from <see cref="ReadMessage"/>) or <see langword="false"/> (from
/// <see cref="TryReadMessage"/>).
/// </para>
/// <para>
/// <b>End-of-stream contract (B2-A-R1, finding Q1).</b> The source stream must follow the
/// standard <see cref="Stream"/> contract, in which a positive-length read that returns zero
/// indicates the end of the stream. A stream that returns zero to signal temporary
/// unavailability rather than end of stream is <b>not</b> supported: the reader would treat
/// that zero-byte read as a clean end of stream between messages and stop. At a message
/// boundary a zero-byte read yields <see langword="null"/>/<see langword="false"/>; after
/// payload bytes have begun it raises <see cref="BoltFramingTruncatedException"/>.
/// </para>
/// <para>
/// <b>Message size limit.</b> The reassembled size of a message must not exceed
/// <see cref="BoltFramingLimits.MaxMessageSize"/>. This is a receiver-side (reader) safeguard:
/// the writer does not enforce the receiving peer's configured limit. The check is made before
/// a chunk payload is read, so a chunk that declares an over-limit size never causes a large
/// allocation.
/// </para>
/// <para>
/// <b>Failure contract.</b> When a read throws <see cref="BoltFramingTruncatedException"/>
/// (the stream ended mid-header or mid-message) or <see cref="BoltFramingFormatException"/>
/// (a declared message exceeds the limit), the reader is left at the failure point and
/// provides no rollback or resynchronisation. <b>After a framing exception the caller must
/// discard the reader and the connection.</b>
/// </para>
/// <para>
/// <b>Memory.</b> A single read accumulates at most one message, bounded by
/// <see cref="BoltFramingLimits.MaxMessageSize"/>; allocation is driven by bytes actually
/// received, never by an unvalidated declared length. There is no separate limit on the
/// number of chunks in a message, so memory stays bounded but a peer can still send a message
/// composed of many tiny chunks and cost I/O proportional to the bytes received (B2-A-Q1,
/// finding Q4). This type performs only stream reads and has no socket, TLS or ASP.NET
/// dependency. It does not implement the Bolt handshake or interpret message contents.
/// </para>
/// </summary>
public sealed class BoltMessageReader
{
    private const int InitialMessageCapacity = 256;

    private readonly Stream _stream;
    private long _position;

    public BoltMessageReader(Stream stream)
        : this(stream, BoltFramingLimits.Default)
    {
    }

    public BoltMessageReader(Stream stream, BoltFramingLimits limits)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(limits);

        _stream = stream;
        Limits = limits;
    }

    /// <summary>The configured framing limits.</summary>
    public BoltFramingLimits Limits { get; }

    /// <summary>Total number of bytes consumed from the stream so far.</summary>
    public long Position => _position;

    /// <summary>
    /// Reads the next complete message, or returns <see langword="null"/> at a clean end of
    /// stream between messages.
    /// </summary>
    public byte[]? ReadMessage()
    {
        var buffer = new ArrayBufferWriter<byte>(InitialCapacity());
        return TryReadMessage(buffer) ? buffer.WrittenSpan.ToArray() : null;
    }

    /// <summary>
    /// Asynchronously reads the next complete message, or returns <see langword="null"/> at a
    /// clean end of stream between messages.
    /// </summary>
    public async ValueTask<byte[]?> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        var buffer = new ArrayBufferWriter<byte>(InitialCapacity());
        return await TryReadMessageAsync(buffer, cancellationToken).ConfigureAwait(false)
            ? buffer.WrittenSpan.ToArray()
            : null;
    }

    /// <summary>
    /// Reads the next complete message, <b>appending</b> its bytes to
    /// <paramref name="destination"/>. Existing content in <paramref name="destination"/> is
    /// preserved (the destination is not cleared), so the caller must supply a fresh or
    /// cleared buffer to capture only this message (B2-A-R1, finding Q3). Returns
    /// <see langword="true"/> when a message was appended, or <see langword="false"/> at a
    /// clean end of stream between messages (in which case nothing is appended).
    /// </summary>
    public bool TryReadMessage(IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var total = 0;
        while (true)
        {
            if (!TryReadHeader(out var chunkLength))
            {
                if (total > 0)
                {
                    throw new BoltFramingTruncatedException(
                        $"The stream ended before the message terminator (00 00) was read, " +
                        $"after {total} payload byte(s).");
                }

                return false;
            }

            if (chunkLength == 0)
            {
                if (total > 0)
                {
                    return true;
                }

                // A zero-length chunk before any payload is a NOOP keep-alive: skip it.
                continue;
            }

            if (chunkLength > Limits.MaxMessageSize - total)
            {
                throw new BoltFramingFormatException(
                    $"The declared message size exceeds the configured maximum of " +
                    $"{Limits.MaxMessageSize} byte(s).");
            }

            ReadPayload(chunkLength, destination);
            total += chunkLength;
        }
    }

    /// <summary>
    /// Asynchronously reads the next complete message, <b>appending</b> its bytes to
    /// <paramref name="destination"/>. Existing content in <paramref name="destination"/> is
    /// preserved (the destination is not cleared) (B2-A-R1, finding Q3). Returns
    /// <see langword="true"/> when a message was appended, or <see langword="false"/> at a
    /// clean end of stream between messages.
    /// </summary>
    public async ValueTask<bool> TryReadMessageAsync(
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var total = 0;
        while (true)
        {
            var (hasHeader, chunkLength) = await ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
            if (!hasHeader)
            {
                if (total > 0)
                {
                    throw new BoltFramingTruncatedException(
                        $"The stream ended before the message terminator (00 00) was read, " +
                        $"after {total} payload byte(s).");
                }

                return false;
            }

            if (chunkLength == 0)
            {
                if (total > 0)
                {
                    return true;
                }

                continue;
            }

            if (chunkLength > Limits.MaxMessageSize - total)
            {
                throw new BoltFramingFormatException(
                    $"The declared message size exceeds the configured maximum of " +
                    $"{Limits.MaxMessageSize} byte(s).");
            }

            await ReadPayloadAsync(chunkLength, destination, cancellationToken).ConfigureAwait(false);
            total += chunkLength;
        }
    }

    private int InitialCapacity() => Math.Min(InitialMessageCapacity, Limits.MaxMessageSize);

    private bool TryReadHeader(out int chunkLength)
    {
        Span<byte> header = stackalloc byte[2];
        var read = 0;

        while (read < 2)
        {
            var count = _stream.Read(header.Slice(read, 2 - read));
            if (count <= 0)
            {
                if (read == 0)
                {
                    chunkLength = 0;
                    return false;
                }

                throw new BoltFramingTruncatedException(
                    "The stream ended after 1 byte of a 2-byte chunk header.");
            }

            read += count;
            _position += count;
        }

        chunkLength = BinaryPrimitives.ReadUInt16BigEndian(header);
        return true;
    }

    private async ValueTask<(bool HasHeader, int ChunkLength)> ReadHeaderAsync(CancellationToken cancellationToken)
    {
        var header = new byte[2];
        var read = 0;

        while (read < 2)
        {
            var count = await _stream
                .ReadAsync(header.AsMemory(read, 2 - read), cancellationToken)
                .ConfigureAwait(false);

            if (count <= 0)
            {
                if (read == 0)
                {
                    return (false, 0);
                }

                throw new BoltFramingTruncatedException(
                    "The stream ended after 1 byte of a 2-byte chunk header.");
            }

            read += count;
            _position += count;
        }

        return (true, BinaryPrimitives.ReadUInt16BigEndian(header));
    }

    private void ReadPayload(int length, IBufferWriter<byte> destination)
    {
        var remaining = length;
        while (remaining > 0)
        {
            var span = destination.GetSpan(remaining).Slice(0, remaining);
            var count = _stream.Read(span);
            if (count <= 0)
            {
                throw new BoltFramingTruncatedException(
                    $"The stream ended {remaining} byte(s) before a declared chunk payload of " +
                    $"{length} byte(s) was complete.");
            }

            destination.Advance(count);
            _position += count;
            remaining -= count;
        }
    }

    private async ValueTask ReadPayloadAsync(
        int length,
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken)
    {
        var remaining = length;
        while (remaining > 0)
        {
            var memory = destination.GetMemory(remaining).Slice(0, remaining);
            var count = await _stream.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
            if (count <= 0)
            {
                throw new BoltFramingTruncatedException(
                    $"The stream ended {remaining} byte(s) before a declared chunk payload of " +
                    $"{length} byte(s) was complete.");
            }

            destination.Advance(count);
            _position += count;
            remaining -= count;
        }
    }
}
