using System.Text.Json;

using BoltSharp.Backend;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

namespace B3BQ1C.IndependentProbe;

/// <summary>Base class for reviewer providers: a visible prepare counter for the probes.</summary>
internal abstract class ProviderBase : IBoltOperationProvider
{
    public int Prepares { get; private set; }

    protected void CountPrepare() => Prepares++;

    public abstract IBoltOperation? Prepare(RunRequest run);
}

// B3-C-Q1 independent reviewer probe. Reviewer-authored; no producer test or evidence code reuse.
// Expected values are constructed from the spec (docs/boltsharp-spec.md §7) and the retained
// B0-B-Q1 standalone decode of the authentic t04 driver fixture — never from production code.
internal static partial class Program
{
    // Reviewer oracle constants (independent of the producer's test fixtures).
    internal const string AliceElementId = "n:1";
    internal const string BobElementId = "n:2";
    internal const string KnowsElementId = "r:1";
    internal const long SinceValue = 2024;

    // Authentic B0-B t03/t04 wire bytes (byte-identical from B3-A d99096a through the reviewed
    // revision; independently decoded by B0-B-Q1's standalone PackStream-subset decoder).
    internal const string T04GraphRecordHex =
        "B17193B44E019186506572736F6EA1846E616D6585416C696365836E3A31" +
        "B852010102854B4E4F5753A18573696E6365C907E883723A31836E3A31836E3A32" +
        "B44E029186506572736F6EA1846E616D6583426F62836E3A32";
    internal const string T03RunHex =
        "B310D01352455455524E20343220415320616E73776572A0A1846D6F64658172";
    internal const string T03PullHex = "B13FA1816EC903E8";
    internal const string T03Record42Hex = "B171912A";
    internal const string T03RunSuccessFieldsAnswerHex = "B170A1866669656C64739186616E73776572";
    internal const string T03RunSuccessFieldsGraphHex = "B170A1866669656C647393816181728162";

    private static readonly List<Q1Check> Checks = new();
    private static readonly List<object> Trace = new();
    private static readonly BoltMessageCodec Codec = new();

    internal sealed record Q1Check(string Id, bool Pass, string Expected, string Actual);

    internal static void Check(string id, bool condition, string expected, string actual) =>
        Checks.Add(new Q1Check(id, condition, expected, actual));

    /// <summary>Reviewer type assertion (the probe does not reference xUnit).</summary>
    internal static T IsType<T>(object? value) where T : class
    {
        if (value is not T typed)
        {
            throw new InvalidOperationException(
                $"reviewer assertion failed: expected {typeof(T).Name}, got {value?.GetType().Name ?? "null"}");
        }

        return typed;
    }

    /// <summary>Reviewer single-item assertion (the probe does not reference xUnit).</summary>
    internal static T SingleOf<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        if (list.Count != 1)
        {
            throw new InvalidOperationException($"reviewer assertion failed: expected exactly one item, got {list.Count}");
        }

