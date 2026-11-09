using BoltSharp.DriverInteropTests.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.DriverInteropTests.Scenarios;

/// <summary>
/// B5-B3 — the first official-driver query through the full accepted pipeline. The driver executes
/// <c>RETURN 42 AS answer</c> as an implicit auto-commit transaction and exposes the integer
/// through its result API. The wire evidence additionally captures the driver's actual RUN extra
/// map, the PULL size and the terminal SUCCESS form.
/// </summary>
[Collection("B5-B")]
public sealed class ScalarQueryTests
{
    [Fact]
    [Trait("Task", "B5-B3")]
    public async Task B5B3_01_ScalarAutoCommit_ReturnsInteger42_AndConnectionStaysUsable()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);

        var driver = DriverFactory.Plaintext(server.HostPort);
        long first = -1;
        long second = -1;
        string? fieldName = null;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using (var session = driver.AsyncSession())
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                var record = cursor.Current;
                fieldName = Assert.Single(record.Keys);
                first = record["answer"].As<long>();
                Assert.False(await DriverFactory.Bounded(cursor.FetchAsync()));
                var summary = await DriverFactory.Bounded(cursor.ConsumeAsync());
                Assert.NotNull(summary);
            }

            // The connection remains usable for a subsequent query.
            await using (var session = driver.AsyncSession())
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                second = cursor.Current["answer"].As<long>();
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Equal("answer", fieldName);
        Assert.Equal(42, first);
        Assert.Equal(42, second);

        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0 && c.Completed == 1, TimeSpan.FromSeconds(10)));
        var counters = server.Counters();
        Assert.Equal(1, counters.Accepted);
        Assert.Equal(0, counters.Failed);
        Assert.Equal(0, counters.Active);

        EvidenceWriter.Write("b5b3-01-scalar-auto-commit", new
        {
            scenario = "B5B3-01",
            title = "Implicit auto-commit scalar query through the official driver",
            uri_scheme = "bolt",
            driver_visible = new { field = fieldName, value = first, value_type = "System.Int64", second_query_value = second },
            counters = EvidenceWriter.CounterDocument(counters),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B3")]
    public async Task B5B3_02_ScalarQuery_WireCapture_RunExtraPullAndTerminalSuccess()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using (var session = driver.AsyncSession())
            {
                // Default (write-mode) session: the driver sends an empty RUN extra map.
                var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                Assert.Equal(42, cursor.Current["answer"].As<long>());
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }

            await using (var session = driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Read)))
            {
                // Read access mode adds exactly mode="r" to the extra map.
                var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                Assert.Equal(42, cursor.Current["answer"].As<long>());
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var connection = tap.SingleConnection();
        var transcript = WireTranscript.Build(connection);

        var runs = transcript.ClientMessages.Where(m => m.Name == "RUN").ToArray();
        Assert.Equal(2, runs.Length);
        Assert.Equal("RUN \"RETURN 42 AS answer\" parameters=0 entry(ies) extra={}", runs[0].Description);
        Assert.Equal("RUN \"RETURN 42 AS answer\" parameters=0 entry(ies) extra={mode=r}", runs[1].Description);
        // Authentic request bytes for the default form (the accepted B5-A golden vector).
        Assert.Equal("B310D01352455455524E20343220415320616E73776572A0A0", runs[0].Hex);
        Assert.Equal("B310D01352455455524E20343220415320616E73776572A0A1846D6F64658172", runs[1].Hex);

        // PULL n=1000 (the driver's default fetch size) and exactly one PULL per query.
        var pulls = transcript.ClientMessages.Where(m => m.Name == "PULL").ToArray();
        Assert.Equal(2, pulls.Length);
        Assert.All(pulls, p => Assert.Equal("PULL n=1000", p.Description));
        Assert.All(pulls, p => Assert.Equal("B13FA1816EC903E8", p.Hex));

        // RUN SUCCESS fields=["answer"], RECORD [42] and the terminal SUCCESS {} (no has_more).
        var runSuccesses = transcript.ServerMessages
            .Where(m => m.Name == "SUCCESS" && m.Description.Contains("fields=[answer]", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, runSuccesses.Length);
        Assert.All(runSuccesses, s => Assert.Equal("B170A1866669656C64739186616E73776572", s.Hex));
        Assert.Equal(2, transcript.CountServer("RECORD"));
        Assert.All(
            transcript.ServerMessages.Where(m => m.Name == "RECORD"),
            r => Assert.Equal("B171912A", r.Hex));
        var terminal = transcript.ServerMessages.Last();
        Assert.Equal("SUCCESS", terminal.Name);
        Assert.Equal("B170A0", terminal.Hex);
        Assert.Equal("SUCCESS {}", terminal.Description);

        // No unsupported or unexpected interaction: no retry, no ROUTE, no TELEMETRY, no DISCARD.
        Assert.Equal(1, transcript.CountClient("HELLO"));
        Assert.Equal(0, transcript.CountClient("ROUTE"));
        Assert.Equal(0, transcript.CountClient("TELEMETRY"));
        Assert.Equal(0, transcript.CountClient("DISCARD"));
        Assert.Null(transcript.ParseError);

        EvidenceWriter.Write("b5b3-02-scalar-wire-capture", new
        {
            scenario = "B5B3-02",
            title = "RUN/PULL/terminal-SUCCESS wire capture with driver-visible results",
            uri_scheme = "bolt",
            run_extra_observations = new
            {
                default_session_extra = "{}",
                read_mode_session_extra = "{mode:\"r\"}",
                pull_extra = "{n:1000}",
                terminal_success_hex = terminal.Hex,
                has_more_field_absent = true,
            },
            transcript = transcript.ToDocument(connection),
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B3")]
    public async Task B5B3_04_HomeDatabaseMetadata_NotRequiredByDriver_NoResolutionRequest()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();

            // Two sequential implicit transactions on the same connection without any explicit
            // database. Nothing in this flow may trigger a database-resolution or routing request.
            for (var i = 0; i < 2; i++)
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                Assert.Equal(42, cursor.Current["answer"].As<long>());
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var connection = tap.SingleConnection();
        var transcript = WireTranscript.Build(connection);

        // Bolt 5.8 defines RUN SUCCESS db (resolved home database) as present only when the client
        // did not explicitly select a database. The accepted server omits it; the driver neither
        // requires it nor issues any resolution request (no ROUTE, no extra query, no TELEMETRY).
        var runSuccesses = transcript.ServerMessages
            .Where(m => m.Name == "SUCCESS" && m.Description.Contains("fields=[answer]", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, runSuccesses.Length);
        Assert.All(runSuccesses, s => Assert.DoesNotContain("db=", s.Description));
        Assert.DoesNotContain(transcript.ServerMessages, m => m.Description.Contains("db=", StringComparison.Ordinal));
        Assert.Equal(0, transcript.CountClient("ROUTE"));
        Assert.Equal(0, transcript.CountClient("TELEMETRY"));
        Assert.Equal(2, transcript.CountClient("RUN"));
        Assert.Equal(1, transcript.CountClient("HELLO"));
        Assert.Equal(1, server.Counters().Accepted);

        EvidenceWriter.Write("b5b3-04-home-database-metadata", new
        {
            scenario = "B5B3-04",
            title = "ABSENT home-database metadata is not required; no database-resolution request occurs",
            uri_scheme = "bolt",
            observations = new
            {
                run_success_metadata = runSuccesses.Select(s => s.Description),
                run_success_hex = runSuccesses.Select(s => s.Hex),
                db_field_present = false,
                route_requests = 0,
                telemetry_requests = 0,
                database_resolution_requests = 0,
                connections = 1,
            },
            specification_note = "Bolt 5.8: RUN SUCCESS 'db' (resolved user's home database) is only " +
                "present when the client did not explicitly provide a database; absence is conformant " +
                "and the official driver functions without it on a direct bolt:// connection.",
            driver_visible = new { queries_successful = 2, value = 42 },
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B3")]
    public async Task B5B3_03_TerminalSuccessWithoutHasMore_IsExhaustion_NoExtraPull()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
            // The driver treats the terminal SUCCESS {} (absent has_more defaults to false) as
            // exhaustion: a further fetch yields no record without another PULL being necessary.
            Assert.False(await DriverFactory.Bounded(cursor.FetchAsync()));
            var summary = await DriverFactory.Bounded(cursor.ConsumeAsync());
            Assert.NotNull(summary);
            Assert.False(cursor.IsOpen);
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var connection = tap.SingleConnection();
        var transcript = WireTranscript.Build(connection);
        Assert.Equal(1, transcript.CountClient("PULL"));
        Assert.Equal(1, transcript.CountServer("RECORD"));
        Assert.Equal("B170A0", transcript.ServerMessages.Last().Hex);

        EvidenceWriter.Write("b5b3-03-terminal-exhaustion", new
        {
            scenario = "B5B3-03",
            title = "Terminal SUCCESS {} without has_more treated as result exhaustion",
            uri_scheme = "bolt",
            pull_count = transcript.CountClient("PULL"),
            terminal_success_hex = transcript.ServerMessages.Last().Hex,
            driver_visible = new { fetch_after_exhaustion = false, cursor_open_after_exhaustion = false },
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }
}
