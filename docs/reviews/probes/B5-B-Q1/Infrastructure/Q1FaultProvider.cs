using BoltSharp.Backend;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

namespace BoltSharp.B5BQ1.Probes.Infrastructure;

/// <summary>
/// Reviewer-owned fault-injecting operation provider. One instance is created per physical server
/// connection (the transport calls <c>OperationProviderFactory</c> once per accepted connection),
/// so the reviewer's per-instance counters are a per-physical-connection oracle:
/// Prepare calls, operations opened, Dispose attempts and Dispose failures are tracked per
/// connection. Faults are armed per test; the default behavior delegates to the accepted
/// B3-C reference backend unchanged.
/// </summary>
internal sealed class Q1ConnectionProvider : IBoltOperationProvider
{
    private readonly ReferenceOperationProvider _inner = new();

    /// <summary>Number of Prepare invocations on this connection.</summary>
    public int PrepareCalls;

    /// <summary>Number of operations actually opened (returned to the session) on this connection.</summary>
    public int OperationsOpened;

    /// <summary>Number of operation Dispose invocations on this connection.</summary>
    public int DisposeCalls;

    /// <summary>Number of operation Dispose invocations that threw on this connection.</summary>
    public int DisposeFailures;

    /// <summary>Number of ReadRow invocations on this connection.</summary>
    public int ReadRowCalls;

    /// <summary>When armed, the next operation Dispose throws (fail-closed retirement probe).</summary>
    public bool DisposeShouldThrow;

    /// <summary>When armed, Prepare throws (recoverable provider-failure probe).</summary>
    public bool PrepareShouldThrow;

    public IBoltOperation? Prepare(RunRequest run)
    {
        Interlocked.Increment(ref PrepareCalls);
        if (PrepareShouldThrow)
        {
            throw new InvalidOperationException("q1 reviewer fault: provider Prepare failure");
        }

        var operation = _inner.Prepare(run);
        if (operation is null)
        {
            return null;
        }

        Interlocked.Increment(ref OperationsOpened);
        return new Q1FaultOperation(operation, this);
    }

    private sealed class Q1FaultOperation : IBoltOperation
    {
        private readonly IBoltOperation _inner;
        private readonly Q1ConnectionProvider _owner;

        public Q1FaultOperation(IBoltOperation inner, Q1ConnectionProvider owner)
        {
            _inner = inner;
            _owner = owner;
        }

        public IReadOnlyList<string> Fields => _inner.Fields;

        public IReadOnlyList<PackStreamValue?>? ReadRow()
        {
            Interlocked.Increment(ref _owner.ReadRowCalls);
            return _inner.ReadRow();
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _owner.DisposeCalls);
            if (_owner.DisposeShouldThrow)
            {
                Interlocked.Increment(ref _owner.DisposeFailures);
                throw new InvalidOperationException("q1 reviewer fault: operation retirement failure");
            }

            _inner.Dispose();
        }
    }
}

/// <summary>
/// A per-test collection of per-connection providers, mirroring the server's physical connections.
/// Index 0 corresponds to the first accepted connection that reached the session stage.
/// </summary>
internal sealed class Q1ProviderLog
{
    private readonly List<Q1ConnectionProvider> _providers = new();
    private readonly object _lock = new();

    /// <summary>
    /// One-shot arming flag: when set before the first connection opens, the first provider
    /// instance (first physical connection) is created with its Dispose fault armed. This exists
    /// because the provider factory runs only when a connection reaches the session stage.
    /// </summary>
    public bool ArmFirstDisposeFault;

    /// <summary>One-shot arming flag for a Prepare fault on the first connection.</summary>
    public bool ArmFirstPrepareFault;

    internal Func<IBoltOperationProvider> Factory { get; }

    public Q1ProviderLog()
    {
        Factory = () =>
        {
            var provider = new Q1ConnectionProvider();
            lock (_lock)
            {
                if (ArmFirstDisposeFault)
                {
                    provider.DisposeShouldThrow = true;
                    ArmFirstDisposeFault = false;
                }

                if (ArmFirstPrepareFault)
                {
                    provider.PrepareShouldThrow = true;
                    ArmFirstPrepareFault = false;
                }

                _providers.Add(provider);
            }

            return provider;
        };
    }

    /// <summary>Snapshots the current per-connection provider list.</summary>
    public IReadOnlyList<Q1ConnectionProvider> Providers
    {
        get
        {
            lock (_lock)
            {
                return _providers.ToArray();
            }
        }
    }

    /// <summary>Total operations opened across all connections.</summary>
    public int TotalOperationsOpened => Providers.Sum(p => p.OperationsOpened);

    /// <summary>Total Dispose calls across all connections.</summary>
    public int TotalDisposeCalls => Providers.Sum(p => p.DisposeCalls);
}
