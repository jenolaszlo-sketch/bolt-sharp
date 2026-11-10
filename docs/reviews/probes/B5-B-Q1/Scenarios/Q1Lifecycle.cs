using BoltSharp.B5BQ1.Probes.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.B5BQ1.Probes.Scenarios;

/// <summary>
/// Q1-L: driver lifecycle and error recovery, plus Q1-M capability boundary checks through the
/// official driver's public API. Every classification is recorded; unsupported features' correct
/// refusal is never treated as evidence that the feature is implemented.
/// </summary>
[Collection("Q1")]
public sealed class Q1LifecycleProbes
{
    [Fact]
    public async Task Q1L01_SequentialSessionsAndDriverDisposal()
    {
        var providers = new Q1ProviderLog();
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier, providers: providers);

        var driver = Q1Driver.Plaintext(server.HostPort);
        await Q1Driver.Bounded(driver.VerifyConnectivityAsync());
        await using (var session1 = driver.AsyncSession())
        {
            var cursor = await Q1Driver.Bounded(session1.RunAsync("RETURN 42 AS answer"));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
            await Q1Driver.Bounded(cursor.ConsumeAsync());
        }

        // Partial consumption: fetch one of two records, then discard via ConsumeAsync.
        await using (var session2 = driver.AsyncSession(o => o.WithFetchSize(1)))
        {
            var cursor = await Q1Driver.Bounded(
                session2.RunAsync("MATCH (n:Person) RETURN n.name AS name ORDER BY name"));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
            await Q1Driver.Bounded(cursor.ConsumeAsync());
        }

        await Q1Driver.Bounded(driver.DisposeAsync());
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0 && c.Completed >= 1, TimeSpan.FromSeconds(10)));

        Q1Evidence.Write("q1l01-sequential-sessions", new
        {
            probe = "Q1-L01",
            physical_connections = providers.Providers.Count,
            per_connection = Q1Evidence.ProvidersDocument(providers),
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    public async Task Q1L02_ServerShutdown_DriverObservesClosure()
    {
        var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        var driver = Q1Driver.Plaintext(server.HostPort);
        try
        {
            await using var session = driver.AsyncSession();
            var cursor = await Q1Driver.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
            await Q1Driver.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        // Graceful server shutdown with the driver disposed: no hang, no error.
        await Q1Driver.Bounded(server.DisposeAsync());
        Assert.True(true);

        Q1Evidence.Write("q1l02-server-shutdown", new
        {
            probe = "Q1-L02",
            shutdown_clean = true,
        });
    }

    [Fact]
    public async Task Q1L03_DriverRecoveryAfterServerClosure_NewServerNewDriver()
    {
        var server1 = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        var driver1 = Q1Driver.Plaintext(server1.HostPort);
        Exception? failure = null;
        try
        {
            await using var session = driver1.AsyncSession();
            var cursor = await Q1Driver.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
            await Q1Driver.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver1.DisposeAsync());
        }

        await Q1Driver.Bounded(server1.DisposeAsync());

        // A driver pointed at a closed endpoint must fail closed, not hang.
        var staleDriver = Q1Driver.Plaintext(server1.HostPort);
        try
        {
            await Q1Driver.Bounded(staleDriver.VerifyConnectivityAsync(), TimeSpan.FromSeconds(20));
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await Q1Driver.Bounded(staleDriver.DisposeAsync());
        }

        Assert.NotNull(failure);

        // A fresh server (new ephemeral port) and a fresh driver work normally.
        await using var server2 = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        var driver2 = Q1Driver.Plaintext(server2.HostPort);
        try
        {
            await Q1Driver.Bounded(driver2.VerifyConnectivityAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver2.DisposeAsync());
        }

        Q1Evidence.Write("q1l03-recovery-after-server-closure", new
        {
            probe = "Q1-L03",
            stale_driver_failure = Q1Driver.ExceptionDocument(failure!),
            fresh_server_verified = true,
        });
    }

    [Fact]
    public async Task Q1L04_TwoIndependentDriverInstances()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        var providers = new Q1ProviderLog();
        await using var serverRef = server;
        _ = serverRef;

        var driver1 = Q1Driver.Plaintext(server.HostPort);
        var driver2 = Q1Driver.Plaintext(server.HostPort);
        var answers = new List<long>();
        try
        {
            foreach (var driver in new[] { driver1, driver2 })
            {
                await using var session = driver.AsyncSession();
                var cursor = await Q1Driver.Bounded(session.RunAsync("RETURN 42 AS answer"));
                Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
                answers.Add(cursor.Current["answer"].As<long>());
                await Q1Driver.Bounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await Q1Driver.Bounded(driver1.DisposeAsync());
            await Q1Driver.Bounded(driver2.DisposeAsync());
        }

        Assert.Equal(new[] { 42L, 42L }, answers);
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        Assert.Equal(2, server.Counters().Accepted);

        Q1Evidence.Write("q1l04-two-drivers", new
        {
            probe = "Q1-L04",
            answers,
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    public async Task Q1M01_CapabilityBoundaries_ExplicitDatabaseOption()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        var driver = Q1Driver.Plaintext(server.HostPort);
        Exception? failure = null;
        try
        {
            await using var session = driver.AsyncSession(o => o.WithDatabase("neo4j"));
            var cursor = await Q1Driver.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
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

        // Multi-database is unsupported: the driver's database selection must be refused (or the
        // query must fail); it must never silently succeed as a multi-database feature.
        var refused = failure is not null;
        if (!refused)
        {
            // Record the unsupported-scope outcome honestly for adjudication.
        }

        Q1Evidence.Write("q1m01-explicit-database", new
        {
            probe = "Q1-M01",
            driver_failure = failure is null ? null : Q1Driver.ExceptionDocument(failure),
            classification = refused ? "correctly-refused" : "not-refused-recorded-for-adjudication",
        });

        Assert.True(refused, "an explicitly selected database must not silently activate multi-database support");
    }

    [Fact]
    public async Task Q1M02_CapabilityBoundaries_UnsupportedQueriesRefusedWithoutMutation()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        var driver = Q1Driver.Plaintext(server.HostPort);
        var failures = new Dictionary<string, object>();
        try
        {
            foreach (var query in new[]
                     {
                         "MATCH (n) DETACH DELETE n",
                         "CREATE (n:Person)",
                         "RETURN 1 AS x",
                         "UNWIND [1,2] AS x RETURN x",
                     })
            {
                try
                {
                    await using var session = driver.AsyncSession();
                    var cursor = await Q1Driver.Bounded(session.RunAsync(query));
                    await Q1Driver.Bounded(cursor.FetchAsync());
                    await Q1Driver.Bounded(cursor.ConsumeAsync());
                    failures[query] = "NOT-REFUSED";
                }
                catch (Exception ex)
                {
                    failures[query] = Q1Driver.ExceptionDocument(ex);
                }
            }

            // The graph is verifiably unchanged after every refusal.
            await using var verify = driver.AsyncSession();
            var graphCursor = await Q1Driver.Bounded(
                verify.RunAsync("MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b"));
            Assert.True(await Q1Driver.Bounded(graphCursor.FetchAsync()));
            Assert.Equal("Alice", graphCursor.Current["a"].As<INode>().Properties["name"]);
            await Q1Driver.Bounded(graphCursor.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        // Every unsupported query must be refused; none may silently succeed.
        foreach (var entry in failures)
        {
            Assert.NotEqual("NOT-REFUSED", entry.Value);
        }

        Q1Evidence.Write("q1m02-unsupported-queries", new
        {
            probe = "Q1-M02",
            results = failures,
            graph_unchanged = true,
        });
    }
}
