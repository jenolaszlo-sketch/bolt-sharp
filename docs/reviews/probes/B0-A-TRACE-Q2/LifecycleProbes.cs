using System.Net;
using System.Net.Sockets;
using System.Reflection;
using BoltSharp.DriverIntegrationTests.HandshakeTrace;
using BoltSharp.Handshake;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace BoltSharp.Qualification.B0ATRACEQ2;

// Exact linked source by default. Reflection arranges states; no production hook is added.
public sealed class LifecycleProbes(ITestOutputHelper output)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(8);
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly byte[] Preamble = Convert.FromHexString("6060B01700000805000000000000000000000000");
    private static LoopbackHandshakeListener Start(int cap = 4, bool observe = false,
        TimeSpan? lifetime = null) => LoopbackHandshakeListener.Start(
        new BoltHandshakeOptions(new[] { new BoltProtocolVersion(5, 8) }, manifestV1Enabled: false),
        new LoopbackListenerLimits {
            MaxConnections = cap, AcceptBacklog = 16, ObserveFirstMessage = observe,
            ConnectionLifetime = lifetime ?? TimeSpan.FromSeconds(30),
            HandshakeTimeout = TimeSpan.FromSeconds(30),
            FirstMessageObservationTimeout = TimeSpan.FromSeconds(30),
        });
    private static T Field<T>(object obj, string name) =>
        (T) obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static string Endpoint(TcpClient client) => (string) typeof(LoopbackHandshakeListener)
        .GetMethod("SafeRemoteEndPoint", BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, new object[] { client })!;

    private static Task Handler(LoopbackHandshakeListener listener, TcpClient client, string endpoint = "reviewer")
    {
        var method = typeof(LoopbackHandshakeListener).GetMethod("HandleConnectionAsync", Private)!;
        // Same tests can run against byte-faithful pre-fix source without changing that source.
        return (Task) method.Invoke(listener, method.GetParameters().Length == 3
            ? new object[] { 1, client, endpoint } : new object[] { 1, client })!;
    }
    private static Task Track(LoopbackHandshakeListener listener, TcpClient client, string endpoint = "reviewer")
    {
        lock (Field<object>(listener, "_gate")) Field<List<TcpClient>>(listener, "_clients").Add(client);
        var task = Handler(listener, client, endpoint);
        lock (Field<object>(listener, "_gate")) Field<List<Task>>(listener, "_handlers").Add(task);
        return task;
    }
    private static async Task Drained(LoopbackHandshakeListener listener, bool verifyPort = true)
    {
        await listener.DisposeAsync().AsTask().WaitAsync(Bound);
        Assert.True(Field<Task>(listener, "_acceptLoop").IsCompletedSuccessfully);
        Assert.All(Field<List<Task>>(listener, "_handlers"), t => Assert.True(t.IsCompletedSuccessfully));
        Assert.Empty(Field<List<TcpClient>>(listener, "_clients"));
        if (listener.Traces.Count > 0) Assert.True(listener.FirstTrace.IsCompletedSuccessfully);
        if (!verifyPort) return;
        using var probe = new TcpClient();
        var ex = await Record.ExceptionAsync(async () =>
            await probe.ConnectAsync(IPAddress.Loopback, listener.Port).WaitAsync(Bound));
        Assert.IsType<SocketException>(ex); // a timeout is a failure, not evidence of closure
    }
    private void Trace(HandshakeTrace trace) => output.WriteLine(
        $"outcome={trace.Outcome}; error={trace.ErrorType}; detail={trace.SanitizedError}; endpoint={trace.RemoteEndPoint}");

    [Fact]
    public void Q01_LocalCloseBeforeEndpointCapture_IsNullSafe()
    {
        using var client = new TcpClient(); client.Close();
        Assert.Equal("UNKNOWN", Endpoint(client));
    }
    [Fact]
    public async Task Q02_AlreadyClosedClient_HandlerAndDisposeComplete()
    {
        await using var listener = Start();
        using var client = new TcpClient(); client.Close();
        await Handler(listener, client).WaitAsync(Bound);
        Trace(Assert.Single(listener.Traces));
        Assert.Equal("CLOSED", listener.Traces[0].Outcome);
        await Drained(listener);
    }
    [Fact]
    public async Task Q03_LocalCloseAfterEndpointCapture_PreservesMetadata()
    {
        await using var listener = Start();
        using var pair = await Pair.Create();
        var endpoint = Endpoint(pair.Server);
        Assert.NotEqual("UNKNOWN", endpoint);
        pair.Server.Close();
        await Handler(listener, pair.Server, endpoint).WaitAsync(Bound);
        var trace = Assert.Single(listener.Traces); Trace(trace);
        Assert.Equal(endpoint, trace.RemoteEndPoint); Assert.Equal("CLOSED", trace.Outcome);
        await Drained(listener);
    }
    [Fact]
    public async Task Q04_RemoteCloseBeforeCapture_DoesNotDisposeLocalSocket()
    {
        await using var listener = Start();
        using var pair = await Pair.Create(); pair.Peer.Close();
        Assert.NotEqual("UNKNOWN", Endpoint(pair.Server));
        await Handler(listener, pair.Server, Endpoint(pair.Server)).WaitAsync(Bound);
        Trace(Assert.Single(listener.Traces));
        Assert.NotEqual("ERROR", listener.Traces[0].Outcome);
        await Drained(listener);
    }
    [Fact]
    public async Task Q05_CloseDuringActualBlockedRead_IsClosed()
    {
        await using var listener = Start(); using var pair = await Pair.Create();
        using var stream = Install(pair.Server);
        var task = Track(listener, pair.Server);
        await stream.Entered.Task.WaitAsync(Bound); Assert.False(task.IsCompleted);
        pair.Server.Close();
        await task.WaitAsync(Bound); Trace(Assert.Single(listener.Traces));
        Assert.Equal("CLOSED", listener.Traces[0].Outcome);
        await Drained(listener);
    }
    [Fact]
    public async Task Q06_ListenerDisposeDuringBlockedRead_JoinsHandler()
    {
        await using var listener = Start(); using var pair = await Pair.Create();
        using var stream = Install(pair.Server);
        _ = Track(listener, pair.Server);
        await stream.Entered.Task.WaitAsync(Bound);
        await Drained(listener); Trace(Assert.Single(listener.Traces));
        Assert.Contains(listener.Traces[0].Outcome, new[] { "CLOSED", "CANCELLED" });
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Q07_CancelAndPeerClose_ControlledOrders(bool cancelFirst)
    {
        await using var listener = Start(); using var pair = await Pair.Create();
        using var stream = Install(pair.Server);
        var task = Track(listener, pair.Server);
        await stream.Entered.Task.WaitAsync(Bound);
        if (cancelFirst) { listener.Cancel(); pair.Peer.Close(); }
        else { pair.Peer.Close(); listener.Cancel(); }
        await task.WaitAsync(Bound); await Drained(listener);
        Trace(Assert.Single(listener.Traces)); Assert.NotEqual("ERROR", listener.Traces[0].Outcome);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Q08_CancelConcurrentDispose_WithReleaseBarrier(bool disposeFirst)
    {
        await using var listener = Start(); using var pair = await Pair.Create();
        using var stream = Install(pair.Server); _ = Track(listener, pair.Server);
        await stream.Entered.Task.WaitAsync(Bound);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var one = Task.Run(async () => { await release.Task; if (disposeFirst) await listener.DisposeAsync(); else listener.Cancel(); });
        var two = Task.Run(async () => { await release.Task; if (disposeFirst) listener.Cancel(); else await listener.DisposeAsync(); });
        release.SetResult(); await Task.WhenAll(one, two).WaitAsync(Bound);
        await Drained(listener);
    }
    [Fact]
    public async Task Q09_PendingAccept_AndRepeatedDispose_Complete()
    {
        var listener = Start(); Assert.False(Field<Task>(listener, "_acceptLoop").IsCompleted);
        await Drained(listener);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => listener.DisposeAsync().AsTask())).WaitAsync(Bound);
        listener.Cancel();
    }
    [Fact]
    public async Task Q10_FourSimultaneousConnections_ActualAcceptLoopDrains()
    {
        await using var listener = Start(4, true);
        var clients = Enumerable.Range(0, 4).Select(_ => new TcpClient()).ToArray();
        try {
            await Task.WhenAll(clients.Select(async client => {
                await client.ConnectAsync(IPAddress.Loopback, listener.Port);
                await client.GetStream().WriteAsync(Preamble);
                var response = new byte[4]; await client.GetStream().ReadExactlyAsync(response).AsTask().WaitAsync(Bound);
                Assert.Equal("00000805", Convert.ToHexString(response));
            })).WaitAsync(Bound);
            // Wire responses establish all four reached negotiation; no delay is a phase guarantee.
            await Drained(listener);
            Assert.Equal(4, listener.Traces.Count);
            Assert.Equal(4, listener.Traces.Select(t => t.ConnectionIndex).Distinct().Count());
            Assert.All(listener.Traces, t => { Trace(t); Assert.NotEqual("ERROR", t.Outcome); });
        } finally { foreach (var client in clients) client.Dispose(); }
    }
    [Fact]
    public async Task Q11_UnexpectedHandlerException_IsError()
    {
        var trace = await Inject(new InvalidOperationException("reviewer unexpected handler fault"));
        Assert.Equal("ERROR", trace.Outcome); Assert.Equal(typeof(InvalidOperationException).FullName, trace.ErrorType);
    }
    [Fact]
    public async Task Q12_MidReadObjectDisposed_IsClosed()
    {
        var trace = await Inject(new ObjectDisposedException("reviewer read"));
        Assert.Equal("CLOSED", trace.Outcome);
    }
    [Fact]
    public async Task Q13_ConnectionReset_IsClosed()
    {
        var trace = await Inject(new SocketException((int) SocketError.ConnectionReset));
        Assert.Equal("CLOSED", trace.Outcome);
    }
    [Theory]
    [InlineData(SocketError.NetworkDown)]
    [InlineData(SocketError.NetworkUnreachable)]
    [InlineData(SocketError.NoBufferSpaceAvailable)]
    public async Task Q14_UnexpectedSocketFault_MustRemainError(SocketError code)
    {
        var trace = await Inject(new SocketException((int) code));
        Assert.Equal("ERROR", trace.Outcome);
    }
    [Fact]
    public async Task Q15_UnexpectedWrappedSocketFault_MustRemainError()
    {
        var trace = await Inject(new IOException("reviewer network read failure",
            new SocketException((int) SocketError.NetworkDown)));
        Assert.Equal("ERROR", trace.Outcome);
    }
    [Fact]
    public async Task Q16_UnexpectedPrologueFailure_IsError_WithCleanup()
    {
        await using var listener = Start(lifetime: TimeSpan.FromMilliseconds(-2));
        using var pair = await Pair.Create();
        await Handler(listener, pair.Server).WaitAsync(Bound);
        var trace = Assert.Single(listener.Traces); Trace(trace);
        Assert.Equal("ERROR", trace.Outcome); Assert.Equal(typeof(ArgumentOutOfRangeException).FullName, trace.ErrorType);
        await Drained(listener);
    }
    [Fact]
    public async Task Q17_AlternateInterleavings_RepeatedFortyTimes()
    {
        for (var i = 0; i < 40; i++) {
            await using var listener = Start(); using var pair = await Pair.Create();
            using var stream = Install(pair.Server); var task = Track(listener, pair.Server);
            await stream.Entered.Task.WaitAsync(Bound);
            switch (i % 4) {
                case 0: pair.Server.Close(); listener.Cancel(); break;
                case 1: listener.Cancel(); pair.Server.Close(); break;
                case 2: pair.Peer.Close(); listener.Cancel(); break;
                default: await listener.DisposeAsync(); pair.Peer.Close(); break;
            }
            await task.WaitAsync(Bound); await Drained(listener, verifyPort: i % 10 == 0);
            Assert.NotEqual("ERROR", Assert.Single(listener.Traces).Outcome);
        }
    }
    [Fact]
    public async Task Q18_RemoteResetDuringRead_NoTaskLeak()
    {
        await using var listener = Start(); using var pair = await Pair.Create();
        using var stream = Install(pair.Server); var task = Track(listener, pair.Server);
        await stream.Entered.Task.WaitAsync(Bound);
        pair.Peer.Client.LingerState = new LingerOption(true, 0); pair.Peer.Close();
        await task.WaitAsync(Bound); await Drained(listener); Trace(Assert.Single(listener.Traces));
        Assert.NotEqual("ERROR", listener.Traces[0].Outcome);
    }
    [Fact]
    public async Task Q21_ObjectDisposedDuringFramedRead_IsExpectedClosure()
    {
        var post = await InjectPostHandshake(new ObjectDisposedException("reviewer framed read"));
        Assert.NotEqual("ERROR", post.Status);
    }
    [Fact]
    public async Task Q22_UnexpectedFramedReadException_RemainsError()
    {
        var post = await InjectPostHandshake(new InvalidOperationException("reviewer framed read failure"));
        Assert.Equal("ERROR", post.Status);
    }
    [Fact]
    public async Task Q23_UnexpectedWrappedFramedSocketFault_MustRemainError()
    {
        var post = await InjectPostHandshake(new IOException("reviewer framed network failure",
            new SocketException((int) SocketError.NetworkDown)));
        Assert.Equal("ERROR", post.Status);
    }
    [Fact]
    public async Task Q24_DisposeDuringActualFramedRead_JoinsHandler()
    {
        await using var listener = Start(observe: true); using var pair = await Pair.Create();
        using var stream = Install(pair.Server, afterHandshake: true); _ = Track(listener, pair.Server);
        await stream.Entered.Task.WaitAsync(Bound); await Drained(listener);
        var trace = Assert.Single(listener.Traces); Trace(trace);
        Assert.NotNull(trace.PostHandshake); Assert.NotEqual("ERROR", trace.PostHandshake.Status);
    }
    private async Task<PostHandshakeObservation> InjectPostHandshake(Exception fault)
    {
        await using var listener = Start(observe: true); using var pair = await Pair.Create();
        using var stream = Install(pair.Server, fault, afterHandshake: true);
        var task = Track(listener, pair.Server); await stream.Entered.Task.WaitAsync(Bound);
        Assert.False(task.IsCompleted); stream.Release.SetResult(); await task.WaitAsync(Bound);
        var trace = Assert.Single(listener.Traces); Trace(trace); Assert.Equal("ACCEPTED", trace.Outcome);
        var post = Assert.IsType<PostHandshakeObservation>(trace.PostHandshake);
        output.WriteLine($"postStatus={post.Status}; postError={post.ErrorType}; injected={fault}");
        await Drained(listener); return post;
    }
    private async Task<HandshakeTrace> Inject(Exception fault)
    {
        await using var listener = Start(); using var pair = await Pair.Create();
        using var stream = Install(pair.Server, fault); var task = Track(listener, pair.Server);
        await stream.Entered.Task.WaitAsync(Bound); Assert.False(task.IsCompleted);
        Assert.False(Field<CancellationTokenSource>(listener, "_shutdown").IsCancellationRequested);
        output.WriteLine($"injected={fault}; serverConnected={pair.Server.Connected}; peerConnected={pair.Peer.Connected}");
        stream.Release.SetResult(); await task.WaitAsync(Bound);
        var trace = Assert.Single(listener.Traces); Trace(trace); await Drained(listener); return trace;
    }
    private static ControlledNetworkStream Install(TcpClient client, Exception? fault = null, bool afterHandshake = false)
    {
        // Runtime-owned field is discovered by type, not an assumed private field name.
        var field = typeof(TcpClient).GetFields(Private).Single(f => f.FieldType == typeof(NetworkStream));
        Assert.Null(field.GetValue(client));
        var stream = new ControlledNetworkStream(client.Client, fault, afterHandshake ? Preamble : null);
        field.SetValue(client, stream); return stream;
    }
