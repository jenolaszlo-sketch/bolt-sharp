using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Tests.Messages;

using Xunit;

namespace BoltSharp.Tests.Sessions;

/// <summary>Deterministic, test-only credential verifier (never a production default).</summary>
internal sealed class TestCredentialVerifier : IBoltCredentialVerifier
{
    private readonly Func<LogonRequest, bool> _predicate;
    private readonly bool _throwOnCall;

    public TestCredentialVerifier(Func<LogonRequest, bool> predicate) => _predicate = predicate;

    private TestCredentialVerifier(bool throwOnCall)
    {
        _throwOnCall = throwOnCall;
        _predicate = _ => false;
    }

    public int Calls { get; private set; }

    /// <summary>Accepts only the given principal/credentials pair with the basic scheme.</summary>
    public static TestCredentialVerifier Accept(string principal, string credentials) =>
        new(logon =>
            logon.RecognizedScheme == BoltAuthScheme.Basic
            && BoltMessageFieldsTestAccess.GetString(logon.Auth, "principal") == principal
            && BoltMessageFieldsTestAccess.GetString(logon.Auth, "credentials") == credentials);

    /// <summary>Accepts every structurally valid LOGON.</summary>
    public static TestCredentialVerifier AcceptAll() => new(_ => true);

    /// <summary>A verifier that throws, to prove the session fails closed and leaks nothing.</summary>
    public static TestCredentialVerifier Thrower() => new(throwOnCall: true);

    public bool Verify(LogonRequest logon)
    {
        Calls++;
        if (_throwOnCall)
        {
            throw new InvalidOperationException("verifier exploded: " + (logon.Auth.Entries.Count));
        }

        return _predicate(logon);
    }
}

/// <summary>A scripted provider that records prepared requests and returns preset operations.</summary>
internal sealed class ScriptedOperationProvider : IBoltOperationProvider
{
    private readonly Func<RunRequest, IBoltOperation?> _factory;

    public ScriptedOperationProvider(Func<RunRequest, IBoltOperation?> factory) => _factory = factory;

    public ScriptedOperationProvider(IBoltOperation? operation) => _factory = _ => operation;

    public List<RunRequest> Prepared { get; } = new();

    public int PrepareCount => Prepared.Count;

    public IBoltOperation? Prepare(RunRequest run)
    {
        Prepared.Add(run);
        return _factory(run);
    }

    /// <summary>A provider that returns a fresh operation over the given rows with fields ["x"].</summary>
    public static ScriptedOperationProvider WithRows(params long[] values) =>
        new(new ScriptedOperation(new[] { "x" }, values.Select(v => Row(v)).ToArray()));

    public static ScriptedOperationProvider WithOperation(IBoltOperation operation) => new(operation);

    public static IReadOnlyList<PackStreamValue?> Row(long value) =>
        new PackStreamValue?[] { new PackStreamInteger(value) };
}

/// <summary>A finite scripted result handle with an optional injected read failure.</summary>
internal sealed class ScriptedOperation : IBoltOperation
{
    private readonly Queue<IReadOnlyList<PackStreamValue?>> _rows;

    public ScriptedOperation(IReadOnlyList<string> fields, params IReadOnlyList<PackStreamValue?>[] rows)
    {
        Fields = fields;
        _rows = new Queue<IReadOnlyList<PackStreamValue?>>(rows);
    }

    public IReadOnlyList<string> Fields { get; }

    public int DisposeCount { get; private set; }

    public int ReadCount { get; private set; }

    /// <summary>When set, <see cref="ReadRow"/> throws on the Nth call (1-based).</summary>
    public int ThrowOnReadNumber { get; set; }

    public Action? DisposeOverride { get; set; }

    public IReadOnlyList<PackStreamValue?>? ReadRow()
    {
        ReadCount++;
        if (ThrowOnReadNumber > 0 && ReadCount == ThrowOnReadNumber)
        {
            throw new InvalidOperationException("scripted read failure");
        }

        return _rows.Count > 0 ? _rows.Dequeue() : null;
    }

    public void Dispose()
    {
        DisposeCount++;
        DisposeOverride?.Invoke();
    }
}

/// <summary>Convenience builders for typed requests and session construction.</summary>
internal static class SessionHarness
{
    public static PackStreamMap Map(params (string Key, PackStreamValue? Value)[] entries)
    {
        var list = new List<KeyValuePair<string, PackStreamValue?>>(entries.Length);
        foreach (var (key, value) in entries)
        {
            list.Add(new KeyValuePair<string, PackStreamValue?>(key, value));
        }

        return new PackStreamMap(list);
    }

    public static PackStreamMap EmptyMap => Map();

    public static HelloRequest Hello() => new(Map(
        ("user_agent", new PackStreamString("testclient/1.0")),
        ("bolt_agent", Map(("product", new PackStreamString("testclient/1.0"))))));

    public static LogonRequest Logon(
        string scheme = "basic",
        string principal = "neo4j",
        string credentials = "secret") => new(Map(
            ("scheme", new PackStreamString(scheme)),
            ("principal", new PackStreamString(principal)),
            ("credentials", new PackStreamString(credentials))),
            BoltAuthScheme.Basic);

    public static RunRequest Run(string query = "RETURN 1") => new(query, EmptyMap, EmptyMap);

    public static PullRequest Pull(long n) => new(n, null);

    public static DiscardRequest Discard(long n) => new(n, null);

    public static ResetRequest Reset() => new();

    public static GoodbyeRequest Goodbye() => new();

    public static LogoffRequest Logoff() => new();

    public static TelemetryRequest Telemetry(long api) => new(api);

    /// <summary>A session with a no-op provider that returns an empty result, and an accept-all verifier.</summary>
    public static BoltConnectionSession Session(IBoltOperationProvider? provider = null, BoltSessionOptions? options = null) =>
        new(provider ?? ScriptedOperationProvider.WithRows(),
            options ?? new BoltSessionOptions { Verifier = TestCredentialVerifier.AcceptAll() });

    /// <summary>Drives a session through HELLO + LOGON into READY.</summary>
    public static BoltConnectionSession Authenticated(
        IBoltOperationProvider? provider = null,
        BoltSessionOptions? options = null)
    {
        var session = Session(provider, options);
        session.Receive(Hello());
        session.Receive(Logon());
        return session;
    }

    /// <summary>Drives a session into STREAMING over the given rows.</summary>
    public static BoltConnectionSession Streaming(params long[] rows)
    {
        var session = Authenticated(ScriptedOperationProvider.WithRows(rows));
        session.Receive(Run());
        return session;
    }

    public static IReadOnlyList<BoltResponse> ResponsesOf(BoltSessionResult result) => result.Responses;

    public static T SingleResponse<T>(BoltSessionResult result) where T : BoltResponse =>
        Assert.IsType<T>(Assert.Single(result.Responses));
}
