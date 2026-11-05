using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace BoltSharp.Qualification.B0ATRACEQ1;

/// <summary>
/// Reviewer-owned passive Bolt listener, implemented independently of all producer code for
/// B0-A-TRACE-Q1. It reads the Bolt magic and four big-endian proposals, responds according to a
/// policy derived directly from the published Bolt handshake specification (legacy selection, or a
/// Manifest v1 offer with a single 5.8 range and no capabilities), and then parses subsequent
/// chunked messages with its own framing reader. Only structural metadata (frame and body lengths,
/// struct marker, signature byte) is recorded; message bodies are never retained.
/// </summary>
internal sealed class ReviewerBoltListener : IAsyncDisposable
{
    public enum Policy { Legacy58, Manifest58Only }

    private static readonly byte[] BoltMagic = { 0x60, 0x60, 0xB0, 0x17 };

    // Independent specification-derived responses (handshake bytes only).
    private static readonly byte[] Legacy58Response = { 0x00, 0x00, 0x08, 0x05 };
    private static readonly byte[] Manifest58Offer =
        { 0x00, 0x00, 0x01, 0xFF, 0x01, 0x00, 0x00, 0x08, 0x05, 0x00 };

    private readonly TcpListener _listener;
    private readonly Policy _policy;
    private readonly TimeSpan _handshakeTimeout;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task<ReviewerTrace> _trace;

    private ReviewerBoltListener(Policy policy, TimeSpan handshakeTimeout)
    {
        _policy = policy;
        _handshakeTimeout = handshakeTimeout;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start(backlog: 2);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _trace = Task.Run(() => RunAsync(_cts.Token));
    }

    public static ReviewerBoltListener Start(Policy policy, TimeSpan? handshakeTimeout = null) =>
        new(policy, handshakeTimeout ?? TimeSpan.FromSeconds(5));

    public int Port { get; }

    public Task<ReviewerTrace> Trace => _trace;

    /// <summary>Diagnostic phase marker: 1 created, 2 accepting, 3 accepted, 4 preamble read,
    /// 5 responded, 6 observing, 7 complete.</summary>
    public int DebugPhase => Volatile.Read(ref _debugPhase);

    /// <summary>Diagnostic: bytes read after the handshake so far.</summary>
    public int DebugObservedBytes => Volatile.Read(ref _observedBytes);

    /// <summary>Diagnostic: size of the read the loop is currently blocked in (-1 none).</summary>
    public int DebugBlockedRead => Volatile.Read(ref _blockedRead);

    private int _debugPhase;
    private int _observedBytes;
    private int _blockedRead = -1;

    private void MarkPhase(int phase) => Volatile.Write(ref _debugPhase, phase);

    private async Task<ReviewerTrace> RunAsync(CancellationToken cancellationToken)
    {
        var trace = new ReviewerTrace { Policy = _policy.ToString() };
        NetworkStream? stream = null;
        TcpClient? client = null;
        try
        {
            MarkPhase(2);
            client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            MarkPhase(3);
            client.NoDelay = true;
            stream = client.GetStream();

            using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshakeTimeout.CancelAfter(_handshakeTimeout);

            // --- Phase 1: fixed 20-byte preamble (magic + exactly four big-endian proposals).
            MarkPhase(4);
            var preamble = await ReadExactAsync(stream, 20, handshakeTimeout.Token).ConfigureAwait(false);
            trace.Preamble = preamble;
            trace.MagicMatches = preamble.AsSpan(0, 4).SequenceEqual(BoltMagic);
            trace.Proposals = new uint[4];
            for (var slot = 0; slot < 4; slot++)
            {
                trace.Proposals[slot] = BinaryPrimitives.ReadUInt32BigEndian(preamble.AsSpan(4 + (slot * 4), 4));
            }

            // --- Phase 2: respond per policy (own implementation of the specification rules).
            if (_policy == Policy.Manifest58Only)
            {
                await stream.WriteAsync(Manifest58Offer, handshakeTimeout.Token).ConfigureAwait(false);
                await stream.FlushAsync(handshakeTimeout.Token).ConfigureAwait(false);
                trace.ServerResponse = Manifest58Offer;

                // --- Phase 3: client selection, Bolt 4.0 style version + capability VarInt.
                // The offer contains one range and zero capabilities, so the reviewer's decoder
                // expects exactly 4 bytes plus a single-byte VarInt (continuation bit clear).
                var selection = await ReadExactAsync(stream, 5, handshakeTimeout.Token).ConfigureAwait(false);
                trace.ClientSelection = selection;
                trace.SelectionVersionMajor = selection[3];
                trace.SelectionVersionMinor = selection[2];
                trace.SelectionCapabilitiesVarint = selection[4];
                trace.SelectionVarintContinuationClear = (selection[4] & 0x80) == 0;
            }
            else
            {
                // Legacy: the first proposal in preference order that covers 5.8 wins â€” the
                // reviewer's own implementation of the published first-match rule.
                trace.Legacy58WasOffered = trace.Proposals.Any(RangeCovers58);
                await stream.WriteAsync(Legacy58Response, handshakeTimeout.Token).ConfigureAwait(false);
                await stream.FlushAsync(handshakeTimeout.Token).ConfigureAwait(false);
                trace.ServerResponse = Legacy58Response;
            }

            MarkPhase(5);
            trace.Outcome = "HANDSHAKE_COMPLETE";

            MarkPhase(6);
            // --- Phase 4: observe post-handshake chunked messages with an independent frame reader.
            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            window.CancelAfter(TimeSpan.FromMilliseconds(1_500));
            var accumulatedBody = new List<byte>();
            try
            {
                while (trace.Messages.Count < 3)
                {
                    Volatile.Write(ref _blockedRead, 2);
                    var header = await ReadExactAsync(stream, 2, window.Token).ConfigureAwait(false);
                    Volatile.Write(ref _blockedRead, -1);
                    Volatile.Write(ref _observedBytes, Volatile.Read(ref _observedBytes) + 2);
                    var chunkLength = BinaryPrimitives.ReadUInt16BigEndian(header);
                    if (chunkLength == 0)
                    {
                        if (accumulatedBody.Count > 0)
                        {
                            trace.Messages.Add(Describe(accumulatedBody.ToArray()));
                            accumulatedBody.Clear();
                            continue;
                        }

                        // A zero-length chunk with no accumulated payload is a NOOP keep-alive.
                        continue;
                    }

                    Volatile.Write(ref _blockedRead, chunkLength);
                    var payload = await ReadExactAsync(stream, chunkLength, window.Token).ConfigureAwait(false);
                    Volatile.Write(ref _blockedRead, -1);
                    Volatile.Write(ref _observedBytes, Volatile.Read(ref _observedBytes) + chunkLength);
                    accumulatedBody.AddRange(payload);
                }
            }
            catch (OperationCanceledException)
            {
                // The observation window elapsed; whatever was captured stands.
            }

            MarkPhase(7);
            trace.Outcome = trace.Messages.Count >= 2 ? "COMPLETED" : "PARTIAL";
            return trace;
        }
        catch (OperationCanceledException)
        {
            // The handshake window elapsed before the client completed the preamble/selection.
            trace.Outcome = "HANDSHAKE_TIMEOUT";
            return trace;
        }
        catch (IOException ex)
        {
            trace.Outcome = "IO_CLOSED";
            trace.Error = ex.GetType().Name;
            return trace;
        }
        catch (Exception ex)
        {
            trace.Outcome = "ERROR";
            trace.Error = $"{ex.GetType().Name}: {Sanitize(ex.Message)}";
            return trace;
        }
        finally
        {
            stream?.Dispose();
            client?.Dispose();
        }
    }

