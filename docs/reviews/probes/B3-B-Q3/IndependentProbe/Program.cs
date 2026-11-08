using System.Collections;
using System.Text.Json;

using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

namespace B3BQ3.IndependentProbe;

// B3-B-Q3 independent reviewer probe. Reviewer-authored; reuses no producer test or probe code.
// Every scenario uses fresh sessions/providers; no shared mutable state between scenarios.
internal static partial class Program
{
    internal const string SentinelRead = "SENTINEL-Q3-read-failure-internal-7f3a";
    internal const string SentinelMeta = "SENTINEL-Q3-metadata-failure-internal-91bc";
    internal const string CleanupText = "Q3 synthetic cleanup failure";
    internal const string ProviderDescription = "The operation provider failed while executing the request.";
    internal const string RowDescription = "A result row does not match the announced result fields.";

    private static readonly List<Q3Check> Checks = new();
    private static readonly List<object> Trace = new();
    private static readonly BoltMessageCodec Codec = new();

    internal sealed record Q3Check(string Id, bool Pass, string Expected, string Actual);

    internal static void Check(string id, bool condition, string expected, string actual) =>
        Checks.Add(new Q3Check(id, condition, expected, actual));

    internal static PackStreamMap Map(params (string K, PackStreamValue? V)[] pairs) =>
        new(pairs.Select(p => new KeyValuePair<string, PackStreamValue?>(p.K, p.V)).ToArray());

    internal static RunRequest Run() => new("Q3 synthetic request", Map(), Map());

    internal static HelloRequest Hello() => new(Map(
        ("user_agent", new PackStreamString("q3/1")),
        ("bolt_agent", Map(("product", new PackStreamString("q3/1"))))));

    internal static LogonRequest Logon() => new(Map(("scheme", new PackStreamString("none"))), BoltAuthScheme.None);

    internal static PackStreamValue?[] Row(long v) => new PackStreamValue?[] { new PackStreamInteger(v) };

    internal static PackStreamValue?[] TwoValues() => new PackStreamValue?[] { new PackStreamInteger(1), new PackStreamInteger(2) };

    internal static string Seq(BoltSessionResult? r) => r is null ? "(void)" : string.Join(",", r.Responses.Select(x =>
        x is SuccessResponse ? "SUCCESS"
        : x is FailureResponse ? "FAILURE"
        : x is RecordResponse ? "RECORD"
        : x is IgnoredResponse ? "IGNORED"
        : "?"));

    internal sealed class CountingVerifier : IBoltCredentialVerifier
    {
        public int Calls;
        public bool Verify(LogonRequest _) { Calls++; return true; }
    }

    internal sealed class ProbeProvider : IBoltOperationProvider
    {
        public Func<ProbeProvider, ProbeOperation?> Factory = _ => new ProbeOperation("op");
        public int Prepares;
        public List<ProbeOperation> Operations = new();

        public IBoltOperation? Prepare(RunRequest run)
        {
            Prepares++;
            var op = Factory(this);
            if (op is not null)
            {
                Operations.Add(op);
            }

            return op;
        }
    }

    internal sealed class ProbeOperation : IBoltOperation
    {
        private readonly Queue<IReadOnlyList<PackStreamValue?>> _rows;

        public ProbeOperation(string name, IEnumerable<IReadOnlyList<PackStreamValue?>>? rows = null)
        {
            Name = name;
            _rows = new Queue<IReadOnlyList<PackStreamValue?>>(rows ?? Array.Empty<IReadOnlyList<PackStreamValue?>>());
            FieldsFactory = _ => new[] { "x" };
        }

        public string Name;
        public Func<ProbeOperation, IReadOnlyList<string>> FieldsFactory;
        public int FieldsReads, DisposeAttempts, ReadCalls;
        public bool ThrowOnDispose;
        public bool ResourceLive = true;
        public Exception CleanupError = new InvalidOperationException(CleanupText);
        public int ThrowReadAt;

        public IReadOnlyList<string> Fields
        {
            get { FieldsReads++; return FieldsFactory(this); }
        }

        public IReadOnlyList<PackStreamValue?>? ReadRow()
        {
            ReadCalls++;
            if (ThrowReadAt > 0 && ReadCalls == ThrowReadAt)
            {
                throw new InvalidOperationException(SentinelRead);
            }

            return _rows.Count > 0 ? _rows.Dequeue() : null;
        }

        public void Dispose()
        {
            DisposeAttempts++;
            if (ThrowOnDispose)
            {
                throw CleanupError;
            }

            ResourceLive = false;
        }
    }

    /// <summary>A read-only string list with configurable throwing members and access counters.</summary>
    internal sealed class FaultyList : IReadOnlyList<string>
    {
        private readonly string[] _items;

        public FaultyList(params string[] items) => _items = items;

        public bool ThrowOnCount, ThrowOnIndexer;
        public int CountReads, IndexerReads;

        public int Count
        {
            get
            {
                CountReads++;
                if (ThrowOnCount)
                {
                    throw new InvalidOperationException("Q3 metadata count failure");
                }

                return _items.Length;
            }
        }

        public string this[int index]
        {
            get
            {
                IndexerReads++;
                if (ThrowOnIndexer)
                {
                    throw new InvalidOperationException("Q3 metadata indexer failure");
                }

                return _items[index];
            }
        }

