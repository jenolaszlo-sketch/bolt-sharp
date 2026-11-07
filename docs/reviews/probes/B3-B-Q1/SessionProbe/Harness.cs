using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

namespace B3BQ1.SessionProbe;

/// <summary>Reviewer verifier with observable call count.</summary>
internal sealed class RecordingVerifier : IBoltCredentialVerifier
{
    private readonly Func<LogonRequest, bool> _predicate;

    public RecordingVerifier(Func<LogonRequest, bool> predicate) => _predicate = predicate;

    public int Calls { get; private set; }

    public static RecordingVerifier AcceptAll() => new(_ => true);

    public bool Verify(LogonRequest logon)
    {
        Calls++;
        return _predicate(logon);
    }
}

/// <summary>Reviewer provider with observable open count.</summary>
internal sealed class ProbeProvider : IBoltOperationProvider
{
    private readonly Func<RunRequest, IBoltOperation?> _factory;

    public ProbeProvider(Func<RunRequest, IBoltOperation?> factory) => _factory = factory;

    public int OpenCount { get; private set; }

    public IBoltOperation? Prepare(RunRequest run)
    {
        OpenCount++;
        return _factory(run);
    }
}

/// <summary>Reviewer scripted operation with observable counters and injected faults.</summary>
internal sealed class ProbeOperation : IBoltOperation
{
    private readonly Queue<IReadOnlyList<PackStreamValue?>> _rows;

    public ProbeOperation(IReadOnlyList<string> fields, params IReadOnlyList<PackStreamValue?>[] rows)
    {
        Fields = fields;
        _rows = new Queue<IReadOnlyList<PackStreamValue?>>(rows);
    }

    public IReadOnlyList<string> Fields { get; }

    public int ReadCount { get; private set; }

    public int DisposeCount { get; private set; }

    public int ThrowOnReadNumber { get; set; }

    public bool ThrowOnDispose { get; set; }

    public Action? OnRead { get; set; }

    public IReadOnlyList<PackStreamValue?>? ReadRow()
    {
        ReadCount++;
        OnRead?.Invoke();
        if (ThrowOnReadNumber > 0 && ReadCount == ThrowOnReadNumber)
        {
            throw new InvalidOperationException("probe read failure");
        }

        return _rows.Count > 0 ? _rows.Dequeue() : null;
    }

    public void Dispose()
    {
        DisposeCount++;
        if (ThrowOnDispose)
        {
            throw new InvalidOperationException("probe dispose failure");
        }
    }
}

internal static class H
{
    public static PackStreamMap Map(params (string Key, PackStreamValue? Value)[] entries)
    {
        var list = new List<KeyValuePair<string, PackStreamValue?>>(entries.Length);
        foreach (var (k, v) in entries)
        {
            list.Add(new KeyValuePair<string, PackStreamValue?>(k, v));
        }

        return new PackStreamMap(list);
    }

    public static PackStreamMap Empty => Map();

    public static HelloRequest Hello() => new(Map(
        ("user_agent", new PackStreamString("probe/1.0")),
        ("bolt_agent", Map(("product", new PackStreamString("probe/1.0"))))));

    public static LogonRequest Logon(string scheme = "basic", string principal = "p", string credentials = "c") =>
        new(Map(("scheme", new PackStreamString(scheme)),
                ("principal", new PackStreamString(principal)),
                ("credentials", new PackStreamString(credentials))),
            scheme == "basic" ? BoltAuthScheme.Basic : BoltAuthScheme.Unknown);

    public static RunRequest Run() => new("RETURN 1", Empty, Empty);

    public static PullRequest Pull(long n) => new(n, null);

    public static DiscardRequest Discard(long n) => new(n, null);

    public static ResetRequest Reset() => new();

    public static GoodbyeRequest Goodbye() => new();

    public static LogoffRequest Logoff() => new();

    public static TelemetryRequest Telemetry(long api) => new(api);

