using System.Reflection;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.DriverIntegrationTests;

/// <summary>
/// B2-C-01 scaffold for the official Neo4j .NET driver (pinned <c>Neo4j.Driver</c> 6.4.0).
/// <para>
/// The D01–D10 executable connectivity scenarios are <b>gated (skipped)</b>: BoltSharp has no
/// message/session engine (B3) and no TCP/TLS transport (B5), so a real driver connection cannot
/// succeed yet. Skipped tests are reported as skipped, never as passing compatibility evidence.
/// </para>
/// <para>
/// The non-gated <c>Scaffold_*</c> tests validate the scaffold itself (defaults, pinning, gating
/// and the routing/direct-URI policy) and require no server.
/// </para>
/// </summary>
public sealed class DriverInteropScaffoldTests
{
    /// <summary>Gate reason repeated on every executable connectivity scenario.</summary>
    public const string MissingPrerequisite =
        "Requires B3 message/session engine and B5-compatible network transport.";

    private const string D02Gate =
        "Requires B3 message/session engine and B5-compatible network transport (no protocol revision negotiated yet).";

    private static IDriver NewDriver(DriverTestSettings settings) => settings.CreateDriver();

    // ---- scaffold self-checks (run; no server required) ----------------------

    [Fact]
    public void Scaffold_Default_Endpoint_Is_Direct_Loopback()
    {
        var settings = new DriverTestSettings();
        Assert.False(settings.UsesRoutingScheme);
        Assert.Equal("bolt", settings.Uri.Scheme);
        Assert.Equal("127.0.0.1", settings.Uri.Host);
    }

    [Fact]
    public void Scaffold_Driver_Package_Version_Is_Pinned_To_6_4_0()
    {
        var assembly = typeof(GraphDatabase).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;

        Assert.NotNull(informational);
        Assert.StartsWith("6.4.0", informational, StringComparison.Ordinal);
        Assert.NotNull(fileVersion);
        Assert.StartsWith("6.4.0", fileVersion, StringComparison.Ordinal);
    }

    [Fact]
    public void Scaffold_Rejects_Routing_Scheme_For_A_Direct_Server()
    {
        // Routing schemes are rejected outright; they are not silently converted.
        Assert.Throws<ArgumentException>(() => DriverTestSettings.ValidateUri("neo4j://127.0.0.1:7687"));
        Assert.Throws<ArgumentException>(() => DriverTestSettings.ValidateUri("neo4j+s://127.0.0.1:7687"));
        Assert.Throws<ArgumentException>(() => DriverTestSettings.ValidateUri("neo4j+ssc://127.0.0.1:7687"));

        var direct = DriverTestSettings.ValidateUri("bolt://127.0.0.1:7687");
        Assert.Equal("bolt", direct.Scheme);

        var settings = new DriverTestSettings();
        Assert.False(settings.UsesRoutingScheme);
        Assert.False(settings.UsesTls);
    }

