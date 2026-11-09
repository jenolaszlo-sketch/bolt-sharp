using System.Buffers.Binary;
using System.Text;

using BoltSharp.Messages;
using BoltSharp.PackStream;

namespace BoltSharp.DriverInteropTests.Infrastructure;

internal sealed record WireMessage(
    int Index,
    string Name,
    string Description,
    string Hex,
    bool Redacted,
    bool Decoded,
    int Offset = -1,
    int Length = 0);

internal sealed record ProposalSlot(int Slot, string RawHex, string Kind, string? Version, string? Range);

internal sealed class ManifestOfferInfo
{
    internal required string RawHex { get; init; }

    internal required ulong RangeCount { get; init; }

    internal required IReadOnlyList<string> RangeHex { get; init; }

    internal required ulong Capabilities { get; init; }
}

/// <summary>
/// Assembles a <see cref="TappedConnection"/> byte capture into protocol observations: the
/// handshake preamble and proposals, the server reply (manifest offer or legacy selection), the
/// optional client manifest selection, and the ordered framed messages in both directions.
/// Message labels and sanitized descriptions use the accepted B3-A codec purely as a readability
/// aid; the primary evidence artifact is the raw hex. LOGON payloads are redacted and never
/// stored. The parser implements the Bolt framing rules itself and reports rather than silently
/// tolerating malformed captures.
/// </summary>
internal sealed class WireTranscript
{
    private static readonly Dictionary<byte, string> ClientSignatures = new()
    {
        [0x01] = "HELLO",
        [0x6A] = "LOGON",
        [0x6B] = "LOGOFF",
        [0x54] = "TELEMETRY",
        [0x02] = "GOODBYE",
        [0x0F] = "RESET",
        [0x10] = "RUN",
        [0x11] = "BEGIN",
        [0x12] = "COMMIT",
        [0x13] = "ROLLBACK",
        [0x2F] = "DISCARD",
        [0x3F] = "PULL",
        [0x66] = "ROUTE",
    };

    private static readonly Dictionary<byte, string> ServerSignatures = new()
    {
        [0x70] = "SUCCESS",
        [0x71] = "RECORD",
        [0x7E] = "IGNORED",
        [0x7F] = "FAILURE",
    };

    private static readonly byte[] ManifestMarker = { 0x00, 0x00, 0x01, 0xFF };

    private WireTranscript()
    {
    }

    internal required byte[] ClientToServer { get; init; }

    internal required byte[] ServerToClient { get; init; }

    internal required IReadOnlyList<ProposalSlot> Proposals { get; init; }

    internal required string ClientHandshakeHex { get; init; }

    internal string? ClientManifestSelectionHex { get; init; }

    internal string? ClientManifestSelectionVersion { get; init; }

    internal ulong? ClientManifestSelectionCapabilities { get; init; }

    internal string? ServerManifestOfferHex { get; init; }

    internal ManifestOfferInfo? ServerManifestOffer { get; init; }

    internal string? ServerLegacyReplyHex { get; init; }

    internal required IReadOnlyList<WireMessage> ClientMessages { get; init; }

    internal required IReadOnlyList<WireMessage> ServerMessages { get; init; }

    internal required IReadOnlyList<TappedChunk> Chunks { get; init; }

    internal string? ParseError { get; init; }

    internal int CountClient(string name) => ClientMessages.Count(m => m.Name == name);

    internal int CountServer(string name) => ServerMessages.Count(m => m.Name == name);

    internal WireMessage? FirstClient(string name) => ClientMessages.FirstOrDefault(m => m.Name == name);

    internal WireMessage? FirstServer(string name) => ServerMessages.FirstOrDefault(m => m.Name == name);

