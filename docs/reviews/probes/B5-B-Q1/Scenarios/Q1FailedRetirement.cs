using BoltSharp.B5BQ1.Probes.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.B5BQ1.Probes.Scenarios;

/// <summary>
/// Q1-K: the mandatory fail-closed retirement control, driven end-to-end through the official
/// driver's public API, the real B5-A transport and a reviewer-owned fault-injecting operation
/// provider injected through the supported BoltTcpServerOptions.OperationProviderFactory seam.
/// No production source is modified.
/// </summary>
[Collection("Q1")]
public sealed class Q1FailedRetirementProbes
{
    [Fact]
    public async Task Q1K01_Mandatory_FailedRetirementThroughOfficialDriver_FailClosed()
    {
        var providers = new Q1ProviderLog();
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier, providers: providers);

        var driver = Q1Driver.Plaintext(server.HostPort);
        Exception? failure = null;
        try
        {
            await using var session = driver.AsyncSession(o => o.WithFetchSize(1));
            // Arm the fault before the connection opens: the FIRST operation's retirement (Dispose)
            // will throw after the otherwise-valid result completes.
            providers.ArmFirstDisposeFault = true;
            var cursor = await Q1Driver.Bounded(session.RunAsync("RETURN 42 AS answer"));
            var fetched = await Q1Driver.Bounded(cursor.FetchAsync());
            // The terminal retirement happens when the result completes; the driver must observe
            // the failure rather than a fabricated successful completion.
            if (fetched)
            {
                await Q1Driver.Bounded(cursor.ConsumeAsync());
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        Assert.NotNull(failure);
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));

        // Per-connection oracle for the affected (first) connection.
        var affected = providers.Providers[0];
        Assert.Equal(1, affected.OperationsOpened);
        Assert.Equal(1, affected.DisposeCalls);
        Assert.Equal(1, affected.DisposeFailures);

        // No subsequent RUN or second operation on the affected connection, even after the driver
        // later opens new work (which must use a different physical connection).
        try
        {
            await using var session2 = driver.AsyncSession();
            var cursor2 = await Q1Driver.Bounded(session2.RunAsync("RETURN 42 AS answer"));
            Assert.True(await Q1Driver.Bounded(cursor2.FetchAsync()));
            Assert.Equal(42L, cursor2.Current["answer"].As<long>());
            await Q1Driver.Bounded(cursor2.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.Equal(1, affected.OperationsOpened);
        Assert.Equal(1, affected.DisposeCalls);
        Assert.Equal(1, affected.DisposeFailures);
        // The affected connection never served another Prepare.
        Assert.Equal(1, affected.PrepareCalls);

        Q1Evidence.Write("q1k01-failed-retirement", new
        {
            probe = "Q1-K01",
            driver_failure = Q1Driver.ExceptionDocument(failure!),
            affected_connection = Q1Evidence.ProvidersDocument(providers),
            physical_connections = providers.Providers.Count,
            counters = Q1Evidence.CounterDocument(server.Counters()),
            verdict = "fail-closed: exactly one disposal attempt, no successful terminal result, " +
                      "no second operation on the affected connection",
        });
    }

    [Fact]
    public async Task Q1K02_FailedRetirement_NoSuccessfulTerminalResponse_WireOracle()
    {
        var providers = new Q1ProviderLog();
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier, providers: providers);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.Plaintext(tap.HostPort);
        Exception? failure = null;
        try
        {
            await using var session = driver.AsyncSession(o => o.WithFetchSize(1));
            providers.ArmFirstDisposeFault = true;
            var cursor = await Q1Driver.Bounded(session.RunAsync("RETURN 42 AS answer"));
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

        Assert.NotNull(failure);
        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));

        var (clientMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ClientToServer);
        var (serverMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ServerToClient);

        // The PULL's terminal response is FAILURE (never a SUCCESS completion after the failed
        // retirement), and the server closes rather than offering RESET recovery.
        var pullIndex = clientMessages.ToList().FindIndex(m => m.Name == "PULL");
        Assert.True(pullIndex >= 0);
        var responsesAfterPull = serverMessages.Skip(pullIndex).ToArray();
        var terminal = responsesAfterPull.LastOrDefault();
        Assert.NotNull(terminal);
        Assert.Equal("FAILURE", terminal!.Name);
        Assert.Contains("Neo.DatabaseError.General.UnknownError", terminal.Description);

