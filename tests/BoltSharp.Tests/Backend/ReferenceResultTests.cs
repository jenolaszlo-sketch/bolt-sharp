using BoltSharp.Backend;
using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.Backend;

/// <summary>B3-C4: result cursor lifecycle — incremental reads, exhaustion and disposal.</summary>
public sealed class ReferenceResultTests
{
    private static ReferenceResultOperation GraphQueryResult() =>
        (ReferenceResultOperation)new ReferenceOperationProvider().Prepare(
            BackendTestSupport.Run(ReferenceQueryEvaluator.GraphQueryText))!;

    private static ReferenceResultOperation PropertyQueryResult() =>
        (ReferenceResultOperation)new ReferenceOperationProvider().Prepare(
            BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText))!;

    [Fact]
    public void ReadRow_IsIncrementalAndStableAfterExhaustion()
    {
        var operation = PropertyQueryResult();

        var first = operation.ReadRow();
        Assert.NotNull(first);
        Assert.Equal("Alice", Assert.IsType<PackStreamString>(Assert.Single(first!)).Value);

        var second = operation.ReadRow();
        Assert.NotNull(second);
        Assert.Equal("Bob", Assert.IsType<PackStreamString>(Assert.Single(second!)).Value);

        // Exhaustion is stable: repeated reads keep returning null, never resurrect records.
        Assert.Null(operation.ReadRow());
        Assert.Null(operation.ReadRow());
    }

    [Fact]
    public void Dispose_IsIdempotent_AndUseAfterDisposeThrows()
    {
        var operation = GraphQueryResult();
        operation.Dispose();
        operation.Dispose();

        Assert.Throws<ObjectDisposedException>(() => operation.ReadRow());
        Assert.Throws<ObjectDisposedException>(() => operation.ReadRow());
    }

    [Fact]
    public void Construction_TakesDefensiveCopies()
    {
        var fields = new List<string> { "answer" };
        var records = new List<IReadOnlyList<PackStreamValue?>>
        {
            new PackStreamValue?[] { new PackStreamInteger(1) },
        };
        var operation = new ReferenceResultOperation(fields, records);

        fields[0] = "mutated";
        ((PackStreamValue?[])records[0])[0] = new PackStreamInteger(999);

        Assert.Equal("answer", Assert.Single(operation.Fields));
        var row = Assert.Single(operation.ReadRow()!);
        Assert.Equal(1, Assert.IsType<PackStreamInteger>(row).Value);
    }

    [Fact]
    public void EmptyResult_ExhaustsImmediately()
    {
        var operation = new ReferenceResultOperation(new[] { "x" }, Array.Empty<IReadOnlyList<PackStreamValue?>>());

        Assert.Null(operation.ReadRow());
        operation.Dispose();
    }
}