#if REVIEWER_HOOKS
    [Fact]
    public async Task Q19_CloseImmediatelyBeforeRealGetStream_IsClosed()
    {
        await using var listener = Start(); using var pair = await Pair.Create(); var calls = 0;
        ReviewerHooks.BeforeGetStream = client => { calls++; client.Close(); };
        try {
            await Handler(listener, pair.Server).WaitAsync(Bound);
            Assert.Equal(1, calls); var trace = Assert.Single(listener.Traces); Trace(trace);
            Assert.Equal("CLOSED", trace.Outcome); Assert.Equal(typeof(ObjectDisposedException).FullName, trace.ErrorType);
        } finally { ReviewerHooks.BeforeGetStream = null; }
        await Drained(listener);
    }
    [Fact]
    public async Task Q20_UnexpectedAcquisitionSocketFault_MustRemainError()
    {
        await using var listener = Start(); using var pair = await Pair.Create();
        ReviewerHooks.BeforeGetStream = _ => throw new SocketException((int) SocketError.NetworkDown);
        try {
            await Handler(listener, pair.Server).WaitAsync(Bound);
            var trace = Assert.Single(listener.Traces); Trace(trace); Assert.Equal("ERROR", trace.Outcome);
        } finally { ReviewerHooks.BeforeGetStream = null; }
    }
