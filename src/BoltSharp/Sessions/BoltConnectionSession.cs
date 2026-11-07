using System.Diagnostics.CodeAnalysis;

using BoltSharp.Messages;
using BoltSharp.PackStream;

namespace BoltSharp.Sessions;

/// <summary>
/// A per-connection Bolt 5.8 protocol state machine. It consumes validated B3-A message objects and
/// produces ordered B3-A response objects; it owns the session-level result lifecycle but performs
/// no transport I/O, no Cypher parsing, no storage access and no authentication beyond delegating
/// to the configured <see cref="IBoltCredentialVerifier"/>.
/// <para>
/// <b>Public and provisional.</b> The session engine is public so that the future B5 host and the
/// B3-B test project can consume it (an <c>InternalsVisibleTo</c> grant was rejected because it
/// changed overload resolution for pre-existing PackStream immutability tests). The
/// <see cref="IBoltOperationProvider"/>/<see cref="IBoltOperation"/> pair is a provisional seam,
/// not the frozen B3-C backend contract.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>States and transitions</b> follow the Bolt 5.8 server-state specification and its version-5
/// appendix (preserved under <c>docs/reviews/evidence/B3-B-IMPL-01/spec/</c>). The explicit
/// transaction states are out of scope; explicit-transaction messages are refused rather than
/// mapped onto auto-commit.
/// </para>
/// <para>
/// <b>Bounded RESET semantics.</b> This synchronous engine does not implement mid-flight jump-ahead
/// interruption of a non-interruptible provider call. <c>RESET</c> is processed in arrival order:
/// it cancels and disposes any outstanding result (using the provider's discretionary
/// <see cref="IBoltOperation.Dispose"/>), clears recoverable failure state and returns the
/// connection to <see cref="BoltConnectionState.Ready"/>. A provider call already executing inside
/// <c>Prepare</c>/<c>ReadRow</c> cannot be preempted; no SUCCESS is claimed for work that is still
/// active. This limitation is recorded in the B3-B implementation report. A provider
/// <see cref="IBoltOperation.Dispose"/> failure during any retirement path is irreversible
/// (B3-B-R2 fail-closed retirement): the connection becomes terminal instead of claiming recovery.
/// </para>
/// <para>
/// <b>Diagnostics.</b> Responses never carry request values, and no exception raised by this type
/// includes query text or credential material.
/// </para>
/// </remarks>
public sealed class BoltConnectionSession : IDisposable
{
    private const int TelemetryMinApi = 0;
    private const int TelemetryMaxApi = 3;

    private static readonly PackStreamMap EmptyMap =
        new(Array.Empty<KeyValuePair<string, PackStreamValue?>>());

    private readonly BoltSessionOptions _options;
    private readonly BoltMessageCodec _codec;
    private readonly IBoltOperationProvider _provider;
    private BoltConnectionState _state = BoltConnectionState.Negotiation;
    private IBoltOperation? _operation;
    private IReadOnlyList<PackStreamValue?>? _pendingRow;
    private int _fieldCount;
    private bool _disposed;

    public BoltConnectionSession(
        IBoltOperationProvider provider,
        BoltSessionOptions? options = null,
        BoltMessageCodec? codec = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _options = options ?? BoltSessionOptions.Default;
        _codec = codec ?? new BoltMessageCodec();

        if (_options.MaxBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxBatchSize must be positive.");
        }

        if (_options.MaxRecordsPerResponse <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxRecordsPerResponse must be positive.");
        }
    }

    /// <summary>The current connection state.</summary>
    public BoltConnectionState State => _state;

    /// <summary>True when the session is terminal and processes no further messages.</summary>
    public bool IsDefunct => _state == BoltConnectionState.Defunct;

    /// <summary>True while an auto-commit result is owned by the session.</summary>
    public bool HasActiveOperation => _operation is not null;