    internal static WireTranscript Build(TappedConnection connection)
    {
        var chunks = connection.Chunks.ToArray();
        var c2s = Concatenate(chunks, TapDirection.ClientToServer);
        var s2c = Concatenate(chunks, TapDirection.ServerToClient);
        var parseError = (string?)null;

        // Server reply first: manifest offer or legacy 4-byte selection.
        string? offerHex = null;
        ManifestOfferInfo? offer = null;
        string? legacyHex = null;
        var serverOffset = 0;
        if (s2c.Length >= 4 && s2c.AsSpan(0, 4).SequenceEqual(ManifestMarker))
        {
            var cursor = 4;
            var count = ReadVarInt(s2c, ref cursor);
            var rangeWords = new List<string>();
            var rangeEnd = cursor + (int)count * 4;
            while (cursor < rangeEnd && cursor + 4 <= s2c.Length)
            {
                rangeWords.Add(Hex(s2c.AsSpan(cursor, 4)));
                cursor += 4;
            }

            var capabilities = cursor < s2c.Length ? ReadVarInt(s2c, ref cursor) : 0;
            offerHex = Hex(s2c.AsSpan(0, cursor));
            legacyHex = null;
            offer = new ManifestOfferInfo
            {
                RawHex = offerHex,
                RangeCount = count,
                RangeHex = rangeWords,
                Capabilities = capabilities,
            };
            serverOffset = cursor;
        }
        else if (s2c.Length >= 4)
        {
            legacyHex = Hex(s2c.AsSpan(0, 4));
            serverOffset = 4;
        }
        else
        {
            parseError = "server stream shorter than the handshake reply";
        }

        // Client preamble and optional manifest selection.
        var clientOffset = 0;
        var proposals = new List<ProposalSlot>();
        string clientHandshakeHex;
        string? selectionHex = null;
        string? selectionVersion = null;
        ulong? selectionCapabilities = null;
        if (c2s.Length >= 20)
        {
            clientHandshakeHex = HexSpaced(c2s.AsSpan(0, 20));
            for (var slot = 0; slot < 4; slot++)
            {
                proposals.Add(DecodeProposal(slot, c2s.AsSpan(4 + (slot * 4), 4)));
            }

            clientOffset = 20;
            if (offer is not null && c2s.Length >= 24)
            {
                var versionWord = BinaryPrimitives.ReadUInt32BigEndian(c2s.AsSpan(clientOffset, 4));
                selectionVersion = $"{versionWord & 0xFF}.{(versionWord >> 8) & 0xFF}";
                clientOffset += 4;
                var cursor = clientOffset;
                if (cursor < c2s.Length)
                {
                    selectionCapabilities = ReadVarInt(c2s, ref cursor);
                    selectionHex = Hex(c2s.AsSpan(20, cursor - 20));
                    clientOffset = cursor;
                }
                else
                {
                    parseError = "client manifest selection truncated";
                }
            }
        }
        else
        {
            clientHandshakeHex = HexSpaced(c2s);
            parseError = "client stream shorter than the handshake preamble";
        }

        var clientMessages = ParseFramed(c2s, clientOffset, ClientSignatures, DescribeClient, out var clientFrameError);
        var serverMessages = ParseFramed(s2c, serverOffset, ServerSignatures, DescribeServer, out var serverFrameError);
        parseError ??= clientFrameError ?? serverFrameError;

        return new WireTranscript
        {
            ClientToServer = c2s,
            ServerToClient = s2c,
            Proposals = proposals,
            ClientHandshakeHex = clientHandshakeHex,
            ClientManifestSelectionHex = selectionHex,
            ClientManifestSelectionVersion = selectionVersion,
            ClientManifestSelectionCapabilities = selectionCapabilities,
            ServerManifestOfferHex = offerHex,
            ServerManifestOffer = offer,
            ServerLegacyReplyHex = legacyHex,
            ClientMessages = clientMessages,
            ServerMessages = serverMessages,
            Chunks = chunks,
            ParseError = parseError,
        };
    }

    internal object ToDocument(TappedConnection connection) => new
    {
        connection_id = connection.Id,
        client_side_close = connection.ClientSideClose,
        server_side_close = connection.ServerSideClose,
        client_handshake_hex = ClientHandshakeHex,
        proposals = Proposals.Select(p => new { slot = p.Slot, raw = p.RawHex, kind = p.Kind, version = p.Version, range = p.Range }),
        server_manifest_offer_hex = ServerManifestOfferHex,
        server_manifest_offer = ServerManifestOffer is null ? null : new
        {
            range_count = ServerManifestOffer.RangeCount,
            ranges = ServerManifestOffer.RangeHex,
            capabilities = ServerManifestOffer.Capabilities,
        },
        server_legacy_reply_hex = ServerLegacyReplyHex,
        client_manifest_selection_hex = ClientManifestSelectionHex,
        client_manifest_selection_version = ClientManifestSelectionVersion,
        client_manifest_selection_capabilities = ClientManifestSelectionCapabilities,
        client_messages = ClientMessages.Select(m => new
        {
            index = m.Index,
            name = m.Name,
            decoded = m.Decoded,
            redacted = m.Redacted,
            description = m.Description,
            hex = m.Hex,
        }),
        server_messages = ServerMessages.Select(m => new
        {
            index = m.Index,
            name = m.Name,
            decoded = m.Decoded,
            redacted = m.Redacted,
            description = m.Description,
            hex = m.Hex,
        }),
        chunks = DescribeChunks(),
        parse_error = ParseError,
    };

