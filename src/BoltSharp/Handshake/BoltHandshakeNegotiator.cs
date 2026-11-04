using System.Buffers;
using System.Buffers.Binary;

namespace BoltSharp.Handshake;

/// <summary>
/// Implements the Bolt handshake as a bounded, transport-independent component over a duplex
/// ordered byte stream. It reads the Bolt magic <c>60 60 B0 17</c>, exactly four 32-bit
/// big-endian version proposals, and then either performs the legacy selection or the Bolt
/// Manifest v1 offer/selection, according to <see cref="BoltHandshakeOptions"/>.
/// <para>
/// <b>Selection.</b> Proposals are treated as ordered by client preference. A proposal that is
/// not a recognized encoding (its reserved first byte is set, or its minor range would span a
/// major version) raises <see cref="BoltHandshakeFormatException"/>. A version-zero proposal is
/// a placeholder and is skipped. The first proposal that yields a concrete version this server
/// supports is selected; for a range the highest supported minor is chosen. If no proposal
/// matches, the server writes <c>00 00 00 00</c> and the result requests closure.
/// </para>
/// <para>
/// <b>Manifest v1.</b> If the client offers the manifest marker and Manifest v1 is enabled with
/// at least one supported version, the server writes the marker, a VarInt range count, the
/// supported ranges (Bolt 4.3 style) and a VarInt capability bitmask, then reads the client's
/// concrete selection and capability subset. The selection must be an offered concrete version
/// and the capabilities must be a subset of those offered; otherwise a
/// <see cref="BoltHandshakeProtocolException"/> is raised and no result is published. Protocol
/// zero is never offered, selected or accepted (B2-B-R1).
/// </para>
/// <para>
/// <b>Pipelining and ownership.</b> The negotiator reads exactly the handshake bytes and never
/// buffers ahead, so any bytes that follow (such as the first pipelined message) remain in the
/// stream. It never disposes the stream; the caller owns it. A manifest selection is the last
/// server-read element, so the client may pipeline its first message immediately after it.
/// </para>
/// <para>
/// <b>Scope.</b> This type performs handshake negotiation only. It does not implement, and does
/// not claim, any Bolt messaging-protocol version. <see cref="BoltHandshakeOptions.Default"/>
/// advertises nothing.
/// </para>
/// </summary>
public sealed class BoltHandshakeNegotiator
{
    private static readonly byte[] MagicBytesValue = { 0x60, 0x60, 0xB0, 0x17 };

    private readonly HashSet<BoltProtocolVersion> _supported;

    public BoltHandshakeNegotiator(BoltHandshakeOptions options, BoltHandshakeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        Options = options;
        Limits = limits ?? BoltHandshakeLimits.Default;
        _supported = new HashSet<BoltProtocolVersion>(options.SupportedVersions);
        OfferRanges = BoltVersionRange.FromVersions(options.SupportedVersions);
    }

    /// <summary>The four Bolt magic bytes <c>60 60 B0 17</c>.</summary>
    public static ReadOnlyMemory<byte> Magic => MagicBytesValue;

    /// <summary>The configured negotiation options.</summary>
    public BoltHandshakeOptions Options { get; }

    /// <summary>The configured handshake bounds.</summary>
    public BoltHandshakeLimits Limits { get; }

    /// <summary>The manifest ranges that will be offered for the configured supported versions.</summary>
    public IReadOnlyList<BoltVersionRange> OfferRanges { get; }

    /// <summary>
    /// Reads a client handshake from <paramref name="stream"/> and writes the server response to
    /// the same duplex stream.
    /// </summary>
    /// <exception cref="BoltHandshakeFormatException">The magic is wrong or the bytes are structurally invalid.</exception>
    /// <exception cref="BoltHandshakeTruncatedException">The stream ended before the handshake was complete.</exception>
    /// <exception cref="BoltHandshakeProtocolException">A manifest selection was not offered or selected unoffered capabilities.</exception>
    public BoltHandshakeResult Negotiate(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var reader = new HandshakeByteReader(stream, Limits.MaxHandshakeBytes);
        ReadClientPreamble(reader, out var proposals);
        var decision = Decide(proposals);

        switch (decision.Kind)
        {
            case HandshakeDecisionKind.Legacy:
                WriteResponse(stream, decision.Version!.Value.ToUInt32());
                return BoltHandshakeResult.AcceptedLegacy(decision.Version.Value);

            case HandshakeDecisionKind.Manifest:
                WriteOffer(stream);
                var selection = BoltManifestV1.ReadSelection(reader);
                ValidateSelection(selection);
                return BoltHandshakeResult.AcceptedManifest(selection.Version, selection.Capabilities);

            default:
                WriteResponse(stream, 0u);
                return BoltHandshakeResult.Rejected();
        }
    }

    /// <summary>
    /// Asynchronously reads a client handshake from <paramref name="stream"/> and writes the
    /// server response to the same duplex stream.
    /// </summary>
    public async ValueTask<BoltHandshakeResult> NegotiateAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var reader = new HandshakeByteReader(stream, Limits.MaxHandshakeBytes);
        var proposals = await ReadClientPreambleAsync(reader, cancellationToken).ConfigureAwait(false);
        var decision = Decide(proposals);

