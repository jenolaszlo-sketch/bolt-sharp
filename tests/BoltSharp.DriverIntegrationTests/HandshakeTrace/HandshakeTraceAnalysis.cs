using System.Buffers.Binary;
using System.Text.Json;
using BoltSharp.Handshake;

namespace BoltSharp.DriverIntegrationTests.HandshakeTrace;

/// <summary>Decoded form of one of the four 32-bit client proposals.</summary>
internal sealed class ProposalDescription
{
    internal int Slot { get; init; }

    internal string RawHex { get; init; } = string.Empty;

    /// <summary>MANIFEST_V1, PLACEHOLDER, CONCRETE, RANGE or MALFORMED.</summary>
    internal string Kind { get; init; } = "UNKNOWN";

    internal int? Major { get; init; }

    internal int? Minor { get; init; }

    internal int? Range { get; init; }

    internal string? Version { get; init; }

    internal IReadOnlyList<string>? ExpandedVersions { get; init; }

    internal string? DecodeError { get; init; }
}

/// <summary>The result of replaying captured client handshake bytes through the production negotiator.</summary>
internal sealed class NegotiationReplay
{
    internal string Outcome { get; set; } = "NOT_RUN";

    internal string? Mode { get; set; }

    internal bool? Accepted { get; set; }

    internal bool? ShouldClose { get; set; }

    internal string? SelectedVersion { get; set; }

    internal ulong? NegotiatedCapabilities { get; set; }

    internal byte[] ServerResponseBytes { get; set; } = Array.Empty<byte>();

    internal bool MatchesLiveResponse { get; set; }

    internal string? ErrorType { get; set; }

    internal string? SanitizedError { get; set; }
}

/// <summary>The official driver's observable outcome for one probe run.</summary>
internal sealed class DriverProbeObservation
{
    internal bool CompletedSuccessfully { get; set; }

    internal bool TimedOut { get; set; }

    internal string? ExceptionType { get; set; }

    internal string? SanitizedMessage { get; set; }

    internal double ElapsedMs { get; set; }
}

internal static class HandshakeTraceAnalysis
{
    private static readonly byte[] BoltMagic = { 0x60, 0x60, 0xB0, 0x17 };

    internal static IReadOnlyList<ProposalDescription> DecodeProposals(byte[] clientHandshakeBytes)
    {
        var proposals = new List<ProposalDescription>(4);
        for (var slot = 0; slot < 4; slot++)
        {
            var offset = 4 + (slot * 4);
            if (offset + 4 > clientHandshakeBytes.Length)
            {
                proposals.Add(new ProposalDescription
                {
                    Slot = slot,
                    RawHex = "MISSING",
                    Kind = "MALFORMED",
                    DecodeError = "The captured client handshake has fewer than four proposals.",
                });
                continue;
            }

            var raw = clientHandshakeBytes.AsSpan(offset, 4);
            var value = BinaryPrimitives.ReadUInt32BigEndian(raw);

            if (raw.SequenceEqual(BoltManifestV1.MarkerBytes.Span))
            {
                proposals.Add(new ProposalDescription
                {
                    Slot = slot,
                    RawHex = TraceEvidence.Hex(raw),
                    Kind = "MANIFEST_V1",
                });
                continue;
            }

            if (value == 0)
            {
                proposals.Add(new ProposalDescription
                {
                    Slot = slot,
                    RawHex = TraceEvidence.Hex(raw),
                    Kind = "PLACEHOLDER",
                });
                continue;
            }

            try
            {
                var range = BoltVersionRange.FromUInt32(value);
                if (range.Range == 0)
                {
                    proposals.Add(new ProposalDescription
                    {
                        Slot = slot,
                        RawHex = TraceEvidence.Hex(raw),
                        Kind = "CONCRETE",
                        Major = range.Major,
                        Minor = range.Minor,
                        Range = 0,
                        Version = new BoltProtocolVersion(range.Major, range.Minor).ToString(),
                    });
                }
                else
                {
                    proposals.Add(new ProposalDescription
                    {
                        Slot = slot,
                        RawHex = TraceEvidence.Hex(raw),
                        Kind = "RANGE",
                        Major = range.Major,
                        Minor = range.Minor,
                        Range = range.Range,
                        Version = range.ToString(),
                        ExpandedVersions = range.Expand().Select(v => v.ToString()).ToArray(),
                    });
                }
            }
            catch (BoltHandshakeFormatException ex)
            {
                proposals.Add(new ProposalDescription
                {
                    Slot = slot,
                    RawHex = TraceEvidence.Hex(raw),
                    Kind = "MALFORMED",
                    DecodeError = TraceEvidence.Sanitize(ex.Message),
                });
            }
        }

        return proposals;
    }