    /// <summary>
    /// Renders the captured read chunks for evidence. Any client-to-server chunk whose byte range
    /// overlaps a LOGON payload is fully redacted: a read boundary can join HELLO and LOGON in one
    /// chunk, and unredacted authentication payloads must never reach an evidence document.
    /// </summary>
    private object[] DescribeChunks()
    {
        var logonRanges = ClientMessages
            .Where(m => m.Name == "LOGON" && m.Offset >= 0)
            .Select(m => (Start: m.Offset - 2, End: m.Offset + m.Length + 2))
            .ToArray();
        var documents = new List<object>(Chunks.Count);
        long clientOffset = 0;
        foreach (var chunk in Chunks)
        {
            if (chunk.Direction == TapDirection.ClientToServer)
            {
                var start = clientOffset;
                var end = clientOffset + chunk.Bytes.Length;
                clientOffset = end;
                var redact = logonRanges.Any(range => start < range.End && end > range.Start);
                documents.Add(new
                {
                    direction = chunk.Direction.ToString(),
                    at_utc = chunk.AtUtc.ToString("O"),
                    length = chunk.Bytes.Length,
                    redacted = redact,
                    hex = redact
                        ? "<redacted: contains LOGON>"
                        : RenderChunkHex(chunk.Bytes),
                });
            }
            else
            {
                documents.Add(new
                {
                    direction = chunk.Direction.ToString(),
                    at_utc = chunk.AtUtc.ToString("O"),
                    length = chunk.Bytes.Length,
                    redacted = false,
                    hex = RenderChunkHex(chunk.Bytes),
                });
            }
        }

        return documents.ToArray();
    }

    private static string RenderChunkHex(byte[] bytes) =>
        bytes.Length <= 512
            ? Hex(bytes.AsSpan())
            : Hex(bytes.AsSpan(0, 64)) + $"<+{bytes.Length - 64} bytes>";

    // ---- Frame parsing ----

    private static List<WireMessage> ParseFramed(
        byte[] stream,
        int offset,
        Dictionary<byte, string> signatureNames,
        Func<byte[], (string Name, string Description, bool Redacted, bool Decoded)> describe,
        out string? error)
    {
        var messages = new List<WireMessage>();
        error = null;
        var payload = new List<byte>();
        var payloadStart = -1;
        var index = 0;
        while (offset < stream.Length)
        {
            if (offset + 1 >= stream.Length)
            {
                error = $"truncated chunk header at offset {offset}";
                break;
            }

            var chunkLength = (stream[offset] << 8) | stream[offset + 1];
            offset += 2;

            if (chunkLength == 0)
            {
                if (payload.Count == 0)
                {
                    // NOOP keep-alive chunk: not a message.
                    continue;
                }

                var bytes = payload.ToArray();
                var start = payloadStart;
                payload.Clear();
                payloadStart = -1;
                var (name, description, redacted, decoded) = describe(bytes);
                messages.Add(new WireMessage(
                    index++,
                    name,
                    description,
                    redacted ? "<redacted>" : Hex(bytes),
                    redacted,
                    decoded,
                    start,
                    bytes.Length));
                continue;
            }

            if (offset + chunkLength > stream.Length)
            {
                error = $"truncated chunk payload at offset {offset} (length {chunkLength})";
                break;
            }

            if (payload.Count == 0)
            {
                payloadStart = offset;
            }

            payload.AddRange(stream.AsSpan(offset, chunkLength).ToArray());
            offset += chunkLength;
        }

        if (error is null && payload.Count > 0)
        {
            error = "unterminated final message payload";
        }

        return messages;
    }

