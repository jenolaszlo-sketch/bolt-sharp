// B5-B-F01-R1-Q1 — direct accepted B3-B session refusal oracle (§7).
//
// Calls BoltConnectionSession.ReceiveBytes directly with reviewer-built payloads and compares the
// response bytes against the INDEPENDENT expected encodings (ProbePackStream). Records the oracle
// bytes used by the TCP tests.
using BoltSharp.B5BF01R1Q1.Probes.Infra;

using BoltSharp.Backend;
using BoltSharp.Sessions;

using Xunit;

namespace BoltSharp.B5BF01R1Q1.Probes;

public sealed class DirectSessionOracleTests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s);

    [Fact]
    public async Task Begin_InReadyState_ReceivesAcceptedTerminalUnsupportedFailure()
    {
        var provider = new ProbeProvider();
        using var session = CreateAuthorizedSession(provider);
        var result = session.ReceiveBytes(ProbeMessages.Begin());
        AssertFailureShape(result, ProbePackStream.ExpectedUnsupportedCommandFailure(), provider);
        WriteOracle("oracle-begin", result, ProbePackStream.ExpectedUnsupportedCommandFailure());
    }

    [Fact]
    public async Task Commit_InReadyState_ReceivesAcceptedTerminalUnsupportedFailure()
    {
        var provider = new ProbeProvider();
        using var session = CreateAuthorizedSession(provider);
        var result = session.ReceiveBytes(ProbeMessages.Commit());
        AssertFailureShape(result, ProbePackStream.ExpectedUnsupportedCommandFailure(), provider);
        WriteOracle("oracle-commit", result, ProbePackStream.ExpectedUnsupportedCommandFailure());
    }

    [Fact]
    public async Task Rollback_InReadyState_ReceivesAcceptedTerminalUnsupportedFailure()
    {
        var provider = new ProbeProvider();
        using var session = CreateAuthorizedSession(provider);
        var result = session.ReceiveBytes(ProbeMessages.Rollback());
        AssertFailureShape(result, ProbePackStream.ExpectedUnsupportedCommandFailure(), provider);
        WriteOracle("oracle-rollback", result, ProbePackStream.ExpectedUnsupportedCommandFailure());
    }

    [Fact]
    public async Task Route_InReadyState_ReceivesAcceptedTerminalUnsupportedFailure()
    {
        var provider = new ProbeProvider();
        using var session = CreateAuthorizedSession(provider);
        var result = session.ReceiveBytes(ProbeMessages.Route());
        AssertFailureShape(result, ProbePackStream.ExpectedUnsupportedCommandFailure(), provider);
        WriteOracle("oracle-route", result, ProbePackStream.ExpectedUnsupportedCommandFailure());
    }

    [Fact]
    public async Task Begin_InNegotiationState_IsStillRefusedByTheUnsupportedPath()
    {
        // State-dependence probe: BEGIN before HELLO/LOGON. The session must still classify the
        // valid payload as unsupported (terminal), never as an executable transaction.
        var provider = new ProbeProvider();
        var session = new BoltConnectionSession(provider, new BoltSessionOptions { Verifier = new ProbeVerifier() });
        var result = session.ReceiveBytes(ProbeMessages.Begin());
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
        Assert.Equal(0, provider.PrepareCount);
        Assert.Equal(
            Convert.ToHexString(ProbePackStream.ExpectedUnsupportedCommandFailure()),
            Convert.ToHexString(new BoltMessageCodecAdapter().Encode(result.Responses.Single())));
    }

    [Fact]
    public async Task Begin_DuringActiveOperation_DisposesTheOperationAndBecomesTerminal()
    {
        // State-dependence probe: BEGIN while an auto-commit result is active. The refusal must
        // retire the owned operation exactly once (fail-closed), never execute a transaction.
        var provider = new ProbeProvider();
        var session = new BoltConnectionSession(provider, new BoltSessionOptions { Verifier = new ProbeVerifier() });
        session.ReceiveBytes(ProbeMessages.Hello());
        session.ReceiveBytes(ProbeMessages.Logon());
        var run = session.ReceiveBytes(ProbeMessages.ScalarQuery());
        Assert.Equal(BoltConnectionState.Streaming, run.State);
        Assert.Equal(1, provider.PrepareCount);

        var begin = session.ReceiveBytes(ProbeMessages.Begin());
        Assert.Equal(BoltConnectionState.Defunct, begin.State);
        Assert.True(begin.ShouldClose);
        Assert.Equal(1, provider.DisposeAttempts); // the active operation was retired exactly once
        Assert.Equal(1, provider.PrepareCount); // BEGIN never opened a second operation
        Assert.Equal(
            Convert.ToHexString(ProbePackStream.ExpectedUnsupportedCommandFailure()),
            Convert.ToHexString(new BoltMessageCodecAdapter().Encode(begin.Responses.Single())));
    }

    [Fact]
    public async Task MalformedRunArity_DirectSession_ReceivesTerminalProtocolViolation()
    {
        var provider = new ProbeProvider();
        using var session = CreateAuthorizedSession(provider);
        var result = session.ReceiveBytes(Hex("B010")); // RUN with zero fields
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
        Assert.Equal(0, provider.PrepareCount);
        Assert.Equal(
            Convert.ToHexString(ProbePackStream.ExpectedProtocolViolationFailure()),
            Convert.ToHexString(new BoltMessageCodecAdapter().Encode(result.Responses.Single())));
        WriteOracle("oracle-malformed", result, ProbePackStream.ExpectedProtocolViolationFailure());
    }

    [Fact]
    public async Task UnknownSignature_DirectSession_ReceivesTerminalProtocolViolation()
    {
        var provider = new ProbeProvider();
        using var session = CreateAuthorizedSession(provider);
        var result = session.ReceiveBytes(Hex("B17BA0")); // valid structure, undefined tag 0x7B
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
        Assert.Equal(
            Convert.ToHexString(ProbePackStream.ExpectedProtocolViolationFailure()),
            Convert.ToHexString(new BoltMessageCodecAdapter().Encode(result.Responses.Single())));
    }

    [Fact]
    public async Task AfterTerminalOutcome_DefunctSessionAnswersNothing()
    {
        var provider = new ProbeProvider();
        using var session = CreateAuthorizedSession(provider);
        var first = session.ReceiveBytes(ProbeMessages.Begin());
        Assert.Equal(BoltConnectionState.Defunct, first.State);

        var second = session.ReceiveBytes(ProbeMessages.Run("RETURN 42 AS answer"));
        Assert.Empty(second.Responses); // no response fabricated for a dead session
        Assert.Equal(BoltConnectionState.Defunct, second.State);
        Assert.True(second.ShouldClose);
        Assert.Equal(0, provider.PrepareCount); // no operation opened after terminality
    }

    private static BoltConnectionSession CreateAuthorizedSession(ProbeProvider provider)
    {
        var session = new BoltConnectionSession(provider, new BoltSessionOptions { Verifier = new ProbeVerifier() });
        session.ReceiveBytes(ProbeMessages.Hello());
        session.ReceiveBytes(ProbeMessages.Logon());
        return session;
    }

    private static void AssertFailureShape(BoltSessionResult result, byte[] expected, ProbeProvider provider)
    {
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
        Assert.Single(result.Responses);
        Assert.Equal(0, provider.PrepareCount);
        var response = result.Responses.Single();
        Assert.Equal(0x7F, new BoltMessageCodecAdapter().SignatureOf(response));
        Assert.Equal(
            Convert.ToHexString(expected),
            Convert.ToHexString(new BoltMessageCodecAdapter().Encode(response)));
    }

    private static void WriteOracle(string name, BoltSessionResult result, byte[] expected)
    {
        ProbeHost.AppendEvidence(
            "03-direct-session-oracle.txt",
            $"{name}: state={result.State} shouldClose={result.ShouldClose} " +
            $"expectedHex={Convert.ToHexString(expected)}\n");
    }

    /// <summary>Adapter so the probe can encode a session response without relying on the
    /// production encoder for expectations: it is used ONLY to serialize the session's own
    /// response for comparison against the independent expected bytes.</summary>
    private sealed class BoltMessageCodecAdapter
    {
        public byte[] Encode(BoltSharp.Messages.BoltResponse response) =>
            new BoltSharp.Messages.BoltMessageCodec().EncodeResponse(response);

        public int SignatureOf(BoltSharp.Messages.BoltResponse response) =>
            new BoltSharp.Messages.BoltMessageCodec().EncodeResponse(response)[1];
    }
}