        switch (decision.Kind)
        {
            case HandshakeDecisionKind.Legacy:
                await WriteResponseAsync(stream, decision.Version!.Value.ToUInt32(), cancellationToken)
                    .ConfigureAwait(false);
                return BoltHandshakeResult.AcceptedLegacy(decision.Version.Value);

            case HandshakeDecisionKind.Manifest:
                await WriteOfferAsync(stream, cancellationToken).ConfigureAwait(false);
                var selection = await BoltManifestV1.ReadSelectionAsync(reader, cancellationToken)
                    .ConfigureAwait(false);
                ValidateSelection(selection);
                return BoltHandshakeResult.AcceptedManifest(selection.Version, selection.Capabilities);

            default:
                await WriteResponseAsync(stream, 0u, cancellationToken).ConfigureAwait(false);
                return BoltHandshakeResult.Rejected();
        }
    }

    private void ReadClientPreamble(HandshakeByteReader reader, out uint[] proposals)
    {
        Span<byte> magic = stackalloc byte[4];
        reader.ReadExactly(magic);
        ValidateMagic(magic);

        Span<byte> proposalBytes = stackalloc byte[16];
        reader.ReadExactly(proposalBytes);
        proposals = new uint[4];
        for (var i = 0; i < proposals.Length; i++)
        {
            proposals[i] = BinaryPrimitives.ReadUInt32BigEndian(proposalBytes.Slice(i * 4, 4));
        }
    }

    private static async ValueTask<uint[]> ReadClientPreambleAsync(
        HandshakeByteReader reader,
        CancellationToken cancellationToken)
    {
        var magic = new byte[4];
        await reader.ReadExactlyAsync(magic, cancellationToken).ConfigureAwait(false);
        ValidateMagic(magic);

        var proposalBytes = new byte[16];
        await reader.ReadExactlyAsync(proposalBytes, cancellationToken).ConfigureAwait(false);
        var proposals = new uint[4];
        for (var i = 0; i < proposals.Length; i++)
        {
            proposals[i] = BinaryPrimitives.ReadUInt32BigEndian(proposalBytes.AsSpan(i * 4, 4));
        }

        return proposals;
    }

    private static void ValidateMagic(ReadOnlySpan<byte> magic)
    {
        if (!magic.SequenceEqual(MagicBytesValue))
        {
            throw new BoltHandshakeFormatException(
                "The connection did not begin with the Bolt magic 60 60 B0 17.");
        }
    }

    private HandshakeDecision Decide(ReadOnlySpan<uint> proposals)
    {
        foreach (var proposal in proposals)
        {
            if (proposal == 0)
            {
                // Version zero is a client placeholder ("no version").
                continue;
            }

            if (proposal == BoltManifestV1.Marker)
            {
                if (Options.ManifestV1Enabled && Options.SupportedVersions.Count > 0)
                {
                    return HandshakeDecision.ForManifest();
                }

                // Manifest is not available: the marker is simply an unsupported version.
                continue;
            }

            var range = BoltVersionRange.FromUInt32(proposal);
            foreach (var candidate in range.Expand())
            {
                // Protocol zero is the "no version" sentinel and is never selectable, even when
                // a client range spans it (B2-B-R1, finding B2BQ1-F01).
                if (candidate == default)
                {
                    continue;
                }

                if (_supported.Contains(candidate))
                {
                    return HandshakeDecision.ForLegacy(candidate);
                }
            }
        }

        return HandshakeDecision.Reject();
    }

    private void ValidateSelection(BoltManifestSelection selection)
    {
        // Protocol zero is the "no version" sentinel; it can never be a valid manifest
        // selection even if it were somehow offered (B2-B-R1, finding B2BQ1-F01).
        if (selection.Version == default)
        {
            throw new BoltHandshakeProtocolException(
                "The manifest client selected protocol zero, which is the 'no version' sentinel.");
        }

        var offered = false;
        foreach (var range in OfferRanges)
        {
            if (range.Contains(selection.Version))
            {
                offered = true;
                break;
            }
        }

        if (!offered)
        {
            throw new BoltHandshakeProtocolException(
                $"The manifest client selected version {selection.Version}, which the server did not offer.");
        }

        if ((selection.Capabilities & ~Options.OfferedCapabilities) != 0)
        {
            throw new BoltHandshakeProtocolException(
                $"The manifest client selected capability bits 0x{selection.Capabilities:X} that are " +
                $"not a subset of the offered bits 0x{Options.OfferedCapabilities:X}.");
        }
    }

    private void WriteOffer(Stream stream)
    {
        var buffer = new ArrayBufferWriter<byte>();
        BoltManifestV1.WriteOffer(buffer, OfferRanges, Options.OfferedCapabilities);
        stream.Write(buffer.WrittenSpan);
        stream.Flush();
    }

    private async ValueTask WriteOfferAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        BoltManifestV1.WriteOffer(buffer, OfferRanges, Options.OfferedCapabilities);
        await stream.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void WriteResponse(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
        stream.Flush();
    }

    private static async ValueTask WriteResponseAsync(Stream stream, uint value, CancellationToken cancellationToken)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private enum HandshakeDecisionKind
    {
        Reject,
        Legacy,
        Manifest,
    }

    private readonly struct HandshakeDecision
    {
        private HandshakeDecision(HandshakeDecisionKind kind, BoltProtocolVersion? version)
        {
            Kind = kind;
            Version = version;
        }

        public HandshakeDecisionKind Kind { get; }

        public BoltProtocolVersion? Version { get; }

        public static HandshakeDecision Reject() => new(HandshakeDecisionKind.Reject, null);

        public static HandshakeDecision ForLegacy(BoltProtocolVersion version) =>
            new(HandshakeDecisionKind.Legacy, version);

        public static HandshakeDecision ForManifest() =>
            new(HandshakeDecisionKind.Manifest, null);
    }
}
