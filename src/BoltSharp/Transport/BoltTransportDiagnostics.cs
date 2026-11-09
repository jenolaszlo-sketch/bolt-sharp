using System.Collections.Concurrent;

namespace BoltSharp.Transport;

/// <summary>The kind of a transport diagnostic event.</summary>
public enum BoltTransportEventKind
{
    /// <summary>The listener bound its endpoint and began accepting.</summary>
    ListenerStarted,

    /// <summary>The listener stopped accepting and shutdown completed (or was bounded).</summary>
    ListenerStopped,

    /// <summary>A connection was accepted by the listener.</summary>
    ConnectionAccepted,

    /// <summary>A connection was closed because the configured capacity was reached.</summary>
    ConnectionRejectedCapacity,

    /// <summary>TLS authentication completed successfully.</summary>
    TlsHandshakeCompleted,

    /// <summary>TLS authentication failed; the connection is closed. Never downgraded.</summary>
    TlsHandshakeFailed,

    /// <summary>The Bolt handshake negotiated a supported version.</summary>
    BoltHandshakeCompleted,

    /// <summary>The Bolt handshake failed or was rejected; the connection is closed.</summary>
    BoltHandshakeFailed,

    /// <summary>A connection completed (clean EOF, GOODBYE, terminal outcome or fault).</summary>
    ConnectionCompleted,

    /// <summary>A connection failed unexpectedly (transport fault classification).</summary>
    ConnectionFailed,

    /// <summary>A request message was processed to completion (responses written).</summary>
    MessageProcessed,
}

/// <summary>An immutable diagnostic event. It carries only transport metadata — never
/// credentials, payloads or raw buffers.</summary>
public sealed record BoltTransportDiagnosticEvent(
    DateTimeOffset TimestampUtc,
    BoltTransportEventKind Kind,
    string ConnectionId,
    string? Detail);

/// <summary>
/// Minimal thread-safe diagnostic surface for the transport: monotonic counters plus an optional
/// event sink. Observer exceptions are contained — a faulty observer can never affect the
/// transport. No credential, payload or raw-buffer data is ever reported.
/// </summary>
public sealed class BoltTransportDiagnostics
{
    private long _connectionsAccepted;
    private long _connectionsRejectedForCapacity;
    private long _connectionsActive;
    private long _connectionsCompleted;
    private long _connectionsFailed;
    private long _tlsHandshakeFailures;
    private long _boltHandshakeFailures;
    private long _messagesProcessed;

    private readonly ConcurrentDictionary<string, byte> _activeConnectionIds = new();

    /// <summary>Optional observer for lifecycle events. Exceptions thrown by an observer are
    /// contained by the transport.</summary>
    public event Action<BoltTransportDiagnosticEvent>? Event;

    /// <summary>Total connections accepted by the listener.</summary>
    public long ConnectionsAccepted => Interlocked.Read(ref _connectionsAccepted);

    /// <summary>Connections closed immediately because the configured capacity was reached.</summary>
    public long ConnectionsRejectedForCapacity => Interlocked.Read(ref _connectionsRejectedForCapacity);

    /// <summary>Currently active (accepted, not yet completed) connections.</summary>
    public long ConnectionsActive => Interlocked.Read(ref _connectionsActive);

    /// <summary>Connections that completed without an unexpected transport fault.</summary>
    public long ConnectionsCompleted => Interlocked.Read(ref _connectionsCompleted);

    /// <summary>Connections that failed with an unexpected transport fault.</summary>
    public long ConnectionsFailed => Interlocked.Read(ref _connectionsFailed);

    /// <summary>Failed TLS authentications.</summary>
    public long TlsHandshakeFailures => Interlocked.Read(ref _tlsHandshakeFailures);

    /// <summary>Failed or rejected Bolt handshakes.</summary>
    public long BoltHandshakeFailures => Interlocked.Read(ref _boltHandshakeFailures);

    /// <summary>Request messages processed to completion.</summary>
    public long MessagesProcessed => Interlocked.Read(ref _messagesProcessed);

    internal void Report(BoltTransportEventKind kind, string connectionId, string? detail)
    {
        switch (kind)
        {
            case BoltTransportEventKind.ConnectionAccepted:
                Interlocked.Increment(ref _connectionsAccepted);
                Interlocked.Increment(ref _connectionsActive);
                break;
            case BoltTransportEventKind.ConnectionRejectedCapacity:
                Interlocked.Increment(ref _connectionsRejectedForCapacity);
                break;
            case BoltTransportEventKind.ConnectionCompleted:
                Interlocked.Decrement(ref _connectionsActive);
                Interlocked.Increment(ref _connectionsCompleted);
                break;
            case BoltTransportEventKind.ConnectionFailed:
                Interlocked.Decrement(ref _connectionsActive);
                Interlocked.Increment(ref _connectionsFailed);
                break;
            case BoltTransportEventKind.TlsHandshakeFailed:
                Interlocked.Increment(ref _tlsHandshakeFailures);
                break;
            case BoltTransportEventKind.BoltHandshakeFailed:
                Interlocked.Increment(ref _boltHandshakeFailures);
                break;
            case BoltTransportEventKind.MessageProcessed:
                Interlocked.Increment(ref _messagesProcessed);
                break;
            default:
                break;
        }

        if (kind == BoltTransportEventKind.ConnectionAccepted)
        {
            _activeConnectionIds.TryAdd(connectionId, 0);
        }
        else if (kind is BoltTransportEventKind.ConnectionCompleted or BoltTransportEventKind.ConnectionFailed)
        {
            _activeConnectionIds.TryRemove(connectionId, out _);
        }

        var observer = Event;
        if (observer is null)
        {
            return;
        }

        try
        {
            observer(new BoltTransportDiagnosticEvent(DateTimeOffset.UtcNow, kind, connectionId, detail));
        }
        catch
        {
            // Contained by design: a faulty observer must not affect the transport.
        }
    }
}
