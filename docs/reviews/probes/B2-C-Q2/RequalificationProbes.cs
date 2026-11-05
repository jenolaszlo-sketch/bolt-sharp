using System.Reflection;
using System.Text.RegularExpressions;
using BoltSharp.Tests.Harness;

using Xunit;

namespace BoltSharp.Qualification.B2CQ2;

/// <summary>
/// Reviewer-owned B2-C-Q2 probes. Independently re-verify the closure of B2-C-Q1 findings
/// Q1-F01 (driver URI validation) and Q1-F02 (dispose wakes blocked reads), and introduce the
/// corrected replacement for the superseded historical C6 assertion. The producer harness source
/// is linked (not modified); the driver validation is exercised through the built producer
/// assembly by reflection.
/// </summary>
public sealed class RequalificationProbes
{
    // ---- F01: driver URI validation via the built producer assembly -------------

    private static readonly Assembly DriverAssembly =
        typeof(BoltSharp.DriverIntegrationTests.DriverInteropScaffoldTests).Assembly;

    private static readonly Type SettingsType =
        DriverAssembly.GetType("BoltSharp.DriverIntegrationTests.DriverTestSettings")
        ?? throw new InvalidOperationException("DriverTestSettings type not found");

    private static readonly MethodInfo ValidateUriMethod =
        SettingsType.GetMethod("ValidateUri", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ValidateUri method not found");

    private static Uri ValidateUri(string value)
    {
        try
        {
            return (Uri)ValidateUriMethod.Invoke(null, new object[] { value })!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    [Theory]
    [InlineData("bolt://127.0.0.1:7687")]
    [InlineData("bolt://localhost")]
    [InlineData("bolt://localhost:7687/")]
    [InlineData("BOLT://127.0.0.1:7687")]
    [InlineData("bolt+s://db.example.com:7687")]
    [InlineData("Bolt+Ssc://127.0.0.1:7687")]
    [InlineData("bolt://[::1]:7687")]
    public void F01_Accepts_Valid_Direct_Uris(string value)
    {
        var uri = ValidateUri(value);

        Assert.StartsWith("bolt", uri.Scheme, StringComparison.Ordinal);
        Assert.False(uri.Scheme.StartsWith("neo4j", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("neo4j://127.0.0.1:7687")]
    [InlineData("neo4j+s://127.0.0.1:7687")]
    [InlineData("neo4j+ssc://127.0.0.1:7687")]
    [InlineData("NEO4J://127.0.0.1:7687")]
    [InlineData("Neo4j+S://127.0.0.1:7687")]
    [InlineData("http://127.0.0.1:7687")]
    [InlineData("https://127.0.0.1:7687")]
    [InlineData("ws://127.0.0.1:7687")]
    [InlineData("wss://127.0.0.1:7687")]
    [InlineData("tcp://127.0.0.1:7687")]
    [InlineData("bolt://")]
    [InlineData("bolt://:7687")]
    [InlineData("bolt:127.0.0.1:7687")]
    [InlineData("bolt://host/path")]
    [InlineData("bolt://host/../other")]
    [InlineData("bolt://host/%2e%2e/evil")]
    [InlineData("bolt://host?x=1")]
    [InlineData("bolt://host#fragment")]
    [InlineData("bolt://user@host")]
    [InlineData("bolt://user:secret@host")]
    [InlineData("not a uri")]
    [InlineData("")]
    public void F01_Rejects_Routing_Unsupported_Or_Malformed(string value)
    {
        Assert.Throws<ArgumentException>(() => ValidateUri(value));
    }

    [Fact]
    public void F01_Driver_construction_is_confined_to_validated_settings()
    {
        var root = FindRepositoryRoot();
        var driverProject = Path.Combine(root, "tests", "BoltSharp.DriverIntegrationTests");
        var sources = Directory.EnumerateFiles(driverProject, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        foreach (var file in sources)
        {
            var count = Regex.Matches(File.ReadAllText(file), @"GraphDatabase\.Driver").Count;
            if (file.EndsWith("DriverTestSettings.cs", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal(1, count); // the single factory
            }
            else
            {
                Assert.Equal(0, count); // no other construction path
            }
        }

        var settings = File.ReadAllText(Path.Combine(driverProject, "DriverTestSettings.cs"));
        Assert.Contains("ValidateUri(Environment.GetEnvironmentVariable", settings, StringComparison.Ordinal);
        Assert.Contains("internal IDriver CreateDriver", settings, StringComparison.Ordinal);
    }

    // ---- F02: disposal wakes blocked reads (linked producer harness) ------------

    private static async Task<bool> CompletedWithin(Task task, int milliseconds) =>
        await Task.WhenAny(task, Task.Delay(milliseconds)) == task;

    [Fact]
    public async Task F02_Dispose_before_read_throws_ObjectDisposed()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(new byte[] { 1, 2, 3 });
        server.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.ReadAsync(new byte[3]).AsTask());
    }

    [Fact]
    public async Task F02_Dispose_during_blocked_read_throws_ObjectDisposed()
    {
        var (_, server) = HarnessDuplex.Create();
        var read = server.ReadAsync(new byte[4]).AsTask();
        Assert.False(await CompletedWithin(read, 100));

        server.Dispose();

        Assert.True(await CompletedWithin(read, 2_000), "blocked read was not released by disposal");
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await read);
    }

    [Fact]
    public async Task F02_Dispose_after_buffering_does_not_return_stale_data()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(new byte[] { 9, 8, 7 });
        Assert.Equal(3, server.AvailableToRead);

        server.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.ReadAsync(new byte[3]).AsTask());
    }

    [Fact]
    public void F02_Repeated_dispose_is_idempotent()
    {
        var (_, server) = HarnessDuplex.Create();
        for (var i = 0; i < 20; i++)
        {
            server.Dispose();
        }

        Assert.True(server.Disposed);
        Assert.Throws<ObjectDisposedException>(() => server.Read(new byte[1]));
    }

    [Fact]
    public async Task F02_Disposal_versus_cancellation_is_deterministic()
    {
        var (_, disposedServer) = HarnessDuplex.Create();
        using var notCancelled = new CancellationTokenSource();
        var disposedRead = disposedServer.ReadAsync(new byte[1], notCancelled.Token).AsTask();
        disposedServer.Dispose();
        Assert.True(await CompletedWithin(disposedRead, 2_000));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await disposedRead);

        var (_, cancelledServer) = HarnessDuplex.Create();
        using var cts = new CancellationTokenSource();
        var cancelledRead = cancelledServer.ReadAsync(new byte[1], cts.Token).AsTask();
        cts.Cancel();
        Assert.True(await CompletedWithin(cancelledRead, 2_000));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelledRead);
    }

