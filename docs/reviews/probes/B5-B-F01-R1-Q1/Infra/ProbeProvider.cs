// B5-B-F01-R1-Q1 reviewer probe — reviewer-owned operation providers (independent of all
// BoltSharp test helpers): a counting provider, a scripted rows provider and a fault-injecting
// provider whose Dispose fails exactly on command (fail-closed retirement control).
using BoltSharp.Backend;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

namespace BoltSharp.B5BF01R1Q1.Probes.Infra;

public sealed class ProbeVerifier : IBoltCredentialVerifier
{
    public bool Verify(LogonRequest request) => true;
}

/// <summary>
/// The accepted B3-C reference backend with reviewer-owned counters, used wherever the real
/// query allowlist AND provider-counting assertions are needed at the same time.
/// </summary>
public sealed class CountingReferenceProvider : IBoltOperationProvider
{
    private readonly ReferenceOperationProvider _inner = new();

    public int PrepareCount;

    public int SuccessCount;

    public int DisposeAttempts;

    public IBoltOperation? Prepare(RunRequest run)
    {
        Interlocked.Increment(ref PrepareCount);
        var operation = _inner.Prepare(run);
        if (operation is null)
        {
            return null; // refused by the accepted B3-C allowlist (counted, not successful)
        }

        Interlocked.Increment(ref SuccessCount);
        return new CountingOperation(operation, this);
    }

    private sealed class CountingOperation : IBoltOperation
    {
        private readonly IBoltOperation _inner;
        private readonly CountingReferenceProvider _owner;

        public CountingOperation(IBoltOperation inner, CountingReferenceProvider owner)
        {
            _inner = inner;
            _owner = owner;
        }

        public IReadOnlyList<string> Fields => _inner.Fields;

        public IReadOnlyList<PackStreamValue?>? ReadRow() => _inner.ReadRow();

        public void Dispose()
        {
            Interlocked.Increment(ref _owner.DisposeAttempts);
            _inner.Dispose();
        }
    }
}

public sealed class ProbeProvider : IBoltOperationProvider
{
    private int _prepareCount;
    private int _disposeAttempts;
    private int _disposeFailures;

    public int PrepareCount => _prepareCount;

    public int DisposeAttempts => _disposeAttempts;

    public int DisposeFailures => _disposeFailures;

    /// <summary>When true, the NEXT Dispose attempt throws and failure is recorded exactly once.</summary>
    public bool FailNextDisposal { get; set; }

    public IBoltOperation? Prepare(RunRequest run)
    {
        Interlocked.Increment(ref _prepareCount);
        return new ProbeOperation(this, run.Query);
    }

    private sealed class ProbeOperation : IBoltOperation
    {
        private readonly ProbeProvider _owner;
        private readonly string[] _fields = { "answer" };
        private readonly PackStreamValue?[] _row = { new PackStreamInteger(42) };
        private bool _rowRead;
        private bool _disposed;

        public ProbeOperation(ProbeProvider owner, string query)
        {
            _owner = owner;
            Query = query;
        }

        public string Query { get; }

        public IReadOnlyList<string> Fields => _fields;

        public IReadOnlyList<PackStreamValue?>? ReadRow()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_rowRead)
            {
                return null; // single-row result: exhausted after one record
            }

            _rowRead = true;
            return _row;
        }

        public void Dispose()
        {
            // Count EVERY call: the "no disposal retry" invariant is only observable if a second
            // call would be visible here. (The exactly-once contract lives in the caller.)
            Interlocked.Increment(ref _owner._disposeAttempts);
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_owner.FailNextDisposal)
            {
                _owner.FailNextDisposal = false;
                Interlocked.Increment(ref _owner._disposeFailures);
                throw new InvalidOperationException("injected disposal failure (reviewer fault injection)");
            }
        }
    }
}