        public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class ProbeSession
    {
        public string Id;
        public ProbeProvider Provider;
        public BoltConnectionSession Session;
        public CountingVerifier Verifier;
        public Exception? Escaped;

        public ProbeSession(string id, ProbeProvider provider)
        {
            Id = id;
            Provider = provider;
            Verifier = new CountingVerifier();
            Session = new BoltConnectionSession(
                provider,
                new BoltSessionOptions { Verifier = Verifier, MaxRecordsPerResponse = 2 });
        }

        public static ProbeSession Authenticated(string id, ProbeProvider provider)
        {
            var s = new ProbeSession(id, provider);
            s.Session.Receive(Hello());
            s.Session.Receive(Logon());
            return s;
        }

        public BoltSessionResult? Send(string action, BoltRequest request)
        {
            var start = Session.State;
            Escaped = null;
            BoltSessionResult? r = null;
            try
            {
                r = Session.Receive(request);
            }
            catch (Exception ex)
            {
                Escaped = ex;
            }

            Record(action, start, r);
            return r;
        }

        public void Call(string action, Action call)
        {
            var start = Session.State;
            Escaped = null;
            try
            {
                call();
            }
            catch (Exception ex)
            {
                Escaped = ex;
            }

            Record(action, start, null);
        }

        private void Record(string action, BoltConnectionState start, BoltSessionResult? r)
        {
            Trace.Add(new
            {
                scenario = Id,
                action,
                starting_state = start.ToString(),
                response = Seq(r),
                responses = r?.Responses.Select(Describe).ToArray(),
                state = Session.State.ToString(),
                should_close = r?.ShouldClose,
                owned = Session.HasActiveOperation,
                escaped = Escaped?.GetType().Name,
                opens = Provider.Prepares,
                cleanup = Session.LastCleanupFailure?.GetType().Name,
                operations = Provider.Operations.Select(o => new
                {
                    o.Name,
                    o.DisposeAttempts,
                    o.ResourceLive,
                    o.FieldsReads,
                    o.ReadCalls
                }).ToArray()
            });
        }

        private static object Describe(BoltResponse r) => new
        {
            kind = r.GetType().Name,
            metadata = r switch
            {
                FailureResponse f => Flat(f.Metadata),
                SuccessResponse s => Flat(s.Metadata),
                _ => null
            }
        };
    }

    internal static Dictionary<string, string?> Flat(PackStreamMap m)
    {
        var d = new Dictionary<string, string?>();
        foreach (var e in m.Entries)
        {
            d[e.Key] = e.Value switch
            {
                null => null,
                PackStreamString s => s.Value,
                PackStreamInteger i => i.Value.ToString(),
                PackStreamBoolean b => b.Value.ToString(),
                PackStreamList l => "<list:" + l.Items.Count + ">",
                PackStreamMap mm => "<map:" + mm.Entries.Count + ">",
                _ => e.Value.GetType().Name
            };
        }

        return d;
    }

    public static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: B3BQ3.IndependentProbe <output.json> <q2-before.json> <q2-after.json>");
            return 2;
        }

        F01Matrix();
        OverlapScenarios();
        RecoveryRefusal();
        MetadataScenarios();
        OwnershipTiming();
        SnapshotAndPublication();
        CombinedAdversarial();
        X04Contract();
        DiagnosticsAndConfidentiality();
        NegativeControls();

        // The recorded response surface must never contain provider exception text or sentinels.
        var traceJson = JsonSerializer.Serialize(Trace);
        Check(
            "trace-sentinel-scan",
            !traceJson.Contains(SentinelRead, StringComparison.Ordinal)
            && !traceJson.Contains(SentinelMeta, StringComparison.Ordinal)
            && !traceJson.Contains(CleanupText, StringComparison.Ordinal),
            "no provider exception text in the recorded response surface",
            traceJson.Contains(SentinelRead, StringComparison.Ordinal) ? "read sentinel leaked"
            : traceJson.Contains(SentinelMeta, StringComparison.Ordinal) ? "metadata sentinel leaked"
            : traceJson.Contains(CleanupText, StringComparison.Ordinal) ? "cleanup text leaked"
            : "clean");

        var recon = Q2Reconciliation(args[1], args[2]);

        var failed = Checks.Count(c => !c.Pass);
        var output = new
        {
            probe = "B3-B-Q3 independent resource-lifecycle requalification probe",
            reviewed_head = "b9356c3fadb484b9b3a39d807205283155886184",
            r2_implementation_commit = "7eb9a8fcee5d4d89d333a920a6c3307e84ad4210",
            checks = Checks.Count,
            failed,
            traces = Trace.Count,
            q2_reconciliation = recon,
            results = Checks,
            trace = Trace
        };
        File.WriteAllText(args[0], JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine($"checks={Checks.Count} passed={Checks.Count - failed} failed={failed} traces={Trace.Count}");
        foreach (var c in Checks.Where(c => !c.Pass))
        {
            Console.WriteLine($"FAIL {c.Id}: expected={c.Expected};actual={c.Actual}");
        }

        return failed == 0 ? 0 : 4;
    }
}
