using BoltSharp.Backend;
using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.Backend;

/// <summary>B3-C3: the explicitly allowlisted supported-query surface (evaluator level).</summary>
public sealed class SupportedQueryTests
{
    private static readonly ReferenceQueryEvaluator Evaluator = new();

    [Fact]
    public void ScalarQuery_ReturnsAnswer42()
    {
        var result = Evaluator.Evaluate("RETURN 42 AS answer", BackendTestSupport.Map());

        Assert.True(result.IsSupported);
        Assert.Null(result.RefusalReason);
        Assert.Equal(new[] { "answer" }, result.Fields);
        var record = Assert.Single(result.Records);
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(Assert.Single(record)).Value);
    }

    [Fact]
    public void ScalarQuery_AcceptsWhitespaceNormalizationOnly()
    {
        var result = Evaluator.Evaluate("  RETURN   42   AS\tanswer \n", BackendTestSupport.Map());

        Assert.True(result.IsSupported);
        Assert.Equal(new[] { "answer" }, result.Fields);
    }

    [Fact]
    public void ScalarQuery_IsCaseSensitive()
    {
        var result = Evaluator.Evaluate("return 42 as answer", BackendTestSupport.Map());

        Assert.False(result.IsSupported);
        Assert.Equal(ReferenceQueryEvaluator.UnsupportedQueryReason, result.RefusalReason);
    }

    [Theory]
    [InlineData("RETURN 42 AS answer; MATCH (n) DELETE n")]
    [InlineData("RETURN 42 AS answer // trailing comment")]
    [InlineData("RETURN 42 AS answer2")]
    [InlineData("MATCH (n) DETACH DELETE n")]
    [InlineData("CREATE (n:Person)")]
    [InlineData("MATCH (n:Person) RETURN n")]
    [InlineData("MATCH (n:Person) RETURN n.name ORDER BY name")]
    [InlineData("MATCH (n:Person) RETURN n.name AS name")]
    [InlineData("RETURN 41 AS answer")]
    [InlineData("")]
    [InlineData("MATCH (a:Person)-[r:KNOWN]->(b:Person) RETURN a, r, b")]
    public void UnsupportedQueries_AreRefused(string query)
    {
        var result = Evaluator.Evaluate(query, BackendTestSupport.Map());

        Assert.False(result.IsSupported);
        Assert.Equal(ReferenceQueryEvaluator.UnsupportedQueryReason, result.RefusalReason);
        Assert.Empty(result.Fields);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void PropertyQuery_ReturnsAliceThenBobFromTheGraph()
    {
        var result = Evaluator.Evaluate("MATCH (n:Person) RETURN n.name", BackendTestSupport.Map());

        Assert.True(result.IsSupported);
        Assert.Equal(new[] { "n.name" }, result.Fields);
        Assert.Equal(2, result.Records.Count);
        Assert.Equal("Alice", Value(result.Records[0]));
        Assert.Equal("Bob", Value(result.Records[1]));
    }

    [Fact]
    public void OrderedPropertyQuery_ReturnsAscendingNames()
    {
        var result = Evaluator.Evaluate(
            "MATCH (n:Person) RETURN n.name AS name ORDER BY name", BackendTestSupport.Map());

        Assert.True(result.IsSupported);
        Assert.Equal(new[] { "name" }, result.Fields);
        Assert.Equal(new[] { "Alice", "Bob" }, result.Records.Select(Value));
    }

    [Fact]
    public void GraphQuery_ReturnsGraphInstancesInOrder()
    {
        var graph = ReferenceGraph.Default;
        var evaluator = new ReferenceQueryEvaluator(graph);

        var result = evaluator.Evaluate(
            "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b", BackendTestSupport.Map());

        Assert.True(result.IsSupported);
        Assert.Equal(new[] { "a", "r", "b" }, result.Fields);
        var record = Assert.Single(result.Records);

        // The record must reference the graph's own instances: proof the result is derived from
        // the graph model rather than a detached hard-coded record.
        Assert.Same(graph.Nodes[0], record[0]);
        Assert.Same(graph.Relationships[0], record[1]);
        Assert.Same(graph.Nodes[1], record[2]);

        var relationship = Assert.IsType<PackStreamRelationship>(record[1]);
        Assert.Equal("KNOWS", relationship.Type);
        Assert.Equal(1, relationship.StartNodeId);
        Assert.Equal(2, relationship.EndNodeId);
        Assert.Equal("n:1", relationship.StartNodeElementId);
        Assert.Equal("n:2", relationship.EndNodeElementId);
    }

    [Fact]
    public void ParameterQuery_PassesTypedScalarsThrough()
    {
        var integer = Evaluator.Evaluate(
            "RETURN $value AS answer", BackendTestSupport.Map(("value", new PackStreamInteger(42))));
        Assert.True(integer.IsSupported);
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(Assert.Single(Assert.Single(integer.Records))).Value);

        var text = Evaluator.Evaluate(
            "RETURN $value AS answer", BackendTestSupport.Map(("value", new PackStreamString("DELETE n"))));
        Assert.Equal("DELETE n", Assert.IsType<PackStreamString>(Assert.Single(Assert.Single(text.Records))).Value);

        var boolean = Evaluator.Evaluate(
            "RETURN $value AS answer", BackendTestSupport.Map(("value", new PackStreamBoolean(true))));
        Assert.True(Assert.IsType<PackStreamBoolean>(Assert.Single(Assert.Single(boolean.Records))).Value);
    }

    [Fact]
    public void ParameterQuery_ExplicitNullRetainsNull()
    {
        var result = Evaluator.Evaluate(
            "RETURN $value AS answer", BackendTestSupport.Map(("value", null)));

        Assert.True(result.IsSupported);
        Assert.Null(Assert.Single(Assert.Single(result.Records)));
    }

    [Fact]
    public void ParameterQuery_MissingParameterIsRefused()
    {
        var result = Evaluator.Evaluate("RETURN $value AS answer", BackendTestSupport.Map());

        Assert.False(result.IsSupported);
        Assert.Equal(ReferenceQueryEvaluator.MissingParameterReason, result.RefusalReason);
    }

    [Fact]
    public void ParameterQuery_UnreferencedParametersAreIgnored()
    {
        var result = Evaluator.Evaluate(
            "RETURN $value AS answer",
            BackendTestSupport.Map(("value", new PackStreamInteger(1)), ("other", new PackStreamString("x"))));

        Assert.True(result.IsSupported);
        Assert.Equal(1, Assert.IsType<PackStreamInteger>(Assert.Single(Assert.Single(result.Records))).Value);
    }

    private static string? Value(IReadOnlyList<PackStreamValue?> record) =>
        Assert.IsType<PackStreamString>(Assert.Single(record)).Value;
}