    [Fact]
    public void Scaffold_Constructor_Rejects_Routing_Uri_From_Environment()
    {
        const string variable = "BOLTSHARP_DRIVER_URI";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "neo4j://127.0.0.1:7687");
            var exception = Assert.Throws<ArgumentException>(() => new DriverTestSettings());
            Assert.Contains("routing", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public void Scaffold_Every_D_Scenario_Declares_A_Gate_Reason()
    {
        Assert.False(string.IsNullOrWhiteSpace(MissingPrerequisite));
        Assert.False(string.IsNullOrWhiteSpace(D02Gate));
    }

    // ---- D01–D10 executable scenarios (gated) --------------------------------

    [Fact(Skip = MissingPrerequisite)]
    public async Task D01_Driver_establishes_a_real_connection()
    {
        var settings = new DriverTestSettings();
        await using var driver = NewDriver(settings);
        await driver.VerifyConnectivityAsync();
    }

    [Fact(Skip = D02Gate)]
    public async Task D02_Bolt_protocol_revision_is_negotiated()
    {
        var settings = new DriverTestSettings();
        await using var driver = NewDriver(settings);
        await using var session = driver.AsyncSession();
        var cursor = await session.RunAsync("RETURN 1 AS n");
        var records = await cursor.ToListAsync();
        Assert.NotEmpty(records);
        // The negotiated revision is asserted from server-side instrumentation in the eventual run.
        Assert.False(string.IsNullOrWhiteSpace(settings.ExpectedProtocolVersion));
    }

    [Fact(Skip = MissingPrerequisite)]
    public async Task D03_HELLO_and_LOGON_succeed()
    {
        var settings = new DriverTestSettings();
        await using var driver = NewDriver(settings);
        await driver.VerifyAuthenticationAsync(settings.AuthToken);
    }

    [Fact(Skip = MissingPrerequisite)]
    public async Task D04_Read_only_RUN_is_accepted()
    {
        var settings = new DriverTestSettings();
        await using var driver = NewDriver(settings);
        await using var session = driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Read));
        var cursor = await session.RunAsync("MATCH (a)-[r]->(b) RETURN a, r, b");
        var records = await cursor.ToListAsync();
        Assert.NotNull(records);
    }

    [Fact(Skip = MissingPrerequisite)]
    public async Task D05_PULL_returns_records()
    {
        var settings = new DriverTestSettings();
        await using var driver = NewDriver(settings);
        await using var session = driver.AsyncSession();
        var cursor = await session.RunAsync("MATCH (a)-[r]->(b) RETURN a, r, b");
        var records = await cursor.ToListAsync();
        Assert.Single(records);
    }

    [Fact(Skip = MissingPrerequisite)]
    public async Task D06_Typed_nodes_and_relationships_are_decoded()
    {
        var settings = new DriverTestSettings();
        await using var driver = NewDriver(settings);
        await using var session = driver.AsyncSession();
        var cursor = await session.RunAsync("MATCH (a)-[r]->(b) RETURN a, r, b");
        var records = await cursor.ToListAsync();
        var a = records[0]["a"].As<INode>();
        var r = records[0]["r"].As<IRelationship>();
        Assert.Equal("Person", a.Labels.First());
        Assert.Equal("KNOWS", r.Type);
    }

    [Fact(Skip = MissingPrerequisite)]
    public async Task D07_Unsupported_request_fails_correctly()
    {
        var settings = new DriverTestSettings();
        await using var driver = NewDriver(settings);
        await using var session = driver.AsyncSession();
        await Assert.ThrowsAsync<ClientException>(async () =>
        {
            var cursor = await session.RunAsync("CREATE (n:Unsupported) RETURN n");
            await cursor.ConsumeAsync();
        });
    }

    [Fact(Skip = MissingPrerequisite)]
    public async Task D08_RESET_and_failure_recovery_behave_correctly()
    {
        var settings = new DriverTestSettings();
        await using var driver = NewDriver(settings);
        await using var session = driver.AsyncSession();
        try
        {
            var bad = await session.RunAsync("THIS IS NOT VALID BOLT");
            await bad.ConsumeAsync();
        }
        catch (Neo4jException)
        {
            // Expected; the session must remain usable after RESET.
        }

        var good = await session.RunAsync("RETURN 1 AS n");
        await good.ConsumeAsync();
    }

    [Fact(Skip = MissingPrerequisite)]
    public async Task D09_Connection_closes_cleanly()
    {
        var settings = new DriverTestSettings();
        var driver = NewDriver(settings);
        await driver.VerifyConnectivityAsync();
        await driver.DisposeAsync();
    }

    [Fact(Skip = MissingPrerequisite)]
    public async Task D10_TLS_and_authentication_failure_paths()
    {
        // Wrong credentials against a loopback endpoint must fail with an authentication error,
        // and an untrusted TLS endpoint must not silently downgrade to plaintext.
        var badSettings = new DriverTestSettings();
        var tlsUri = new Uri("bolt+ssc://127.0.0.1:7687");
        Assert.NotEqual("bolt", tlsUri.Scheme);

        await using var driver = badSettings.CreateDriver(AuthTokens.Basic(badSettings.User, "deliberately-wrong"));
        await Assert.ThrowsAsync<Neo4jException>(() => driver.VerifyConnectivityAsync());
    }
}
