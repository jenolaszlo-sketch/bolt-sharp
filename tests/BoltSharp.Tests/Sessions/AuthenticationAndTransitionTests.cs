using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Tests.Messages;

using Xunit;

namespace BoltSharp.Tests.Sessions;

/// <summary>
/// B3-B1: connection lifecycle, authentication gating and legal/illegal state transitions.
/// </summary>
public sealed class AuthenticationAndTransitionTests
{
    private static string DescriptionOf(BoltSessionResult result)
    {
        var failure = Assert.IsType<FailureResponse>(Assert.Single(result.Responses));
        return Assert.IsType<PackStreamString>(
            BoltMessageFieldsTestAccess.GetValue(failure.Metadata, "description"))!.Value;
    }

    [Fact]
    public void NewSession_StartsInNegotiation()
    {
        var session = SessionHarness.Session();

        Assert.Equal(BoltConnectionState.Negotiation, session.State);
        Assert.False(session.IsDefunct);
        Assert.False(session.HasActiveOperation);
    }

    [Fact]
    public void Hello_InNegotiation_SucceedsWithRequiredMetadata_AndEntersAuthentication()
    {
        var session = SessionHarness.Session();

        var result = session.Receive(SessionHarness.Hello());

        var success = SessionHarness.SingleResponse<SuccessResponse>(result);
        Assert.Equal(BoltConnectionState.Authentication, result.State);
        Assert.False(result.ShouldClose);
        Assert.Equal("BoltSharp/0.1.0", success.Server);
        Assert.NotNull(success.ConnectionId);
        Assert.NotNull(success.Hints);
    }