    private static (string Name, string Description, bool Redacted, bool Decoded) DescribeClient(byte[] payload)
    {
        var name = SignatureName(payload, ClientSignatures);
        try
        {
            var codec = new BoltMessageCodec();
            var request = codec.DecodeRequest(payload);
            return request switch
            {
                HelloRequest hello => ("HELLO", DescribeHello(hello), false, true),
                LogonRequest logon => ("LOGON", DescribeLogon(logon), true, true),
                RunRequest run => ("RUN", DescribeRun(run), false, true),
                PullRequest pull => ("PULL", DescribeCount("PULL", pull.N, pull.Qid), false, true),
                DiscardRequest discard => ("DISCARD", DescribeCount("DISCARD", discard.N, discard.Qid), false, true),
                TelemetryRequest telemetry => ("TELEMETRY", $"TELEMETRY api={telemetry.Api}", false, true),
                ResetRequest => ("RESET", "RESET", false, true),
                GoodbyeRequest => ("GOODBYE", "GOODBYE", false, true),
                LogoffRequest => ("LOGOFF", "LOGOFF", false, true),
                _ => (name, name, false, false),
            };
        }
        catch (Exception)
        {
            // Undecodable request (for example BEGIN/ROUTE, which B3-A deliberately does not
            // implement): keep the signature name and the raw bytes, decoded=false.
            return (name, name, false, false);
        }
    }

    private static (string Name, string Description, bool Redacted, bool Decoded) DescribeServer(byte[] payload)
    {
        var name = SignatureName(payload, ServerSignatures);
        try
        {
            var codec = new BoltMessageCodec();
            var response = codec.DecodeResponse(payload);
            return response switch
            {
                SuccessResponse success => ("SUCCESS", DescribeSuccess(success), false, true),
                RecordResponse record => ("RECORD", DescribeRecord(record), false, true),
                FailureResponse failure => ("FAILURE", DescribeFailure(failure), false, true),
                IgnoredResponse => ("IGNORED", "IGNORED", false, true),
                _ => (name, name, false, false),
            };
        }
        catch (Exception)
        {
            return (name, name, false, false);
        }
    }

    private static string DescribeHello(HelloRequest hello)
    {
        var builder = new StringBuilder("HELLO keys=[");
        builder.Append(string.Join(",", hello.Extra.Entries.Select(e => e.Key)));
        builder.Append(']');
        foreach (var entry in hello.Extra.Entries)
        {
            switch (entry.Key)
            {
                case "user_agent" when entry.Value is PackStreamString agent:
                    builder.Append($" user_agent=\"{agent.Value}\"");
                    break;
                case "routing":
                    if (entry.Value is PackStreamMap routing)
                    {
                        builder.Append(" routing={");
                        builder.Append(string.Join(",", routing.Entries.Select(
                            r => $"{r.Key}=\"{ValueText(r.Value)}\"")));
                        builder.Append('}');
                    }
                    else
                    {
                        builder.Append(" routing=null");
                    }

                    break;
                case "bolt_agent" when entry.Value is PackStreamMap boltAgent:
                    builder.Append(" bolt_agent={");
                    builder.Append(string.Join(",", boltAgent.Entries.Select(
                        a => $"{a.Key}=\"{ValueText(a.Value)}\"")));
                    builder.Append('}');
                    break;
                case "notifications_minimum_severity":
                case "notifications_disabled_classifications":
                    builder.Append($" {entry.Key}={ValueText(entry.Value)}");
                    break;
            }
        }

        return builder.ToString();
    }

    private static string DescribeLogon(LogonRequest logon)
    {
        var builder = new StringBuilder("LOGON keys=[");
        builder.Append(string.Join(",", logon.Auth.Entries.Select(e => e.Key)));
        builder.Append(']');
        if (logon.Auth.Entries.Any(e => e.Key == "scheme"))
        {
            builder.Append($" scheme=\"{logon.RecognizedScheme.ToString().ToLowerInvariant()}\"");
        }

        builder.Append(" principal=<redacted> credentials=<redacted>");
        return builder.ToString();
    }

