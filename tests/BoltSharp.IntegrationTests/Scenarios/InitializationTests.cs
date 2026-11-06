using BoltSharp.IntegrationTests.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.IntegrationTests.Scenarios;

/// <summary>
/// B0-B-T02: initialization and authentication. Captures HELLO/LOGON startup metadata, establishes
/// which SUCCESS metadata the driver requires, and records the exact failure boundary for invalid
/// credentials. No fabricated authentication success is produced.
/// </summary>
[Collection("B0-B")]
public sealed class InitializationTests
{
    [Fact]
    [Trait("Task", "B0-B-T02")]
    public async Task T02_Initialization_ObservesHelloThenLogonAndRequiredMetadata()
    {
        var script = ScenarioSupport.PositiveScript();

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;
        EvidenceSink.WriteJson("t02-initialization.json", ScenarioSupport.CaptureDocument("T02", capture));

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

        // Sensitive frames are redacted and never carry credential bytes into evidence.
        Assert.True(hello.Sensitive);
        Assert.True(logon.Sensitive);
        Assert.All(
            capture.Frames.Where(f => f.Sensitive),
            f => Assert.Equal("<redacted>", f.SanitizedHex));

        // The HELLO SUCCESS metadata the driver requires is established by a successful connection.
        Assert.Contains(capture.Frames, f => f.Name == "SUCCESS");
        Assert.DoesNotContain(capture.Requests, r => r.Unscripted);
    }

    [Fact]
    [Trait("Task", "B0-B-T02")]
    public async Task T02_InvalidCredentials_FailsAtDocumentedBoundary()
    {
        var script = new BoltConnectionScript();
        script.ExpectHello();
        script.On("LOGON").Failure(
            ("gql_status", ProtocolFixtures.Str("50N42")),
            ("neo4j_code", ProtocolFixtures.Str("Neo.ClientError.Security.Unauthorized")),
            ("description", ProtocolFixtures.Str("The client is unauthorized due to authentication failure.")),
            ("diagnostic_record", ProtocolFixtures.Map()));
        script.On("RESET").SuccessEmpty();
        script.On("GOODBYE").Close();

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        Exception? failure = null;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync(), TimeSpan.FromSeconds(15));
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
        EvidenceSink.WriteJson("t02-invalid-credentials.json", ScenarioSupport.CaptureDocument("T02-auth", capture));

        Assert.NotNull(failure);
        Assert.IsAssignableFrom<Neo4jException>(failure);
        Assert.Contains(capture.Requests, r => r.Name == "LOGON");
        Assert.Contains(capture.Frames, f => f.Name == "FAILURE");
        // The driver must not proceed to a query after an authentication failure.
        Assert.DoesNotContain(capture.Requests, r => r.Name == "RUN");
    }
}
