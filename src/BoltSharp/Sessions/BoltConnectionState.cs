namespace BoltSharp.Sessions;

/// <summary>
/// The Bolt 5.8 server connection state model relevant to this bounded implementation
/// (<see href="https://neo4j.com/docs/bolt/current/bolt/server-state/">Bolt server-state
/// specification</see>; preserved copies under <c>docs/reviews/evidence/B3-B-IMPL-01/spec/</c>).
/// <para>
/// <c>DISCONNECTED</c> is a logical pre-connection state and is not represented here: a session
/// instance only exists after a successful B2 handshake, so it starts in
/// <see cref="Negotiation"/>. The explicit-transaction states <c>TX_READY</c> and
/// <c>TX_STREAMING</c> are outside this milestone and are therefore not modelled; explicit
/// transactions are never silently mapped onto auto-commit sessions.
/// </para>
/// </summary>
public enum BoltConnectionState
{
    /// <summary>Handshake complete; awaiting <c>HELLO</c>.</summary>
    Negotiation,

    /// <summary><c>HELLO</c> (or <c>LOGOFF</c>) complete; awaiting <c>LOGON</c>.</summary>
    Authentication,

    /// <summary>Authenticated, no active auto-commit result; ready to accept <c>RUN</c>.</summary>
    Ready,

    /// <summary>An auto-commit result is available for streaming.</summary>
    Streaming,

    /// <summary>A recoverable failure occurred; work messages are ignored until <c>RESET</c>.</summary>
    Failed,

    /// <summary>
    /// The <c>&lt;INTERRUPT&gt;</c> signal raised by <c>RESET</c> before recovery. In this
    /// synchronous engine the state is transient: it is entered while outstanding work is
    /// cancelled and left for <see cref="Ready"/> when recovery completes.
    /// </summary>
    Interrupted,

    /// <summary>Terminal, permanently unusable connection; no further messages are processed.</summary>
    Defunct,
}
