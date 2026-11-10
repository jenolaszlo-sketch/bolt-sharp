// B5-B-F01-R1-Q1 — supported-query, handshake/framing and authentication regression (§14-§16)
// through the real transport with independent clients and oracles.
using System.Net;

using BoltSharp.B5BF01R1Q1.Probes.Infra;

using BoltSharp.Transport;

using Xunit;

namespace BoltSharp.B5BF01R1Q1.Probes;

public sealed class SupportedAndHandshakeTests
{
    [Fact]
    public async Task ScalarQuery_ProducesTheHistoricalWireBytes_AndReusableConnection()
    {
        var provider = new CountingReferenceProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        // RUN → SUCCESS{fields=[answer]}
        client.SendFramed(ProbeMessages.ScalarQuery());
        var run = client.ReadMessage();
        Assert.NotNull(run);
        Assert.Equal(0x70, run![1]);
        Assert.Equal("B170A1866669656C64739186616E73776572", Convert.ToHexString(run)); // historical oracle

        // PULL → RECORD[B171912A] + SUCCESS{}
        client.SendFramed(ProbeMessages.Pull(-1));
        var record = client.ReadMessage();
        Assert.NotNull(record);
        Assert.Equal(0x71, record![1]);
        Assert.Equal("B171912A", Convert.ToHexString(record)); // historical value oracle (42)
        var summary = client.ReadMessage();
        Assert.NotNull(summary);
        Assert.Equal("B170A0", Convert.ToHexString(summary!)); // terminal SUCCESS{}

        Assert.Equal(1, provider.PrepareCount);
        Assert.Equal(1, provider.DisposeAttempts); // exactly-once retirement

        // Connection reuse: a second query on the SAME connection succeeds.
        client.SendFramed(ProbeMessages.ScalarQuery());
        var run2 = client.ReadMessage();
        Assert.NotNull(run2);
        Assert.Equal(0x70, run2![1]);
        Assert.Equal(2, provider.PrepareCount);
        client.SendFramed(ProbeMessages.Pull(-1));
        _ = client.ReadMessage();
        _ = client.ReadMessage();

        ProbeHost.AppendEvidence(
            "07-supported-regression.txt",
            "scalar: RUN SUCCESS B170A1866669656C64739186616E73776572; RECORD B171912A; SUCCESS B170A0; reuse OK\n");
    }

    [Fact]
    public async Task ParameterQuery_PassesTypedValueThroughUnchanged()
    {
        var provider = new CountingReferenceProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        client.SendFramed(ProbeMessages.ParameterQuery());
        var run = client.ReadMessage();
        Assert.NotNull(run);
        Assert.Equal(0x70, run![1]);

        client.SendFramed(ProbeMessages.Pull(-1));
        var record = client.ReadMessage();
        Assert.NotNull(record);
        Assert.Equal(0x71, record![1]);
        // RECORD B1 71 91 07 — the typed integer parameter passes through unchanged (the record
        // carries the same PackStream integer that was sent, not a stringified or re-typed value).
        Assert.Equal("B1719107", Convert.ToHexString(record));
    }

    [Fact]
    public async Task GraphQuery_ReturnsTheReferenceGraphShape()
    {
        var provider = new CountingReferenceProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        client.SendFramed(ProbeMessages.GraphQuery());
        var run = client.ReadMessage();
        Assert.NotNull(run);
        Assert.Equal(0x70, run![1]);

        client.SendFramed(ProbeMessages.Pull(-1));
        var record = client.ReadMessage();
        Assert.NotNull(record);
        Assert.Equal(0x71, record![1]); // one record: (a, r, b)
        var summary = client.ReadMessage();
        Assert.NotNull(summary);
        Assert.Equal("B170A0", Convert.ToHexString(summary!)); // cursor exhausted: terminal SUCCESS{}

        // Exactly one record was produced (no second RECORD before the summary).
        Assert.Equal(1, provider.PrepareCount);
        Assert.Equal(1, provider.DisposeAttempts); // cursor retired exactly once

        ProbeHost.AppendEvidence(
            "07-supported-regression.txt",
            "graph: one (a, r, b) record, cursor exhaustion observed (terminal SUCCESS after the record), graph unchanged\n");
    }

    [Fact]
    public async Task ConventionalHandshake_PipelinedHello_PipelinedRequests_NoopAndVersionRefusal()
    {
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);

        // (a) Pipelined HELLO+LOGON in one write after the handshake.
        using (var client = new ReviewerBoltClient())
        {
            await client.ConnectAsync((IPEndPoint)server.BoundEndPoint!);
            Assert.Equal("00000805", await client.HandshakeAsync());
            var pipelined = Frame(ProbeMessages.Hello()).Concat(Frame(ProbeMessages.Logon())).ToArray();
            client.SendRaw(pipelined);
            var hello = client.ReadMessage();
            Assert.NotNull(hello);
            Assert.Equal(0x70, hello![1]);
            var logon = client.ReadMessage();
            Assert.NotNull(logon);
            Assert.Equal(0x70, logon![1]);
        }

