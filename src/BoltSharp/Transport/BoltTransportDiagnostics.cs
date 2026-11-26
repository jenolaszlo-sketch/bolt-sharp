using System.Collections.Concurrent;

namespace BoltSharp.Transport;

/// <summary>The kind of a transport diagnostic event.</summary>
public enum BoltTransportEventKind
{
    /// <summary>The listener bound its endpoint and began accepting.</summary>
    ListenerStarted,

    /// <summary>The listener stopped accepting and shutdown completed (or was bounded).</summary>
    ListenerStopped,

    /// <summary>
    /// The listener's accept loop observed a transient fault and continued accepting. This is a
    /// listener-level event, not a connection outcome: it is accounting-neutral and never
    /// decrements active connection accounting.
    /// </summary>
    ListenerFault,

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
    private long _listenerFaults;
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

    /// <summary>
    /// Transient accept-loop faults observed by the listener. These are not connection outcomes:
    /// they never affect <see cref="ConnectionsAccepted"/>, <see cref="ConnectionsActive"/>,
    /// <see cref="ConnectionsCompleted"/> or <see cref="ConnectionsFailed"/>.
    /// </summary>
    public long ListenerFaults => Interlocked.Read(ref _listenerFaults);

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
                // The id is the accounting authority: a connection only becomes active (and is
                // only counted as accepted) when its id can be registered.
                if (_activeConnectionIds.TryAdd(connectionId, 0))
                {
                    Interlocked.Increment(ref _connectionsAccepted);
                    Interlocked.Increment(ref _connectionsActive);
                }

                break;
            case BoltTransportEventKind.ConnectionRejectedCapacity:
                Interlocked.Increment(ref _connectionsRejectedForCapacity);
                break;
            case BoltTransportEventKind.ConnectionCompleted:
                // A terminal event decrements active accounting only for a connection that was
                // previously accepted. This keeps ConnectionsActive non-negative and the
                // accepted == completed + failed invariant intact even if a terminal event is
                // ever reported for an unknown id.
                if (_activeConnectionIds.TryRemove(connectionId, out _))
                {
                    Interlocked.Decrement(ref _connectionsActive);
                    Interlocked.Increment(ref _connectionsCompleted);
                }

                break;
            case BoltTransportEventKind.ConnectionFailed:
                if (_activeConnectionIds.TryRemove(connectionId, out _))
                {
                    Interlocked.Decrement(ref _connectionsActive);
                    Interlocked.Increment(ref _connectionsFailed);
                }

                break;
            case BoltTransportEventKind.ListenerFault:
                // Listener-level, accounting-neutral: observable without touching connection
                // lifecycle counters.
                Interlocked.Increment(ref _listenerFaults);
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