        return list[0];
    }

    /// <summary>Reviewer single-item assertion with a predicate.</summary>
    internal static T SingleOf<T>(IEnumerable<T> items, Func<T, bool> predicate) =>
        SingleOf(items.Where(predicate));

    internal static PackStreamMap Map(params (string K, PackStreamValue? V)[] pairs) =>
        new(pairs.Select(p => new KeyValuePair<string, PackStreamValue?>(p.K, p.V)).ToArray());

    internal static RunRequest Run(string query, PackStreamMap? parameters = null, PackStreamMap? extra = null) =>
        new(query, parameters ?? Map(), extra ?? Map());

    internal static HelloRequest Hello() => new(Map(
        ("user_agent", new PackStreamString("b3cq1/1")),
        ("bolt_agent", Map(("product", new PackStreamString("b3cq1/1"))))));

    internal static LogonRequest Logon() => new(Map(("scheme", new PackStreamString("none"))), BoltAuthScheme.None);

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

    internal sealed class CountingProvider : ProviderBase
    {
        public readonly ReferenceOperationProvider Inner = new();
        public readonly List<CountingOperation> Operations = new();

        public override IBoltOperation? Prepare(RunRequest run)
        {
            CountPrepare();
            var operation = Inner.Prepare(run);
            if (operation is null)
            {
                return null;
            }

            var counted = new CountingOperation(operation);
            Operations.Add(counted);
            return counted;
        }
    }

    internal sealed class CountingOperation : IBoltOperation
    {
        public readonly IBoltOperation Inner;
        public int DisposeAttempts;
        public int ReadCalls;
        public int FieldsReads;

        public CountingOperation(IBoltOperation inner) => Inner = inner;

        public IReadOnlyList<string> Fields
        {
            get { FieldsReads++; return Inner.Fields; }
        }

        public IReadOnlyList<PackStreamValue?>? ReadRow()
        {
            ReadCalls++;
            return Inner.ReadRow();
        }

        public void Dispose()
        {
            DisposeAttempts++;
            Inner.Dispose();
        }
    }

    /// <summary>Reviewer fault wrapper over a real reference operation.</summary>
    internal sealed class FaultOperation : IBoltOperation
    {
        private readonly IBoltOperation _inner;

        public FaultOperation(IBoltOperation inner) => _inner = inner;

        public int DisposeAttempts, ReadCalls, FieldsReads;
        public bool ThrowOnFields;
        public bool NullFields;
        public bool NullElement;
        public int ThrowOnReadNumber;
        public int RowWidthOverride = -1;
        public bool ThrowOnDispose;
        public Exception CleanupError = new InvalidOperationException("B3-C-Q1 reviewer disposal failure");

        public IReadOnlyList<string> Fields
        {
            get
            {
                FieldsReads++;
                if (ThrowOnFields)
                {
                    throw new InvalidOperationException("B3-C-Q1 reviewer metadata failure");
                }

                if (NullFields)
                {
                    return null!;
                }

                if (NullElement)
                {
                    return new string[] { null! };
                }

                return _inner.Fields;
            }
        }

        public IReadOnlyList<PackStreamValue?>? ReadRow()
        {
            ReadCalls++;
            if (ThrowOnReadNumber > 0 && ReadCalls == ThrowOnReadNumber)
            {
                throw new InvalidOperationException("B3-C-Q1 reviewer read failure");
            }

            if (RowWidthOverride >= 0)
            {
                var row = _inner.ReadRow();
                if (row is null)
                {
                    return null;
                }

                var widened = new PackStreamValue?[RowWidthOverride];
                for (var i = 0; i < widened.Length; i++)
                {
                    widened[i] = new PackStreamInteger(i);
                }

                return widened;
            }

            return _inner.ReadRow();
        }

        public void Dispose()
        {
            DisposeAttempts++;
            if (ThrowOnDispose)
            {
                throw CleanupError;
            }

            _inner.Dispose();
        }
    }

    internal sealed class FaultProvider : ProviderBase
    {
        public readonly ReferenceOperationProvider Inner = new();
        public bool ThrowOnPrepare;
        public Func<IBoltOperation, FaultOperation>? Wrap;
        public readonly List<FaultOperation> Operations = new();

        public override IBoltOperation? Prepare(RunRequest run)
        {
            CountPrepare();
            if (ThrowOnPrepare)
            {
                throw new InvalidOperationException("B3-C-Q1 reviewer prepare failure");
            }

            var operation = Inner.Prepare(run);
            if (operation is null)
            {
                return null;
            }

            var faulty = (Wrap ?? (o => new FaultOperation(o)))(operation);
            Operations.Add(faulty);
            return faulty;
        }
    }

    internal sealed class ProbeSession
    {
        public string Id;
        public ProviderBase Provider;
        public BoltConnectionSession Session;
        public CountingVerifier Verifier;
        public Exception? Escaped;

        public ProbeSession(string id, ProviderBase provider)
        {
            Id = id;
            Provider = provider;
            Verifier = new CountingVerifier();
            Session = new BoltConnectionSession(
                provider,
                new BoltSessionOptions { Verifier = Verifier, MaxRecordsPerResponse = 2 });
        }

        public static ProbeSession Authenticated(string id, ProviderBase provider)
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

        public BoltSessionResult? SendRaw(string action, byte[] payload)
        {
            var start = Session.State;
            Escaped = null;
            BoltSessionResult? r = null;
            try
            {
                r = Session.ReceiveBytes(payload);
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
                cleanup = Session.LastCleanupFailure?.GetType().Name
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
            },
            record_values = r is RecordResponse rec
                ? rec.Data.Items.Select(ValueOf).ToArray()
                : null
        };
    }

    internal static object? ValueOf(PackStreamValue? v) => v switch
    {
        null => null,
        PackStreamString s => s.Value,
        PackStreamInteger i => i.Value,
        PackStreamBoolean b => b.Value,
        PackStreamNode n => new { node = new { n.Id, n.ElementId, labels = n.Labels, name = (n.Properties.Entries.FirstOrDefault(e => e.Key == "name").Value as PackStreamString)?.Value } },
        PackStreamRelationship r => new { relationship = new { r.Id, r.StartNodeId, r.EndNodeId, r.Type, r.ElementId, r.StartNodeElementId, r.EndNodeElementId, since = (r.Properties.Entries.FirstOrDefault(e => e.Key == "since").Value as PackStreamInteger)?.Value } },
        PackStreamList l => l.Items.Select(ValueOf).ToArray(),
        _ => v.GetType().Name
    };

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
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: B3BQ1C.IndependentProbe <output.json>");
            return 2;
        }

        GraphOracle();
        AlternateFixture();
        SessionFlows();
        AllowlistBattery();
        OptionsMatrix();
        ParameterBattery();
        CursorLifecycle();
        FailClosedLifecycle();
        ByteOracle();
        NegativeControls();

        var failed = Checks.Count(c => !c.Pass);
        var output = new
        {
            probe = "B3-C-Q1 independent reference-backend qualification probe",
            reviewed_head = "79ddd04da40d5c6eb7fb8dc0177ef7188b613e75",
            implementation_commit = "37fe40633624a62bc4c6bd6162de2c884e1f0188",
            checks = Checks.Count,
            failed,
            traces = Trace.Count,
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