    public static IReadOnlyList<PackStreamValue?> Row(long value) =>
        new PackStreamValue?[] { new PackStreamInteger(value) };

    public static IReadOnlyList<PackStreamValue?> WideRow(long a, long b) =>
        new PackStreamValue?[] { new PackStreamInteger(a), new PackStreamInteger(b) };

    public static byte[] Hex(string hex) =>
        Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));

    public static Session New(
        IBoltOperationProvider provider,
        IBoltCredentialVerifier? verifier = null,
        int cap = 1000,
        int maxBatch = 64) =>
        new(provider, new BoltSessionOptions
        {
            Verifier = verifier ?? RecordingVerifier.AcceptAll(),
            MaxRecordsPerResponse = cap,
            MaxBatchSize = maxBatch,
        });

    /// <summary>A session with no configured verifier (production fail-closed default).</summary>
    public static Session NoVerifier(IBoltOperationProvider provider) =>
        new(provider, new BoltSessionOptions
        {
            Verifier = null,
            MaxRecordsPerResponse = 1000,
            MaxBatchSize = 64,
        });

    public static Session Authenticated(IBoltOperationProvider provider, int cap = 1000) =>
        New(provider, cap: cap).HelloLogon();

    public static long IntOf(BoltResponse response) =>
        ((PackStreamInteger)AssertType<RecordResponse>(response).Data.Items[0]!).Value;

    public static T AssertType<T>(BoltResponse response) where T : BoltResponse =>
        response as T ?? throw new InvalidOperationException($"expected {typeof(T).Name}, got {response.GetType().Name}");

    public static bool? HasMoreOf(BoltResponse response) =>
        ((SuccessResponse)response).HasMore;

    public static string DescriptionOf(BoltResponse response) =>
        (BoltMessageFieldsProbe.GetValue(((FailureResponse)response).Metadata, "description") as PackStreamString)?.Value
        ?? string.Empty;

    public static string? CodeOf(BoltResponse response) =>
        (BoltMessageFieldsProbe.GetValue(((FailureResponse)response).Metadata, "neo4j_code") as PackStreamString)?.Value;

    public static string? GqlOf(BoltResponse response) =>
        (BoltMessageFieldsProbe.GetValue(((FailureResponse)response).Metadata, "gql_status") as PackStreamString)?.Value;

    public static string Sequence(IReadOnlyList<BoltResponse> responses) =>
        string.Join(",", responses.Select(r => r switch
        {
            RecordResponse => "RECORD",
            IgnoredResponse => "IGNORED",
            FailureResponse => "FAILURE",
            _ => "SUCCESS" + (((SuccessResponse)r).HasMore is { } hm ? $"(has_more:{hm.ToString().ToLowerInvariant()})" : string.Empty),
        }));
}

/// <summary>Session convenience wrapper used by the probe (no session state is hidden).</summary>
internal sealed class Session
{
    private readonly BoltConnectionSession _session;

    public Session(IBoltOperationProvider provider, BoltSessionOptions options) =>
        _session = new BoltConnectionSession(provider, options);

    public BoltConnectionState State => _session.State;

    public BoltSessionResult Send(BoltRequest request) => _session.Receive(request);

    public BoltSessionResult SendBytes(byte[] payload) => _session.ReceiveBytes(payload);

    public IReadOnlyList<BoltResponseEnvelope> Batch(IReadOnlyList<BoltRequest> requests) =>
        _session.ReceiveBatch(requests);

    public void Disconnect() => _session.Disconnect();

    public void DisposeSession() => _session.Dispose();

    public Session HelloLogon()
    {
        _session.Receive(H.Hello());
        _session.Receive(H.Logon());
        return this;
    }
}

internal static class BoltMessageFieldsProbe
{
    public static PackStreamValue? GetValue(PackStreamMap map, string key)
    {
        PackStreamValue? result = null;
        foreach (var e in map.Entries)
        {
            if (string.Equals(e.Key, key, StringComparison.Ordinal))
            {
                result = e.Value;
            }
        }

        return result;
    }
}
