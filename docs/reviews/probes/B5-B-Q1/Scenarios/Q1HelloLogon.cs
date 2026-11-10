using BoltSharp.B5BQ1.Probes.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.B5BQ1.Probes.Scenarios;

/// <summary>
/// Q1-B: HELLO and LOGON metadata captured independently through the reviewer tap, plus
/// authentication negative controls. Credential-bearing LOGON payloads are redacted at the
/// raw-chunk level by the tap before storage; the sentinel guard re-checks every document.
/// </summary>
[Collection("Q1")]
public sealed class Q1HelloLogonProbes
{
    [Fact]
    public async Task Q1B01_HelloLogon_MetadataShapesAndServerMetadata()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.Plaintext(tap.HostPort);
        IServerInfo? info = null;
        try
        {
            await Q1Driver.Bounded(driver.VerifyConnectivityAsync());
            info = await Q1Driver.Bounded(driver.GetServerInfoAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var (clientMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ClientToServer);
        var (serverMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ServerToClient);

        var hello = clientMessages.First(m => m.Name == "HELLO");
        var logon = clientMessages.First(m => m.Name == "LOGON");
        var helloSuccess = serverMessages.Where(m => m.Name == "SUCCESS").First();
        var logonSuccess = serverMessages.Where(m => m.Name == "SUCCESS").Skip(1).First();

        // HELLO keys, independently decoded from the wire by the reviewer's probe.
        Assert.Contains("user_agent=", hello.Description);
        Assert.Contains("routing=", hello.Description);
        Assert.Contains("bolt_agent=", hello.Description);
        Assert.StartsWith("neo4j-dotnet/", GetStringBetween(hello.Description, "user_agent=", ","));
        Assert.Contains("product=neo4j-dotnet/", hello.Description);

        // LOGON keys: scheme, principal, credentials — but the payload itself is redacted.
        Assert.True(logon.Redacted);
        Assert.Equal("<redacted>", logon.Hex);

        // HELLO SUCCESS metadata: server, connection_id, hints.
        Assert.Contains("server=BoltSharp/0.1.0", helloSuccess.Description);
        Assert.Contains("connection_id=", helloSuccess.Description);
        Assert.Contains("hints={}", helloSuccess.Description);
        // Optional metadata absent: protocol_version, advertised_address. The driver must not
        // require them (independently verified by the successful driver-visible flow above).
        Assert.DoesNotContain("protocol_version", helloSuccess.Description);
        Assert.DoesNotContain("advertised_address", logonSuccess.Description);
        // LOGON SUCCESS is the empty SUCCESS {} (B1 70 A0).
        Assert.Equal("B170A0", logonSuccess.Hex);

        // Driver-visible authentication succeeded through the normal path.
        Assert.NotNull(info);
        Assert.Contains("BoltSharp/0.1.0", info!.Agent);

        // The reviewer credential value must not appear anywhere in the captured evidence.
        var serialized = System.Text.Json.JsonSerializer.Serialize(new
        {
            client = Q1Evidence.MessagesDocument(clientMessages),
            server = Q1Evidence.MessagesDocument(serverMessages),
        });
        Assert.DoesNotContain(Q1Credentials.Password, serialized, StringComparison.Ordinal);

        Q1Evidence.Write("q1b01-hello-logon", new
        {
            probe = "Q1-B01",
            hello = new { keys_description = hello.Description, hex = hello.Hex },
            logon = new { redacted = logon.Redacted, note = logon.Description },
            hello_success = helloSuccess.Description,
            hello_success_hex = helloSuccess.Hex,
            logon_success_hex = logonSuccess.Hex,
            driver_visible = new { server_agent = info!.Agent },
            client_messages = Q1Evidence.MessagesDocument(clientMessages),
            server_messages = Q1Evidence.MessagesDocument(serverMessages),
        });
    }

    [Fact]
    public async Task Q1B02_NegativeControl_InvalidCredentials_FailClosed_NoRun()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.Plaintext(tap.HostPort, Q1Driver.WrongToken());
        Exception? failure = null;
        try
        {
            await Q1Driver.Bounded(driver.VerifyConnectivityAsync());
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
        // The driver surfaces the terminal LOGON FAILURE as an authentication error carrying the
        // accepted Unauthorized code (the exact driver type is recorded in the evidence document).
        var neo4jFailure = Assert.IsAssignableFrom<Neo4jException>(failure);
        Assert.Equal("Neo.ClientError.Security.Unauthorized", neo4jFailure.Code);

        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        var counters = server.Counters();
        // Exactly HELLO + LOGON were processed; no RUN ever executed.
        Assert.Equal(2, counters.MessagesProcessed);
        Assert.Equal(0, server.CountProcessedSignatures("0x10"));

        // Server closed the connection after the terminal LOGON FAILURE.
        var (serverMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ServerToClient);
        var logonFailure = serverMessages.FirstOrDefault(m => m.Name == "FAILURE");
        Assert.NotNull(logonFailure);
        Assert.Contains("50N42", logonFailure!.Description);
        Assert.Contains("Neo.ClientError.Security.Unauthorized", logonFailure.Description);

        // No credential echo: the FAILURE description carries no credential value.
        Assert.DoesNotContain(Q1Credentials.Password, logonFailure.Description, StringComparison.Ordinal);

        Q1Evidence.Write("q1b02-invalid-credentials", new
        {
            probe = "Q1-B02",
            driver_failure = Q1Driver.ExceptionDocument(failure!),
            server_failure = logonFailure.Description,
            counters = Q1Evidence.CounterDocument(counters),
        });
    }

    [Fact]
    public async Task Q1B03_NegativeControl_MissingVerifier_FailClosed()
    {
        await using var server = await Q1Server.StartPlaintextAsync(verifier: null);

        var driver = Q1Driver.Plaintext(server.HostPort);
        Exception? failure = null;
        try
        {
            await Q1Driver.Bounded(driver.VerifyConnectivityAsync());
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
        var neo4jFailure = Assert.IsAssignableFrom<Neo4jException>(failure);
        Assert.Equal("Neo.ClientError.Security.Unauthorized", neo4jFailure.Code);
        Assert.Equal(2, server.Counters().MessagesProcessed);

        Q1Evidence.Write("q1b03-missing-verifier", new
        {
            probe = "Q1-B03",
            driver_failure = Q1Driver.ExceptionDocument(failure!),
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    public async Task Q1B04_NegativeControl_ThrowingVerifier_FailClosed_NoCredentialSurface()
    {
        // A throwing verifier is an unexpected fault: the accepted contract fails closed.
        var throwingVerifier = new ThrowingVerifier();
        await using var server = await Q1Server.StartPlaintextAsync(throwingVerifier);

        var driver = Q1Driver.Plaintext(server.HostPort);
        Exception? failure = null;
        try
        {
            await Q1Driver.Bounded(driver.VerifyConnectivityAsync());
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
        var neo4jFailure = Assert.IsAssignableFrom<Neo4jException>(failure);
        Assert.Equal("Neo.ClientError.Security.Unauthorized", neo4jFailure.Code);
        // The verifier exception text must not surface to the driver.
        Assert.DoesNotContain(ThrowingVerifier.Sentinel, failure!.Message, StringComparison.Ordinal);
        Assert.Equal(2, server.Counters().MessagesProcessed);

        Q1Evidence.Write("q1b04-throwing-verifier", new
        {
            probe = "Q1-B04",
            driver_failure = Q1Driver.ExceptionDocument(failure!),
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }

    private static string GetStringBetween(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal) + start.Length;
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        return text[from..to];
    }

    private sealed class ThrowingVerifier : BoltSharp.Sessions.IBoltCredentialVerifier
    {
        internal const string Sentinel = "q1-throwing-verifier-internal-detail";

        public bool Verify(BoltSharp.Messages.LogonRequest logon)
        {
            throw new InvalidOperationException(Sentinel);
        }
    }
}