        // No SUCCESS after the failed retirement on this connection.
        Assert.DoesNotContain(responsesAfterPull, m => m.Name == "SUCCESS");

        // The server never PROCESSES anything after the failure (no RESET recovery, no second
        // RUN): whatever the driver wrote after closure, it was never handled. The per-connection
        // event stream is the oracle; MessagesProcessed counts only HELLO/LOGON/RUN/PULL.
        var processed = server.Events()
            .Where(e => e.Kind == BoltSharp.Transport.BoltTransportEventKind.MessageProcessed)
            .Select(e => e.Detail ?? string.Empty)
            .ToArray();
        Assert.Equal(4, processed.Length);
        Assert.Contains(processed, d => d.Contains("sig=0x01"));
        Assert.Contains(processed, d => d.Contains("sig=0x6A"));
        Assert.Contains(processed, d => d.Contains("sig=0x10"));
        Assert.Contains(processed, d => d.Contains("sig=0x3F"));
        Assert.DoesNotContain(processed, d => d.Contains("sig=0x0F")); // RESET (Bolt 5.8 signature 0x0F)

        Q1Evidence.Write("q1k02-failed-retirement-wire", new
        {
            probe = "Q1-K02",
            driver_failure = Q1Driver.ExceptionDocument(failure!),
            client_sequence = clientMessages.Select(m => m.Name).ToArray(),
            server_sequence = serverMessages.Select(m => m.Name).ToArray(),
            terminal_description = terminal.Description,
            server_close = tap.ServerCloses,
            client_close = tap.ClientCloses,
            processed_events = processed,
        });
    }

    [Fact]
    public async Task Q1K03_Control_RecoverableProviderFailure_PrepareThrows()
    {
        var providers = new Q1ProviderLog();
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier, providers: providers);

        var driver = Q1Driver.Plaintext(server.HostPort);
        Exception? failure = null;
        long recoveryAnswer = 0;
        try
        {
            await using var session = driver.AsyncSession();
            providers.ArmFirstPrepareFault = true;
            var cursor = await Q1Driver.Bounded(session.RunAsync("RETURN 42 AS answer"));
            await Q1Driver.Bounded(cursor.FetchAsync());
            await Q1Driver.Bounded(cursor.ConsumeAsync());
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        Assert.NotNull(failure);
        Assert.Equal(0, providers.Providers[0].OperationsOpened);
        Assert.Equal(0, providers.Providers[0].DisposeCalls);

        try
        {
            await using var session2 = driver.AsyncSession();
            var cursor2 = await Q1Driver.Bounded(session2.RunAsync("RETURN 42 AS answer"));
            Assert.True(await Q1Driver.Bounded(cursor2.FetchAsync()));
            recoveryAnswer = cursor2.Current["answer"].As<long>();
            await Q1Driver.Bounded(cursor2.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.Equal(42L, recoveryAnswer);

        Q1Evidence.Write("q1k03-recoverable-provider-failure", new
        {
            probe = "Q1-K03",
            driver_failure = Q1Driver.ExceptionDocument(failure!),
            recovery_answer = recoveryAnswer,
            physical_connections = providers.Providers.Count,
            per_connection = Q1Evidence.ProvidersDocument(providers),
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    public async Task Q1K04_Control_ClientDisposalDuringActiveResult_OperationRetiredOnce()
    {
        var providers = new Q1ProviderLog();
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier, providers: providers);

        var driver = Q1Driver.Plaintext(server.HostPort);
        try
        {
            await using var session = driver.AsyncSession(o => o.WithFetchSize(1));
            var cursor = await Q1Driver.Bounded(
                session.RunAsync("MATCH (n:Person) RETURN n.name AS name ORDER BY name"));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
            // Dispose the session with the result still active.
            await Q1Driver.Bounded(session.CloseAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        var affected = providers.Providers[0];
        Assert.Equal(1, affected.OperationsOpened);
        Assert.Equal(1, affected.DisposeCalls);
        Assert.Equal(0, affected.DisposeFailures);

        Q1Evidence.Write("q1k04-client-disposal-active-result", new
        {
            probe = "Q1-K04",
            per_connection = Q1Evidence.ProvidersDocument(providers),
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }
}
