using BoltSharp.Backend;
using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.Backend;

/// <summary>
/// B3-C3/B3-C1: the provider-level read-only capability boundary. Unsupported queries and unsafe
/// RUN options are refused with <see langword="null"/> (the session's deterministic 50N42
/// recoverable FAILURE); nothing is executed, nothing is fabricated.
/// </summary>
public sealed class ReadOnlyBoundaryTests
{
    private static readonly ReferenceOperationProvider Provider = new();

    [Fact]
    public void SupportedQueries_ArePrepared()
    {
        Assert.NotNull(Provider.Prepare(BackendTestSupport.Run(ReferenceQueryEvaluator.ScalarQueryText)));
        Assert.NotNull(Provider.Prepare(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText)));
        Assert.NotNull(Provider.Prepare(BackendTestSupport.Run(
            ReferenceQueryEvaluator.GraphQueryText)));
        Assert.NotNull(Provider.Prepare(BackendTestSupport.Run(
            ReferenceQueryEvaluator.ParameterQueryText,
            BackendTestSupport.Map(("value", new PackStreamInteger(1))))));
        Assert.NotNull(Provider.Prepare(BackendTestSupport.Run(
            ReferenceQueryEvaluator.ScalarQueryText,
            extra: BackendTestSupport.Map(("mode", new PackStreamString("r"))))));
    }

    [Theory]
    [InlineData("MATCH (n) DETACH DELETE n")]
    [InlineData("CREATE (n:Person {name: 'Mallory'})")]
    [InlineData("MERGE (n:Person)")]
    [InlineData("MATCH (n:Person) SET n.name = 'x'")]
    [InlineData("MATCH (n:Person) REMOVE n.name")]
    [InlineData("DROP INDEX x")]
    [InlineData("CALL db.labels()")]
    [InlineData("RETURN 42 AS answer; CREATE (n)")]
    [InlineData("/* supported phrase */ RETURN 42 AS answer")]
    [InlineData("RETURN 1 AS answer")]
    public void UnsupportedAndWriteQueries_ReturnNull(string query)
    {
        Assert.Null(Provider.Prepare(BackendTestSupport.Run(query)));
    }

    [Theory]
    [InlineData("w")]
    [InlineData("rw")]
    [InlineData("write")]
    public void WriteMode_IsRefused(string mode)
    {
        Assert.Null(Provider.Prepare(BackendTestSupport.Run(
            ReferenceQueryEvaluator.ScalarQueryText,
            extra: BackendTestSupport.Map(("mode", new PackStreamString(mode))))));
    }

    [Fact]
    public void DatabaseSelection_IsRefused_ExplicitNullIsAccepted()
    {
        Assert.Null(Provider.Prepare(BackendTestSupport.Run(
            ReferenceQueryEvaluator.ScalarQueryText,
            extra: BackendTestSupport.Map(("db", new PackStreamString("neo4j"))))));

        Assert.NotNull(Provider.Prepare(BackendTestSupport.Run(
            ReferenceQueryEvaluator.ScalarQueryText,
            extra: BackendTestSupport.Map(("db", null)))));
    }

    [Fact]
    public void Impersonation_IsRefused_ExplicitNullIsAccepted()
    {
        Assert.Null(Provider.Prepare(BackendTestSupport.Run(
            ReferenceQueryEvaluator.ScalarQueryText,
            extra: BackendTestSupport.Map(("imp_user", new PackStreamString("alice"))))));

        Assert.NotNull(Provider.Prepare(BackendTestSupport.Run(
            ReferenceQueryEvaluator.ScalarQueryText,
            extra: BackendTestSupport.Map(("imp_user", null)))));
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("tx_timeout")]
    [InlineData("bookmarks")]
    [InlineData("tx_metadata")]
    [InlineData("notifications_minimum_severity")]
    [InlineData("unknown_option")]
    public void UnknownRunOptions_AreRefused(string key)
    {
        Assert.Null(Provider.Prepare(BackendTestSupport.Run(
            ReferenceQueryEvaluator.ScalarQueryText,
            extra: BackendTestSupport.Map((key, new PackStreamString("x"))))));
    }

    [Fact]
    public void CapabilityPolicy_ReportsDiagnosticReasons()
    {
        Assert.True(ReferenceCapabilityPolicy.TryAcceptExtras(BackendTestSupport.Map(), out var reason));
        Assert.Null(reason);

        Assert.False(ReferenceCapabilityPolicy.TryAcceptExtras(
            BackendTestSupport.Map(("mode", new PackStreamString("w"))), out reason));
        Assert.Equal(ReferenceCapabilityPolicy.UnsupportedOptionReason, reason);
    }

    [Fact]
    public void PreparedOperation_ExposesFieldsWithoutReadingRecords()
    {
        var operation = Provider.Prepare(BackendTestSupport.Run(ReferenceQueryEvaluator.GraphQueryText));

        Assert.NotNull(operation);
        Assert.Equal(new[] { "a", "r", "b" }, operation!.Fields);
        var row = operation.ReadRow();
        Assert.NotNull(row);
        Assert.Equal(3, row!.Count);
        Assert.Null(operation.ReadRow());
    }
}
