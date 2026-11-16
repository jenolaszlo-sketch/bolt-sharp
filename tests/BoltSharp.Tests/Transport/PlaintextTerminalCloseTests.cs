using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using BoltSharp.Framing;
using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Tests.Sessions;
using BoltSharp.Transport;
using Xunit;
using Xunit.Abstractions;

namespace BoltSharp.Tests.Transport;

[CollectionDefinition("Plaintext terminal transport", DisableParallelization = true)]
public sealed class PlaintextTerminalTransportCollection;

[Collection("Plaintext terminal transport")]
public sealed class PlaintextTerminalCloseTests(ITestOutputHelper output)
{
    private const string ExactFailure =
        "B17FA48A67716C5F7374617475738530384E30368A6E656F346A5F636F6465D01F4E656F2E436C69656E744572726F722E526571756573742E496E76616C69648B6465736372697074696F6ED0434578706C69636974207472616E73616374696F6E7320616E6420726F7574696E6720617265206E6F7420737570706F727465642062792074686973207365727665722ED011646961676E6F737469635F7265636F7264A0";
    private static readonly byte[] Begin = TransportTestSupport.Frame([0xB1, 0x11, 0xA0]);
    private static readonly byte[] FollowUp = Frame(SessionHarness.Run("RETURN 42 AS answer"));
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("empty")]
    [InlineData("small")]
    [InlineData("large")]
    [InlineData("partial")]
    [InlineData("malformed")]
    [InlineData("delayed")]
    public async Task ExactFailureAndNoTrailingExecution(string scenario)
    {
        using var deadline = Limit();
        var provider = ScriptedOperationProvider.WithRows(42);
        await using var server = await Start(provider: provider);
        using var trace = new Capture(server);
        using var peer = await Peer.Open(server, deadline.Token);
        var tail = scenario switch
        {
            "small" or "delayed" => FollowUp,
            "large" => new byte[24 * 1024],
            "partial" => FollowUp[..1],
            "malformed" => new byte[] { 0, 2, 0xc0, 0xc0, 0, 0 },
            _ => []
        };
        await peer.Send(Begin.Concat(tail).ToArray(), deadline.Token);
        if (scenario != "large")
            peer.Socket.Client.Shutdown(SocketShutdown.Send);
        if (scenario == "delayed")
            await Task.Delay(15, deadline.Token);
        Assert.Equal(ExactFailure, Convert.ToHexString((await peer.Read(deadline.Token))!));
        try
        {
            Assert.Null(await peer.Read(deadline.Token));
        }
        catch (IOException ex) when (scenario == "large" && ex.InnerException is SocketException
        { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted })
        {
            // A cap exit can leave unread bytes. An exact first FAILURE remains mandatory.
        }
        await WaitUntil(() => server.Diagnostics.ConnectionsActive == 0, deadline.Token);
        Assert.Equal(0, provider.PrepareCount);
        Assert.Equal(3, server.Diagnostics.MessagesProcessed);
        Assert.Equal(1, server.Diagnostics.ConnectionsCompleted);
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
        var end = Assert.Single(trace.Named("disposed"));
        Assert.Equal(scenario == "large" ? "byte-limit" : "peer-eof", end.Reason);
        Assert.Equal(Math.Min(tail.Length, 8192), end.Bytes);
        trace.AssertHandlesClosed(1);
        output.WriteLine($"{scenario}: {end.Reason}, {end.Bytes} bytes, {end.Elapsed:F2} ms");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedTailComparison(bool delayed)
    {
        using var deadline = Limit();
        var provider = ScriptedOperationProvider.WithRows(42);
        await using var server = await Start(provider: provider);
        using var peer = await Peer.Open(server, deadline.Token);
        await peer.Send(Begin.Concat(FollowUp).ToArray(), deadline.Token);
        if (delayed)
            await Task.Delay(18, deadline.Token);
        string? failure = null;
        var ending = "reader-eof";
        try
        {
            var response = await peer.Read(deadline.Token);
            failure = response is null ? null : Convert.ToHexString(response);
            Assert.Null(await peer.Read(deadline.Token));
        }
        catch (IOException error) when (error.InnerException is SocketException
        { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted })
        {
            ending = "peer-reset";
        }
        await WaitUntil(() => server.Diagnostics.ConnectionsActive == 0, deadline.Token);
        output.WriteLine($"delayed={delayed} exactFailure={failure == ExactFailure} ending={ending}");
        Assert.Equal(ExactFailure, failure);
        Assert.Equal("reader-eof", ending);
        Assert.Equal(0, provider.PrepareCount);
        Assert.Equal(3, server.Diagnostics.MessagesProcessed);
    }

    [Theory]
    [InlineData("stalled")]
    [InlineData("continues")]
    [InlineData("reset")]
    public async Task BoundedAdversePeer(string scenario)
    {
        using var deadline = Limit();
        var provider = ScriptedOperationProvider.WithRows(42);
        await using var server = await Start(provider: provider);
        using var trace = new Capture(server);
        using var peer = await Peer.Open(server, deadline.Token);
        await peer.Send(Begin, deadline.Token);
        await WaitUntil(() => trace.Named("drain-start").Any(), deadline.Token);
        if (scenario == "continues")
        {
            for (var i = 0; i < 4; i++)
            {
                try { await peer.Send(new byte[512], deadline.Token); }
                catch (IOException) { break; }
                await Task.Delay(8, deadline.Token);
            }
        }
        if (scenario == "reset")
        {
            peer.Socket.LingerState = new LingerOption(true, 0);
            peer.Socket.Client.Dispose();
        }
        await WaitUntil(() => server.Diagnostics.ConnectionsActive == 0, deadline.Token);
        var end = Assert.Single(trace.Named("disposed"));
        Assert.InRange(end.Elapsed, 0, 450);
        Assert.InRange(end.Bytes, 0, 8192);
        Assert.Equal(scenario == "reset" ? "peer-disconnect" : "drain-deadline", end.Reason);
        Assert.Equal(0, provider.PrepareCount);
        Assert.Equal(3, server.Diagnostics.MessagesProcessed);
        Assert.Equal(1, server.Diagnostics.ConnectionsCompleted);
        trace.AssertHandlesClosed(1);
        output.WriteLine($"{scenario}: {end.Reason}, {end.Bytes} bytes, {end.Elapsed:F2} ms");
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(3, 1, 1)]
    [InlineData(4, 3, 1)]
    public async Task DrainAdmissionAndRecovery(int connectionLimit, int simultaneous, int admitted)
    {
        using var deadline = Limit();
        await using var server = await Start(connectionLimit);
        using var trace = new Capture(server);
        var peers = new List<Peer>();
        try
        {
            for (var i = 0; i < simultaneous; i++)
                peers.Add(await Peer.Open(server, deadline.Token));
            await Task.WhenAll(peers.Select(peer => peer.Send(Begin, deadline.Token)));
            await WaitUntil(() => server.Diagnostics.ConnectionsCompleted == simultaneous, deadline.Token);
            var ended = trace.Named("disposed").ToArray();
            Assert.Equal(admitted, ended.Count(item => item.Admitted));
            Assert.Equal(simultaneous - admitted, ended.Count(item => item.Reason == "capacity"));
            trace.AssertHandlesClosed(simultaneous);
            Assert.Equal(0, server.Diagnostics.ConnectionsActive);
            if (admitted > 0)
            {
                using var replacement = await Peer.Open(server, deadline.Token);
                await replacement.Send(Begin, deadline.Token);
                replacement.Socket.Client.Shutdown(SocketShutdown.Send);
                Assert.Equal(ExactFailure, Convert.ToHexString((await replacement.Read(deadline.Token))!));
                await WaitUntil(() => server.Diagnostics.ConnectionsCompleted == simultaneous + 1, deadline.Token);
                Assert.Equal(admitted + 1, trace.Named("disposed").Count(item => item.Admitted));
            }
            output.WriteLine($"admitted={admitted} capacity={simultaneous - admitted} connections={simultaneous}");
        }
        finally
        {
            foreach (var peer in peers)
                peer.Dispose();
        }
    }

    [Fact]
    public async Task ListenerStopJoinsActualHandleAndReturnsPermit()
    {
        using var deadline = Limit();
        await using var server = await Start();
        using var trace = new Capture(server);
        using var peer = await Peer.Open(server, deadline.Token);
        await peer.Send(Begin, deadline.Token);
        await WaitUntil(() => trace.Named("drain-start").Any(), deadline.Token);
        var stopAt = Stopwatch.GetTimestamp();
        await server.StopAsync(deadline.Token);
        var end = Assert.Single(trace.Named("disposed"));
        Assert.Equal("listener-shutdown", end.Reason);
        Assert.True(end.Admitted);
        trace.AssertHandlesClosed(1);
        Assert.Equal(0, server.Diagnostics.ConnectionsActive);
        Assert.Equal(1, server.Diagnostics.ConnectionsCompleted);
        output.WriteLine($"stop={Stopwatch.GetElapsedTime(stopAt).TotalMilliseconds:F2} ms");
    }

    [Fact]
    public async Task ShutdownObservesConnectionAcceptedDuringStop()
    {
        using var deadline = Limit();
        using var allowRegistration = new ManualResetEventSlim(false);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await Start();
        server.Diagnostics.Event += item =>
        {
            if (item.Kind == BoltTransportEventKind.ConnectionAccepted)
            {
                accepted.TrySetResult();
                allowRegistration.Wait(deadline.Token);
            }
        };

        using var peer = new TcpClient();
        await peer.ConnectAsync(server.BoundEndPoint!, deadline.Token);
        try
        {
            await accepted.Task.WaitAsync(deadline.Token);
            var stop = server.StopAsync(deadline.Token);
            Assert.False(stop.IsCompleted);
            allowRegistration.Set();
            await stop;
        }
        finally
        {
            allowRegistration.Set();
        }

        Assert.Equal(1, server.Diagnostics.ConnectionsAccepted);
        Assert.Equal(0, server.Diagnostics.ConnectionsActive);
        Assert.Equal(1, server.Diagnostics.ConnectionsCompleted);
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
    }

    [Fact]
    public async Task OneDeadlineCoversWriteAndDrain()
    {
        using var deadline = Limit();
        await using var server = await Start();
        using var trace = new Capture(server, item =>
        {
            // Spend time before the actual write to make the remaining total budget shorter than
            // the drain cap. The fixture never pauses an actual network write.
            if (item.Name == "write-start")
                Thread.Sleep(205);
        });
        using var peer = await Peer.Open(server, deadline.Token);
        await peer.Send(Begin, deadline.Token);
        Assert.Equal(ExactFailure, Convert.ToHexString((await peer.Read(deadline.Token))!));
        await WaitUntil(() => server.Diagnostics.ConnectionsActive == 0, deadline.Token);
        var end = Assert.Single(trace.Named("disposed"));
        Assert.Equal("total-deadline", end.Reason);
        Assert.InRange(end.Elapsed, 205, 370);
        trace.AssertHandlesClosed(1);
        output.WriteLine($"absolute deadline: {end.Elapsed:F2} ms");
    }

    [Theory]
    [InlineData("deadline")]
    [InlineData("owner")]
    [InlineData("listener")]
    public async Task ActualPendingTerminalWriteIsInterrupted(string stimulus)
    {
        (int Size, int Receive, int Send)[] profiles = OperatingSystem.IsWindows()
            ? new[] { (4 * 1024 * 1024, 4096, 0), (512 * 1024, 4096, 2048) }
            : new[]
            {
                (256 * 1024, 1024, 2048), (512 * 1024, 2048, 1024), (1024 * 1024, 4096, 2048),
                (256 * 1024 - 128, 1024, 1024), (512 * 1024 - 128, 1024, 1024),
                (1024 * 1024 - 128, 1024, 1024), (1536 * 1024, 1024, 1024),
                (2048 * 1024 - 128, 1024, 1024), (2048 * 1024, 1024, 1024),
                (1536 * 1024 - 512, 4096, 1024), (2048 * 1024 - 512, 4096, 1024),
                (2560 * 1024 - 128, 4096, 1024)
            };
        foreach (var profile in profiles)
        {
            using var deadline = Limit();
            var row = new PackStreamValue?[] { new PackStreamString(new string('x', profile.Size)) };
            var provider = ScriptedOperationProvider.WithOperation(new ScriptedOperation(["value"], row));
            await using var server = await Start(provider: provider, writeTimeout: TimeSpan.FromMilliseconds(650));
            using var trace = new Capture(server, item =>
            {
                if (profile.Send != 0 && item.Name == "write-start")
                {
                    var client = Capture.FindClient(server, item.Id)!;
                    client.SendBufferSize = profile.Send;
                    output.WriteLine($"terminal profile {profile}, send={client.SendBufferSize}, writable={client.Client.Poll(0, SelectMode.SelectWrite)}");
                }
            });
            using var peer = await Peer.Open(server, deadline.Token, receive: profile.Receive);
            await peer.Send(Frame(SessionHarness.Run()), deadline.Token);
            Assert.NotNull(await peer.Read(deadline.Token));
            await peer.Send(Frame(SessionHarness.Pull(2)).Concat(Begin).ToArray(), deadline.Token);
            await WaitUntil(() => trace.Named("write-pending").Any() || server.Diagnostics.ConnectionsActive == 0,
                deadline.Token);
            if (!trace.Named("write-pending").Any())
            {
                output.WriteLine($"profile {profile}: no pending terminal write, messages={server.Diagnostics.MessagesProcessed}");
                continue;
            }

            await Task.Delay(18, deadline.Token);
            if (trace.Named("write-complete").Any() || server.Diagnostics.ConnectionsActive == 0)
            {
                output.WriteLine($"profile {profile}: terminal write did not stay pending");
                continue;
            }

            var signalAt = Stopwatch.GetTimestamp();
            if (stimulus == "listener")
                await server.StopAsync(deadline.Token);
            if (stimulus == "owner")
            {
                var id = Assert.Single(trace.Named("write-start")).Id;
                var cancel = typeof(BoltTcpServer).GetMethod("CancelPlaintextCloseAsync", Private)!;
                Assert.True(await ((Task<bool>)cancel.Invoke(server, [id])!).WaitAsync(deadline.Token));
            }

            await WaitUntil(() => server.Diagnostics.ConnectionsActive == 0, deadline.Token);
            var end = Assert.Single(trace.Named("disposed"));
            Assert.Equal("write", end.Phase);
            Assert.Equal(stimulus switch
            {
                "deadline" => "total-deadline",
                "listener" => "listener-shutdown",
                _ => "owner-cancelled"
            }, end.Reason);
            if (stimulus != "deadline")
                Assert.InRange(Stopwatch.GetElapsedTime(signalAt).TotalMilliseconds, 0, 130);
            else
                Assert.InRange(end.Elapsed, 200, 450);
            Assert.Equal(4, server.Diagnostics.MessagesProcessed);
            Assert.Equal(1, server.Diagnostics.ConnectionsCompleted + server.Diagnostics.ConnectionsFailed);
            Assert.False(end.Admitted);
            trace.AssertHandlesClosed(1);
            output.WriteLine($"profile {profile}: {end.Reason}, {end.Elapsed:F2} ms");
            return;
        }

        Assert.Fail("No profile produced a persistent pending plaintext terminal write.");
    }

    [Fact]
    public async Task TlsRefusalNeverEntersPlaintextHelper()
    {
        using var deadline = Limit();
        using var certificate = TransportTestSupport.CreateSelfSignedCertificate();
        await using var server = await Start(certificate: certificate);
        using var trace = new Capture(server);
        using var peer = await Peer.Open(server, deadline.Token, certificate);
        await peer.Send(Begin, deadline.Token);
        Assert.Equal(ExactFailure, Convert.ToHexString((await peer.Read(deadline.Token))!));
        await WaitUntil(() => server.Diagnostics.ConnectionsActive == 0, deadline.Token);
        Assert.Empty(trace.Events);
        Assert.Equal(3, server.Diagnostics.MessagesProcessed);
        Assert.Equal(1, server.Diagnostics.ConnectionsCompleted);
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
    }

    private static CancellationTokenSource Limit() => new(TimeSpan.FromSeconds(5));

    private static Task<BoltTcpServer> Start(int maxConnections = 4, ScriptedOperationProvider? provider = null,
        TimeSpan? writeTimeout = null, X509Certificate2? certificate = null)
        => TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            MaxConnections = maxConnections,
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = provider is null ? new BoltTcpServerOptions().OperationProviderFactory : () => provider,
            WriteTimeout = writeTimeout ?? TimeSpan.FromSeconds(5),
            Tls = certificate is null ? null : new BoltTlsServerOptions(certificate)
        });

    private static async Task WaitUntil(Func<bool> predicate, CancellationToken token)
    {
        while (!predicate())
            await Task.Delay(1, token);
    }

    private static byte[] Frame(BoltRequest request) => TransportTestSupport.Frame(TransportRequestEncoder.Encode(request));

    private sealed class Peer(TcpClient socket, Stream stream) : IDisposable
    {
        private readonly BoltMessageReader _reader = new(stream);
        internal TcpClient Socket { get; } = socket;

        internal static async Task<Peer> Open(BoltTcpServer server, CancellationToken token,
            X509Certificate2? certificate = null, int receive = 0)
        {
            var client = new TcpClient(AddressFamily.InterNetwork);
            Stream? protocol = null;
            try
            {
                if (receive != 0)
                    client.ReceiveBufferSize = receive;
                await client.ConnectAsync(server.BoundEndPoint!, token);
                protocol = client.GetStream();
                if (certificate is not null)
                {
                    var ssl = new SslStream(protocol, leaveInnerStreamOpen: true);
                    protocol = ssl;
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = "localhost",
                        RemoteCertificateValidationCallback = (_, cert, _, _) => cert?.GetCertHashString() == certificate.Thumbprint
                    }, token);
                }

                var peer = new Peer(client, protocol);
                await peer.Send(TransportTestSupport.HandshakeFromVersions(new BoltProtocolVersion(5, 8)), token);
                var selected = new byte[4];
                await protocol.ReadExactlyAsync(selected, token);
                Assert.Equal(new byte[] { 0, 0, 8, 5 }, selected);
                var codec = new BoltMessageCodec();
                foreach (var request in new BoltRequest[] { SessionHarness.Hello(), SessionHarness.Logon() })
                {
                    await peer.Send(Frame(request), token);
                    Assert.IsType<SuccessResponse>(codec.DecodeResponse((await peer.Read(token))!));
                }
                return peer;
            }
            catch
            {
                client.Dispose();
                protocol?.Dispose();
                throw;
            }
        }

        internal Task Send(byte[] data, CancellationToken token) => stream.WriteAsync(data, token).AsTask();
        internal ValueTask<byte[]?> Read(CancellationToken token) => _reader.ReadMessageAsync(token);
        public void Dispose()
        {
            Socket.Dispose();
            stream.Dispose();
        }
    }

    private sealed record Event(string Name, string Id, string? Phase, string? Reason,
        int Bytes, bool Admitted, double Elapsed);

    private sealed class Capture : IDisposable
    {
        private readonly IDisposable _all;
        private IDisposable? _events;
        private readonly ConcurrentDictionary<string, SafeSocketHandle> _handles = new();
        internal ConcurrentQueue<Event> Events { get; } = new();

        internal Capture(BoltTcpServer server, Action<Event>? observe = null)
        {
            _all = DiagnosticListener.AllListeners.Subscribe(new Observer<DiagnosticListener>(listener =>
            {
                if (listener.Name == "BoltSharp.PlaintextTerminalClose")
                    _events = listener.Subscribe(new Observer<KeyValuePair<string, object?>>(entry => Record(entry)));
            }));

            void Record(KeyValuePair<string, object?> entry)
            {
                var payload = entry.Value!;
                object? Get(string name) => payload.GetType().GetProperty(name)!.GetValue(payload);
                var item = new Event(entry.Key, (string)Get("ConnectionId")!, (string?)Get("Phase"),
                    (string?)Get("Reason"), (int)Get("Bytes")!, (bool)Get("Admitted")!,
                    (double)Get("ElapsedMilliseconds")!);
                if (item.Name == "write-start")
                {
                    var client = FindClient(server, item.Id)!;
                    var linger = client.LingerState;
                    if (linger is null || linger.Enabled)
                        return;
                    _handles[item.Id] = client.Client.SafeHandle;
                }
                Events.Enqueue(item);
                observe?.Invoke(item);
            }
        }

        internal static TcpClient? FindClient(BoltTcpServer server, string id)
        {
            var close = typeof(BoltTcpServer).GetField("_plaintextClose", Private)!.GetValue(server);
            var owners = close?.GetType().GetField("_owners", Private)!.GetValue(close) as IDictionary;
            var owner = owners?[id];
            return owner?.GetType().GetProperty("Client", Private)!.GetValue(owner) as TcpClient;
        }

        internal IEnumerable<Event> Named(string name) => Events.Where(item => item.Name == name);

        internal void AssertHandlesClosed(int expected)
        {
            Assert.Equal(expected, _handles.Count);
            Assert.All(_handles.Values, handle => Assert.True(handle.IsClosed));
        }

        public void Dispose()
        {
            _events?.Dispose();
            _all.Dispose();
        }
    }

    private sealed class Observer<T>(Action<T> callback) : IObserver<T>
    {
        public void OnNext(T value) => callback(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
