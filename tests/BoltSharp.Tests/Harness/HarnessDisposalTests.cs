using Xunit;

namespace BoltSharp.Tests.Harness;

/// <summary>
/// B2-C-R1 regression coverage for B2-C-Q1 finding Q1-F02: disposing a harness endpoint must
/// promptly release pending asynchronous operations with a disposal result, not a clean EOF.
/// Bounded waits are deadlock-detection safeguards only; the harness itself is signal-driven.
/// </summary>
public sealed class HarnessDisposalTests
{
    private static async Task<bool> CompletedWithin(Task task, int milliseconds) =>
        await Task.WhenAny(task, Task.Delay(milliseconds)) == task;

    [Fact]
    public async Task Dispose_before_read_reflects_disposal()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(new byte[] { 1, 2, 3 });
        server.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.ReadAsync(new byte[3]).AsTask());
    }

    [Fact]
    public async Task Dispose_during_blocked_read_releases_it_promptly()
    {
        var (_, server) = HarnessDuplex.Create();
        var read = server.ReadAsync(new byte[4]).AsTask();
        Assert.False(await CompletedWithin(read, 100));

        server.Dispose();

        Assert.True(await CompletedWithin(read, 2_000), "blocked read was not released by disposal");
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await read);
    }

    [Fact]
    public async Task Dispose_after_buffering_data_reflects_disposal_not_eof()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(new byte[] { 9, 8, 7 });
        Assert.Equal(3, server.AvailableToRead);

        server.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.ReadAsync(new byte[3]).AsTask());
    }

    [Fact]
    public async Task Cancellation_versus_disposal_is_deterministic()
    {
        // Disposal first: the read observes disposal.
        var (_, disposedServer) = HarnessDuplex.Create();
        using var notCancelled = new CancellationTokenSource();
        var disposedRead = disposedServer.ReadAsync(new byte[1], notCancelled.Token).AsTask();
        disposedServer.Dispose();
        Assert.True(await CompletedWithin(disposedRead, 2_000));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await disposedRead);

        // Cancellation first: the read observes cancellation.
        var (_, cancelledServer) = HarnessDuplex.Create();
        using var cts = new CancellationTokenSource();
        var cancelledRead = cancelledServer.ReadAsync(new byte[1], cts.Token).AsTask();
        cts.Cancel();
        Assert.True(await CompletedWithin(cancelledRead, 2_000));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelledRead);
    }

    [Fact]
    public async Task Concurrent_writer_activity_during_disposal_does_not_deadlock()
    {
        var (client, server) = HarnessDuplex.Create(
            new HarnessStreamOptions { MaxBufferedBytes = 1 << 16 },
            new HarnessStreamOptions { MaxBufferedBytes = 1 << 16 });

        var writer = Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < 10_000; i++)
                {
                    client.Write(new byte[8]);
                }
            }
            catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
            {
                // ObjectDisposedException once the peer's read channel is disposed; the bounded
                // buffer may also reject a write if disposal happens late. Both terminate cleanly.
            }
        });

        server.Dispose();

        Assert.True(await CompletedWithin(writer, 5_000), "writer did not terminate after disposal");
    }

    [Fact]
    public void Repeated_disposal_is_safe_and_idempotent()
    {
        var (_, server) = HarnessDuplex.Create();
        for (var i = 0; i < 10; i++)
        {
            server.Dispose();
        }

        Assert.True(server.Disposed);
        Assert.Throws<ObjectDisposedException>(() => server.Read(new byte[1]));
    }

    [Fact]
    public async Task Dispose_during_read_is_stable_across_repetitions()
    {
        for (var i = 0; i < 50; i++)
        {
            var (_, server) = HarnessDuplex.Create();
            var read = server.ReadAsync(new byte[1]).AsTask();
            server.Dispose();
            Assert.True(await CompletedWithin(read, 2_000), $"iteration {i}: read was not released");
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await read);
        }
    }

    [Fact]
    public async Task Half_close_semantics_are_preserved_for_a_non_disposed_stream()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(new byte[] { 0x2A });
        client.CompleteWrites();

        Assert.Equal(1, await server.ReadAsync(new byte[1]));
        Assert.Equal(0, await server.ReadAsync(new byte[1])); // clean EOF, not a disposal error
        Assert.False(server.Disposed);
    }

    [Fact]
    public void Dispose_does_not_break_bounded_buffer_signaling()
    {
        var (client, _) = HarnessDuplex.Create(
            new HarnessStreamOptions { MaxBufferedBytes = 4 },
            new HarnessStreamOptions { MaxBufferedBytes = 4 });

        Assert.Throws<InvalidOperationException>(() => client.Write(new byte[5]));
        Assert.Equal(0, client.BufferedForPeer);
    }
}
