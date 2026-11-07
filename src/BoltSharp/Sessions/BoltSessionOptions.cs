using BoltSharp.Messages;
using BoltSharp.PackStream;

namespace BoltSharp.Sessions;

/// <summary>
/// Verifies the credentials carried by a structurally validated <c>LOGON</c> request. The session
/// makes no authentication decision of its own: it delegates to this seam and, when no verifier is
/// configured, <b>fails closed</b> (every LOGON is rejected). No anonymous fallback exists.
/// Implementations must not throw for ordinary credential mismatch; throwing is treated as an
/// unexpected verifier failure and also fails closed.
/// </summary>
public interface IBoltCredentialVerifier
{
    /// <summary>Returns <see langword="true"/> when the supplied LOGON should be accepted.</summary>
    bool Verify(LogonRequest logon);
}

/// <summary>
/// Prepares a bounded, read-only operation for a validated <c>RUN</c> request.
/// <para>
/// <b>Provisional seam.</b> This is the smallest seam needed to exercise the state machine; it is
/// not a frozen backend contract. B3-C owns the real backend/result architecture and may refine or
/// replace this interface. It is public only because the B3-B test project lives in a separate
/// assembly (an <c>InternalsVisibleTo</c> grant was rejected: it changed C# overload resolution for
/// pre-existing PackStream immutability tests).
/// </para>
/// </summary>
public interface IBoltOperationProvider
{
    /// <summary>
    /// Prepares the operation for <paramref name="run"/>, or returns <see langword="null"/> when the
    /// query is not supported by this provider (a recoverable failure, surfaced as FAILURE).
    /// </summary>
    IBoltOperation? Prepare(RunRequest run);
}

/// <summary>
/// A prepared, finite result handle owned by the session. The session validates every row's column
/// count against <see cref="Fields"/> before emitting it (NC-05b) and calls <see cref="Dispose"/> at
/// most once when the result completes, fails, is discarded, is reset or the connection closes.
/// </summary>
public interface IBoltOperation : IDisposable
{
    /// <summary>The result field names announced by the RUN SUCCESS response.</summary>
    IReadOnlyList<string> Fields { get; }

    /// <summary>Returns the next row, or <see langword="null"/> when the operation is exhausted.</summary>
    IReadOnlyList<PackStreamValue?>? ReadRow();
}

/// <summary>
/// Bounded session configuration. All limits exist to prevent unbounded buffering; none of them
/// changes protocol semantics for results inside the limits.
/// </summary>
public sealed class BoltSessionOptions
{
    /// <summary>The <c>server</c> metadata value announced in HELLO SUCCESS.</summary>
    public string ServerName { get; init; } = "BoltSharp/0.1.0";

    /// <summary>The <c>connection_id</c> metadata value announced in HELLO SUCCESS.</summary>
    public string ConnectionId { get; init; } = "bolt-b3b-1";

    /// <summary>Maximum number of requests accepted by one <c>ReceiveBatch</c> call.</summary>
    public int MaxBatchSize { get; init; } = 64;

    /// <summary>
    /// Maximum number of records produced by a single PULL/DISCARD handling pass. A finite
    /// <c>n</c> larger than this, or <c>n = -1</c>, is served in bounded passes with
    /// <c>has_more = true</c> rather than draining an unbounded result into memory.
    /// </summary>
    public int MaxRecordsPerResponse { get; init; } = 1000;

    /// <summary>The credential verifier; when <see langword="null"/> the session fails closed.</summary>
    public IBoltCredentialVerifier? Verifier { get; init; }

    /// <summary>Default bounded configuration.</summary>
    public static BoltSessionOptions Default { get; } = new();
}
