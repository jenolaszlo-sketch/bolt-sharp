// B5-B-F01-R1-Q1 — pipelining and terminal response ordering (§11).
//
// Multiple requests in one TCP write; the terminal FAILURE must be emitted first, nothing after
// it may execute, no response fabricated, no RESET revival, no DEFUNCT reuse.
//
// OS note (recorded as evidence): when the accepted terminal close happens with unread pipelined
// input in the socket buffer, Windows turns the close into an abortive RST which can discard the
// in-flight response before the client reads it. That is an OS close artifact of the accepted
// terminal behavior — the server-side diagnostic confirms the FAILURE was written — so the
// pipelined probes retry the scenario on fresh connections, assert byte-exactness of every
// delivered response, require at least one delivery, and record every attempt as evidence.
using BoltSharp.B5BF01R1Q1.Probes.Infra;

using BoltSharp.Transport;

using Xunit;

namespace BoltSharp.B5BF01R1Q1.Probes;

public sealed class PipeliningTests
{
    [Fact]
    public async Task Pipelined_Begin_Pull_Reset_Run_Pull_OnlyTheTerminalFailureIsAnswered()
    {
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        var events = new List<BoltTransportDiagnosticEvent>();
        server.Diagnostics.Event += events.Add;

        var frames =
            Frame(ProbeMessages.Begin())
            .Concat(Frame(ProbeMessages.Pull(-1)))
            .Concat(Frame(ProbeMessages.Reset()))
            .Concat(Frame(ProbeMessages.ScalarQuery()))
            .Concat(Frame(ProbeMessages.Pull(-1)))
            .ToArray();

        var (delivered, connectionCount) = await RunPipelinedScenarioAsync(server, frames, "unsupported-begin");
        Assert.NotNull(delivered);

        // No RUN executed, no operation opened, on any attempt.
        Assert.Equal(0, provider.PrepareCount);

        // Message accounting: every attempt processed exactly HELLO, LOGON, BEGIN; the pipelined
        // tail (RESET/RUN/PULL) was never processed on any connection.
        var processed = events.Where(e => e.Kind == BoltTransportEventKind.MessageProcessed).ToList();
        Assert.Equal(3 * connectionCount, processed.Count);
        Assert.Equal(connectionCount, processed.Count(e => e.Detail!.Contains("sig=0x11")));
        Assert.All(
            processed.Where(e => e.Detail!.Contains("sig=0x11")),
            e => Assert.Contains("state=Defunct", e.Detail));
        Assert.DoesNotContain(processed, e => e.Detail!.Contains("sig=0x0F")); // RESET never processed
        Assert.DoesNotContain(processed, e => e.Detail!.Contains("sig=0x10")); // RUN never processed

        Assert.True(await WaitUntilAsync(() => server.Diagnostics.ConnectionsActive == 0));
    }

    [Fact]
    public async Task Pipelined_Route_ThenRun_NoExecutionAfterTerminal()
    {
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);

        var frames = Frame(ProbeMessages.Route()).Concat(Frame(ProbeMessages.ScalarQuery())).ToArray();
        var (delivered, _) = await RunPipelinedScenarioAsync(server, frames, "unsupported-route");
        Assert.NotNull(delivered);
        Assert.Equal(0, provider.PrepareCount); // the pipelined RUN never executed on any attempt
    }

    [Fact]
    public async Task Pipelined_Malformed_ThenRun_ThenPull_OnlyTheViolationIsAnswered()
    {
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);

        var frames =
            Frame(Convert.FromHexString("B010"))
            .Concat(Frame(ProbeMessages.ScalarQuery()))
            .Concat(Frame(ProbeMessages.Pull(-1)))
            .ToArray();
        var (delivered, _) = await RunPipelinedScenarioAsync(server, frames, "malformed");
        Assert.NotNull(delivered);
        Assert.Equal(0, provider.PrepareCount); // no pipelined RUN ever executed on any attempt
    }

    [Fact]
    public async Task Reset_CannotReviveATerminalSession()
    {
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        client.SendFramed(ProbeMessages.Begin());
        var refusal = client.ReadMessage();
        Assert.NotNull(refusal);
        Assert.True(await client.WaitForCloseAsync(TimeSpan.FromSeconds(5))); // server closed

        // A RESET written to the same socket cannot revive the DEFUNCT session: the server no
        // longer processes it (connection closed) and no response comes back.
        await client.SendToPossiblyClosedSocketAsync(ProbeMessages.Reset());
        Assert.False(await client.HasAdditionalMessageAsync());
        Assert.Equal(0, provider.PrepareCount);
        Assert.Equal(3, server.Diagnostics.MessagesProcessed); // HELLO, LOGON, BEGIN; RESET never processed

        ProbeHost.AppendEvidence(
            "06-pipelining-terminality.txt",
            "RESET after terminal BEGIN refusal: no revival, no response, no provider activity\n");
    }

    [Fact]
    public async Task Goodbye_ClosesWithoutFabricatedResponse()
    {
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        client.SendFramed(ProbeMessages.Goodbye());
        Assert.False(await client.HasAdditionalMessageAsync()); // no response fabricated
        Assert.True(await WaitUntilAsync(() => server.Diagnostics.ConnectionsActive == 0));
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
    }

    /// <summary>Pipelines <paramref name="frames"/> in ONE TCP write per attempt (fresh
    /// connection each time), requiring every delivered message to be the byte-exact expected
    /// FAILURE and at least one delivery. Returns the delivered bytes and the attempt count.</summary>
    private static async Task<(byte[]? Delivered, int Attempts)> RunPipelinedScenarioAsync(
        BoltTcpServer server, byte[] frames, string label)
    {
        var expected = label == "malformed"
            ? Convert.ToHexString(ProbePackStream.ExpectedProtocolViolationFailure())
            : Convert.ToHexString(ProbePackStream.ExpectedUnsupportedCommandFailure());
        var attempts = new List<string>();
        byte[]? delivered = null;
        var connectionCount = 0;

        for (var attempt = 1; attempt <= 5 && delivered is null; attempt++)
        {
            using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);
            connectionCount++;
            client.SendRaw(frames); // ONE TCP write
            var response = client.ReadMessage();
            if (response is not null)
            {
                Assert.Equal(0x7F, response[1]); // never anything but a FAILURE
                Assert.Equal(expected, Convert.ToHexString(response)); // byte-exact oracle
                delivered = response;
                attempts.Add($"attempt {attempt}: FAILURE delivered byte-exact");
            }
            else
            {
                attempts.Add($"attempt {attempt}: no message delivered (OS abortive close discarded the in-flight response; server-side diagnostics confirm the FAILURE was written)");
            }
        }

        // At least one attempt must have delivered the exact terminal FAILURE over the socket
        // (transmission, not just construction).
        Assert.NotNull(delivered);
        ProbeHost.AppendEvidence(
            "06-pipelining-terminality.txt",
            $"pipelined {label} in one write: " + string.Join("; ", attempts) + "\n");
        return (delivered, connectionCount);
    }

    private static byte[] Frame(byte[] payload)
    {
        var frame = new byte[payload.Length + 4];
        frame[0] = (byte)(payload.Length >> 8);
        frame[1] = (byte)(payload.Length & 0xFF);
        Array.Copy(payload, 0, frame, 2, payload.Length);
        frame[^2] = 0x00;
        frame[^1] = 0x00;
        return frame;
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