    [Fact]
    public async Task F02_Pending_writer_terminates_on_disposal()
    {
        var (client, server) = HarnessDuplex.Create(
            new HarnessStreamOptions { MaxBufferedBytes = 1 << 16 },
            new HarnessStreamOptions { MaxBufferedBytes = 1 << 16 });

        var writer = Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < 25_000; i++)
                {
                    client.Write(new byte[8]);
                }
            }
            catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
            {
                // Expected once the peer's read channel is disposed, or when the bounded buffer fills.
            }
        });

        server.Dispose();

        Assert.True(await CompletedWithin(writer, 5_000), "writer did not terminate after disposal");
    }

    [Fact]
    public void F02_Bounded_buffer_signalling_preserved()
    {
        var (client, _) = HarnessDuplex.Create(
            new HarnessStreamOptions { MaxBufferedBytes = 4 },
            new HarnessStreamOptions { MaxBufferedBytes = 4 });

        Assert.Throws<InvalidOperationException>(() => client.Write(new byte[5]));
        Assert.Equal(0, client.BufferedForPeer);
    }

    [Fact]
    public async Task F02_Half_close_is_distinct_from_disposal()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(new byte[] { 0x2A });
        client.CompleteWrites();

        Assert.Equal(1, await server.ReadAsync(new byte[1]));
        Assert.Equal(0, await server.ReadAsync(new byte[1])); // clean EOF, not a disposal error
        Assert.False(server.Disposed);

        // Disposal on a separate endpoint yields a disposal result, not EOF.
        var (_, other) = HarnessDuplex.Create();
        var pending = other.ReadAsync(new byte[1]).AsTask();
        other.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await pending);
    }

    [Fact]
    public async Task F02_No_pending_read_remains_across_many_interleavings()
    {
        // Multiple schedules: read-then-dispose, cancel-then-dispose, dispose-then-read, and
        // write-then-read-then-dispose. Each iteration must terminate; none may hang.
        for (var i = 0; i < 100; i++)
        {
            switch (i % 4)
            {
                case 0:
                {
                    var (_, server) = HarnessDuplex.Create();
                    var read = server.ReadAsync(new byte[1]).AsTask();
                    server.Dispose();
                    Assert.True(await CompletedWithin(read, 2_000), $"case0 iteration {i} hung");
                    await Assert.ThrowsAsync<ObjectDisposedException>(async () => await read);
                    break;
                }

                case 1:
                {
                    var (_, server) = HarnessDuplex.Create();
                    using var cts = new CancellationTokenSource();
                    var read = server.ReadAsync(new byte[1], cts.Token).AsTask();
                    cts.Cancel();
                    server.Dispose();
                    Assert.True(await CompletedWithin(read, 2_000), $"case1 iteration {i} hung");
                    var exception = await Record.ExceptionAsync(async () => await read);
                    Assert.True(exception is OperationCanceledException or ObjectDisposedException,
                        $"case1 iteration {i} unexpected exception {exception?.GetType().Name}");
                    break;
                }

                case 2:
                {
                    var (_, server) = HarnessDuplex.Create();
                    server.Dispose();
                    Assert.Throws<ObjectDisposedException>(() => server.Read(new byte[1]));
                    break;
                }

                default:
                {
                    var (client, server) = HarnessDuplex.Create();
                    client.Write(new byte[] { 0x01 });
                    Assert.Equal(1, await server.ReadAsync(new byte[1]));
                    var read = server.ReadAsync(new byte[1]).AsTask();
                    server.Dispose();
                    Assert.True(await CompletedWithin(read, 2_000), $"case3 iteration {i} hung");
                    await Assert.ThrowsAsync<ObjectDisposedException>(async () => await read);
                    break;
                }
            }
        }
    }

    // ---- C6 replacement: corrected disposal contract ---------------------------

    [Fact]
    public async Task C6R_Dispose_releases_blocked_read_with_disposal_result()
    {
        var (_, server) = HarnessDuplex.Create();
        var read = server.ReadAsync(new byte[4]).AsTask();
        Assert.False(await CompletedWithin(read, 100));

        server.Dispose();

        var woke = await CompletedWithin(read, 2_000);
        Assert.True(woke, "disposal must wake a blocked read (corrected contract)");
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await read);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BoltSharp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("repository root not found");
    }
}
