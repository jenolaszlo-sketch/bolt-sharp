using BoltSharp.Backend;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Tests.Sessions;

using Xunit;

namespace BoltSharp.Tests.Backend;

/// <summary>Shared builders and fault-injection wrappers for the B3-C backend tests.</summary>
internal static class BackendTestSupport
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

    public static RunRequest Run(string query, PackStreamMap? parameters = null, PackStreamMap? extra = null) =>
        new(query, parameters ?? Map(), extra ?? Map());

    public static BoltConnectionSession Authenticated(IBoltOperationProvider provider) =>
        SessionHarness.Authenticated(provider);

    public static long? IntegerOf(BoltResponse response, int recordIndex = 0) =>
        Assert.IsType<PackStreamInteger>(
            Assert.IsType<RecordResponse>(response).Data.Items[recordIndex]).Value;

    public static string? StringOf(BoltResponse response, int recordIndex = 0) =>
        Assert.IsType<PackStreamString>(
            Assert.IsType<RecordResponse>(response).Data.Items[recordIndex]).Value;
}

/// <summary>Wraps the real reference provider and counts prepares plus per-operation usage.</summary>
internal sealed class CountingProvider : IBoltOperationProvider
{
    public readonly ReferenceOperationProvider Inner;
    public int PrepareCount;
    public readonly List<CountingOperation> Operations = new();

    public CountingProvider(ReferenceGraph? graph = null) => Inner = new ReferenceOperationProvider(graph);

    public IBoltOperation? Prepare(RunRequest run)
    {
        PrepareCount++;
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

/// <summary>Counts metadata reads, record reads and disposal attempts over a real operation.</summary>
internal sealed class CountingOperation : IBoltOperation
{
    public readonly IBoltOperation Inner;
    public int DisposeCount;
    public int ReadCount;
    public int FieldsCount;

    public CountingOperation(IBoltOperation inner) => Inner = inner;

    public IReadOnlyList<string> Fields
    {
        get
        {
            FieldsCount++;
            return Inner.Fields;
        }
    }

    public IReadOnlyList<PackStreamValue?>? ReadRow()
    {
        ReadCount++;
        return Inner.ReadRow();
    }

    public void Dispose()
    {
        DisposeCount++;
        Inner.Dispose();
    }
}

/// <summary>
/// Wraps the real reference provider and can inject controlled failures at every documented
/// point (prepare, metadata, reads, record shape, disposal and combinations).
/// </summary>
internal sealed class FaultyProvider : IBoltOperationProvider
{
    public readonly ReferenceOperationProvider Inner = new(ReferenceGraph.Default);
    public int PrepareCount;
    public bool ThrowOnPrepare;
    public Func<IBoltOperation, FaultyOperation>? Wrap;
    public readonly List<FaultyOperation> Operations = new();

    public IBoltOperation? Prepare(RunRequest run)
    {
        PrepareCount++;
        if (ThrowOnPrepare)
        {
            throw new InvalidOperationException("B3-C test prepare failure");
        }

        var operation = Inner.Prepare(run);
        if (operation is null)
        {
            return null;
        }

        var faulty = (Wrap ?? (o => new FaultyOperation(o)))(operation);
        Operations.Add(faulty);
        return faulty;
    }
}

/// <summary>Fault-injecting wrapper over a real reference operation.</summary>
internal sealed class FaultyOperation : IBoltOperation
{
    private readonly IBoltOperation _inner;

    public FaultyOperation(IBoltOperation inner) => _inner = inner;

    public int DisposeAttempts;
    public int ReadCalls;
    public int FieldsReads;
    public bool ThrowOnFields;
    public bool NullFields;
    public bool NullElement;
    public int ThrowOnReadNumber;
    public int RowWidthOverride = -1;
    public bool ThrowOnDispose;
    public Exception CleanupError = new InvalidOperationException("B3-C test disposal failure");

    public IReadOnlyList<string> Fields
    {
        get
        {
            FieldsReads++;
            if (ThrowOnFields)
            {
                throw new InvalidOperationException("B3-C test metadata failure");
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
            throw new InvalidOperationException("B3-C test read failure");
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
