using BoltSharp.DriverInteropTests.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.DriverInteropTests.Scenarios;

/// <summary>
/// B5-B5 — parameterized execution through the accepted <c>RETURN $value AS answer</c> form. The
/// parameter map is supplied through the driver's public API and must arrive unchanged; the
/// backend must never interpolate parameter values into the query text.
/// </summary>
[Collection("B5-B")]
public sealed class ParameterInteropTests
{
    private const string ParameterQuery = "RETURN $value AS answer";

    [Fact]
    [Trait("Task", "B5-B5")]
    public async Task B5B5_01_TypedParameters_ArePreservedThroughDriverAndBackend()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);

        var driver = DriverFactory.Plaintext(server.HostPort);
        var rows = new List<object>();
        var observed = new Dictionary<string, object?>();
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();

            foreach (var (label, parameters) in new (string, Dictionary<string, object?>)[]
            {
                ("positive-integer", new() { ["value"] = 42L }),
                ("negative-integer", new() { ["value"] = -7L }),
                ("string", new() { ["value"] = "hello" }),
                ("unicode-string", new() { ["value"] = "h\u00e9llo \u2713 \u4e16\u754c" }),
                ("boolean-true", new() { ["value"] = true }),
                ("boolean-false", new() { ["value"] = false }),
                ("explicit-null", new() { ["value"] = null }),
                ("list", new() { ["value"] = new long[] { 1L, 2L, 3L } }),
                ("nested-map", new()
                {
                    ["value"] = new Dictionary<string, object>
                    {
                        ["x"] = 1L,
                        ["inner"] = new Dictionary<string, object> { ["y"] = "z" },
                    },
                }),
            })
            {
                var (value, document) = await RunAndReturn(session, parameters);
                observed[label] = value;
                rows.Add(document);
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Equal(42L, Assert.IsType<long>(observed["positive-integer"]));
        Assert.Equal(-7L, Assert.IsType<long>(observed["negative-integer"]));
        Assert.Equal("hello", Assert.IsType<string>(observed["string"]));
        Assert.Equal("h\u00e9llo \u2713 \u4e16\u754c", Assert.IsType<string>(observed["unicode-string"]));
        Assert.True(Assert.IsType<bool>(observed["boolean-true"]));
        Assert.False(Assert.IsType<bool>(observed["boolean-false"]));
        Assert.Null(observed["explicit-null"]);

        var list = Assert.IsAssignableFrom<IReadOnlyList<object>>(observed["list"]);
        Assert.Equal(new object[] { 1L, 2L, 3L }, list);

        var map = Assert.IsAssignableFrom<IDictionary<string, object>>(observed["nested-map"]);
        Assert.Equal(1L, Assert.IsType<long>(map["x"]));
        var inner = Assert.IsAssignableFrom<IDictionary<string, object>>(map["inner"]);
        Assert.Equal("z", Assert.IsType<string>(inner["y"]));

        var counters = server.Counters();
        Assert.Equal(0, counters.Failed);
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));

        EvidenceWriter.Write("b5b5-01-typed-parameters", new
        {
            scenario = "B5B5-01",
            title = "Typed parameter preservation through the official driver",
            uri_scheme = "bolt",
            cases = rows,
            counters = EvidenceWriter.CounterDocument(counters),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B5")]
    public async Task B5B5_02_ExplicitNull_VersusMissingParameter()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        bool explicitNullIsNull = false;
        Exception? missingFailure = null;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();

            // Explicit null: the driver sends {"value": null} and the backend returns a null value.
            var cursor = await DriverFactory.Bounded(session.RunAsync(
                ParameterQuery,
                new Dictionary<string, object?> { ["value"] = null }));
            Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
            explicitNullIsNull = cursor.Current["answer"] is null;
            Assert.True(explicitNullIsNull);
            await DriverFactory.Bounded(cursor.ConsumeAsync());

            // Missing parameter: the RUN reaches the wire with an empty parameter map and the
            // backend refuses it server-side (recoverable failure), never fabricating a record.
            try
            {
                var missing = await DriverFactory.Bounded(session.RunAsync(ParameterQuery));
                await DriverFactory.Bounded(missing.ConsumeAsync());
                Assert.Fail("a missing required parameter must not succeed");
            }
            catch (Neo4jException ex)
            {
                missingFailure = ex;
            }

            // The session remains usable after the recoverable refusal.
            var recovery = await DriverFactory.Bounded(session.RunAsync(ParameterQuery, new Dictionary<string, object?> { ["value"] = 7L }));
            Assert.True(await DriverFactory.Bounded(recovery.FetchAsync()));
            Assert.Equal(7L, recovery.Current["answer"].As<long>());
            await DriverFactory.Bounded(recovery.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.NotNull(missingFailure);
        var neo4jFailure = Assert.IsAssignableFrom<Neo4jException>(missingFailure);
        Assert.Equal("Neo.ClientError.Request.Invalid", neo4jFailure.Code);

        Assert.True(await tap.WaitForClosedConnectionsAsync(2, TimeSpan.FromSeconds(10)));

        // The refusal happened on the first connection (explicit-null RUN, missing RUN, FAILURE);
        // the recovery RUN followed on a replacement connection after the driver destroyed it.
        var (failedConnection, transcript) = tap.FindConnection(t => t.CountServer("FAILURE") == 1);

        // The missing-parameter RUN was rejected on the wire (server-side refusal, not client-side
        // validation): it carries the authentic empty parameter map; and it produced no record.
        var missingRun = Assert.Single(
            transcript.ClientMessages,
            m => m.Name == "RUN" && m.Description.Contains("parameters=0 entry(ies)", StringComparison.Ordinal));
        Assert.Contains("parameters=0 entry(ies)", missingRun.Description);
        var failure = transcript.ServerMessages.First(m => m.Name == "FAILURE");
        Assert.Contains("Neo.ClientError.Request.Invalid", failure.Description);
        Assert.Equal(2, transcript.CountClient("RUN"));
        Assert.Equal(1, transcript.CountServer("RECORD"));

        var (recoveryConnection, recoveryTranscript) = tap.FindConnection(t => t.CountServer("FAILURE") == 0);
        Assert.Equal(1, recoveryTranscript.CountClient("RUN"));
        Assert.Equal(1, recoveryTranscript.CountServer("RECORD"));

        EvidenceWriter.Write("b5b5-02-explicit-null-versus-missing", new
        {
            scenario = "B5B5-02",
            title = "Explicit null preserved; missing parameter refused server-side",
            uri_scheme = "bolt",
            driver_visible = new { explicit_null_record_is_null = explicitNullIsNull },
            missing_parameter_failure = EvidenceWriter.ExceptionDocument(missingFailure!),
            missing_run_hex = missingRun.Hex,
            failure_description = failure.Description,
            failed_connection = transcript.ToDocument(failedConnection),
            recovery_connection = recoveryTranscript.ToDocument(recoveryConnection),
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B5")]
    public async Task B5B5_03_QueryLikeStringPayload_IsReturnedVerbatim_NotExecuted()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        const string payload = "MATCH (n) DETACH DELETE n";
        var driver = DriverFactory.Plaintext(tap.HostPort);
        string? returned = null;
        var names = new List<string>();
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();

            var cursor = await DriverFactory.Bounded(session.RunAsync(
                ParameterQuery,
                new Dictionary<string, object?> { ["value"] = payload }));
            Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
            returned = cursor.Current["answer"].As<string>();
            await DriverFactory.Bounded(cursor.ConsumeAsync());

            // The destructive text must not have been executed: the graph is unchanged.
            var graph = await DriverFactory.Bounded(session.RunAsync("MATCH (n:Person) RETURN n.name AS name ORDER BY name"));
            while (await DriverFactory.Bounded(graph.FetchAsync()))
            {
                names.Add(graph.Current["name"].As<string>());
            }

            await DriverFactory.Bounded(graph.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Equal(payload, returned);
        Assert.Equal(new[] { "Alice", "Bob" }, names);

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var transcript = WireTranscript.Build(tap.SingleConnection());

        // The query text sent to the server is exactly the parameterized form; the payload value
        // appears only inside the parameter map bytes and is never interpolated.
        var payloadRun = transcript.ClientMessages.First(m => m.Name == "RUN");
        Assert.Equal($"RUN \"{ParameterQuery}\" parameters=1 entry(ies) extra={{}}", payloadRun.Description);
        Assert.Contains(WireTranscript.Hex(System.Text.Encoding.UTF8.GetBytes(payload)), payloadRun.Hex.Replace(" ", string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain("DETACH", transcript.ClientMessages.First(m => m.Name == "RUN").Description);

        EvidenceWriter.Write("b5b5-03-query-like-payload", new
        {
            scenario = "B5B5-03",
            title = "Query-like string parameter returned verbatim; graph unchanged",
            uri_scheme = "bolt",
            driver_visible = new { returned_payload = returned, person_names_after = names },
            payload_run_hex = payloadRun.Hex,
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }

    private static async Task<(object? Value, object Document)> RunAndReturn(
        IAsyncSession session,
        IDictionary<string, object?> parameters)
    {
        var cursor = await DriverFactory.Bounded(session.RunAsync(ParameterQuery, parameters));
        Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
        var value = cursor.Current["answer"];
        string description;
        object documentValue;
        switch (value)
        {
            case null:
                description = "null";
                documentValue = "<null>";
                break;
            case string text:
                description = $"string:{text}";
                documentValue = text;
                break;
            case bool boolean:
                description = $"boolean:{boolean}";
                documentValue = boolean;
                break;
            case long integer:
                description = $"integer:{integer}";
                documentValue = integer;
                break;
            case System.Collections.IEnumerable list when value is not string:
                var items = list.Cast<object>().Select(i => i?.ToString() ?? "<null>").ToArray();
                description = $"list:[{string.Join(",", items)}]";
                documentValue = items;
                break;
            case IDictionary<string, object> map:
                var pairs = string.Join(",", map.Select(kv => kv.Key + ":" + (kv.Value ?? "<null>")));
                description = "map:{" + pairs + "}";
                documentValue = map.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
                break;
            default:
                description = $"type:{value.GetType().FullName}";
                documentValue = value.ToString() ?? "unknown";
                break;
        }

        await DriverFactory.Bounded(cursor.ConsumeAsync());
        return (
            value,
            new { value_type = value?.GetType().Name ?? "null", description, value = documentValue });
    }
}
