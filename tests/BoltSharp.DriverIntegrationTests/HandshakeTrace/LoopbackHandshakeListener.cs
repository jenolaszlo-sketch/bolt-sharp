using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using BoltSharp.Framing;
using BoltSharp.Handshake;

namespace BoltSharp.DriverIntegrationTests.HandshakeTrace;

/// <summary>
/// B0-A-TRACE-01 bounds for the disposable loopback listener. Every value is deliberately small:
/// this is a test-only reconnaissance endpoint, not a server implementation.
/// </summary>
internal sealed class LoopbackListenerLimits
{
    /// <summary>Maximum number of TCP connections accepted by one listener instance.</summary>
    internal int MaxConnections { get; init; } = 4;

    /// <summary>Accept backlog; the listener never exposes a public interface.</summary>
    internal int AcceptBacklog { get; init; } = 4;

    /// <summary>Maximum bytes captured for the handshake exchange (client and server separately).</summary>
    internal int MaxHandshakeCaptureBytes { get; init; } = 4_096;

    /// <summary>Maximum number of manifest version ranges accepted by the negotiator.</summary>
    internal int MaxManifestVersions { get; init; } = 32;

    /// <summary>Maximum reassembled size of the first post-handshake message.</summary>
    internal int MaxObservedMessageBytes { get; init; } = 64 * 1024;

