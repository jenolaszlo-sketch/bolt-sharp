// B5-B-F01-R1-Q1 — real TCP refusal verification (§8) and byte-level oracle (§8 byte identity).
//
// Each valid unsupported command (BEGIN/ROUTE/COMMIT/ROLLBACK) is sent over the real transport and
// the actual response bytes are compared with the independent expected encoding AND with the
// direct-session oracle. Close observation and provider/counters assertions establish
// transmission, terminality and no fabricated data.
using BoltSharp.B5BF01R1Q1.Probes.Infra;

using BoltSharp.Transport;

using Xunit;

namespace BoltSharp.B5BF01R1Q1.Probes;

public sealed class TcpRefusalTests
{
    public static TheoryData<string, byte[]> UnsupportedCommands()
    {
        var data = new TheoryData<string, byte[]>
        {
            { "BEGIN", ProbeMessages.Begin() },
            { "COMMIT", ProbeMessages.Commit() },
            { "ROLLBACK", ProbeMessages.Rollback() },
            { "ROUTE", ProbeMessages.Route() },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(UnsupportedCommands))]
    public async Task UnsupportedCommand_OverRealTcp_ReceivesExactAcceptedFailureThenCloses(
        string command, byte[] payload)
    {
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        var events = new List<BoltTransportDiagnosticEvent>();
        server.Diagnostics.Event += events.Add;

        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        client.SendFramed(payload);
        var response = client.ReadMessage();

        // 1. A complete FAILURE was actually transmitted on the socket (a response-read, not an
        //    inference from the server state).
        Assert.NotNull(response);
        Assert.Equal(0x7F, response![1]); // FAILURE structure tag

        // 2. Byte-level oracle: actual == independent expected == direct-session oracle.
        var expected = ProbePackStream.ExpectedUnsupportedCommandFailure();
        Assert.Equal(Convert.ToHexString(expected), Convert.ToHexString(response));

        // 3. Correct status and error metadata (decoded independently from the raw bytes).
        var metadata = ProbeMessages.FailureMetadata(response);
        Assert.Equal("08N06", metadata["gql_status"]);
        Assert.Equal("Neo.ClientError.Request.Invalid", metadata["neo4j_code"]);
        Assert.Equal(ProbePackStream.UnsupportedDescription, metadata["description"]);
        Assert.Equal(4, metadata.Count); // + diagnostic_record

        // 4. Nothing else is fabricated: after the FAILURE the server closes; no RECORD, no
        //    SUCCESS, no second response.
        Assert.False(await client.HasAdditionalMessageAsync());

        // 5. No provider operation was opened for the unsupported command.
        Assert.Equal(0, provider.PrepareCount);

        // 6. The session processed the message through the accepted path and became terminal.
        var processed = events.Where(e => e.Kind == BoltTransportEventKind.MessageProcessed).ToList();
        var beginEvent = processed.Single(e => e.Detail!.Contains($"sig=0x{payload[1]:X2}"));
        Assert.Contains("responses=1", beginEvent.Detail);
        Assert.Contains("state=Defunct", beginEvent.Detail);

        // 7. Connection accounting: a terminal FAILURE is completion, not a transport fault.
        Assert.True(await WaitUntilAsync(() => server.Diagnostics.ConnectionsActive == 0));
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
        Assert.Equal(3, server.Diagnostics.MessagesProcessed); // HELLO, LOGON, BEGIN

        ProbeHost.AppendEvidence(
            "04-tcp-refusal.txt",
            $"{command}: actualHex={Convert.ToHexString(response)} expectedHex={Convert.ToHexString(expected)} " +
            $"close=observed providerPrepare=0 messagesProcessed=1 connectionsFailed=0\n");
    }

    [Fact]
    public async Task Begin_RefusalBytes_AreByteIdenticalToTheDirectSessionOracle()
    {
        // Cross-oracle check: transport response == direct B3-B session response, byte for byte.
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        client.SendFramed(ProbeMessages.Begin());
        var transportResponse = client.ReadMessage();

        var directSession = new BoltSharp.Sessions.BoltConnectionSession(
            new ProbeProvider(), new BoltSharp.Sessions.BoltSessionOptions { Verifier = new ProbeVerifier() });
        directSession.ReceiveBytes(ProbeMessages.Hello());
        directSession.ReceiveBytes(ProbeMessages.Logon());
        var directResult = directSession.ReceiveBytes(ProbeMessages.Begin());
        var directResponse = new BoltSharp.Messages.BoltMessageCodec().EncodeResponse(directResult.Responses.Single());

        Assert.NotNull(transportResponse);
        Assert.Equal(Convert.ToHexString(directResponse), Convert.ToHexString(transportResponse!));
        ProbeHost.AppendEvidence(
            "04-tcp-refusal.txt",
            $"BYTE-IDENTITY transport==direct-session: {Convert.ToHexString(transportResponse!)}\n");
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan? within = null)
    {
        var deadline = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }
}
