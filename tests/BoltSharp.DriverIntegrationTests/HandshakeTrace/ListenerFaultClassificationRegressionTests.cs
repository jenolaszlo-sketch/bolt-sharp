using System.Net;
using System.Net.Sockets;
using System.Reflection;
using BoltSharp.Handshake;
using Xunit;

namespace BoltSharp.DriverIntegrationTests.HandshakeTrace;

/// <summary>
/// B0-A-TRACE-Q2 remediation regression coverage (TRACEQ2-F01 and TRACEQ2-F02).
/// <para>
/// These tests exercise the production listener's real classification boundaries with
/// deterministic injected transport faults. They do not replace the retained independent Q2
/// reviewer probes; they pin the corrected behaviour in the driver project's own suite.
/// </para>
/// </summary>
[Collection("B0-A-TRACE-01")]
public sealed class ListenerFaultClassificationRegressionTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(8);
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly byte[] LegacyPreamble =
        Convert.FromHexString("6060B01700000805000000000000000000000000");

    private static LoopbackHandshakeListener Start(bool observe = false) =>
        LoopbackHandshakeListener.Start(
            new BoltHandshakeOptions(new[] { new BoltProtocolVersion(5, 8) }, manifestV1Enabled: false),
            new LoopbackListenerLimits
            {
                MaxConnections = 1,
                AcceptBacklog = 16,
                ObserveFirstMessage = observe,
                ConnectionLifetime = TimeSpan.FromSeconds(30),
                HandshakeTimeout = TimeSpan.FromSeconds(30),
                FirstMessageObservationTimeout = TimeSpan.FromSeconds(10),
                PostMessageObservationWindow = TimeSpan.FromMilliseconds(100),
            });

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;

    private static Task InvokeHandler(LoopbackHandshakeListener listener, TcpClient client)
    {
        var method = typeof(LoopbackHandshakeListener).GetMethod("HandleConnectionAsync", Private)!;
        return (Task)method.Invoke(listener, new object[] { 1, client, "regression" })!;
    }

    private static Task Track(LoopbackHandshakeListener listener, TcpClient client, Task handler)
    {
        lock (Field<object>(listener, "_gate"))
        {
            Field<List<TcpClient>>(listener, "_clients").Add(client);
            Field<List<Task>>(listener, "_handlers").Add(handler);
        }

        return handler;
    }

    private static ControlledNetworkStream Install(
        TcpClient client,
        Exception? fault = null,
        byte[]? prefix = null,
        bool faultIgnoresCancellation = false)
    {
        // TcpClient stores its NetworkStream in a private field. Replacing it is the same
        // deterministic fault-injection technique used by the retained independent Q2 probes.
        var field = typeof(TcpClient).GetFields(Private).Single(f => f.FieldType == typeof(NetworkStream));
        Assert.Null(field.GetValue(client));
        var stream = new ControlledNetworkStream(
            client.Client,
            fault,
            prefix,
            faultIgnoresCancellation);
        field.SetValue(client, stream);
        return stream;
    }

    private static async Task<PostHandshakeObservation> ObserveFirstMessage(
        LoopbackHandshakeListener listener,
        Stream source)
    {
        using var tracing = new HandshakeTracingStream(source, 4_096, leaveOpen: true);
        using var client = new TcpClient();
        var method = typeof(LoopbackHandshakeListener)
            .GetMethod("ObserveFirstMessageAsync", Private)!;
        var task = (Task<PostHandshakeObservation>)method.Invoke(
            listener,
            new object[] { tracing, client, CancellationToken.None })!;
        return await task.WaitAsync(Bound);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R2")]
    public async Task UnexpectedSocketFault_DuringNegotiation_IsError_AndCleanupRetainsIt()
    {
        await using var listener = Start();
        using var pair = await Pair.Create();
        var stream = Install(
            pair.Server,
            new SocketException((int)SocketError.NetworkDown));

        var handler = InvokeHandler(listener, pair.Server);
        await stream.Entered.Task.WaitAsync(Bound);
        Assert.False(handler.IsCompleted);
        stream.Release.SetResult();
        await handler.WaitAsync(Bound);

        var trace = Assert.Single(listener.Traces);
        Assert.Equal("ERROR", trace.Outcome);
        Assert.Equal(typeof(SocketException).FullName, trace.ErrorType);
        Assert.Contains("dead network", trace.SanitizedError, StringComparison.OrdinalIgnoreCase);

        // Cleanup must not overwrite the causal failure.
        await listener.DisposeAsync().AsTask().WaitAsync(Bound);
        Assert.Same(trace, Assert.Single(listener.Traces));
        Assert.Equal("ERROR", trace.Outcome);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R2")]
    public async Task UnexpectedWrappedSocketFault_DuringNegotiation_IsError()
    {
        await using var listener = Start();
        using var pair = await Pair.Create();
        var stream = Install(
            pair.Server,
            new IOException(
                "regression negotiation network failure",
                new SocketException((int)SocketError.NetworkDown)));

        var handler = InvokeHandler(listener, pair.Server);
        await stream.Entered.Task.WaitAsync(Bound);
        stream.Release.SetResult();
        await handler.WaitAsync(Bound);

        var trace = Assert.Single(listener.Traces);
        Assert.Equal("ERROR", trace.Outcome);
        Assert.Equal(typeof(IOException).FullName, trace.ErrorType);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R2")]
    public async Task UnexpectedWrappedSocketFault_DuringFramedRead_IsError()
    {
        await using var listener = Start(observe: true);
        using var pair = await Pair.Create();
        var stream = Install(
            pair.Server,
            new IOException(
                "regression framed network failure",
                new SocketException((int)SocketError.NetworkDown)),
            prefix: LegacyPreamble);

        var handler = InvokeHandler(listener, pair.Server);
        await stream.Entered.Task.WaitAsync(Bound);
        Assert.False(handler.IsCompleted);
        stream.Release.SetResult();
        await handler.WaitAsync(Bound);

        var trace = Assert.Single(listener.Traces);
        Assert.Equal("ACCEPTED", trace.Outcome);
        var post = Assert.IsType<PostHandshakeObservation>(trace.PostHandshake);
        Assert.Equal("ERROR", post.Status);
        Assert.Equal(typeof(IOException).FullName, post.ErrorType);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R2")]
    public async Task UnexpectedSocketFault_DuringFramedRead_IsError()
    {
        await using var listener = Start(observe: true);
        using var pair = await Pair.Create();
        var stream = Install(
            pair.Server,
            new SocketException((int)SocketError.NoBufferSpaceAvailable),
            prefix: LegacyPreamble);

        var handler = InvokeHandler(listener, pair.Server);
        await stream.Entered.Task.WaitAsync(Bound);
        stream.Release.SetResult();
        await handler.WaitAsync(Bound);

        var post = Assert.IsType<PostHandshakeObservation>(
            Assert.Single(listener.Traces).PostHandshake);
        Assert.Equal("ERROR", post.Status);
        Assert.Equal(typeof(SocketException).FullName, post.ErrorType);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R2")]
    public async Task UnexpectedWrappedSocketFault_DuringAdditionalFramedRead_IsError()
    {
        // Valid tiny-struct message (B1 01) followed by an unexpected network fault.
        var wire = Convert.FromHexString("0002B1010000");
        await using var listener = Start(observe: true);
        var post = await ObserveFirstMessage(
            listener,
            new ScriptedStream(
                wire,
                new IOException(
                    "regression additional framed network failure",
                    new SocketException((int)SocketError.NetworkDown))));

        Assert.Equal("ERROR", post.Status);
        Assert.Equal(typeof(IOException).FullName, post.ErrorType);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R2")]
    public async Task ObjectDisposed_DuringFramedRead_IsLocalClosureNotErrorOrCleanEof()
    {
        await using var listener = Start(observe: true);
        var post = await ObserveFirstMessage(
            listener,
            new ScriptedStream(
                Array.Empty<byte>(),
                new ObjectDisposedException("regression framed read")));

        Assert.Equal("CANCELLED", post.Status);
        Assert.NotEqual("ERROR", post.Status);
        Assert.NotEqual("CLEAN_EOF", post.Status);
        Assert.Equal(typeof(ObjectDisposedException).FullName, post.ErrorType);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R2")]
    public async Task GenuineCleanEof_DuringFramedRead_RemainsCleanEof()
    {
        await using var listener = Start(observe: true);
        var post = await ObserveFirstMessage(
            listener,
            new ScriptedStream(Array.Empty<byte>(), faultAtEnd: null));

        Assert.Equal("CLEAN_EOF", post.Status);
        Assert.Null(post.ErrorType);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R2")]
    public async Task ExpectedPeerReset_DuringFramedRead_RemainsCleanEof()
    {
        await using var listener = Start(observe: true);
        var post = await ObserveFirstMessage(
            listener,
            new ScriptedStream(
                Array.Empty<byte>(),
                new IOException(
                    "regression peer reset",
                    new SocketException((int)SocketError.ConnectionReset))));

        Assert.Equal("CLEAN_EOF", post.Status);
        Assert.Equal(typeof(IOException).FullName, post.ErrorType);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R2")]
    public async Task GenuinelyTruncatedFrame_RemainsTruncatedNotCleanEof()
    {
        // One byte of a two-byte chunk header, then clean end of stream.
        await using var listener = Start(observe: true);
        var post = await ObserveFirstMessage(
            listener,
            new ScriptedStream(new byte[] { 0x00 }, faultAtEnd: null));

        Assert.Equal("TRUNCATED", post.Status);
        Assert.NotEqual("CLEAN_EOF", post.Status);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R2")]
    public async Task ExplicitLocalClose_DuringFramedRead_IsNotError()
    {
        await using var listener = Start(observe: true);
        using var pair = await Pair.Create();
        var stream = Install(pair.Server, prefix: LegacyPreamble);

        var handler = InvokeHandler(listener, pair.Server);
        await stream.Entered.Task.WaitAsync(Bound);
        pair.Server.Close();
        await handler.WaitAsync(Bound);

        var trace = Assert.Single(listener.Traces);
        Assert.Equal("ACCEPTED", trace.Outcome);
        var post = Assert.IsType<PostHandshakeObservation>(trace.PostHandshake);
        Assert.Contains(post.Status, new[] { "CANCELLED", "CLEAN_EOF" });
        Assert.NotEqual("ERROR", post.Status);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R2")]
    public async Task ExpectedDisposalDuringShutdown_DuringFramedRead_IsNotError()
    {
        await using var listener = Start(observe: true);
        using var pair = await Pair.Create();
        var stream = Install(pair.Server, prefix: LegacyPreamble);

        var handler = Track(listener, pair.Server, InvokeHandler(listener, pair.Server));
        await stream.Entered.Task.WaitAsync(Bound);
        await listener.DisposeAsync().AsTask().WaitAsync(Bound);
        await handler.WaitAsync(Bound);

        var trace = Assert.Single(listener.Traces);
        Assert.Equal("ACCEPTED", trace.Outcome);
        var post = Assert.IsType<PostHandshakeObservation>(trace.PostHandshake);
        Assert.Contains(post.Status, new[] { "CANCELLED", "CLEAN_EOF" });
        Assert.NotEqual("ERROR", post.Status);
        Assert.Empty(Field<List<TcpClient>>(listener, "_clients"));
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R2")]
    public async Task ShutdownCompetingWithInjectedTransportFailure_RetainsError()
    {
        await using var listener = Start();
        using var pair = await Pair.Create();
        var stream = Install(
            pair.Server,
            new SocketException((int)SocketError.NetworkDown),
            faultIgnoresCancellation: true);

        var handler = InvokeHandler(listener, pair.Server);
        await stream.Entered.Task.WaitAsync(Bound);

        // Cancellation is requested before the injected transport failure is released. The
        // actual terminal transport event is still an unexpected network fault, so it must not
        // be silently converted into an expected shutdown outcome.
        listener.Cancel();
        stream.Release.SetResult();
        await handler.WaitAsync(Bound);

        var trace = Assert.Single(listener.Traces);
        Assert.Equal("ERROR", trace.Outcome);
        Assert.Equal(typeof(SocketException).FullName, trace.ErrorType);
        Assert.Contains("dead network", trace.SanitizedError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R2")]
    public void AcquisitionClassification_DistinguishesExpectedClosureFromUnexpectedFault()
    {
        var method = typeof(LoopbackHandshakeListener).GetMethod(
            "IsExpectedAcquisitionClosure",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.False((bool)method.Invoke(
            null,
            new object[] { new SocketException((int)SocketError.NetworkDown) })!);
        Assert.False((bool)method.Invoke(
            null,
            new object[] { new SocketException((int)SocketError.NoBufferSpaceAvailable) })!);
        Assert.False((bool)method.Invoke(
            null,
            new object[]
            {
                new IOException(
                    "wrapped network fault",
                    new SocketException((int)SocketError.NetworkUnreachable)),
            })!);
        Assert.True((bool)method.Invoke(
            null,
            new object[] { new SocketException((int)SocketError.ConnectionReset) })!);
        Assert.True((bool)method.Invoke(
            null,
            new object[] { new ObjectDisposedException("already closed client") })!);
    }

    private sealed class Pair(TcpClient server, TcpClient peer) : IDisposable
    {
        internal TcpClient Server { get; } = server;

        internal TcpClient Peer { get; } = peer;

        internal static async Task<Pair> Create()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var guard = new CancellationTokenSource(Bound);
            var peer = new TcpClient();
            try
            {
                await peer.ConnectAsync(
                    IPAddress.Loopback,
                    ((IPEndPoint)listener.LocalEndpoint).Port,
                    guard.Token);
                var server = await listener.AcceptTcpClientAsync(guard.Token);
                return new Pair(server, peer);
            }
            catch
            {
                peer.Dispose();
                throw;
            }
            finally
            {
                listener.Stop();
            }
        }

        public void Dispose()
        {
            Server.Dispose();
            Peer.Dispose();
        }
    }

    /// <summary>
    /// Deterministic stream fault schedule for the handler. A prefix lets the real negotiator
    /// complete; the configured exception is then thrown at the requested read boundary.
    /// </summary>
    private sealed class ControlledNetworkStream : NetworkStream
    {
        private readonly NetworkStream _native;
        private readonly Exception? _fault;
        private readonly byte[]? _prefix;
        private readonly bool _faultIgnoresCancellation;
        private int _prefixOffset;
        private int _faultThrown;

        internal ControlledNetworkStream(
            Socket socket,
            Exception? fault,
            byte[]? prefix,
            bool faultIgnoresCancellation)
            : base(socket, ownsSocket: true)
        {
            _native = new NetworkStream(socket, ownsSocket: false);
            _fault = fault;
            _prefix = prefix;
            _faultIgnoresCancellation = faultIgnoresCancellation;
        }

        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_prefix is not null && _prefixOffset < _prefix.Length)
            {
                var count = Math.Min(buffer.Length, _prefix.Length - _prefixOffset);
                _prefix.AsMemory(_prefixOffset, count).CopyTo(buffer);
                _prefixOffset += count;
                return count;
            }

            if (_fault is not null && Interlocked.Exchange(ref _faultThrown, 1) == 0)
            {
                Entered.TrySetResult();
                if (_faultIgnoresCancellation)
                {
                    await Release.Task.ConfigureAwait(false);
                }
                else
                {
                    await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                throw _fault;
            }

            var pending = _native.ReadAsync(buffer, cancellationToken);
            Entered.TrySetResult();
            return await pending.ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _native.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// A small ordered stream that serves zero or more bytes and then either reports a clean
    /// end of stream or throws a configured fault. It is used to exercise the framed-read
    /// observation boundary without a socket.
    /// </summary>
    private sealed class ScriptedStream(byte[] bytes, Exception? faultAtEnd) : Stream
    {
        private int _offset;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_offset < bytes.Length)
            {
                var copied = Math.Min(count, bytes.Length - _offset);
                Array.Copy(bytes, _offset, buffer, offset, copied);
                _offset += copied;
                return copied;
            }

            if (faultAtEnd is not null)
            {
                throw faultAtEnd;
            }

            return 0;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_offset < bytes.Length)
            {
                var copied = Math.Min(buffer.Length, bytes.Length - _offset);
                bytes.AsMemory(_offset, copied).CopyTo(buffer);
                _offset += copied;
                return ValueTask.FromResult(copied);
            }

            if (faultAtEnd is not null)
            {
                return ValueTask.FromException<int>(faultAtEnd);
            }

            return ValueTask.FromResult(0);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