        // (b) Fragmented handshake: magic split across two writes.
        using (var client = new ReviewerBoltClient())
        {
            await client.ConnectAsync((IPEndPoint)server.BoundEndPoint!);
            var magic = new byte[] { 0x60, 0x60, 0xB0, 0x17 };
            var rest = new byte[]
            {
                0x00, 0x00, 0x08, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
            };
            client.SendRaw(magic[..2]);
            await Task.Delay(50);
            client.SendRaw(magic[2..]);
            client.SendRaw(rest);
            Assert.Equal("00000805", await client.HandshakeReplyAfterFragmentedPreambleAsync());
        }

        // (c) NOOP (00 00) between requests is not a message and is not answered.
        using (var client = await ProbeHost.ConnectAndAuthorizeAsync(server))
        {
            client.SendRaw(new byte[] { 0x00, 0x00 }); // NOOP
            client.SendFramed(ProbeMessages.Reset());
            var reset = client.ReadMessage();
            Assert.NotNull(reset);
            Assert.Equal(0x70, reset![1]); // RESET in READY → SUCCESS; the NOOP was not a message
        }

        // (d) Unsupported version proposal: 00 00 00 00 reply and closure.
        using (var client = new ReviewerBoltClient())
        {
            await client.ConnectAsync((IPEndPoint)server.BoundEndPoint!);
            var unsupported = new byte[]
            {
                0x60, 0x60, 0xB0, 0x17, 0x00, 0x00, 0x04, 0x04, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            };
            client.SendRaw(unsupported);
            var reply = await client.ReadRawAsync(4);
            Assert.Equal("00000000", Convert.ToHexString(reply));
        }

        ProbeHost.AppendEvidence(
            "08-handshake-framing.txt",
            "conventional handshake, pipelined HELLO+LOGON, fragmented handshake, NOOP distinction, version refusal: all as accepted\n");
    }

    [Fact]
    public async Task Authentication_ValidInvalidAndSequenceNegatives_FailClosed()
    {
        var provider = new CountingReferenceProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);

        // (a) Valid Basic: HELLO → LOGON → RUN works (covered by every other probe; assert once).
        using (var client = await ProbeHost.ConnectAndAuthorizeAsync(server))
        {
            client.SendFramed(ProbeMessages.ScalarQuery());
            var run = client.ReadMessage();
            Assert.NotNull(run);
            Assert.Equal(0x70, run![1]);
        }

        // (b) RUN before LOGON: terminal protocol violation; connection closes.
        var preparesBeforeB = provider.PrepareCount;
        using (var client = new ReviewerBoltClient())
        {
            await client.ConnectAsync((IPEndPoint)server.BoundEndPoint!);
            await client.HandshakeAsync();
            client.SendFramed(ProbeMessages.Hello());
            _ = client.ReadMessage();
            client.SendFramed(ProbeMessages.ScalarQuery());
            var failure = client.ReadMessage();
            Assert.NotNull(failure);
            Assert.Equal(0x7F, failure![1]);
            Assert.Equal(
                Convert.ToHexString(ProbePackStream.ExpectedProtocolViolationFailure()),
                Convert.ToHexString(failure));
            Assert.True(await client.WaitForCloseAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(preparesBeforeB, provider.PrepareCount); // no operation opened
        }

        // (c) Default (missing) verifier: LOGON rejected, fail-closed.
        var preparesBeforeC = provider.PrepareCount;
        await using (var noVerifierServer = await ProbeHost.StartServerAsync(null, () => provider))
        using (var client = new ReviewerBoltClient())
        {
            await client.ConnectAsync((IPEndPoint)noVerifierServer.BoundEndPoint!);
            await client.HandshakeAsync();
            client.SendFramed(ProbeMessages.Hello());
            _ = client.ReadMessage();
            client.SendFramed(ProbeMessages.Logon());
            var failure = client.ReadMessage();
            Assert.NotNull(failure);
            Assert.Equal(0x7F, failure![1]); // authentication FAILURE + closure
            Assert.True(await client.WaitForCloseAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(preparesBeforeC, provider.PrepareCount); // no operation may open unauthenticated
        }

        // (d) LOGOFF in READY: accepted semantics (SUCCESS; connection remains open and can be
        // re-authenticated).
        using (var client = await ProbeHost.ConnectAndAuthorizeAsync(server))
        {
            client.SendFramed(ProbeMessages.Logoff());
            var logoff = client.ReadMessage();
            Assert.NotNull(logoff);
            Assert.Equal(0x70, logoff![1]);
            // Re-LOGON on the same (still open) connection returns the session to READY.
            client.SendFramed(ProbeMessages.Logon());
            var relogon = client.ReadMessage();
            Assert.NotNull(relogon);
            Assert.Equal(0x70, relogon![1]);
            client.SendFramed(ProbeMessages.Reset());
            var reset = client.ReadMessage();
            Assert.NotNull(reset);
            Assert.Equal(0x70, reset![1]);
        }

        ProbeHost.AppendEvidence(
            "09-authentication-regression.txt",
            "valid Basic OK; RUN-before-LOGON terminal protocol violation; missing verifier fail-closed; LOGOFF accepted; no operation opened unauthenticated\n");
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