    internal static async Task<NegotiationReplay> ReplayAsync(
        byte[] clientHandshakeBytes,
        BoltHandshakeOptions options,
        byte[] liveServerResponseBytes,
        int maxManifestVersions,
        int maxHandshakeBytes)
    {
        var replay = new NegotiationReplay();
        try
        {
            using var stream = new ReplayDuplexStream(clientHandshakeBytes);
            var negotiator = new BoltHandshakeNegotiator(
                options,
                new BoltHandshakeLimits(maxManifestVersions, maxHandshakeBytes));

            var result = await negotiator.NegotiateAsync(stream).ConfigureAwait(false);
            replay.Outcome = result.Accepted ? "ACCEPTED" : "REJECTED";
            replay.Mode = result.Mode.ToString();
            replay.Accepted = result.Accepted;
            replay.ShouldClose = result.ShouldClose;
            replay.SelectedVersion = result.SelectedVersion?.ToString();
            replay.NegotiatedCapabilities = result.NegotiatedCapabilities;
            replay.ServerResponseBytes = stream.WrittenBytes;
            replay.MatchesLiveResponse = stream.WrittenBytes.AsSpan()
                .SequenceEqual(liveServerResponseBytes);
            return replay;
        }
        catch (BoltHandshakeException ex)
        {
            replay.Outcome = ex switch
            {
                BoltHandshakeFormatException => "FORMAT_ERROR",
                BoltHandshakeTruncatedException => "TRUNCATED",
                BoltHandshakeProtocolException => "PROTOCOL_ERROR",
                _ => "HANDSHAKE_ERROR",
            };
            replay.ErrorType = ex.GetType().FullName;
            replay.SanitizedError = TraceEvidence.Sanitize(ex.Message);
            return replay;
        }
        catch (Exception ex)
        {
            replay.Outcome = "ERROR";
            replay.ErrorType = ex.GetType().FullName;
            replay.SanitizedError = TraceEvidence.Sanitize(ex.Message);
            return replay;
        }
    }

