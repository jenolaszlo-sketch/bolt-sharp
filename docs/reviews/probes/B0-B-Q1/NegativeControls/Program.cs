using System.Text.Json;
using BoltSharp.IntegrationTests.Infrastructure;
using Neo4j.Driver;
using Xunit;

// B0-B-T04 deliberately verifies the legacy numeric identity still reconstructed by driver 6.x.
#pragma warning disable CS0618

namespace B0BQ1.NegativeControls;

/// <summary>
/// B0-B-Q1 reviewer negative controls.
///
/// Each control reproduces one producer scenario and then mutates exactly one server-side fact.
/// The producer's assertion sequence (copied verbatim where practical) is then evaluated against
/// the mutated interaction:
///   - DETECTED     : the copied producer assertion or uncaught driver call failed, as required;
///   - NOT_DETECTED : the producer assertion sequence still passed, documenting a blind spot.
///
/// No producer source is modified. The producer infrastructure files are linked read-only.
/// </summary>
internal static class Program
{
    private const string OutputDirectory = "docs/reviews/evidence/B0-B-Q1/negative-controls";

    private static async Task<int> Main(string[] args)
    {
        var outputDirectory = args.Length > 0 ? args[0] : OutputDirectory;
        var controls = new (string Id, string ProducerTest, string Mutation, bool ExpectDetection, Func<Task> Run)[]
        {
            (
                "NC-01-wrong-record-value",
                "T03_ReturnScalar_RealDriverReconstructsValue",
                "PULL returns RECORD 43 instead of RECORD 42.",
                true,
                WrongRecordValueAsync),
            (
                "NC-02-reversed-relationship-direction",
                "T04_SyntheticGraph_RealDriverReconstructsTypedGraph",
                "Relationship endpoints reversed: start=n:2/2, end=n:1/1.",
                true,
                ReversedRelationshipDirectionAsync),
            (
                "NC-03a-missing-hello-server",
                "T02_Initialization_ObservesHelloThenLogonAndRequiredMetadata",
                "HELLO SUCCESS omits the required server field.",
                true,
                MissingHelloServerAsync),
            (
                "NC-03b-wrong-hello-server-value",
                "T02_Initialization_ObservesHelloThenLogonAndRequiredMetadata",
                "HELLO SUCCESS carries server = \"Neo4j/9.9.9\" instead of the producer fixture value.",
                false,
                WrongHelloServerValueAsync),
            (
                "NC-03c-missing-hello-connection-id",
                "T02_Initialization_ObservesHelloThenLogonAndRequiredMetadata",
                "HELLO SUCCESS omits connection_id (producer matrix claims it is required).",
                false,
                MissingHelloConnectionIdAsync),
            (
                "NC-04-missing-ignored",
                "T06_ProtocolFailure_IsSurfacedAccurately",
                "PULL after FAILURE is answered with SUCCESS instead of IGNORED.",
                true,
                MissingIgnoredAsync),
            (
                "NC-05a-wrong-response-signature",
                "T03_ReturnScalar_RealDriverReconstructsValue",
                "PULL is answered with FAILURE instead of RECORD/SUCCESS.",
                true,
                WrongResponseSignatureAsync),
            (
                "NC-05b-wrong-record-arity",
                "T03_ReturnScalar_RealDriverReconstructsValue",
                "RECORD carries two values for the single RUN field (T03 asserts no record arity).",
                false,
                WrongRecordArityAsync),
            (
                "NC-06-extra-record-response",
                "T05_FinitePullSize_MultiplePullsDeliverExactlyTheRecords",
                "First PULL reply contains an extra RECORD beyond the scripted three.",
                true,
                ExtraRecordResponseAsync),
            (
                "NC-07-premature-termination-masked",
                "T06_IncompleteResponse_IsSurfacedAsFailure",
                "Truncated PULL frame replaced by a complete RECORD/SUCCESS (clean end).",
                true,
                PrematureTerminationMaskedAsync),
            (
                "NC-08-extra-success-after-has-more-false",
                "T03_ReturnScalar_RealDriverReconstructsValue",
                "PULL reply carries an extra empty SUCCESS after has_more=false.",
                false,
                ExtraSuccessAfterHasMoreFalseAsync),
            (
                "NC-09-swapped-node-identities",
                "T04_SyntheticGraph_RealDriverReconstructsTypedGraph",
                "Node numeric ids swapped (Alice=2, Bob=1) while element ids stay n:1/n:2.",
                true,
                SwappedNodeIdentitiesAsync),
            (
                "NC-10-wrong-relationship-element-id",
                "T04_SyntheticGraph_RealDriverReconstructsTypedGraph",
                "Relationship element id changed from r:1 to r:99.",
                true,
                WrongRelationshipElementIdAsync),
        };

        Directory.CreateDirectory(outputDirectory);
        var results = new List<ControlResult>();
        foreach (var control in controls)
        {
            var result = await RunControlAsync(control.Id, control.ProducerTest, control.Mutation, control.ExpectDetection, control.Run)
                .ConfigureAwait(false);
            results.Add(result);
            Console.WriteLine($"{result.Id}: detected={result.Detected} expected={result.ExpectedDetection} -> {result.Disposition}");
        }

        var notes = Notes();
        object document = new
        {
            probe = "B0-B-Q1 reviewer negative controls",
            producer_infrastructure = "tests/BoltSharp.IntegrationTests/Infrastructure (linked read-only)",
            driver = typeof(GraphDatabase).Assembly.GetName().FullName,
            controls = results.Select(r => new
            {
                r.Id,
                r.ProducerTest,
                r.Mutation,
                r.ExpectedDetection,
                r.Detected,
                r.Disposition,
                r.ExceptionType,
                r.ExceptionMessage,
                note = notes.GetValueOrDefault(r.Id),
            }),
        };
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "summary.json"), json + Environment.NewLine)
            .ConfigureAwait(false);
        Console.WriteLine(json);

        return results.All(r => r.Disposition == "EXPECTED") ? 0 : 4;
    }

    private static Dictionary<string, string> Notes() => new(StringComparer.Ordinal)
    {
        ["NC-01-wrong-record-value"] =
            "Detected by the exact-value assertion; the producer scalar test is value-sensitive.",
        ["NC-02-reversed-relationship-direction"] =
            "Detected by the StartNodeId/StartNodeElementId assertions; direction is verified.",
        ["NC-03a-missing-hello-server"] =
            "Driver 6.4.0 throws NullReferenceException without server; the uncaught T02 call fails.",
        ["NC-03b-wrong-hello-server-value"] =
            "T02 only requires the connection to succeed, so a wrong-but-present server value passes. Assertion blind spot, not a false claim.",
        ["NC-03c-missing-hello-connection-id"] =
            "Driver 6.4.0 accepts HELLO SUCCESS without connection_id; this contradicts the producer gap-matrix claim that both fields are mandatory (D01 correction).",
        ["NC-04-missing-ignored"] =
            "Detected by the IGNORED-frame assertion; the T06 recovery test is IGNORED-sensitive.",
        ["NC-05a-wrong-response-signature"] =
            "The FAILURE response surfaces as Neo4j.Driver.ClientException from the uncaught T03 call.",
        ["NC-05b-wrong-record-arity"] =
            "The driver ignores the extra value and T03 never asserts record arity, so the mutation passes. Assertion blind spot.",
        ["NC-06-extra-record-response"] =
            "Detected by the exact values and exact RECORD-count assertions in T05.",
        ["NC-07-premature-termination-masked"] =
            "With a complete reply the driver returns normally; T06 fails on Assert.NotNull(failure).",
        ["NC-08-extra-success-after-has-more-false"] =
            "The driver stops at has_more=false and ignores the trailing SUCCESS; T03 asserts no frame count. Assertion blind spot.",
        ["NC-09-swapped-node-identities"] =
            "Detected by the numeric node-id assertions; node identity is verified.",
        ["NC-10-wrong-relationship-element-id"] =
            "Detected by the relationship element-id assertion; relationship identity is verified.",
    };

    private static async Task<ControlResult> RunControlAsync(
        string id,
        string producerTest,
        string mutation,
        bool expectDetection,
        Func<Task> run)
    {
        Exception? failure = null;
        try
        {
            await run().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        var detected = failure is not null;
        var expected = detected == expectDetection;
        return new ControlResult
        {
            Id = id,
            ProducerTest = producerTest,
            Mutation = mutation,
            ExpectedDetection = expectDetection,
            Detected = detected,
            Disposition = expected ? "EXPECTED" : "UNEXPECTED",
            ExceptionType = failure?.GetType().FullName,
            ExceptionMessage = failure is null ? null : OneLine(failure.Message),
        };
    }

    // ---- NC-01: wrong record value (copied T03 assertions) ----

    private static async Task WrongRecordValueAsync()
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(ProtocolFixtures.Str("answer"))));
        script.On("PULL")
            .Record(ProtocolFixtures.Int(43))
            .Success(("has_more", ProtocolFixtures.Bool(false)));

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        long observed = -1;
        IResultSummary? summary = null;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Read));
            var cursor = await DriverHarness.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverHarness.Bounded(cursor.FetchAsync()));
            observed = cursor.Current["answer"].As<long>();
            summary = await DriverHarness.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;

        // Producer T03 assertions, copied.
        Assert.Equal(42, observed);
        Assert.NotNull(summary);
        Assert.Equal(0, summary!.Counters.NodesCreated + summary.Counters.PropertiesSet);
        var run = Assert.Single(capture.Requests, r => r.Name == "RUN");
        var query = (run.Structure.Fields.ElementAtOrDefault(0) as BoltSharp.PackStream.PackStreamString)?.Value;
        Assert.Equal("RETURN 42 AS answer", query);
        Assert.Contains(capture.Requests, r => r.Name == "PULL");
        Assert.DoesNotContain(capture.Requests, r => r.Unscripted);
        Assert.Contains(capture.Frames, f => f.Name == "RECORD");
    }

    // ---- NC-02: reversed relationship direction (copied T04 assertions) ----

    private static async Task ReversedRelationshipDirectionAsync()
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(
            ProtocolFixtures.Str("a"), ProtocolFixtures.Str("r"), ProtocolFixtures.Str("b"))));

        var alice = ProtocolFixtures.Node(1, "n:1", new[] { "Person" }, ("name", ProtocolFixtures.Str("Alice")));
        var bob = ProtocolFixtures.Node(2, "n:2", new[] { "Person" }, ("name", ProtocolFixtures.Str("Bob")));
        // Mutation: endpoints reversed relative to the producer fixture.
        var knows = ProtocolFixtures.Relationship(
            1, 2, 1, "KNOWS", "r:1", "n:2", "n:1", ("since", ProtocolFixtures.Int(2024)));
        script.On("PULL").Record(alice, knows, bob).Success(("has_more", ProtocolFixtures.Bool(false)));

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        INode? nodeA = null;
        IRelationship? rel = null;
        INode? nodeB = null;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverHarness.Bounded(session.RunAsync("MATCH (a)-[r]->(b) RETURN a,r,b"));
            Assert.True(await DriverHarness.Bounded(cursor.FetchAsync()));
            var record = cursor.Current;
            nodeA = record["a"].As<INode>();
            rel = record["r"].As<IRelationship>();
            nodeB = record["b"].As<INode>();
            await DriverHarness.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));

        // Producer T04 assertions, copied.
        Assert.NotNull(nodeA);
        Assert.NotNull(rel);
        Assert.NotNull(nodeB);
        Assert.Equal(1, nodeA!.Id);
        Assert.Equal(2, nodeB!.Id);
        Assert.Equal(1, rel!.Id);
        Assert.IsAssignableFrom<INode>(nodeA);
        Assert.IsAssignableFrom<IRelationship>(rel);
        Assert.Equal("n:1", nodeA.ElementId);
        Assert.Equal("n:2", nodeB.ElementId);
        Assert.Equal("r:1", rel.ElementId);
        Assert.Contains("Person", nodeA.Labels);
        Assert.Contains("Person", nodeB.Labels);
        Assert.Equal("Alice", nodeA.Properties["name"].As<string>());
        Assert.Equal("Bob", nodeB.Properties["name"].As<string>());
        Assert.Equal("KNOWS", rel.Type);
        Assert.Equal(nodeA.Id, rel.StartNodeId);
        Assert.Equal(nodeB.Id, rel.EndNodeId);
        Assert.Equal("n:1", rel.StartNodeElementId);
        Assert.Equal("n:2", rel.EndNodeElementId);
        Assert.Equal(2024, rel.Properties["since"].As<long>());
    }

    // ---- NC-09/NC-10: node and relationship identity mutations (copied T04 assertions) ----

    private static async Task SwappedNodeIdentitiesAsync()
    {
        var alice = ProtocolFixtures.Node(2, "n:1", new[] { "Person" }, ("name", ProtocolFixtures.Str("Alice")));
        var bob = ProtocolFixtures.Node(1, "n:2", new[] { "Person" }, ("name", ProtocolFixtures.Str("Bob")));
        var knows = ProtocolFixtures.Relationship(
            1, 1, 2, "KNOWS", "r:1", "n:1", "n:2", ("since", ProtocolFixtures.Int(2024)));
        await RunGraphT04Async(alice, knows, bob);
    }

    private static async Task WrongRelationshipElementIdAsync()
    {
        var alice = ProtocolFixtures.Node(1, "n:1", new[] { "Person" }, ("name", ProtocolFixtures.Str("Alice")));
        var bob = ProtocolFixtures.Node(2, "n:2", new[] { "Person" }, ("name", ProtocolFixtures.Str("Bob")));
        var knows = ProtocolFixtures.Relationship(
            1, 1, 2, "KNOWS", "r:99", "n:1", "n:2", ("since", ProtocolFixtures.Int(2024)));
        await RunGraphT04Async(alice, knows, bob);
    }

    private static async Task RunGraphT04Async(byte[] alice, byte[] knows, byte[] bob)
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(
            ProtocolFixtures.Str("a"), ProtocolFixtures.Str("r"), ProtocolFixtures.Str("b"))));
        script.On("PULL").Record(alice, knows, bob).Success(("has_more", ProtocolFixtures.Bool(false)));

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        INode? nodeA = null;
        IRelationship? rel = null;
        INode? nodeB = null;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverHarness.Bounded(session.RunAsync("MATCH (a)-[r]->(b) RETURN a,r,b"));
            Assert.True(await DriverHarness.Bounded(cursor.FetchAsync()));
            var record = cursor.Current;
            nodeA = record["a"].As<INode>();
            rel = record["r"].As<IRelationship>();
            nodeB = record["b"].As<INode>();
            await DriverHarness.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));

        // Producer T04 assertions, copied.
        Assert.NotNull(nodeA);
        Assert.NotNull(rel);
        Assert.NotNull(nodeB);
        Assert.Equal(1, nodeA!.Id);
        Assert.Equal(2, nodeB!.Id);
        Assert.Equal(1, rel!.Id);
        Assert.IsAssignableFrom<INode>(nodeA);
        Assert.IsAssignableFrom<IRelationship>(rel);
        Assert.Equal("n:1", nodeA.ElementId);
        Assert.Equal("n:2", nodeB.ElementId);
        Assert.Equal("r:1", rel.ElementId);
        Assert.Contains("Person", nodeA.Labels);
        Assert.Contains("Person", nodeB.Labels);
        Assert.Equal("Alice", nodeA.Properties["name"].As<string>());
        Assert.Equal("Bob", nodeB.Properties["name"].As<string>());
        Assert.Equal("KNOWS", rel.Type);
        Assert.Equal(nodeA.Id, rel.StartNodeId);
        Assert.Equal(nodeB.Id, rel.EndNodeId);
        Assert.Equal("n:1", rel.StartNodeElementId);
        Assert.Equal("n:2", rel.EndNodeElementId);
        Assert.Equal(2024, rel.Properties["since"].As<long>());
    }

    // ---- NC-03a/03b/03c: HELLO metadata mutations (copied T02 assertions) ----

    private static Task MissingHelloServerAsync() => RunHelloMetadataVariantAsync(
        ("connection_id", ProtocolFixtures.Str(ScenarioSupport.ConnectionId)),
        ("hints", ProtocolFixtures.Map()));

    private static Task WrongHelloServerValueAsync() => RunHelloMetadataVariantAsync(
        ("server", ProtocolFixtures.Str("Neo4j/9.9.9")),
        ("connection_id", ProtocolFixtures.Str(ScenarioSupport.ConnectionId)),
        ("hints", ProtocolFixtures.Map()));

    private static Task MissingHelloConnectionIdAsync() => RunHelloMetadataVariantAsync(
        ("server", ProtocolFixtures.Str(ScenarioSupport.ServerName)),
        ("hints", ProtocolFixtures.Map()));

    private static async Task RunHelloMetadataVariantAsync(params (string Key, byte[] Value)[] helloMetadata)
    {
        var script = new BoltConnectionScript();
        script.On("HELLO").Success(helloMetadata);
        script.On("LOGON").SuccessEmpty();
        script.On("RESET").SuccessEmpty();
        script.On("GOODBYE").Close();

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        try
        {
            // Producer T02 does not catch driver failures, so any exception fails the producer test.
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;

        // Producer T02 assertions, copied.
        var helloIndex = capture.Requests.ToList().FindIndex(r => r.Name == "HELLO");
        var logonIndex = capture.Requests.ToList().FindIndex(r => r.Name == "LOGON");
        Assert.True(helloIndex >= 0, "HELLO was not observed");
        Assert.True(logonIndex > helloIndex, "LOGON must follow HELLO");

        var hello = capture.Requests[helloIndex];
        Assert.Contains("user_agent", hello.SanitizedDescription);
        Assert.Contains("routing", hello.SanitizedDescription);
        Assert.Contains("bolt_agent", hello.SanitizedDescription);

        var logon = capture.Requests[logonIndex];
        Assert.Contains("scheme", logon.SanitizedDescription);
        Assert.Contains("principal", logon.SanitizedDescription);
        Assert.Contains("credentials", logon.SanitizedDescription);

        Assert.True(hello.Sensitive);
        Assert.True(logon.Sensitive);
        Assert.All(
            capture.Frames.Where(f => f.Sensitive),
            f => Assert.Equal("<redacted>", f.SanitizedHex));

        Assert.Contains(capture.Frames, f => f.Name == "SUCCESS");
        Assert.DoesNotContain(capture.Requests, r => r.Unscripted);
    }

    // ---- NC-04: missing IGNORED (copied T06 assertions) ----

    private static async Task MissingIgnoredAsync()
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Failure(
            ("gql_status", ProtocolFixtures.Str("42001")),
            ("neo4j_code", ProtocolFixtures.Str("Neo.ClientError.Statement.SyntaxError")),
            ("description", ProtocolFixtures.Str("Invalid input for the deliberately unsupported query.")),
            ("diagnostic_record", ProtocolFixtures.Map()));
        // Mutation: answer the pipelined PULL with SUCCESS instead of IGNORED.
        script.On("PULL").SuccessEmpty();

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        Exception? failure = null;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            var session = driver.AsyncSession();
            try
            {
                var cursor = await DriverHarness.Bounded(session.RunAsync("NOT A SUPPORTED QUERY"));
                await DriverHarness.Bounded(cursor.ConsumeAsync());
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            try
            {
                await DriverHarness.Bounded(session.DisposeAsync().AsTask());
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }
        finally
        {
            try
            {
                await driver.DisposeAsync();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;

        // Producer T06 assertions, copied.
        Assert.NotNull(failure);
        Assert.IsAssignableFrom<Neo4jException>(failure);
        Assert.Contains(capture.Frames, f => f.Name == "FAILURE");
        Assert.Contains(capture.Frames, f => f.Name == "IGNORED");
        Assert.DoesNotContain(capture.Requests, r => r.Unscripted);
    }

    // ---- NC-05a/05b: wrong response signature / arity (copied T03 assertions) ----

    private static async Task WrongResponseSignatureAsync() => await RunMutatedPullT03Async(
        reply => reply.Failure(
            ("gql_status", ProtocolFixtures.Str("42001")),
            ("neo4j_code", ProtocolFixtures.Str("Neo.ClientError.Statement.SyntaxError")),
            ("description", ProtocolFixtures.Str("mutated response"))));

    private static async Task WrongRecordArityAsync() => await RunMutatedPullT03Async(
        reply => reply.Record(ProtocolFixtures.Int(42), ProtocolFixtures.Int(43))
            .Success(("has_more", ProtocolFixtures.Bool(false))));

    private static async Task RunMutatedPullT03Async(Action<ReplyChain> mutatePull)
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(ProtocolFixtures.Str("answer"))));
        mutatePull(script.On("PULL"));

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        long observed = -1;
        IResultSummary? summary = null;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Read));
            var cursor = await DriverHarness.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverHarness.Bounded(cursor.FetchAsync()));
            observed = cursor.Current["answer"].As<long>();
            summary = await DriverHarness.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;

        // Producer T03 assertions, copied.
        Assert.Equal(42, observed);
        Assert.NotNull(summary);
        Assert.Equal(0, summary!.Counters.NodesCreated + summary.Counters.PropertiesSet);
        var run = Assert.Single(capture.Requests, r => r.Name == "RUN");
        var query = (run.Structure.Fields.ElementAtOrDefault(0) as BoltSharp.PackStream.PackStreamString)?.Value;
        Assert.Equal("RETURN 42 AS answer", query);
        Assert.Contains(capture.Requests, r => r.Name == "PULL");
        Assert.DoesNotContain(capture.Requests, r => r.Unscripted);
        Assert.Contains(capture.Frames, f => f.Name == "RECORD");
    }

    // ---- NC-06: extra record response (copied T05 assertions) ----

    private static async Task ExtraRecordResponseAsync()
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(ProtocolFixtures.Str("n"))));
        // Mutation: first PULL delivers an additional RECORD (99) before the scripted record (1).
        script.On("PULL")
            .Record(ProtocolFixtures.Int(99))
            .Record(ProtocolFixtures.Int(1))
            .Success(("has_more", ProtocolFixtures.Bool(true)));
        script.On("PULL")
            .Record(ProtocolFixtures.Int(2))
            .Success(("has_more", ProtocolFixtures.Bool(true)));
        script.On("PULL")
            .Record(ProtocolFixtures.Int(3))
            .Success(("has_more", ProtocolFixtures.Bool(false)));

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        var values = new List<long>();
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession(o => o.WithFetchSize(1));
            var cursor = await DriverHarness.Bounded(session.RunAsync("RETURN n"));
            while (await DriverHarness.Bounded(cursor.FetchAsync()))
            {
                values.Add(cursor.Current["n"].As<long>());
            }

            await DriverHarness.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;

        // Producer T05 assertions, copied.
        Assert.Equal(new long[] { 1, 2, 3 }, values);
        Assert.True(
            capture.Requests.Count(r => r.Name == "PULL") >= 3,
            "finite PULL sizes should require multiple PULL requests");
        Assert.Equal(3, capture.Frames.Count(f => f.Name == "RECORD"));
        Assert.DoesNotContain(capture.Requests, r => r.Unscripted);
    }

    // ---- NC-07: premature termination masked (copied T06 assertions) ----

    private static async Task PrematureTerminationMaskedAsync()
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(ProtocolFixtures.Str("n"))));
        // Mutation: the producer's truncated raw frame + close is replaced by a complete reply.
        script.On("PULL")
            .Record(ProtocolFixtures.Int(42))
            .Success(("has_more", ProtocolFixtures.Bool(false)));

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        Exception? failure = null;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverHarness.Bounded(session.RunAsync("RETURN n"));
            await DriverHarness.Bounded(cursor.FetchAsync());
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;

        // Producer T06_incomplete assertions, copied.
        Assert.NotNull(failure);
        Assert.DoesNotContain(capture.Frames, f => f.Name == "RECORD");
    }

    // ---- NC-08: extra SUCCESS after has_more=false (copied T03 assertions) ----

    private static async Task ExtraSuccessAfterHasMoreFalseAsync()
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(ProtocolFixtures.Str("answer"))));
        // Mutation: extra empty SUCCESS after the terminal has_more=false SUCCESS.
        script.On("PULL")
            .Record(ProtocolFixtures.Int(42))
            .Success(("has_more", ProtocolFixtures.Bool(false)))
            .SuccessEmpty();

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        long observed = -1;
        IResultSummary? summary = null;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Read));
            var cursor = await DriverHarness.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverHarness.Bounded(cursor.FetchAsync()));
            observed = cursor.Current["answer"].As<long>();
            summary = await DriverHarness.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;

        // Producer T03 assertions, copied.
        Assert.Equal(42, observed);
        Assert.NotNull(summary);
        Assert.Equal(0, summary!.Counters.NodesCreated + summary.Counters.PropertiesSet);
        var run = Assert.Single(capture.Requests, r => r.Name == "RUN");
        var query = (run.Structure.Fields.ElementAtOrDefault(0) as BoltSharp.PackStream.PackStreamString)?.Value;
        Assert.Equal("RETURN 42 AS answer", query);
        Assert.Contains(capture.Requests, r => r.Name == "PULL");
        Assert.DoesNotContain(capture.Requests, r => r.Unscripted);
        Assert.Contains(capture.Frames, f => f.Name == "RECORD");
    }

    private static string? OneLine(string? value) =>
        value is null ? null : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}

internal sealed class ControlResult
{
    public required string Id { get; init; }

    public required string ProducerTest { get; init; }

    public required string Mutation { get; init; }

    public required bool ExpectedDetection { get; init; }

    public required bool Detected { get; init; }

    public required string Disposition { get; init; }

    public string? ExceptionType { get; init; }

    public string? ExceptionMessage { get; init; }
}