    /// <summary>Maximum lifetime of one accepted connection, including observation.</summary>
    internal TimeSpan ConnectionLifetime { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum time to wait for the client to complete its handshake before closing the connection,
    /// independent of the overall connection lifetime (consolidated from the parallel
    /// implementation B0-A-TRACE-01-B, test N06).
    /// </summary>
    internal TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long to wait for the first post-handshake message before closing.</summary>
    internal TimeSpan FirstMessageObservationTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long to watch the socket after the first message to classify client behaviour.</summary>
    internal TimeSpan PostMessageObservationWindow { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Maximum number of additional framed messages to observe after the first.</summary>
    internal int MaxAdditionalObservedMessages { get; init; } = 4;

    /// <summary>Whether to attempt to observe the first post-handshake message.</summary>
    internal bool ObserveFirstMessage { get; init; } = true;
}

/// <summary>Safe structural metadata about one framed post-handshake message. No body is retained.</summary>
internal sealed class FramedMessageObservation
{
    /// <summary>MESSAGE_READ, CLEAN_EOF, TIMED_OUT, TRUNCATED, FORMAT_ERROR or CANCELLED.</summary>
    internal string Status { get; set; } = "NOT_ATTEMPTED";

    /// <summary>Reassembled message length, or null when no complete framed message was read.</summary>
    internal int? MessageBodyLength { get; set; }

    /// <summary>First PackStream structure marker byte, if it was a struct marker.</summary>
    internal string? StructureMarkerHex { get; set; }

    /// <summary>Number of fields declared by the PackStream tiny-struct marker, if applicable.</summary>
    internal int? StructureFieldCount { get; set; }

    /// <summary>Raw message signature byte (the second body byte for a tiny struct).</summary>
    internal string? SignatureHex { get; set; }

    /// <summary>A known Bolt message name for the signature, or UNKNOWN.</summary>
    internal string? SignatureName { get; set; }
}

/// <summary>Safe structural metadata about the first post-handshake message. No body is retained.</summary>
internal sealed class PostHandshakeObservation
{
    /// <summary>One of NOT_ATTEMPTED, MESSAGE_READ, CLEAN_EOF, TIMED_OUT, TRUNCATED, FORMAT_ERROR, CANCELLED, ERROR.</summary>
    internal string Status { get; set; } = "NOT_ATTEMPTED";

    /// <summary>Total bytes read from the client after the handshake completed.</summary>
    internal long BytesReadAfterHandshake { get; set; }

    /// <summary>Reassembled message length, or null when no complete framed message was read.</summary>
    internal int? MessageBodyLength { get; set; }

    /// <summary>Whether the observed message used valid chunked framing.</summary>
    internal bool? FramingValid { get; set; }

    /// <summary>First PackStream structure marker byte, if it was a struct marker.</summary>
    internal string? StructureMarkerHex { get; set; }

    /// <summary>Number of fields declared by the PackStream tiny-struct marker, if applicable.</summary>
    internal int? StructureFieldCount { get; set; }

    /// <summary>Raw message signature byte (the second body byte for a tiny struct).</summary>
    internal string? SignatureHex { get; set; }

    /// <summary>A known Bolt message name for the signature, or UNKNOWN.</summary>
    internal string? SignatureName { get; set; }

    /// <summary>True when the client closed the connection after its first message.</summary>
    internal bool? ClientClosedAfterFirstMessage { get; set; }

    /// <summary>True when the client still had the connection open and was waiting at observation end.</summary>
    internal bool? ClientWaitingAfterFirstMessage { get; set; }

    /// <summary>Bytes already buffered by the socket after the first message, if any.</summary>
    internal int? ExtraBufferedBytesAfterFirstMessage { get; set; }

    /// <summary>Exception type from framing or observation, if any.</summary>
    internal string? ErrorType { get; set; }

    /// <summary>Sanitized exception message from framing or observation, if any.</summary>
    internal string? SanitizedError { get; set; }

    /// <summary>Safe structural metadata for additional framed messages observed after the first.</summary>
    internal List<FramedMessageObservation> AdditionalMessages { get; } = new();
}

/// <summary>
/// One connection's sanitized trace. Raw bytes are captured only through completion of the Bolt
/// handshake; no post-handshake body is stored.
/// </summary>
internal sealed class HandshakeTrace
{
    internal int ConnectionIndex { get; init; }

    internal string RemoteEndPoint { get; init; } = string.Empty;

    internal DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>PENDING, ACCEPTED, REJECTED, FORMAT_ERROR, TRUNCATED, PROTOCOL_ERROR, CANCELLED, CLOSED, ERROR.</summary>
    internal string Outcome { get; set; } = "PENDING";

    internal string? Mode { get; set; }

    internal bool? Accepted { get; set; }

    internal bool? ShouldClose { get; set; }

    internal string? SelectedVersion { get; set; }

    internal ulong? NegotiatedCapabilities { get; set; }

    internal byte[] ClientHandshakeBytes { get; set; } = Array.Empty<byte>();

    internal byte[] ServerResponseBytes { get; set; } = Array.Empty<byte>();

    internal bool HandshakeCaptureTruncated { get; set; }

    internal string? ErrorType { get; set; }

    internal string? SanitizedError { get; set; }

    internal PostHandshakeObservation? PostHandshake { get; set; }

    internal double DurationMs { get; set; }
}

/// <summary>
/// A bounded, test-only TCP listener on <c>127.0.0.1</c> with an OS-assigned ephemeral port. It
/// delegates the actual negotiation to <see cref="BoltHandshakeNegotiator"/> so the production
/// B2-B algorithm is exercised rather than reimplemented. It never listens on a LAN interface and
/// is not a production transport.
/// </summary>
internal sealed class LoopbackHandshakeListener : IAsyncDisposable
{
    private static readonly TimeSpan HandlerShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly TcpListener _listener;
    private readonly BoltHandshakeNegotiator _negotiator;
    private readonly LoopbackListenerLimits _limits;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource<HandshakeTrace> _firstTrace =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private readonly List<Task> _handlers = new();
    private readonly List<TcpClient> _clients = new();
    private readonly List<HandshakeTrace> _traces = new();
    private readonly Task _acceptLoop;
    private int _accepted;
    private int _disposed;

    private LoopbackHandshakeListener(BoltHandshakeOptions handshakeOptions, LoopbackListenerLimits limits)
    {
        _limits = limits;
        _negotiator = new BoltHandshakeNegotiator(
            handshakeOptions,
            new BoltHandshakeLimits(limits.MaxManifestVersions, limits.MaxHandshakeCaptureBytes));

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start(limits.AcceptBacklog);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        LocalEndPoint = (IPEndPoint)_listener.LocalEndpoint;
        _acceptLoop = AcceptLoopAsync();
    }

    internal static LoopbackHandshakeListener Start(
        BoltHandshakeOptions handshakeOptions,
        LoopbackListenerLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(handshakeOptions);
        return new LoopbackHandshakeListener(handshakeOptions, limits ?? new LoopbackListenerLimits());
    }

    internal int Port { get; }

    internal IPEndPoint LocalEndPoint { get; }

    /// <summary>The direct, loopback-only Bolt URI for the official driver.</summary>
    internal Uri Uri => new(string.Create(
        CultureInfo.InvariantCulture,
        $"bolt://127.0.0.1:{Port}"));

    internal Task<HandshakeTrace> FirstTrace => _firstTrace.Task;

    internal async Task<HandshakeTrace> WaitForFirstTraceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        await _firstTrace.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);

    internal IReadOnlyList<HandshakeTrace> Traces
    {
        get
        {
            lock (_gate)
            {
                return _traces.ToArray();
            }
        }
    }

    /// <summary>Requests cancellation of the accept loop and every in-flight connection.</summary>
    internal void Cancel()
    {
        try
        {
            _shutdown.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Cancellation after disposal is a no-op.
        }
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                if (Volatile.Read(ref _accepted) >= _limits.MaxConnections)
                {
                    break;
                }

                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException) when (_shutdown.IsCancellationRequested)
                {
                    break;
                }

                var index = Interlocked.Increment(ref _accepted);
                lock (_gate)
                {
                    _clients.Add(client);
                }

                var handler = HandleConnectionAsync(index, client);
                lock (_gate)
                {
                    _handlers.Add(handler);
                }
            }
        }
        finally
        {
            try
            {
                _listener.Stop();
            }
            catch (ObjectDisposedException)
            {
                // Already stopped.
            }
        }
    }

    private async Task HandleConnectionAsync(int index, TcpClient client)
    {
        var started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var trace = new HandshakeTrace
        {
            ConnectionIndex = index,
            StartedAtUtc = started,
            RemoteEndPoint = SafeRemoteEndPoint(client),
        };

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        lifetime.CancelAfter(_limits.ConnectionLifetime);

        var stream = client.GetStream();
        using var tracing = new HandshakeTracingStream(
            stream,
            _limits.MaxHandshakeCaptureBytes,
            leaveOpen: true);
        using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        handshakeTimeout.CancelAfter(_limits.HandshakeTimeout);

        try
        {
            client.NoDelay = true;
            var result = await _negotiator.NegotiateAsync(tracing, handshakeTimeout.Token).ConfigureAwait(false);

            trace.Mode = result.Mode.ToString();
            trace.Accepted = result.Accepted;
            trace.ShouldClose = result.ShouldClose;
            trace.SelectedVersion = result.SelectedVersion?.ToString();
            trace.NegotiatedCapabilities = result.NegotiatedCapabilities;
            trace.Outcome = result.Accepted ? "ACCEPTED" : "REJECTED";

            tracing.StopCapture();

            if (result.Accepted && _limits.ObserveFirstMessage)
            {
                trace.PostHandshake = await ObserveFirstMessageAsync(
                    tracing,
                    client,
                    lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (BoltHandshakeFormatException ex)
        {
            trace.Outcome = "FORMAT_ERROR";
            trace.ErrorType = ex.GetType().FullName;
            trace.SanitizedError = Sanitize(ex.Message);
        }
        catch (BoltHandshakeTruncatedException ex)
        {
            trace.Outcome = "TRUNCATED";
            trace.ErrorType = ex.GetType().FullName;
            trace.SanitizedError = Sanitize(ex.Message);
        }
        catch (BoltHandshakeProtocolException ex)
        {
            trace.Outcome = "PROTOCOL_ERROR";
            trace.ErrorType = ex.GetType().FullName;
            trace.SanitizedError = Sanitize(ex.Message);
        }
        catch (OperationCanceledException) when (handshakeTimeout.IsCancellationRequested && !lifetime.IsCancellationRequested)
        {
            trace.Outcome = "HANDSHAKE_TIMEOUT";
            trace.ErrorType = typeof(OperationCanceledException).FullName;
            trace.SanitizedError =
                "The client did not complete its handshake within the configured handshake timeout.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            trace.Outcome = _shutdown.IsCancellationRequested ? "CANCELLED" : "LIFETIME_TIMEOUT";
            trace.ErrorType = typeof(OperationCanceledException).FullName;
            trace.SanitizedError = _shutdown.IsCancellationRequested
                ? "The listener cancelled the pending negotiation."
                : "The per-connection lifetime elapsed during negotiation or observation.";
        }
        catch (IOException ex)
        {
            trace.Outcome = "CLOSED";
            trace.ErrorType = ex.GetType().FullName;
            trace.SanitizedError = Sanitize(ex.Message);
        }
        catch (Exception ex)
        {
            trace.Outcome = "ERROR";
            trace.ErrorType = ex.GetType().FullName;
            trace.SanitizedError = Sanitize(ex.Message);
        }
        finally
        {
            tracing.StopCapture();
            trace.ClientHandshakeBytes = tracing.CapturedClientHandshake;
            trace.ServerResponseBytes = tracing.CapturedServerResponse;
            trace.HandshakeCaptureTruncated = tracing.CaptureTruncated;
            if (trace.PostHandshake is { } postHandshake)
            {
                postHandshake.BytesReadAfterHandshake = tracing.PostHandshakeBytesRead;
            }

            stopwatch.Stop();
            trace.DurationMs = stopwatch.Elapsed.TotalMilliseconds;

            lock (_gate)
            {
                _clients.Remove(client);
                _traces.Add(trace);
            }

            if (index == 1)
            {
                _firstTrace.TrySetResult(trace);
            }

            try
            {
                client.Close();
            }
            catch (ObjectDisposedException)
            {
                // Already closed.
            }
        }
    }

    private async Task<PostHandshakeObservation> ObserveFirstMessageAsync(
        HandshakeTracingStream tracing,
        TcpClient client,
        CancellationToken cancellationToken)
    {
        var observation = new PostHandshakeObservation();
        var reader = new BoltMessageReader(
            tracing,
            new BoltFramingLimits(_limits.MaxObservedMessageBytes));

        using var observationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        observationTimeout.CancelAfter(_limits.FirstMessageObservationTimeout);

        try
        {
            var body = await reader.ReadMessageAsync(observationTimeout.Token).ConfigureAwait(false);
            if (body is null)
            {
                observation.Status = "CLEAN_EOF";
                return observation;
            }

            observation.Status = "MESSAGE_READ";
            observation.FramingValid = true;
            observation.MessageBodyLength = body.Length;
            var structure = new FramedMessageObservation();
            IdentifyStructure(body, structure);
            observation.StructureMarkerHex = structure.StructureMarkerHex;
            observation.StructureFieldCount = structure.StructureFieldCount;
            observation.SignatureHex = structure.SignatureHex;
            observation.SignatureName = structure.SignatureName;

            await ObserveAdditionalMessagesAsync(reader, observation, cancellationToken)
                .ConfigureAwait(false);
            await ClassifyClientBehaviourAsync(client, observation, cancellationToken).ConfigureAwait(false);
            return observation;
        }
        catch (BoltFramingTruncatedException ex)
        {
            observation.Status = "TRUNCATED";
            observation.FramingValid = false;
            observation.ErrorType = ex.GetType().FullName;
            observation.SanitizedError = Sanitize(ex.Message);
        }
        catch (BoltFramingFormatException ex)
        {
            observation.Status = "FORMAT_ERROR";
            observation.FramingValid = false;
            observation.ErrorType = ex.GetType().FullName;
            observation.SanitizedError = Sanitize(ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            observation.Status = "TIMED_OUT";
            observation.FramingValid = null;
            observation.SanitizedError =
                "No complete first message arrived within the configured observation window.";
        }
        catch (OperationCanceledException)
        {
            observation.Status = "CANCELLED";
            observation.FramingValid = null;
            observation.SanitizedError = "The connection lifetime elapsed while observing.";
        }
        catch (IOException ex)
        {
            observation.Status = "CLEAN_EOF";
            observation.FramingValid = false;
            observation.ErrorType = ex.GetType().FullName;
            observation.SanitizedError = Sanitize(ex.Message);
        }
        catch (Exception ex)
        {
            observation.Status = "ERROR";
            observation.FramingValid = false;
            observation.ErrorType = ex.GetType().FullName;
            observation.SanitizedError = Sanitize(ex.Message);
        }

        return observation;
    }

    /// <summary>
    /// Observes up to <see cref="LoopbackListenerLimits.MaxAdditionalObservedMessages"/> further
    /// framed messages, recording only safe structural metadata (length, struct marker, signature).
    /// Message bodies are never retained, logged or persisted.
    /// </summary>
    private async Task ObserveAdditionalMessagesAsync(
        BoltMessageReader reader,
        PostHandshakeObservation observation,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < _limits.MaxAdditionalObservedMessages; index++)
        {
            var message = new FramedMessageObservation();
            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            window.CancelAfter(_limits.PostMessageObservationWindow);
            try
            {
                var body = await reader.ReadMessageAsync(window.Token).ConfigureAwait(false);
                if (body is null)
                {
                    message.Status = "CLEAN_EOF";
                    observation.AdditionalMessages.Add(message);
                    return;
                }

                message.Status = "MESSAGE_READ";
                message.MessageBodyLength = body.Length;
                IdentifyStructure(body, message);
                observation.AdditionalMessages.Add(message);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                message.Status = "TIMED_OUT";
                observation.AdditionalMessages.Add(message);
                return;
            }
            catch (OperationCanceledException)
            {
                message.Status = "CANCELLED";
                observation.AdditionalMessages.Add(message);
                return;
            }
            catch (BoltFramingTruncatedException ex)
            {
                message.Status = "TRUNCATED";
                message.SignatureName = "UNKNOWN";
                observation.AdditionalMessages.Add(message);
                observation.ErrorType ??= ex.GetType().FullName;
                return;
            }
            catch (BoltFramingFormatException ex)
            {
                message.Status = "FORMAT_ERROR";
                message.SignatureName = "UNKNOWN";
                observation.AdditionalMessages.Add(message);
                observation.ErrorType ??= ex.GetType().FullName;
                return;
            }
            catch (IOException)
            {
                message.Status = "CLEAN_EOF";
                observation.AdditionalMessages.Add(message);
                return;
            }
        }
    }

    private async Task ClassifyClientBehaviourAsync(
        TcpClient client,
        PostHandshakeObservation observation,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_limits.PostMessageObservationWindow, cancellationToken).ConfigureAwait(false);
            var socket = client.Client;
            if (!socket.Poll(0, SelectMode.SelectRead))
            {
                observation.ClientWaitingAfterFirstMessage = true;
                observation.ClientClosedAfterFirstMessage = false;
                return;
            }

            var available = socket.Available;
            if (available == 0)
            {
                observation.ClientClosedAfterFirstMessage = true;
                observation.ClientWaitingAfterFirstMessage = false;
                return;
            }

            observation.ClientClosedAfterFirstMessage = false;
            observation.ClientWaitingAfterFirstMessage = true;
            observation.ExtraBufferedBytesAfterFirstMessage = available;
        }
        catch (SocketException)
        {
            observation.ClientClosedAfterFirstMessage = true;
            observation.ClientWaitingAfterFirstMessage = false;
        }
        catch (ObjectDisposedException)
        {
            observation.ClientClosedAfterFirstMessage = true;
            observation.ClientWaitingAfterFirstMessage = false;
        }
        catch (OperationCanceledException)
        {
            // The caller classifies the overall cancellation.
        }
    }

    private static void IdentifyStructure(byte[] body, FramedMessageObservation observation)
    {
        // Do not retain or expose the body. Only a tiny-struct header and signature are recorded.
        if (body.Length < 2 || (body[0] & 0xF0) != 0xB0)
        {
            observation.SignatureName = "UNKNOWN";
            return;
        }

        observation.StructureMarkerHex = $"0x{body[0]:X2}";
        observation.StructureFieldCount = body[0] & 0x0F;
        observation.SignatureHex = $"0x{body[1]:X2}";
        observation.SignatureName = body[1] switch
        {
            0x01 => "HELLO",
            0x02 => "GOODBYE",
            0x0F => "RESET",
            0x10 => "RUN",
            0x11 => "BEGIN",
            0x12 => "COMMIT",
            0x13 => "ROLLBACK",
            0x2F => "DISCARD",
            0x3F => "PULL",
            0x54 => "TELEMETRY",
            0x66 => "ROUTE",
            0x6A => "LOGON",
            0x6B => "LOGOFF",
            _ => "UNKNOWN",
        };
    }

    private static string SafeRemoteEndPoint(TcpClient client)
    {
        try
        {
            return client.Client.RemoteEndPoint?.ToString() ?? "UNKNOWN";
        }
        catch (SocketException)
        {
            return "UNKNOWN";
        }
        catch (ObjectDisposedException)
        {
            return "UNKNOWN";
        }
    }

    internal static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var singleLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 400 ? singleLine : singleLine[..400];
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Cancel();

        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
            // Already stopped.
        }

        TcpClient[] clients;
        lock (_gate)
        {
            clients = _clients.ToArray();
        }

        foreach (var client in clients)
        {
            try
            {
                client.Close();
            }
            catch (ObjectDisposedException)
            {
                // Already closed.
            }
        }

        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        catch (ObjectDisposedException)
        {
            // Expected during shutdown.
        }
        catch (SocketException)
        {
            // Expected during shutdown.
        }

        Task[] handlers;
        lock (_gate)
        {
            handlers = _handlers.ToArray();
        }

        if (handlers.Length > 0)
        {
            try
            {
                await Task.WhenAll(handlers).WaitAsync(HandlerShutdownTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Every handler is already linked to the shutdown token; report a bounded timeout
                // rather than blocking disposal indefinitely.
            }
        }

        _shutdown.Dispose();
    }
}

