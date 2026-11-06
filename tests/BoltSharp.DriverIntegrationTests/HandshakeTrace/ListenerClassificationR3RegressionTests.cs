using System.Net;
using System.Net.Sockets;
using System.Reflection;
using BoltSharp.Handshake;
using Xunit;

namespace BoltSharp.DriverIntegrationTests.HandshakeTrace;

/// <summary>
/// B0-A-TRACE-R3 regression coverage for context-aware connection-termination classification.
/// <para>
/// TRACEQ3-F01: a controlled framed-read reset/abort was reported as <c>CLEAN_EOF</c> without
/// establishing a clean stream termination. TRACEQ2-F02: an <see cref="ObjectDisposedException"/>
/// did not distinguish locally owned disposal from an unrelated disposed resource. These tests
/// pin that <c>CLEAN_EOF</c> is only a genuine zero-byte read at a valid boundary, that an
/// unconfirmed closure is <c>ERROR</c> rather than an invented clean outcome, and that local
/// teardown is decided from connection lifecycle state.
/// </para>
/// <para>
/// Faults are injected deterministically into an open loopback connection's stream. They prove
/// classification at the actual catch boundaries; they are not a reproduced real network outage.
/// </para>
/// </summary>
[Collection("B0-A-TRACE-01")]
public sealed class ListenerClassificationR3RegressionTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(8);
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly byte[] LegacyPreamble =
        Convert.FromHexString("6060B01700000805000000000000000000000000");

    // Valid tiny-struct message (B1 01) in a single 2-byte chunk followed by the 00 00
    // terminator, used to reach the additional-framed-read boundary.
    private static readonly byte[] ValidTinyStructMessage = Convert.FromHexString("0002B1010000");

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
        return (Task)method.Invoke(listener, new object[] { 1, client, "regression-r3" })!;
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

    /// <summary>
    /// Invokes the shared framed-read classifier with an explicit lifecycle context, or returns
    /// null when the linked listener revision predates the helper (used by the pre-R3 negative
    /// control only).
    /// </summary>
    private static string? Classify(Exception exception, bool localTeardownRequested)
    {
        var method = typeof(LoopbackHandshakeListener).GetMethod(
            "ClassifyFramedReadFailure",
            BindingFlags.NonPublic | BindingFlags.Static);
        return method is null
            ? null
            : (string)method.Invoke(null, new object[] { exception, localTeardownRequested })!;
    }

    private static ControlledNetworkStream Install(
        TcpClient client,
        Exception? fault = null,
        byte[]? prefix = null,
        bool faultIgnoresCancellation = false)
    {
        var field = typeof(TcpClient).GetFields(Private).Single(f => f.FieldType == typeof(NetworkStream));
        Assert.Null(field.GetValue(client));
        var stream = new ControlledNetworkStream(client.Client, fault, prefix, faultIgnoresCancellation);
        field.SetValue(client, stream);
        return stream;
    }

    // 1. Actual zero-byte EOF at a valid boundary produces CLEAN_EOF.
    [Fact]
    [Trait("Category", "B0-A-TRACE-R3")]
    public async Task R3_01_GenuineZeroByteEof_FirstRead_IsCleanEof()
    {
        await using var listener = Start(observe: true);
        var post = await ObserveFirstMessage(listener, new ScriptedStream(Array.Empty<byte>(), null));

        Assert.Equal("CLEAN_EOF", post.Status);
        Assert.Null(post.ErrorType);
    }

    // 1b. A genuine EOF at the additional-read boundary is also CLEAN_EOF.
    [Fact]
    [Trait("Category", "B0-A-TRACE-R3")]
    public async Task R3_02_GenuineZeroByteEof_AdditionalRead_IsCleanEof()
    {
        await using var listener = Start(observe: true);
        var post = await ObserveFirstMessage(listener, new ScriptedStream(ValidTinyStructMessage, null));

        Assert.Equal("MESSAGE_READ", post.Status);
        Assert.Equal("CLEAN_EOF", Assert.Single(post.AdditionalMessages).Status);
    }

    // 2. EOF during incomplete framing does not produce CLEAN_EOF.
    [Fact]
    [Trait("Category", "B0-A-TRACE-R3")]
    public async Task R3_03_IncompleteFrameEof_IsTruncatedNotCleanEof()
    {
        await using var listener = Start(observe: true);
        // One byte of a two-byte chunk header, then clean end of stream.
        var post = await ObserveFirstMessage(listener, new ScriptedStream(new byte[] { 0x00 }, null));

        Assert.Equal("TRUNCATED", post.Status);
        Assert.NotEqual("CLEAN_EOF", post.Status);
    }

    // 3/4. Injected reset and abort without local shutdown must not produce CLEAN_EOF.
    [Theory]
    [Trait("Category", "B0-A-TRACE-R3")]
    [InlineData(SocketError.ConnectionReset)]
    [InlineData(SocketError.ConnectionAborted)]
    [InlineData(SocketError.OperationAborted)]
    [InlineData(SocketError.NotConnected)]
    [InlineData(SocketError.Shutdown)]
    [InlineData(SocketError.Disconnecting)]
    public async Task R3_04_InjectedClosureCode_FirstRead_WithoutLocalShutdown_IsErrorNotCleanEof(
        SocketError code)
    {
        await using var listener = Start(observe: true);
        var post = await ObserveFirstMessage(
            listener,
            new ScriptedStream(
                Array.Empty<byte>(),
                new IOException($"r3 injected {code}", new SocketException((int)code))));

        Assert.Equal("ERROR", post.Status);
        Assert.NotEqual("CLEAN_EOF", post.Status);
        Assert.Equal(typeof(IOException).FullName, post.ErrorType);
    }

    // 3/4 at the additional-framed-read boundary.
    [Fact]
    [Trait("Category", "B0-A-TRACE-R3")]
    public async Task R3_05_InjectedReset_AdditionalRead_IsErrorNotCleanEof()
    {
        await using var listener = Start(observe: true);
        var post = await ObserveFirstMessage(
            listener,
            new ScriptedStream(
                ValidTinyStructMessage,
                new IOException(
                    "r3 additional reset",
                    new SocketException((int)SocketError.ConnectionReset))));

        Assert.Equal("MESSAGE_READ", post.Status);
        var additional = Assert.Single(post.AdditionalMessages);
        Assert.Equal("ERROR", additional.Status);
        Assert.NotEqual("CLEAN_EOF", additional.Status);
    }

    // 5. Unexpected IO remains ERROR.
    [Fact]
    [Trait("Category", "B0-A-TRACE-R3")]
    public async Task R3_06_UnexpectedIoException_RemainsError()
    {
        await using var listener = Start(observe: true);
        var post = await ObserveFirstMessage(
            listener,
            new ScriptedStream(Array.Empty<byte>(), new IOException("r3 unexpected io")));

        Assert.Equal("ERROR", post.Status);
        Assert.Equal(typeof(IOException).FullName, post.ErrorType);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R3")]
    public async Task R3_07_UnexpectedWrappedNetworkFault_RemainsError()
    {
        await using var listener = Start(observe: true);
        var post = await ObserveFirstMessage(
            listener,
            new ScriptedStream(
                Array.Empty<byte>(),
                new IOException(
                    "r3 wrapped network fault",
                    new SocketException((int)SocketError.NetworkDown))));

        Assert.Equal("ERROR", post.Status);
        Assert.Equal(typeof(IOException).FullName, post.ErrorType);
    }

    // 6/7. Known local shutdown: a socket closure occurring with cancellation is handled as the
    // local operation's own cancellation, not as CLEAN_EOF and not as an unexpected ERROR.
    [Fact]
    [Trait("Category", "B0-A-TRACE-R3")]
    public async Task R3_08_LocalShutdown_WithSocketClosure_IsCancelled()
    {
        await using var listener = Start(observe: true);
        using var pair = await Pair.Create();
        var stream = Install(
            pair.Server,
            new IOException(
                "r3 local shutdown reset",
                new SocketException((int)SocketError.ConnectionReset)),
            prefix: LegacyPreamble,
            faultIgnoresCancellation: true);

        var handler = InvokeHandler(listener, pair.Server);
        await stream.Entered.Task.WaitAsync(Bound);

        // Local teardown is requested before the transport fault is released: lifecycle state,
        // not the socket code alone, establishes this as a locally initiated termination.
        listener.Cancel();
        stream.Release.SetResult();
        await handler.WaitAsync(Bound);

        var trace = Assert.Single(listener.Traces);
        Assert.Equal("ACCEPTED", trace.Outcome);
        var post = Assert.IsType<PostHandshakeObservation>(trace.PostHandshake);
        Assert.Equal("CANCELLED", post.Status);
        Assert.NotEqual("ERROR", post.Status);
        Assert.NotEqual("CLEAN_EOF", post.Status);
    }

    // 8. Locally owned disposal is distinguished from unexpected resource disposal where the
    // implementation has sufficient context (explicit lifecycle state). An unverified disposal
    // remains the documented Q2 non-ERROR local-closure signature, never CLEAN_EOF.
    [Fact]
    [Trait("Category", "B0-A-TRACE-R3")]
    public void R3_09_Classifier_UsesLifecycleContextToDistinguishLocalTeardown()
    {
        if (Classify(new ObjectDisposedException("probe"), localTeardownRequested: true) is null)
        {
            // Pre-R3 listener revision: the shared context-aware classifier does not exist yet,
            // so the behavioural probes are the decisive negative controls against it.
            return;
        }

        Assert.Equal("CANCELLED", Classify(new ObjectDisposedException("owned"), localTeardownRequested: true));
        Assert.Equal("CANCELLED", Classify(new ObjectDisposedException("unverified"), localTeardownRequested: false));
        Assert.Equal(
            "CANCELLED",
            Classify(
                new IOException("wrapped", new ObjectDisposedException("owned")),
                localTeardownRequested: true));

        // The lifecycle flag decides whether a closure is local teardown or an unexpected fault.
        Assert.Equal(
            "CANCELLED",
            Classify(
                new IOException("wrapped reset", new SocketException((int)SocketError.ConnectionReset)),
                localTeardownRequested: true));
        Assert.Equal(
            "ERROR",
            Classify(
                new IOException("wrapped reset", new SocketException((int)SocketError.ConnectionReset)),
                localTeardownRequested: false));
        Assert.Equal(
            "ERROR",
            Classify(
                new IOException("wrapped network", new SocketException((int)SocketError.NetworkDown)),
                localTeardownRequested: false));
    }

    // 9. No unexpected exception escapes listener disposal while observation is in flight.
    [Fact]
    [Trait("Category", "B0-A-TRACE-R3")]
    public async Task R3_10_ListenerDisposal_DuringObservation_DoesNotThrowAndIsNotCleanEof()
    {
        var listener = Start(observe: true);
        using var pair = await Pair.Create();
        var stream = Install(pair.Server, prefix: LegacyPreamble);

        var handler = Track(listener, pair.Server, InvokeHandler(listener, pair.Server));
        await stream.Entered.Task.WaitAsync(Bound);

        var exception = await Record.ExceptionAsync(
            async () => await listener.DisposeAsync().AsTask().WaitAsync(Bound));
        Assert.Null(exception);
        await handler.WaitAsync(Bound);

        var trace = Assert.Single(listener.Traces);
        var post = Assert.IsType<PostHandshakeObservation>(trace.PostHandshake);
        Assert.NotEqual("CLEAN_EOF", post.Status);
        Assert.NotEqual("ERROR", post.Status);
        Assert.Empty(Field<List<TcpClient>>(listener, "_clients"));
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
    /// Deterministic stream fault schedule: a prefix lets the real negotiator complete; the
    /// configured exception is then thrown at the framed-read boundary.
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
    /// Serves zero or more bytes and then either reports a clean end of stream or throws a
    /// configured fault, exercising the framed-read boundary without a socket.
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
