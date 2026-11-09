using BoltSharp.DriverInteropTests.Infrastructure;
using BoltSharp.Transport;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.DriverInteropTests.Scenarios;

/// <summary>
/// B5-B2 authentication — the driver's Basic LOGON path and the accepted B3-B fail-closed rules.
/// Invalid credentials and a missing verifier must both be refused with no query execution and no
/// credential material in any evidence.
/// </summary>
[Collection("B5-B")]
public sealed class AuthenticationTests
{
    [Fact]
    [Trait("Task", "B5-B2")]
    public async Task B5B2_20_InvalidCredentials_RejectedAndNoRunExecuted()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(
            tap.HostPort,
            AuthTokens.Basic(TestCredentials.User, "wrong-password"));
        Exception? failure = null;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.NotNull(failure);
        var neo4jFailure = Assert.IsAssignableFrom<Neo4jException>(failure);
        Assert.Equal("Neo.ClientError.Security.Unauthorized", neo4jFailure.Code);

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var connection = tap.SingleConnection();
        var transcript = WireTranscript.Build(connection);

        // The server refused at the documented boundary: LOGON FAILURE, no RUN, connection closed.
        Assert.Equal(1, transcript.CountClient("LOGON"));
        Assert.Equal(0, transcript.CountClient("RUN"));
        var logonFailure = transcript.ServerMessages.First(m => m.Name == "FAILURE");
        Assert.Contains("gql_status=50N42", logonFailure.Description);
        Assert.Contains("Neo.ClientError.Security.Unauthorized", logonFailure.Description);
        Assert.DoesNotContain("IGNORED", transcript.ServerMessages.Select(m => m.Name));
        // The driver terminates the connection after the terminal FAILURE; the observed close is
        // recorded (a reset is expected here because the refusal closes mid-lifecycle).
        Assert.NotEqual("open", connection.ClientSideClose);
        Assert.NotEqual("open", connection.ServerSideClose);

        // No credential material anywhere in diagnostics or evidence documents.
        Assert.DoesNotContain(
            server.Events(),
            e => (e.Detail ?? string.Empty).Contains("wrong-password", StringComparison.Ordinal));
        Assert.DoesNotContain(
            server.Events(),
            e => (e.Detail ?? string.Empty).Contains(TestCredentials.Password, StringComparison.Ordinal));

        // The listener remains healthy for a correctly authenticated driver afterwards.
        var healthyDriver = DriverFactory.Plaintext(server.HostPort);
        try
        {
            await DriverFactory.Bounded(healthyDriver.VerifyConnectivityAsync());
        }
        finally
        {
            await healthyDriver.DisposeAsync();
        }

        EvidenceWriter.Write("b5b2-20-invalid-credentials", new
        {
            scenario = "B5B2-20",
            title = "Invalid credentials refused; no RUN; no credential echo",
            uri_scheme = "bolt",
            driver_failure = EvidenceWriter.ExceptionDocument(failure!),
            logon_failure = logonFailure.Description,
            client_sequence = transcript.ClientMessages.Select(m => m.Name),
            server_sequence = transcript.ServerMessages.Select(m => m.Name),
            client_side_close = connection.ClientSideClose,
            server_side_close = connection.ServerSideClose,
            counters_after_first_attempt = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B2")]
    public async Task B5B2_21_MissingVerifier_FailsClosed()
    {
        EvidenceWriter.WriteEnvironment();
        // Explicit null verifier: B3-B's accepted fail-closed default is preserved by the transport.
        await using var server = await B5BServerHost.StartPlaintextAsync(verifier: null);

        var driver = DriverFactory.Plaintext(server.HostPort);
        Exception? failure = null;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.NotNull(failure);
        var neo4jFailure = Assert.IsAssignableFrom<Neo4jException>(failure);
        Assert.Equal("Neo.ClientError.Security.Unauthorized", neo4jFailure.Code);

        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        var counters = server.Counters();
        // HELLO + LOGON only; no query ever ran and no verifier was configured.
        Assert.Equal(2, counters.MessagesProcessed);
        Assert.DoesNotContain(
            server.Events(),
            e => e.Kind == BoltTransportEventKind.MessageProcessed && e.Detail!.Contains("sig=0x10"));

        EvidenceWriter.Write("b5b2-21-missing-verifier", new
        {
            scenario = "B5B2-21",
            title = "No verifier configured: LOGON fails closed, no query executes",
            uri_scheme = "bolt",
            driver_failure = EvidenceWriter.ExceptionDocument(failure!),
            counters = EvidenceWriter.CounterDocument(counters),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B2")]
    public async Task B5B2_22_AuthenticationFailure_IsTerminal_NoRetryOnConnection()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);

        var driver = DriverFactory.Plaintext(
            server.HostPort,
            AuthTokens.Basic(TestCredentials.User, TestCredentials.Password + "-invalid"));
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            Assert.Fail("invalid credentials must not establish connectivity");
        }
        catch (Neo4jException)
        {
            // Expected: the driver reports the authentication failure.
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        var counters = server.Counters();

        // Exactly one authentication attempt on exactly one connection: the terminal FAILURE
        // closes the connection and the driver establishes no second attempt for a non-retriable
        // authentication error.
        Assert.Equal(1, counters.Accepted);
        Assert.Equal(2, counters.MessagesProcessed);
        var logonEvents = server.Events()
            .Count(e => e.Kind == BoltTransportEventKind.MessageProcessed && e.Detail!.Contains("sig=0x6A"));
        Assert.Equal(1, logonEvents);

        EvidenceWriter.Write("b5b2-22-auth-terminal-no-retry", new
        {
            scenario = "B5B2-22",
            title = "Terminal authentication failure: single attempt, single connection",
            uri_scheme = "bolt",
            counters = EvidenceWriter.CounterDocument(counters),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }
}