#endif
    private sealed class Pair(TcpClient server, TcpClient peer) : IDisposable
    {
        internal TcpClient Server { get; } = server;
        internal TcpClient Peer { get; } = peer;
        internal static async Task<Pair> Create()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using var guard = new CancellationTokenSource(Bound);
            var peer = new TcpClient();
            try {
                await peer.ConnectAsync(IPAddress.Loopback, ((IPEndPoint) listener.LocalEndpoint).Port, guard.Token);
                var server = await listener.AcceptTcpClientAsync(guard.Token); return new Pair(server, peer);
            } catch { peer.Dispose(); throw; } finally { listener.Stop(); }
        }
        public void Dispose() { Server.Dispose(); Peer.Dispose(); }
    }
    private sealed class ControlledNetworkStream(Socket socket, Exception? fault, byte[]? prefix) : NetworkStream(socket, ownsSocket: true)
    {
        // Delegate native I/O to an exact NetworkStream, preserving its socket-async path.
        // Calling base.ReadAsync on a derived NetworkStream can use Stream's sync fallback.
        private readonly NetworkStream _native = new(socket, ownsSocket: false);
        private int _prefixOffset;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (prefix is not null && _prefixOffset < prefix.Length) {
                var count = Math.Min(buffer.Length, prefix.Length - _prefixOffset);
                prefix.AsMemory(_prefixOffset, count).CopyTo(buffer); _prefixOffset += count; return count;
            }
            if (fault is not null) {
                Entered.TrySetResult(); await Release.Task.WaitAsync(token); throw fault;
            }
            var pending = _native.ReadAsync(buffer, token); Entered.TrySetResult(); return await pending;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _native.Dispose();
            base.Dispose(disposing);
        }
    }
}

internal static class ReviewerHooks
{
    internal static Action<TcpClient>? BeforeGetStream { get; set; }
    internal static NetworkStream GetStream(TcpClient client)
    {
        BeforeGetStream?.Invoke(client); return client.GetStream();
    }
}