    internal static object BuildReport(
        string mode,
        Uri endpoint,
        HandshakeTrace trace,
        DriverProbeObservation driver,
        NegotiationReplay replay,
        BoltHandshakeOptions options,
        int maxManifestVersions,
        int maxHandshakeBytes)
    {
        var proposals = DecodeProposals(trace.ClientHandshakeBytes);
        var magic = trace.ClientHandshakeBytes.Length >= 4
            ? trace.ClientHandshakeBytes.AsSpan(0, 4)
            : ReadOnlySpan<byte>.Empty;
        var magicValid = magic.SequenceEqual(BoltMagic);
        var manifestOffered = proposals.Any(p => p.Kind == "MANIFEST_V1");

        var candidateVersions = new List<string>();
        foreach (var proposal in proposals)
        {
            if (proposal.ExpandedVersions is { } expanded)
            {
                candidateVersions.AddRange(expanded);
            }
            else if (proposal.Version is { } version)
            {
                candidateVersions.Add(version);
            }
            else if (proposal.Kind == "MANIFEST_V1")
            {
                candidateVersions.Add("MANIFEST_V1");
            }
        }

        var offerRanges = BoltVersionRange.FromVersions(options.SupportedVersions);
        object? manifestOffer = null;
        if (trace.Mode == "ManifestV1" && trace.ServerResponseBytes.Length > 0)
        {
            try
            {
                var offer = BoltManifestV1.ReadOffer(
                    new MemoryStream(trace.ServerResponseBytes),
                    new BoltHandshakeLimits(options.SupportedVersions.Count + 1, maxHandshakeBytes));
                manifestOffer = new
                {
                    marker_hex = "00 00 01 FF",
                    version_range_count = offer.Ranges.Count,
                    offered_ranges = offer.Ranges.Select(r => r.ToString()).ToArray(),
                    offered_versions = offer.Versions.Select(v => v.ToString()).ToArray(),
                    capabilities = $"0x{offer.Capabilities:X}",
                };
            }
            catch (Exception ex)
            {
                manifestOffer = new
                {
                    parse_error_type = ex.GetType().FullName,
                    parse_error = TraceEvidence.Sanitize(ex.Message),
                };
            }
        }

        object? manifestSelection = null;
        if (trace.Mode == "ManifestV1" && trace.ClientHandshakeBytes.Length > 20)
        {
            try
            {
                var selectionBytes = trace.ClientHandshakeBytes.AsSpan(20).ToArray();
                var selection = BoltManifestV1.ReadSelection(new MemoryStream(selectionBytes));
                var selectionOffered = offerRanges.Any(r => r.Contains(selection.Version));
                var capabilitiesSubset =
                    (selection.Capabilities & ~options.OfferedCapabilities) == 0;
                manifestSelection = new
                {
                    selection_hex = TraceEvidence.Hex(selectionBytes),
                    selected_version = selection.Version.ToString(),
                    selected_capabilities = $"0x{selection.Capabilities:X}",
                    selected_version_was_offered = selectionOffered,
                    capabilities_are_subset_of_offer = capabilitiesSubset,
                    selection_respects_advertised_offer = selectionOffered && capabilitiesSubset,
                    server_side_negotiation_completed = trace.Accepted == true,
                };
            }
            catch (Exception ex)
            {
                manifestSelection = new
                {
                    parse_error_type = ex.GetType().FullName,
                    parse_error = TraceEvidence.Sanitize(ex.Message),
                };
            }
        }

        var postHandshake = trace.PostHandshake;
        var continuationObserved = postHandshake is { Status: "MESSAGE_READ" };
        var selectionEvidence = trace.Mode == "ManifestV1"
            ? (manifestSelection is null ? "UNKNOWN" : "OBSERVED")
            : (continuationObserved ? "OBSERVED_CONTINUATION" : "UNKNOWN");

        return new
        {
            task = "B0-A-TRACE-01",
            mode,
            observed_at_utc = DateTimeOffset.UtcNow,
            endpoint = endpoint.ToString(),
            listener = new
            {
                connection_index = trace.ConnectionIndex,
                remote_endpoint = trace.RemoteEndPoint,
                started_at_utc = trace.StartedAtUtc,
                duration_ms = Math.Round(trace.DurationMs, 3),
                outcome = trace.Outcome,
                negotiation_mode = trace.Mode,
                accepted = trace.Accepted,
                should_close = trace.ShouldClose,
                selected_version = trace.SelectedVersion,
                negotiated_capabilities = trace.NegotiatedCapabilities is { } caps
                    ? $"0x{caps:X}"
                    : null,
                error_type = trace.ErrorType,
                sanitized_error = trace.SanitizedError,
                handshake_capture_truncated = trace.HandshakeCaptureTruncated,
            },
            T01_identification = new
            {
                client_handshake_hex = TraceEvidence.Hex(trace.ClientHandshakeBytes),
                bolt_magic_hex = TraceEvidence.Hex(magic),
                bolt_magic_valid = magicValid,
                proposal_count = proposals.Count,
                proposals = proposals.Select(p => new
                {
                    slot = p.Slot,
                    raw_hex = p.RawHex,
                    kind = p.Kind,
                    major = p.Major,
                    minor = p.Minor,
                    range = p.Range,
                    version = p.Version,
                    expanded_versions = p.ExpandedVersions,
                    decode_error = p.DecodeError,
                }).ToArray(),
                proposal_order = string.Join(", ", proposals.Select(p =>
                    p.Kind == "MANIFEST_V1" ? "MANIFEST_V1" : p.Version ?? p.Kind)),
                manifest_v1_offered = manifestOffered,
            },
            T02_server_negotiation = new
            {
                server_supported_versions = options.SupportedVersions.Select(v => v.ToString()).ToArray(),
                server_manifest_v1_enabled = options.ManifestV1Enabled,
                server_offered_capabilities = $"0x{options.OfferedCapabilities:X}",
                server_offer_ranges = offerRanges.Select(r => r.ToString()).ToArray(),
                candidate_versions = candidateVersions,
                manifest_advertisement = manifestOffer,
                chosen_version_or_rejection = trace.Accepted == true
                    ? trace.SelectedVersion
                    : trace.Outcome,
                accepted = trace.Accepted,
                should_close = trace.ShouldClose,
                exact_server_response_hex = TraceEvidence.Hex(trace.ServerResponseBytes),
                acceptance_closure_decision = trace.Accepted == true
                    ? "ACCEPTED — handshake negotiated; no message/session support claimed."
                    : trace.Outcome,
                replay = new
                {
                    outcome = replay.Outcome,
                    negotiation_mode = replay.Mode,
                    accepted = replay.Accepted,
                    should_close = replay.ShouldClose,
                    selected_version = replay.SelectedVersion,
                    negotiated_capabilities = replay.NegotiatedCapabilities is { } replayCaps
                        ? $"0x{replayCaps:X}"
                        : null,
                    exact_server_response_hex = TraceEvidence.Hex(replay.ServerResponseBytes),
                    response_matches_live_listener = replay.MatchesLiveResponse,
                    error_type = replay.ErrorType,
                    sanitized_error = replay.SanitizedError,
                },
            },
            T03_client_selection = new
            {
                negotiation_mode = trace.Mode,
                selection_evidence = selectionEvidence,
                manifest_selection = manifestSelection,
                legacy_continuation = new
                {
                    selected_version_response_hex = trace.Mode == "Legacy"
                        ? TraceEvidence.Hex(trace.ServerResponseBytes)
                        : null,
                    first_post_handshake_status = postHandshake?.Status,
                    driver_continued_after_selected_version = continuationObserved,
                    note = trace.Mode == "Legacy"
                        ? "Continuation is OBSERVED only when a valid first framed message follows the four-byte selection response."
                        : null,
                },
                server_side_negotiation_completed = trace.Accepted == true,
                client_side_acceptance = continuationObserved
                    ? "OBSERVED_CONTINUATION"
                    : "UNKNOWN",
            },
            T04_first_post_handshake = new
            {
                status = postHandshake?.Status ?? "NOT_ATTEMPTED",
                framing_valid = postHandshake?.FramingValid,
                message_body_length = postHandshake?.MessageBodyLength,
                bytes_read_after_handshake = postHandshake?.BytesReadAfterHandshake,
                structure_marker_hex = postHandshake?.StructureMarkerHex,
                structure_field_count = postHandshake?.StructureFieldCount,
                message_signature_hex = postHandshake?.SignatureHex,
                message_signature_name = postHandshake?.SignatureName,
                additional_messages = postHandshake?.AdditionalMessages.Select(m => new
                {
                    status = m.Status,
                    message_body_length = m.MessageBodyLength,
                    structure_marker_hex = m.StructureMarkerHex,
                    structure_field_count = m.StructureFieldCount,
                    message_signature_hex = m.SignatureHex,
                    message_signature_name = m.SignatureName,
                }).ToArray(),
                client_closed_after_first_message = postHandshake?.ClientClosedAfterFirstMessage,
                client_waiting_after_first_message = postHandshake?.ClientWaitingAfterFirstMessage,
                extra_buffered_bytes_after_first_message = postHandshake?.ExtraBufferedBytesAfterFirstMessage,
                error_type = postHandshake?.ErrorType,
                sanitized_error = postHandshake?.SanitizedError,
                raw_post_handshake_body_persisted = false,
            },
            driver_observation = new
            {
                completed_successfully = driver.CompletedSuccessfully,
                timed_out = driver.TimedOut,
                exception_type = driver.ExceptionType,
                sanitized_message = driver.SanitizedMessage,
                elapsed_ms = Math.Round(driver.ElapsedMs, 3),
            },
            compatibility_non_claims = new[]
            {
                "A completed handshake does not prove Neo4j server compatibility.",
                "A completed handshake does not prove message/session compatibility.",
                "The listener implements no HELLO, LOGON, RUN, PULL, authentication or query execution.",
                "Any driver exception after negotiation is expected for this probe.",
            },
        };
    }
}