    /// <summary>
    /// The most recent provider <see cref="IBoltOperation.Dispose"/> failure, or <see langword="null"/>
    /// when no cleanup attempt has failed. Disposal failures are contained (never thrown out of this
    /// API) and recorded here as a diagnostic; they never prevent a terminal transition and never
    /// change a primary protocol outcome. A failed retirement is irreversible (B3-B-R2): where the
    /// session would otherwise continue or recover, it becomes <see cref="BoltConnectionState.Defunct"/>
    /// instead of falsely reporting recovery, and the diagnostic survives that transition.
    /// </summary>
    public Exception? LastCleanupFailure { get; private set; }

    /// <summary>
    /// Processes one complete, unchunked client request payload: decodes it with the B3-A codec and
    /// handles it. Malformed, unknown and unsupported payloads produce a terminal protocol-error
    /// response and transition the connection to <see cref="BoltConnectionState.Defunct"/>; they are
    /// never interpreted as a valid request.
    /// </summary>
    public BoltSessionResult ReceiveBytes(ReadOnlyMemory<byte> payload)
    {
        ThrowIfDisposed();

        if (_state == BoltConnectionState.Defunct)
        {
            return BoltSessionResult.None(BoltConnectionState.Defunct, shouldClose: true);
        }

        BoltRequest request;
        try
        {
            request = _codec.DecodeRequest(payload);
        }
        catch (BoltMessageException ex) when (ex.Category == BoltMessageErrorCategory.UnsupportedMessage)
        {
            // BEGIN/COMMIT/ROLLBACK/ROUTE are valid Bolt 5.8 signatures that this bounded server does
            // not implement. They are refused deterministically â€” never fabricated as SUCCESS.
            return FailTerminal(UnsupportedMessageFailure());
        }
        catch (BoltMessageException)
        {
            // Malformed, unknown, wrong-arity or wrong-typed input: fatal protocol error. No request
            // values are echoed.
            return FailTerminal(ProtocolViolationFailure());
        }

        return Receive(request);
    }

    /// <summary>Processes one validated request object.</summary>
    public BoltSessionResult Receive(BoltRequest request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);

        if (_state == BoltConnectionState.Defunct)
        {
            return BoltSessionResult.None(BoltConnectionState.Defunct, shouldClose: true);
        }