    private static string DescribeRun(RunRequest run)
    {
        var builder = new StringBuilder($"RUN \"{run.Query}\" parameters={run.Parameters.Entries.Count} entry(ies)");
        builder.Append(" extra={");
        builder.Append(string.Join(",", run.Extra.Entries.Select(e => $"{e.Key}={ValueText(e.Value)}")));
        builder.Append('}');
        return builder.ToString();
    }

    private static string DescribeCount(string name, long n, long? qid) =>
        qid is null ? $"{name} n={n}" : $"{name} n={n} qid={qid}";

    private static string DescribeSuccess(SuccessResponse success)
    {
        var builder = new StringBuilder("SUCCESS {");
        builder.Append(string.Join(",", success.Metadata.Entries.Select(DescribeMetadataEntry)));
        builder.Append('}');
        return builder.ToString();
    }

    private static string DescribeMetadataEntry(KeyValuePair<string, PackStreamValue?> entry) => entry.Key switch
    {
        "hints" when entry.Value is PackStreamMap hints =>
            $"hints={{{string.Join(",", hints.Entries.Select(h => h.Key))}}}",
        "fields" when entry.Value is PackStreamList fields =>
            $"fields=[{string.Join(",", fields.Items.Select(f => ValueText(f)))}]",
        _ => $"{entry.Key}={ValueText(entry.Value)}",
    };

    private static string DescribeFailure(FailureResponse failure) =>
        $"FAILURE {{{string.Join(",", failure.Metadata.Entries.Select(DescribeMetadataEntry))}}}";

    private static string DescribeRecord(RecordResponse record)
    {
        var values = record.Data.Items;
        return $"RECORD [{values.Count} value(s): {string.Join(",", values.Select(TypeName))}]";
    }

    private static string ValueText(PackStreamValue? value) => value switch
    {
        null => "null",
        PackStreamString text => text.Value,
        PackStreamInteger integer => integer.Value.ToString(),
        PackStreamBoolean boolean => boolean.Value.ToString().ToLowerInvariant(),
        PackStreamList list => $"[{list.Items.Count} item(s)]",
        PackStreamMap map => $"{{{map.Entries.Count} entry(ies)}}",
        _ => TypeName(value),
    };

    private static string TypeName(PackStreamValue? value) => value switch
    {
        null => "null",
        PackStreamNode => "Node",
        PackStreamRelationship => "Relationship",
        PackStreamPath => "Path",
        _ => value.GetType().Name,
    };

    // ---- Primitive helpers ----

    private static byte[] Concatenate(IReadOnlyList<TappedChunk> chunks, TapDirection direction)
    {
        var length = chunks.Where(c => c.Direction == direction).Sum(c => c.Bytes.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            if (chunk.Direction != direction)
            {
                continue;
            }

            chunk.Bytes.CopyTo(result, offset);
            offset += chunk.Bytes.Length;
        }

        return result;
    }

    private static ulong ReadVarInt(byte[] stream, ref int offset)
    {
        ulong value = 0;
        var shift = 0;
        while (offset < stream.Length)
        {
            var current = stream[offset++];
            value |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
        }

        return value;
    }

    private static ProposalSlot DecodeProposal(int slot, ReadOnlySpan<byte> word)
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(word);
        var raw = $"{value:X8}";
        if (value == 0)
        {
            return new ProposalSlot(slot, raw, "placeholder", null, null);
        }

        if (value == 0x0000_01FF)
        {
            return new ProposalSlot(slot, raw, "manifest-v1", null, null);
        }

        var major = (int)(value & 0xFF);
        var minor = (int)((value >> 8) & 0xFF);
        var range = (int)((value >> 16) & 0xFF);
        return range > 0
            ? new ProposalSlot(slot, raw, "range", $"{major}.{minor}", $"{major}.{minor - range}..{major}.{minor}")
            : new ProposalSlot(slot, raw, "concrete", $"{major}.{minor}", null);
    }

    private static string SignatureName(byte[] payload, Dictionary<byte, string> names)
    {
        if (payload.Length >= 2 && (payload[0] & 0xF0) == 0xB0)
        {
            return names.TryGetValue(payload[1], out var name) ? name : $"0x{payload[1]:X2}";
        }

        return "not-a-structure";
    }

    internal static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    internal static string HexSpaced(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length * 3);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(bytes[i].ToString("X2"));
        }

        return builder.ToString();
    }
}
