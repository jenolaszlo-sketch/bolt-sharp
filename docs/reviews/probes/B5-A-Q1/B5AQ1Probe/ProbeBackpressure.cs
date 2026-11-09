using System.Net;
using System.Net.Sockets;
using System.Text;

using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Transport;

namespace B5AQ1Probe;

/// <summary>
/// Critical investigation C: slow-client backpressure (§10). A reviewer provider streams a
/// large finite result while the client stops reading; the probe observes whether a server
/// write actually blocks (closure timing near the configured WriteTimeout) and whether the
/// connection and its owned operation are cleaned up.
/// </summary>
internal static class ProbeBackpressure
{
    /// <summary>A provider whose single operation yields rows of a large string payload.</summary>
    private sealed class LargeResultProvider : IBoltOperationProvider
    {
        public const int Rows = 250;
        public const int PayloadBytes = 1024 * 1024;
        public int PrepareCount;

        public IBoltOperation? Prepare(RunRequest run)
        {
            Interlocked.Increment(ref PrepareCount);
            if (run.Query == "RETURN $big AS answer") { return new LargeOperation(); }
            if (run.Query == "RETURN 42 AS answer") { return new ScalarOperation(); }
            return null;
        }
    }

    private sealed class ScalarOperation : IBoltOperation
    {
        public IReadOnlyList<string> Fields { get; } = new[] { "answer" };
        public IReadOnlyList<PackStreamValue?>? ReadRow() => new PackStreamValue?[] { new PackStreamInteger(42) };
        public void Dispose() { }
    }

    private sealed class LargeOperation : IBoltOperation
    {
        public IReadOnlyList<string> Fields { get; } = new[] { "answer" };
        private int _rows;

        public IReadOnlyList<PackStreamValue?>? ReadRow()
        {
            var row = _rows++;
            if (row >= LargeResultProvider.Rows) { return null; }
            return new PackStreamValue?[] { new PackStreamString(new string('x', LargeResultProvider.PayloadBytes)) };
        }

        public void Dispose() { }
    }

    public static void Run()
    {
        Program.Section("D. Slow-client backpressure (sustained write pressure)");

        var provider = new LargeResultProvider();
        using var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
            OperationProviderFactory = () => provider,
            WriteTimeout = TimeSpan.FromSeconds(2),
        });
        

        // The client shrinks its receive buffer and never reads after issuing PULL, creating
        // sustained output pressure (~19 MB response vs ~8 KB client buffer + loopback buffers).
        var client = ReviewClient.Connect(server.EndPoint, receiveBufferSize: 8 * 1024);
        client.HandshakeLegacy(0x00000805u);
        client.Send(Review.Hello());
        client.ReadMessageOrThrow();
        client.Send(Review.Logon());
        client.ReadMessageOrThrow();

        var closed = false;
        var elapsed = TimeSpan.Zero;
        var drainedBytes = 0L;
        try
        {
            client.Send(Review.Run("RETURN $big AS answer"));
            client.ReadMessageOrThrow(); // RUN SUCCESS (small; the reviewer still reads)
            client.Send(Review.Pull(-1)); // all rows: ~300 x 64 KB ≈ 19 MB in one bounded pass

            // From here the reviewer stops reading ENTIRELY (Poll without Read): no window
            // updates, no byte consumption. If the transport honours the WriteTimeout, the
            // connection closes at approximately the timeout boundary.
            var started = DateTime.UtcNow;
            while (DateTime.UtcNow - started < TimeSpan.FromSeconds(12))
            {
                if (client.PollClosedOrReadable(100) == "closed") { closed = true; break; }
            }

            elapsed = DateTime.UtcNow - started;

            // Drain-and-count: how much of the response actually reached the client?
            drainedBytes = client.DrainAvailable(out var sawEof);
        }
        catch (Exception ex)
        {
            Program.TraceLine($"exception while stalled: {ex.GetType().Name}");
            elapsed = TimeSpan.Zero;
        }

        Program.Check(
            "D1 stalled client connection closed",
            closed,
            $"closed={closed}");

        // Discriminator: if the write completed (no real backpressure), MessageProcessed fired;
        // if the write blocked, processing is stuck before the completion report.
        Program.Check(
            "D2a write-pending discriminator (processed counter)",
            server.Server.Diagnostics.MessagesProcessed == 3,
            $"processed={server.Server.Diagnostics.MessagesProcessed} (expect 3 = HELLO+LOGON+RUN only; PULL pending)");

        // A 19 MB response through an 8 KB non-reading socket cannot complete: if the server
        // write actually blocked, closure only occurs via the 2 s WriteTimeout (observed elapsed
        // near 2 s with scheduler slack). Instant (<1 s) closure would indicate the write never
        // blocked and the timeout was not what ended the connection.
        Program.Check(
            "D2 server write actually blocked (closure timed near the 2 s WriteTimeout)",
            closed && elapsed >= TimeSpan.FromSeconds(1.2) && elapsed < TimeSpan.FromSeconds(8),
            $"elapsed={elapsed.TotalMilliseconds:F0} ms (WriteTimeout=2000 ms); drainedToClient={drainedBytes}");

        Program.Check(
            "D3 provider consulted exactly once (single RUN)",
            provider.PrepareCount == 1,
            $"prepareCount={provider.PrepareCount}");

        // A healthy client keeps working while the stalled one is aborted.
        using (var healthy = ReviewClient.Connect(server.EndPoint))
        {
            healthy.HandshakeLegacy(0x00000805u);
            healthy.Send(Review.Hello());
            healthy.ReadMessageOrThrow();
            healthy.Send(Review.Logon());
            healthy.ReadMessageOrThrow();
            healthy.Send(Review.Run("RETURN 42 AS answer"));
            healthy.ReadMessageOrThrow();
            healthy.Send(Review.Pull(1));
            var record = healthy.ReadMessageOrThrow();
            Program.Check("D4 healthy client unaffected by stalled client", Program.Hex(record) == Golden.Record42, Program.Hex(record));
            healthy.Send(Review.Goodbye);
        }

        Program.Check("D5 listener still serving after the aborted write", server.Server.IsRunning, "IsRunning");

        client.Dispose();

        // Shutdown must still complete within its documented bound.
        var stop = server.Server.StopAsync();
        var finished = Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(10))).GetAwaiter().GetResult();
        Program.Check("D6 shutdown completes bounded after an aborted write", finished == stop, "StopAsync task completed");
    }
}