/// <summary>
/// A duplex in-memory stream with independent read and write positions, used only to replay
/// captured client handshake bytes through the production negotiator.
/// </summary>
internal sealed class ReplayDuplexStream : Stream
{
    private readonly byte[] _input;
    private readonly List<byte> _output = new();
    private int _readPosition;

    internal ReplayDuplexStream(byte[] input)
    {
        _input = input;
    }

    internal byte[] WrittenBytes => _output.ToArray();

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _readPosition;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var remaining = _input.Length - _readPosition;
        if (remaining <= 0 || buffer.Length == 0)
        {
            return 0;
        }

        var count = Math.Min(buffer.Length, remaining);
        _input.AsSpan(_readPosition, count).CopyTo(buffer);
        _readPosition += count;
        return count;
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override void Write(byte[] buffer, int offset, int count) =>
        Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        foreach (var value in buffer)
        {
            _output.Add(value);
        }
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        Write(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>
/// Writes reproducible evidence files when <c>BOLTSHARP_TRACE01_EVIDENCE_DIR</c> is set. Evidence
/// contains only handshake bytes and safe structural metadata; post-handshake bodies are never
/// persisted.
/// </summary>
internal static class TraceEvidence
{
    internal const string EvidenceDirectoryVariable = "BOLTSHARP_TRACE01_EVIDENCE_DIR";

    internal static string? EvidenceDirectory =>
        Environment.GetEnvironmentVariable(EvidenceDirectoryVariable);

    internal static string Hex(ReadOnlySpan<byte> bytes) =>
        string.Join(' ', bytes.ToArray().Select(b => b.ToString("X2")));

    internal static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var singleLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 400 ? singleLine : singleLine[..400];
    }

    internal static void WriteJson(string fileName, object document)
    {
        var directory = EvidenceDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(directory, fileName), json + Environment.NewLine);
    }

    internal static void WriteHex(string fileName, ReadOnlySpan<byte> bytes)
    {
        var directory = EvidenceDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), Hex(bytes) + Environment.NewLine);
    }
}
