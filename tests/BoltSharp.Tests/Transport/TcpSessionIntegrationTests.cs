using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.Transport;
using BoltSharp.PackStream;
using BoltSharp.Tests.Sessions;

using Xunit;

namespace BoltSharp.Tests.Transport;

/// <summary>B5-A5/§13.1/§15: full conversation and authentication/failure-state flows over TCP.</summary>
public sealed class TcpSessionIntegrationTests
{
    [Fact]
    public async Task FullConversation_HelloLogonRunPull_Returns42()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));

        client.SendRequest(SessionHarness.Hello());
        var hello = Assert.IsType<SuccessResponse>(client.ReadResponse());
        Assert.Equal("BoltSharp/0.1.0", hello.Server);
        Assert.NotNull(hello.ConnectionId);

        client.SendRequest(SessionHarness.Logon());
        Assert.IsType<SuccessResponse>(client.ReadResponse());

        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        var run = Assert.IsType<SuccessResponse>(client.ReadResponse());
        Assert.Equal(new[] { "answer" }, run.Fields);

        client.SendRequest(new PullRequest(1000, null));
        var responses = client.ReadResult();
        Assert.Equal(2, responses.Length);
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(Assert.IsType<RecordResponse>(responses[0]).Data.Items[0]).Value);
        Assert.NotEqual(true, Assert.IsType<SuccessResponse>(responses[1]).HasMore);

        // The connection remains usable after normal query completion.
        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(new PullRequest(1000, null));
        Assert.Equal(2, client.ReadResult().Length);
    }

    [Fact]
    public async Task RunBeforeLogon_IsTerminalProtocolViolation()
    {
        // Accepted B3-B semantics: RUN outside READY (unauthorized) is a protocol violation —
        // FAILURE, DEFUNCT, connection closes. It is not IGNORED.
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));

        client.SendRequest(SessionHarness.Hello());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<FailureResponse>(client.ReadResponse());
        Assert.Equal("closed", client.WaitForClose(5_000));
    }

    [Fact]
    public async Task InvalidLogon_FailsThenConnectionCloses()
    {
        // Accepted B3-B semantics: an authentication failure answers FAILURE and makes the
        // connection terminal (fail-closed); no further work is processed.
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.Accept("neo4j", "secret"),
        });
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));

        client.SendRequest(SessionHarness.Hello());
        Assert.IsType<SuccessResponse>(client.ReadResponse());

        client.SendRequest(SessionHarness.Logon(principal: "neo4j", credentials: "wrong"));
        var failure = Assert.IsType<FailureResponse>(client.ReadResponse());
        Assert.Equal(
            "Neo.ClientError.Security.Unauthorized",
            TransportTestSupport.GetStringEntry(failure.Metadata, "neo4j_code"));

        Assert.Equal("closed", client.WaitForClose(5_000));
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
    }

    [Fact]
    public async Task MissingVerifier_FailsClosed()
    {
        // Production default: no verifier configured, every LOGON is rejected.
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions());
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));

        client.SendRequest(SessionHarness.Hello());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(SessionHarness.Logon());
        Assert.IsType<FailureResponse>(client.ReadResponse());
    }

    [Fact]
    public async Task ThrowingVerifier_FailsClosed_AndLeaksNoCredentialData()
    {
        var observed = new List<string>();
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.Thrower(),
        });
        server.Diagnostics.Event += e => observed.Add(e.Detail ?? string.Empty);
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));

        client.SendRequest(SessionHarness.Hello());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(SessionHarness.Logon(principal: "sentinel-principal", credentials: "sentinel-credential"));
        Assert.IsType<FailureResponse>(client.ReadResponse());

        // No transport diagnostic output carries credential material.
        Assert.DoesNotContain("sentinel-principal", observed);
        Assert.DoesNotContain("sentinel-credential", observed);
    }

    [Fact]
    public async Task LogoffAndReauthentication_RestoresAuthorizedState()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));
        client.SendRequest(SessionHarness.Hello());
        TransportTestSupport.ExpectSuccess(client.ReadResponse(), "HELLO");
        client.SendRequest(SessionHarness.Logon());
        TransportTestSupport.ExpectSuccess(client.ReadResponse(), "LOGON");

        client.SendRequest(new LogoffRequest());
        TransportTestSupport.ExpectSuccess(client.ReadResponse(), "LOGOFF");

        // Accepted B3-B semantics: RUN outside READY after LOGOFF is a terminal protocol
        // violation (FAILURE + close); reauthentication requires a new connection.
        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<FailureResponse>(client.ReadResponse());
        Assert.Equal("closed", client.WaitForClose(5_000));
    }

    [Fact]
    public async Task UnsupportedQuery_IsRecoverableFailure_ResetRestoresReady()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRequest(new RunRequest(
            "RETURN $value AS answer", SessionHarness.Map(("value", new PackStreamInteger(7))), SessionHarness.Map()));
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(new PullRequest(1000, null));
        var ok = client.ReadResult();
        Assert.Equal(7, Assert.IsType<PackStreamInteger>(Assert.IsType<RecordResponse>(ok[0]).Data.Items[0]).Value);

        client.SendRequest(SessionHarness.Run("MATCH (n) RETURN n LIMIT 1"));
        Assert.IsType<FailureResponse>(client.ReadResponse());

        // FAILED state: further work is ignored, then RESET recovers.
        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<IgnoredResponse>(client.ReadResponse());
        client.SendRequest(new ResetRequest());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<SuccessResponse>(client.ReadResponse());
    }

    [Fact]
    public async Task WriteQuery_IsRefused()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRequest(SessionHarness.Run("CREATE (n:Person)"));
        var failure = Assert.IsType<FailureResponse>(client.ReadResponse());
        Assert.False(string.IsNullOrEmpty(TransportTestSupport.GetStringEntry(failure.Metadata, "neo4j_code")));

        // Recoverable: RESET then read queries still work.
        client.SendRequest(new ResetRequest());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<SuccessResponse>(client.ReadResponse());
    }

    [Fact]
    public async Task PullBeforeRun_IsFailure()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRequest(new PullRequest(1000, null));
        Assert.IsType<FailureResponse>(client.ReadResponse());
    }

    [Fact]
    public async Task Goodbye_ClosesWithoutResponse()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TransportTestSupport.ConnectAndAuthorize(server, "RETURN 42 AS answer");

        client.SendRequest(new GoodbyeRequest());
        Assert.Equal("closed", client.WaitForClose(5_000));
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
    }

    [Fact]
    public async Task ZeroFieldIgnored_FramingAroundItIsExact()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));
        client.SendRequest(SessionHarness.Hello());
        TransportTestSupport.ExpectSuccess(client.ReadResponse(), "HELLO");
        client.SendRequest(SessionHarness.Logon());
        TransportTestSupport.ExpectSuccess(client.ReadResponse(), "LOGON");

        // A recoverable failure puts the session in FAILED; the next request is answered with
        // the zero-field IGNORED structure (accepted B3-B/Bolt 5.8 semantics).
        client.SendRequest(SessionHarness.Run("MATCH (n) RETURN n LIMIT 1"));
        Assert.IsType<FailureResponse>(client.ReadResponse());

        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        var raw = client.ReadFramedMessage();
        Assert.NotNull(raw);
        Assert.Equal("B07E", Convert.ToHexString(raw));

        // Framing continues correctly afterwards (RESET still processed).
        client.SendRequest(new ResetRequest());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
    }
}