/// <summary>
/// A duplex stream wrapper that captures the bytes read and written during the handshake. Once
/// <see cref="StopCapture"/> is called, it only counts post-handshake reads and never stores their
/// content. It never owns or disposes the inner stream.
/// </summary>
internal sealed class HandshakeTracingStream : Stream
{
    private readonly Stream _inner;
    private readonly int _maxCaptureBytes;
    private readonly bool _leaveOpen;
    private readonly MemoryStream _clientCapture = new();
    private readonly MemoryStream _serverCapture = new();
    private bool _captureStopped;
    private long _postHandshakeBytesRead;

    internal HandshakeTracingStream(Stream inner, int maxCaptureBytes, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCaptureBytes);
        _inner = inner;
        _maxCaptureBytes = maxCaptureBytes;
        _leaveOpen = leaveOpen;
    }

    internal byte[] CapturedClientHandshake => _clientCapture.ToArray();

    internal byte[] CapturedServerResponse => _serverCapture.ToArray();

    internal bool CaptureTruncated { get; private set; }

    internal long PostHandshakeBytesRead => Interlocked.Read(ref _postHandshakeBytesRead);

    internal void StopCapture()
    {
        _captureStopped = true;
    }

    private void RecordRead(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        if (_captureStopped)
        {
            Interlocked.Add(ref _postHandshakeBytesRead, bytes.Length);
            return;
        }

        if (_clientCapture.Length + bytes.Length > _maxCaptureBytes)
        {
            CaptureTruncated = true;
            _captureStopped = true;
            Interlocked.Add(ref _postHandshakeBytesRead, bytes.Length);
            return;
        }

        _clientCapture.Write(bytes);
    }

    private void RecordWrite(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0 || _captureStopped)
        {
            return;
        }

        if (_serverCapture.Length + bytes.Length > _maxCaptureBytes)
        {
            CaptureTruncated = true;
            _captureStopped = true;
            return;
        }

        _serverCapture.Write(bytes);
    }

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => _inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        RecordRead(buffer.AsSpan(offset, read));
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer);
        RecordRead(buffer[..read]);
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        RecordRead(buffer.Span[..read]);
        return read;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
            .ConfigureAwait(false);
        RecordRead(buffer.AsSpan(offset, read));
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        RecordWrite(buffer.AsSpan(offset, count));
        _inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        RecordWrite(buffer);
        _inner.Write(buffer);
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        RecordWrite(buffer.Span);
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override async Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        RecordWrite(buffer.AsSpan(offset, count));
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken)
            .ConfigureAwait(false);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