    private static FramedMessageSummary Describe(byte[] body)
    {
        // Structural metadata only: the body array is discarded when this method returns.
        var summary = new FramedMessageSummary { BodyLength = body.Length, FramingValid = true };
        if (body.Length >= 2 && (body[0] & 0xF0) == 0xB0)
        {
            summary.StructureMarker = body[0];
            summary.StructureFieldCount = body[0] & 0x0F;
            summary.Signature = body[1];
        }

        return summary;
    }

    /// <summary>Independent Bolt 4.3 range decode: does this proposal value cover Bolt 5.8?</summary>
    internal static bool RangeCovers58(uint value)
    {
        var reserved = (value >> 24) & 0xFF;
        var range = (int)((value >> 16) & 0xFF);
        var minor = (int)((value >> 8) & 0xFF);
        var major = (int)(value & 0xFF);
        if (reserved != 0)
        {
            return false;
        }

        return major == 5 && minor >= 8 && minor - range <= 8;
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException($"unexpected end of stream after {offset}/{count} byte(s)");
            }

            offset += read;
        }

        return buffer;
    }

    internal static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var singleLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 200 ? singleLine : singleLine[..200];
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            await _trace.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Bounded shutdown; the trace task always completes via its own error handling.
        }

        _cts.Dispose();
    }
}

/// <summary>Structural summary of one post-handshake message. No body bytes are retained.</summary>
internal sealed class FramedMessageSummary
{
    public int BodyLength { get; set; }

    public bool FramingValid { get; set; }

    public int? StructureMarker { get; set; }

    public int? StructureFieldCount { get; set; }

    public int? Signature { get; set; }

    public string SignatureName =>
        Signature switch
        {
            0x01 => "HELLO",
            0x6A => "LOGON",
            null => "UNKNOWN",
            _ => $"0x{Signature:X2}",
        };
}

/// <summary>
/// Reviewer-owned trace of one connection. Only handshake bytes (preamble, server response,
/// client selection) and structural post-handshake metadata are retained.
/// </summary>
internal sealed class ReviewerTrace
{
    public string Policy { get; set; } = string.Empty;

    public byte[] Preamble { get; set; } = Array.Empty<byte>();

    public bool MagicMatches { get; set; }

    public uint[] Proposals { get; set; } = Array.Empty<uint>();

    public byte[] ServerResponse { get; set; } = Array.Empty<byte>();

    public byte[]? ClientSelection { get; set; }

    public int? SelectionVersionMajor { get; set; }

    public int? SelectionVersionMinor { get; set; }

    public int? SelectionCapabilitiesVarint { get; set; }

    public bool? SelectionVarintContinuationClear { get; set; }

    public bool? Legacy58WasOffered { get; set; }

    public List<FramedMessageSummary> Messages { get; } = new();

    public string Outcome { get; set; } = "PENDING";

    public string? Error { get; set; }

    /// <summary>Handshake bytes read before the first post-handshake message.</summary>
    public int HandshakeByteCount => Preamble.Length + (ClientSelection?.Length ?? 0);
}