        return request switch
        {
            HelloRequest hello => HandleHello(hello),
            LogonRequest logon => HandleLogon(logon),
            LogoffRequest => HandleLogoff(),
            RunRequest run => HandleRun(run),
            PullRequest pull => HandlePull(pull),
            DiscardRequest discard => HandleDiscard(discard),
            ResetRequest => HandleReset(),
            GoodbyeRequest => HandleGoodbye(),
            TelemetryRequest telemetry => HandleTelemetry(telemetry),
            _ => FailTerminal(ProtocolViolationFailure()),
        };
    }

    /// <summary>
    /// Processes an ordered batch of requests (pipelining). Processing stops at the first terminal
    /// outcome; subsequent requests in the batch are not processed. The batch is bounded by
    /// <see cref="BoltSessionOptions.MaxBatchSize"/>.
    /// </summary>
    public IReadOnlyList<BoltResponseEnvelope> ReceiveBatch(IReadOnlyList<BoltRequest> requests)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count > _options.MaxBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requests), requests.Count,
                $"A batch may contain at most {_options.MaxBatchSize} requests.");
        }

        var envelopes = new List<BoltResponseEnvelope>(requests.Count);
        foreach (var request in requests)
        {
            var result = Receive(request);
            envelopes.Add(new BoltResponseEnvelope(request.Signature, result.Responses, result.State));
            if (result.ShouldClose)
            {
                break;
            }
        }

        return envelopes;
    }

    /// <summary>
    /// Signals an unexpected transport disconnection or an orderly shutdown. Disposes the
    /// outstanding operation, clears any authentication context and makes the session terminal.
    /// Idempotent; performs no I/O and emits no response.
    /// </summary>
    public void Disconnect()
    {
        if (_state == BoltConnectionState.Defunct)
        {
            return;
        }

        // Terminal state is established independently of whether provider cleanup succeeds; a
        // throwing provider Dispose is contained and recorded (F-01).
        _ = DetachAndDisposeOperation();
        _state = BoltConnectionState.Defunct;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Disconnect();
        }
        finally
        {
            // Idempotency must hold even if a cleanup path were to misbehave.
            _disposed = true;
        }
    }

    // ---- Handlers ----

    private BoltSessionResult HandleHello(HelloRequest hello)
    {
        if (_state != BoltConnectionState.Negotiation)
        {
            return FailTerminal(ProtocolViolationFailure());
        }

        var success = SuccessResponse.FromEntries(
            ("server", new PackStreamString(_options.ServerName)),
            ("connection_id", new PackStreamString(_options.ConnectionId)),
            ("hints", EmptyMap));

        _state = BoltConnectionState.Authentication;
        return BoltSessionResult.Single(_state, success);
    }

    private BoltSessionResult HandleLogon(LogonRequest logon)
    {
        if (_state != BoltConnectionState.Authentication)
        {
            return FailTerminal(ProtocolViolationFailure());
        }

        bool verified;
        try
        {
            verified = _options.Verifier?.Verify(logon) ?? false;
        }
        catch (Exception)
        {
            // A throwing verifier is an unexpected failure and fails closed; no exception detail is
            // surfaced (it could contain credential material).
            verified = false;
        }

        if (!verified)
        {
            // Authentication failure: documented Bolt 5.8 FAILURE followed by immediate closure.
            return FailTerminal(AuthenticationFailure());
        }

        _state = BoltConnectionState.Ready;
        return BoltSessionResult.Single(_state, SuccessResponse.Empty);
    }

    private BoltSessionResult HandleLogoff()
    {
        switch (_state)
        {
            case BoltConnectionState.Ready:
                // Clears the authenticated session context; queries require another LOGON.
                _state = BoltConnectionState.Authentication;
                return BoltSessionResult.Single(_state, SuccessResponse.Empty);

            case BoltConnectionState.Failed:
                return BoltSessionResult.Single(_state, IgnoredResponse.Instance);

            case BoltConnectionState.Interrupted:
                return BoltSessionResult.Single(_state, IgnoredResponse.Instance);

            default:
                // LOGOFF outside READY: the message description requires FAILURE followed by
                // immediate closure. (The version-5 appendix additionally lists a READY failure
                // branch into FAILED; this engine has no failing LOGOFF path, so that branch is
                // unreachable. Recorded as a resolved specification ambiguity.)
                return FailTerminal(ProtocolViolationFailure());
        }
    }

    private BoltSessionResult HandleRun(RunRequest run)
    {
        switch (_state)
        {
            case BoltConnectionState.Failed:
            case BoltConnectionState.Interrupted:
                return BoltSessionResult.Single(_state, IgnoredResponse.Instance);

            case BoltConnectionState.Ready:
                break;

            default:
                return FailTerminal(ProtocolViolationFailure());
        }

        IBoltOperation? operation;
        try
        {
            operation = _provider.Prepare(run);
        }
        catch (Exception)
        {
            return FailRecoverable(ProviderFailure());
        }

        if (operation is null)
        {
            return FailRecoverable(UnsupportedQueryFailure());
        }

        // B3-B-R2: session ownership starts the moment Prepare returns a handle, before any
        // provider-controlled metadata is touched (INV-01). Every later failure therefore retires
        // this handle through the single session retirement path (INV-02). No provider metadata is
        // read until that ownership is established.
        _operation = operation;
        _pendingRow = null;

        // The field metadata is acquired and snapshotted exactly once, inside the ownership
        // boundary; no RUN response is ever built from a second provider read (INV-07).
        if (!TryCaptureFields(operation, out var fields))
        {
            // Invalid or unavailable metadata is a provider failure: never publish a cursor and
            // never enter STREAMING. Retirement is attempted exactly once; a failed retirement is
            // irreversible, so the session fails closed rather than recoverable (R2-F02).
            return FailRecoverable(ProviderFailure());
        }

        _fieldCount = fields.Items.Count;
        _state = BoltConnectionState.Streaming;
        return BoltSessionResult.Single(
            _state,
            SuccessResponse.FromEntries(("fields", fields)));
    }

    private BoltSessionResult HandlePull(PullRequest pull)
    {
        if (_state == BoltConnectionState.Failed || _state == BoltConnectionState.Interrupted)
        {
            return BoltSessionResult.Single(_state, IgnoredResponse.Instance);
        }

        if (_state != BoltConnectionState.Streaming || _operation is null)
        {
            return FailTerminal(ProtocolViolationFailure());
        }

        var responses = new List<BoltResponse>();
        try
        {
            var requested = RequestedCount(pull.N);
            for (var i = 0; i < requested; i++)
            {
                var row = NextRow();
                if (row is null)
                {
                    return CompleteResult(responses, hasMore: false);
                }

                // NC-05b: the session validates cross-message column count against the RUN-time
                // metadata snapshot, never by re-reading the provider (B3-B-R2 / INV-07).
                if (row.Count != _fieldCount)
                {
                    // Rows already emitted before the bad row stay in the response stream.
                    return FailRecoverable(RowShapeFailure(), responses);
                }

                responses.Add(RecordResponse.FromValues(row.ToArray()));
            }

            if (NextRow() is { } pending)
            {
                _pendingRow = pending;
                return BoltSessionResult.Of(_state, Append(responses, HasMoreSuccess(true)));
            }

            return CompleteResult(responses, hasMore: false);
        }
        catch (Exception)
        {
            // A mid-stream provider failure emits the records already read, then FAILURE.
            return FailRecoverable(ProviderFailure(), responses);
        }
    }

    private BoltSessionResult HandleDiscard(DiscardRequest discard)
    {
        if (_state == BoltConnectionState.Failed || _state == BoltConnectionState.Interrupted)
        {
            return BoltSessionResult.Single(_state, IgnoredResponse.Instance);
        }

        if (_state != BoltConnectionState.Streaming || _operation is null)
        {
            return FailTerminal(ProtocolViolationFailure());
        }

        try
        {
            var requested = RequestedCount(discard.N);
            for (var i = 0; i < requested; i++)
            {
                if (NextRow() is null)
                {
                    return CompleteResult(Array.Empty<BoltResponse>(), hasMore: false);
                }
            }

            if (NextRow() is { } pending)
            {
                _pendingRow = pending;
                return BoltSessionResult.Single(_state, HasMoreSuccess(true));
            }

            return CompleteResult(Array.Empty<BoltResponse>(), hasMore: false);
        }
        catch (Exception)
        {
            return FailRecoverable(ProviderFailure());
        }
    }

    private BoltSessionResult HandleReset()
    {
        switch (_state)
        {
            case BoltConnectionState.Ready:
            case BoltConnectionState.Streaming:
            case BoltConnectionState.Failed:
            case BoltConnectionState.Interrupted:
                break;

            default:
                // RESET before the connection reaches READY: FAILURE followed by immediate closure.
                return FailTerminal(ProtocolViolationFailure());
        }

        // Model the <INTERRUPT> signal, cancel and dispose outstanding work, then recover.
        _state = BoltConnectionState.Interrupted;
        if (DetachAndDisposeOperation() is not null)
        {
            // Cleanup failure makes safe recovery impossible: fail closed (unchanged from Q1).
            return FailTerminal(ProviderFailure());
        }

        _state = BoltConnectionState.Ready;
        return BoltSessionResult.Single(_state, SuccessResponse.Empty);
    }

    private BoltSessionResult HandleGoodbye()
    {
        // Graceful termination: no response is defined, outstanding work is released, connection ends.
        // DEFUNCT is guaranteed even when provider cleanup throws; no protocol response is fabricated.
        _ = DetachAndDisposeOperation();
        _state = BoltConnectionState.Defunct;
        return BoltSessionResult.None(_state, shouldClose: true);
    }

    private BoltSessionResult HandleTelemetry(TelemetryRequest telemetry)
    {
        switch (_state)
        {
            case BoltConnectionState.Failed:
            case BoltConnectionState.Interrupted:
                return BoltSessionResult.Single(_state, IgnoredResponse.Instance);

            case BoltConnectionState.Ready:
                if (telemetry.Api is >= TelemetryMinApi and <= TelemetryMaxApi)
                {
                    return BoltSessionResult.Single(_state, SuccessResponse.Empty);
                }

                return FailRecoverable(TelemetryFailure());

            default:
                return FailTerminal(ProtocolViolationFailure());
        }
    }

    // ---- Helpers ----

    private int RequestedCount(long n)
    {
        if (n < 0)
        {
            return _options.MaxRecordsPerResponse;
        }

        return (int)Math.Min(n, _options.MaxRecordsPerResponse);
    }

    private IReadOnlyList<PackStreamValue?>? NextRow()
    {
        if (_pendingRow is not null)
        {
            var pending = _pendingRow;
            _pendingRow = null;
            return pending;
        }

        return _operation?.ReadRow();
    }

    /// <summary>
    /// Acquires the provider's field metadata once and converts it to a session-owned immutable
    /// snapshot. Returns <see langword="false"/> when the provider returns null metadata, returns a
    /// null field name, or throws while the metadata is read (including <c>Count</c> or indexer
    /// access on a custom or deferred list). B3-B-R2: this is the only place the session reads
    /// <see cref="IBoltOperation.Fields"/>.
    /// </summary>
    private static bool TryCaptureFields(
        IBoltOperation operation,
        [NotNullWhen(true)] out PackStreamList? fields)
    {
        fields = null;
        try
        {
            var names = operation.Fields;
            if (names is null)
            {
                return false;
            }

            var values = new PackStreamValue?[names.Count];
            for (var i = 0; i < values.Length; i++)
            {
                var name = names[i];
                if (name is null)
                {
                    return false;
                }

                values[i] = new PackStreamString(name);
            }

            fields = new PackStreamList(values);
            return true;
        }
        catch (Exception)
        {
            fields = null;
            return false;
        }
    }

    private BoltSessionResult CompleteResult(IReadOnlyList<BoltResponse> responses, bool hasMore)
    {
        var summary = hasMore ? HasMoreSuccess(true) : SuccessResponse.Empty;
        if (DetachAndDisposeOperation() is not null)
        {
            // B3-B-R2 (R2-F01): the exhausted result could not be released. The failed retirement is
            // irreversible: report the completion as FAILURE (never SUCCESS) and make the connection
            // permanently unusable instead of recoverable.
            _state = BoltConnectionState.Defunct;
            return BoltSessionResult.Of(_state, Append(responses, ProviderFailure()), shouldClose: true);
        }

        _state = BoltConnectionState.Ready;
        return BoltSessionResult.Of(_state, Append(responses, summary));
    }

    private static BoltResponse HasMoreSuccess(bool hasMore) =>
        SuccessResponse.FromEntries(("has_more", new PackStreamBoolean(hasMore)));

    private static IReadOnlyList<BoltResponse> Append(IReadOnlyList<BoltResponse> responses, BoltResponse terminal)
    {
        var list = new List<BoltResponse>(responses.Count + 1);
        list.AddRange(responses);
        list.Add(terminal);
        return list;
    }

    /// <summary>
    /// Detaches and disposes the active operation exactly once. A throwing provider
    /// <see cref="IBoltOperation.Dispose"/> is contained: the failure is recorded and returned, never
    /// propagated. Returns <see langword="null"/> when there was nothing to dispose or disposal
    /// succeeded. A failed attempt is not retried.
    /// </summary>
    private Exception? DetachAndDisposeOperation()
    {
        var operation = _operation;
        _operation = null;
        _pendingRow = null;
        _fieldCount = 0;
        return RecordCleanupFailure(DisposeGuarded(operation));
    }

    /// <summary>Disposes one operation, containing any provider failure.</summary>
    private static Exception? DisposeGuarded(IBoltOperation? operation)
    {
        if (operation is null)
        {
            return null;
        }

        try
        {
            operation.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private Exception? RecordCleanupFailure(Exception? failure)
    {
        if (failure is not null)
        {
            LastCleanupFailure = failure;
        }

        return failure;
    }

    private BoltSessionResult FailTerminal(BoltResponse failure)
    {
        // DEFUNCT is guaranteed regardless of the cleanup outcome (F-01).
        _ = DetachAndDisposeOperation();
        _state = BoltConnectionState.Defunct;
        return BoltSessionResult.Single(_state, failure, shouldClose: true);
    }

    /// <summary>
    /// A recoverable execution failure: one FAILURE, enter FAILED, connection stays open — unless
    /// retirement of an owned operation fails, in which case the session fails closed (B3-B-R2).
    /// </summary>
    private BoltSessionResult FailRecoverable(BoltResponse failure, IReadOnlyList<BoltResponse>? priorResponses = null)
    {
        // Cleanup failure never suppresses the primary protocol failure (F-01 / INV-08). It does,
        // however, make recovery impossible (R2-F01 / INV-04): the FAILURE is still reported exactly
        // once, and the session becomes terminal instead of recoverable (INV-05 / INV-09).
        var responses = priorResponses is null ? new[] { failure } : Append(priorResponses, failure);
        if (DetachAndDisposeOperation() is not null)
        {
            _state = BoltConnectionState.Defunct;
            return BoltSessionResult.Of(_state, responses, shouldClose: true);
        }

        _state = BoltConnectionState.Failed;
        return BoltSessionResult.Of(_state, responses, shouldClose: false);
    }

    private static FailureResponse AuthenticationFailure() => FailureResponse.FromEntries(
        ("gql_status", new PackStreamString("50N42")),
        ("neo4j_code", new PackStreamString("Neo.ClientError.Security.Unauthorized")),
        ("description", new PackStreamString("The client is unauthorized due to authentication failure.")),
        ("diagnostic_record", EmptyMap));

    private static FailureResponse UnsupportedQueryFailure() => FailureResponse.FromEntries(
        ("gql_status", new PackStreamString("50N42")),
        ("neo4j_code", new PackStreamString("Neo.ClientError.Request.Invalid")),
        ("description", new PackStreamString("The requested operation is not supported by this server.")),
        ("diagnostic_record", EmptyMap));

    private static FailureResponse ProviderFailure() => FailureResponse.FromEntries(
        ("gql_status", new PackStreamString("50N00")),
        ("neo4j_code", new PackStreamString("Neo.DatabaseError.General.UnknownError")),
        ("description", new PackStreamString("The operation provider failed while executing the request.")),
        ("diagnostic_record", EmptyMap));

    private static FailureResponse RowShapeFailure() => FailureResponse.FromEntries(
        ("gql_status", new PackStreamString("50N42")),
        ("neo4j_code", new PackStreamString("Neo.DatabaseError.General.UnknownError")),
        ("description", new PackStreamString("A result row does not match the announced result fields.")),
        ("diagnostic_record", EmptyMap));

    // Bolt 5.8: an integer identifier outside the defined domain is an invalid *value* (the
    // specification's own TELEMETRY 9001 example answers 50N00 / General.UnknownError); 22G03 is
    // reserved for an invalid value *type*, which B3-A rejects as a malformed message (F-02).
    private static FailureResponse TelemetryFailure() => FailureResponse.FromEntries(
        ("gql_status", new PackStreamString("50N00")),
        ("neo4j_code", new PackStreamString("Neo.DatabaseError.General.UnknownError")),
        ("description", new PackStreamString("The telemetry API identifier is not valid for this server.")),
        ("diagnostic_record", EmptyMap));

    private static FailureResponse UnsupportedMessageFailure() => FailureResponse.FromEntries(
        ("gql_status", new PackStreamString("08N06")),
        ("neo4j_code", new PackStreamString("Neo.ClientError.Request.Invalid")),
        ("description", new PackStreamString("Explicit transactions and routing are not supported by this server.")),
        ("diagnostic_record", EmptyMap));

    private static FailureResponse ProtocolViolationFailure() => FailureResponse.FromEntries(
        ("gql_status", new PackStreamString("08N06")),
        ("neo4j_code", new PackStreamString("Neo.ClientError.Request.Invalid")),
        ("description", new PackStreamString("The message cannot be handled in the current connection state.")),
        ("diagnostic_record", EmptyMap));

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
