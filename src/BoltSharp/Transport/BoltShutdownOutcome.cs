namespace BoltSharp.Transport;

/// <summary>
/// The outcome of a listener shutdown request.
/// <para>
/// Shutdown is bounded by <see cref="BoltTcpServerOptions.ShutdownTimeout"/>. A bounded policy can
/// legitimately return before every accepted connection has terminated; that case is reported as
/// <see cref="Incomplete"/> instead of being hidden behind a successful-looking stop.
/// </para>
/// </summary>
public enum BoltShutdownOutcome
{
    /// <summary>Shutdown has not been requested yet.</summary>
    NotRequested,

    /// <summary>
    /// Acceptance quiesced and every accepted connection terminated before the bounded policy
    /// returned. The listener no longer owns any connection.
    /// </summary>
    Completed,

    /// <summary>
    /// The bounded policy expired while acceptance was still outstanding or a connection was
    /// still running. Callers must treat this as incomplete termination, not as a clean stop:
    /// the listener remains the owner of the outstanding connection until its task retires.
    /// </summary>
    Incomplete,
}
