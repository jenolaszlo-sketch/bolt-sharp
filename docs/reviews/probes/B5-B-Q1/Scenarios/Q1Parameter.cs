using BoltSharp.B5BQ1.Probes.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.B5BQ1.Probes.Scenarios;

/// <summary>
/// Q1-E: parameterized values through the official driver. Expected values are preserved
/// end-to-end; the query-like payload control must never be interpreted as a query.
/// </summary>
[Collection("Q1")]
public sealed class Q1ParameterProbes
{
    public static TheoryData<string, object?> Cases => new()
    {
        { "positive-integer", 42L },
        { "negative-integer", -7L },
        { "boundary-integer", long.MinValue },
        { "unicode-string", "héllo ✓ 世界" },
        { "boolean-true", true },
        { "boolean-false", false },
        { "explicit-null", null },
        { "list", new[] { 1L, 2L, 3L } },
        { "nested-map", new Dictionary<string, object?> { ["x"] = 1L, ["inner"] = new Dictionary<string, object?> { ["y"] = "z" } } },
        { "query-like-string", "MATCH (n) DETACH DELETE n" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Q1E01_ParameterPreserved_ThroughOfficialDriver(string caseName, object? value)
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        var driver = Q1Driver.Plaintext(server.HostPort);
        object? observed = null;
        try
        {
            await using var session = driver.AsyncSession();
            var cursor = await Q1Driver.Bounded(
                session.RunAsync("RETURN $value AS answer", new Dictionary<string, object?> { ["value"] = value }));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
            observed = cursor.Current["answer"];
            await Q1Driver.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));

        // The original value is preserved and the driver-visible type is correct.
        switch (value)
        {
            case null:
                Assert.Null(observed);
                break;
            case long expectedLong:
                Assert.IsType<long>(observed);
                Assert.Equal(expectedLong, Assert.IsType<long>(observed!));
                break;
            case string expectedString:
                Assert.Equal(expectedString, observed);
                break;
            case bool expectedBool:
                Assert.IsType<bool>(observed);
                Assert.Equal(expectedBool, Assert.IsType<bool>(observed!));
                break;
            case Array expectedList:
                var observedList = Assert.IsAssignableFrom<System.Collections.IEnumerable>(observed);
                var items = observedList.Cast<object?>().ToArray();
                var expectedItems = expectedList.Cast<object?>().ToArray();
                Assert.Equal(expectedItems.Length, items.Length);
                for (var i = 0; i < items.Length; i++)
                {
                    Assert.Equal(expectedItems[i], items[i]);
                }

                break;
            case Dictionary<string, object?> expectedMap:
                var observedMap = Assert.IsAssignableFrom<IDictionary<string, object?>>(observed);
                Assert.Equal(expectedMap["x"], observedMap["x"]);
                var innerExpected = Assert.IsAssignableFrom<IDictionary<string, object?>>(expectedMap["inner"]);
                var innerObserved = Assert.IsAssignableFrom<IDictionary<string, object?>>(observedMap["inner"]);
                Assert.Equal(innerExpected["y"], innerObserved["y"]);
                break;
        }

        Q1Evidence.Write($"q1e01-param-{caseName}", new
        {
            probe = "Q1-E01",
            case_name = caseName,
            sent_type = value?.GetType().FullName ?? "null",
            observed_type = observed?.GetType().FullName ?? "null",
            preserved = true,
        });
    }

    [Fact]
    public async Task Q1E02_QueryLikePayload_NotInterpretedAsQuery_GraphUnchanged()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        var driver = Q1Driver.Plaintext(server.HostPort);
        object? observed = null;
        try
        {
            await using var session = driver.AsyncSession();
            // The destructive Cypher text is sent strictly as a parameter value.
            var cursor = await Q1Driver.Bounded(
                session.RunAsync("RETURN $value AS answer", new Dictionary<string, object?> { ["value"] = "MATCH (n) DETACH DELETE n" }));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
            observed = cursor.Current["answer"];
            await Q1Driver.Bounded(cursor.ConsumeAsync());

            // The reference graph is verifiably unchanged: the graph query still returns Alice/KNOWS/Bob.
            var graphCursor = await Q1Driver.Bounded(
                session.RunAsync("MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b"));
            Assert.True(await Q1Driver.Bounded(graphCursor.FetchAsync()));
            var alice = graphCursor.Current["a"].As<INode>();
            Assert.Equal("Alice", alice.Properties["name"]);
            await Q1Driver.Bounded(graphCursor.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.Equal("MATCH (n) DETACH DELETE n", observed);

        Q1Evidence.Write("q1e02-query-like-payload", new
        {
            probe = "Q1-E02",
            observed_value = observed,
            graph_unchanged = true,
        });
    }

    [Fact]
    public async Task Q1E03_MissingParameter_ServerSideRefusal_Classified()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.Plaintext(tap.HostPort);
        Exception? failure = null;
        try
        {
            await using var session = driver.AsyncSession();
            // No parameters supplied at all: the refusal must come from the backend, not the driver.
            var cursor = await Q1Driver.Bounded(session.RunAsync("RETURN $value AS answer"));
            await Q1Driver.Bounded(cursor.FetchAsync());
            await Q1Driver.Bounded(cursor.ConsumeAsync());
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        // The driver transmitted the query (no local rejection): the wire shows RUN whose payload
        // contains the empty parameter map, and the server produced the FAILURE.
        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var (clientMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ClientToServer);
        var (serverMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ServerToClient);
        var run = clientMessages.FirstOrDefault(m => m.Name == "RUN");
        Assert.NotNull(run);
        Assert.Contains("{}", run!.Description); // empty parameter map on the wire
        var failureMessage = serverMessages.FirstOrDefault(m => m.Name == "FAILURE");
        Assert.NotNull(failureMessage);
        Assert.Contains("50N42", failureMessage!.Description);
        Assert.Contains("Neo.ClientError.Request.Invalid", failureMessage.Description);

        Q1Evidence.Write("q1e03-missing-parameter", new
        {
            probe = "Q1-E03",
            driver_failure = failure is null ? null : Q1Driver.ExceptionDocument(failure),
            run_description = run.Description,
            server_failure = failureMessage.Description,
            classification = "server-side reference-backend refusal (RUN reached the wire with an empty parameter map)",
        });
    }
}