    [Fact]
    public void Hello_Twice_IsProtocolViolation_AndDefunct()
    {
        var session = SessionHarness.Session();
        session.Receive(SessionHarness.Hello());

        var result = session.Receive(SessionHarness.Hello());

        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
        Assert.Contains("current connection state", DescriptionOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Logon_InNegotiation_IsProtocolViolation_AndDefunct()
    {
        var session = SessionHarness.Session();

        var result = session.Receive(SessionHarness.Logon());

        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
    }

    [Fact]
    public void Logon_WithConfiguredVerifier_Succeeds_AndEntersReady()
    {
        var session = SessionHarness.Session(options: new BoltSessionOptions
        {
            Verifier = TestCredentialVerifier.Accept("neo4j", "secret"),
        });
        session.Receive(SessionHarness.Hello());

        var result = session.Receive(SessionHarness.Logon("basic", "neo4j", "secret"));

        SessionHarness.SingleResponse<SuccessResponse>(result);
        Assert.Equal(BoltConnectionState.Ready, result.State);
        Assert.False(result.ShouldClose);
    }

    [Fact]
    public void Logon_WithoutVerifier_FailsClosed_AndDefunct()
    {
        var session = SessionHarness.Session(options: new BoltSessionOptions { Verifier = null });
        session.Receive(SessionHarness.Hello());

        var result = session.Receive(SessionHarness.Logon());

        var failure = SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
        Assert.Equal("Neo.ClientError.Security.Unauthorized", failure.Neo4jCode);
        Assert.Equal("50N42", failure.GqlStatus);
    }

    [Fact]
    public void Logon_WithWrongCredentials_FailsWithAuthenticationFailure()
    {
        var session = SessionHarness.Session(options: new BoltSessionOptions
        {
            Verifier = TestCredentialVerifier.Accept("neo4j", "correct"),
        });
        session.Receive(SessionHarness.Hello());

        var result = session.Receive(SessionHarness.Logon("basic", "neo4j", "wrong"));

        var failure = SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.Equal("Neo.ClientError.Security.Unauthorized", failure.Neo4jCode);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
    }

    [Fact]
    public void Logon_WithThrowingVerifier_FailsClosed_WithoutLeakingDetails()
    {
        const string sentinel = "SENTINEL-CRED-B3B";
        var session = SessionHarness.Session(options: new BoltSessionOptions
        {
            Verifier = TestCredentialVerifier.Thrower(),
        });
        session.Receive(SessionHarness.Hello());

        var result = session.Receive(SessionHarness.Logon("basic", sentinel, sentinel));

        var failure = SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        var text = failure.ToString() + " " + DescriptionOf(result);
        Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
        Assert.DoesNotContain("verifier exploded", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Logon_NoneScheme_IsNotAnAuthenticationBypass()
    {
        // A 'none' scheme LOGON still requires the verifier to accept it; the default is deny.
        var session = SessionHarness.Session(options: new BoltSessionOptions { Verifier = null });
        session.Receive(SessionHarness.Hello());

        var result = session.Receive(SessionHarness.Logon("none"));

        Assert.IsType<FailureResponse>(Assert.Single(result.Responses));
        Assert.Equal(BoltConnectionState.Defunct, result.State);
    }

    [Fact]
    public void Logoff_InReady_Succeeds_AndEntersAuthentication()
    {
        var session = SessionHarness.Authenticated();

        var result = session.Receive(SessionHarness.Logoff());

        SessionHarness.SingleResponse<SuccessResponse>(result);
        Assert.Equal(BoltConnectionState.Authentication, result.State);
        Assert.False(result.ShouldClose);
    }

    [Fact]
    public void Logoff_RequiresAnotherLogonBeforeRun()
    {
        var session = SessionHarness.Authenticated();
        session.Receive(SessionHarness.Logoff());

        var result = session.Receive(SessionHarness.Run());

        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
    }

    [Fact]
    public void Logoff_InAuthentication_IsProtocolViolation_AndDefunct()
    {
        var session = SessionHarness.Session();
        session.Receive(SessionHarness.Hello());

        var result = session.Receive(SessionHarness.Logoff());

        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
    }

    [Fact]
    public void Logoff_InFailed_IsIgnored()
    {
        var session = SessionHarness.Authenticated(new ScriptedOperationProvider(_ => null));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Logoff());

        SessionHarness.SingleResponse<IgnoredResponse>(result);
        Assert.Equal(BoltConnectionState.Failed, result.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Telemetry_ValidIdentifier_Succeeds_AndStaysReady(long api)
    {
        var session = SessionHarness.Authenticated();

        var result = session.Receive(SessionHarness.Telemetry(api));

        SessionHarness.SingleResponse<SuccessResponse>(result);
        Assert.Equal(BoltConnectionState.Ready, result.State);
        Assert.False(result.ShouldClose);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(9001)]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public void Telemetry_InvalidIdentifier_IsFailure_AndFailed(long api)
    {
        var session = SessionHarness.Authenticated();

        var result = session.Receive(SessionHarness.Telemetry(api));

        // B3-B-R1 F-02: an out-of-domain integer identifier is an invalid *value* — the Bolt 5.8
        // specification's TELEMETRY 9001 example answers 50N00 / General.UnknownError (22G03 is for
        // an invalid value type, which B3-A rejects as malformed).
        var failure = SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.Equal(BoltConnectionState.Failed, result.State);
        Assert.Equal("50N00", failure.GqlStatus);
        Assert.Equal("Neo.DatabaseError.General.UnknownError", failure.Neo4jCode);
        Assert.False(result.ShouldClose);
    }

    [Fact]
    public void Telemetry_NotReady_IsProtocolViolation()
    {
        var session = SessionHarness.Session();

        var result = session.Receive(SessionHarness.Telemetry(1));

        Assert.Equal(BoltConnectionState.Defunct, result.State);
    }

    [Fact]
    public void Run_InNegotiation_IsProtocolViolation()
    {
        var session = SessionHarness.Session();

        var result = session.Receive(SessionHarness.Run());

        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
    }

    [Fact]
    public void Run_InAuthentication_IsProtocolViolation()
    {
        var session = SessionHarness.Session();
        session.Receive(SessionHarness.Hello());

        var result = session.Receive(SessionHarness.Run());

        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
    }

    [Fact]
    public void Pull_And_Discard_InReady_AreProtocolViolations()
    {
        var pullSession = SessionHarness.Authenticated();
        Assert.Equal(BoltConnectionState.Defunct, pullSession.Receive(SessionHarness.Pull(1)).State);

        var discardSession = SessionHarness.Authenticated();
        Assert.Equal(BoltConnectionState.Defunct, discardSession.Receive(SessionHarness.Discard(1)).State);
    }

    [Fact]
    public void Run_WhileStreaming_IsProtocolViolation()
    {
        var session = SessionHarness.Streaming(1, 2, 3);

        var result = session.Receive(SessionHarness.Run());

        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.False(session.HasActiveOperation);
    }

    [Fact]
    public void Reset_BeforeReady_IsFailure_AndDefunct()
    {
        var negotiation = SessionHarness.Session();
        var fromNegotiation = negotiation.Receive(SessionHarness.Reset());
        Assert.Equal(BoltConnectionState.Defunct, fromNegotiation.State);

        var authentication = SessionHarness.Session();
        authentication.Receive(SessionHarness.Hello());
        var fromAuthentication = authentication.Receive(SessionHarness.Reset());
        Assert.Equal(BoltConnectionState.Defunct, fromAuthentication.State);
    }

    [Fact]
    public void Goodbye_EmitsNoResponse_AndDefunct()
    {
        var session = SessionHarness.Authenticated();

        var result = session.Receive(SessionHarness.Goodbye());

        Assert.Empty(result.Responses);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
    }

    [Fact]
    public void Goodbye_InNegotiation_IsAcceptedWithoutResponse()
    {
        var session = SessionHarness.Session();

        var result = session.Receive(SessionHarness.Goodbye());

        Assert.Empty(result.Responses);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
    }

    [Fact]
    public void NoProcessingAfterDefunct()
    {
        var provider = ScriptedOperationProvider.WithRows(1);
        var session = SessionHarness.Authenticated(provider);
        session.Receive(SessionHarness.Goodbye());

        var result = session.Receive(SessionHarness.Run());

        Assert.Empty(result.Responses);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.Equal(0, provider.PrepareCount);
    }
}
