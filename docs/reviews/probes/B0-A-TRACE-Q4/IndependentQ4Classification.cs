using System.Net;
using System.Net.Sockets;
using System.Reflection;
using BoltSharp.Handshake;
using Xunit;

namespace BoltSharp.DriverIntegrationTests.HandshakeTrace;

/// <summary>
/// B0-A-TRACE-Q4 independent reviewer probes for connection-termination classification.
/// <para>
/// These probes are authored by the Q4 reviewer and are deliberately independent of the producer
/// R3 regression source. They target the paths the producer suite covers least directly: the
/// additional-framed-read teardown boundary, a subsequent disposal that must not overwrite an
/// earlier unexpected failure, the real acquisition/negotiation/observation handler path, and the
/// disposal-origin case. Faults are injected deterministically into the framed read; no sleep-based
/// racing is used to establish a phase.
/// </para>
/// </summary>
[Collection("B0-A-TRACE-01")]
public sealed class IndependentQ4Classification
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(8);
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly byte[] LegacyPreamble =
        Convert.FromHexString("6060B01700000805000000000000000000000000");

    // Valid tiny-struct message (B1 01) in a single 2-byte chunk followed by the 00 00 terminator.
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
        return (Task)method.Invoke(listener, new object[] { 1, client, "q4-reviewer" })!;
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

    private static ControlledNetworkStream Install(
        TcpClient client,
        Exception? fault,
        byte[]? prefix,
        bool faultIgnoresCancellation)
    {
        var field = typeof(TcpClient).GetFields(Private).Single(f => f.FieldType == typeof(NetworkStream));
        Assert.Null(field.GetValue(client));
        var stream = new ControlledNetworkStream(client.Client, fault, prefix, faultIgnoresCancellation);
        field.SetValue(client, stream);
        return stream;
    }

    private static readonly SocketError[] UnexpectedFaults =
    {
        SocketError.NetworkDown,
        SocketError.NetworkUnreachable,
        SocketError.NoBufferSpaceAvailable,
        SocketError.AccessDenied,
        SocketError.TimedOut,
    };

    private static readonly SocketError[] ClosureCodes =
    {
        SocketError.ConnectionReset,
        SocketError.ConnectionAborted,
        SocketError.OperationAborted,
        SocketError.NotConnected,
        SocketError.Shutdown,
        SocketError.Disconnecting,
    };

    // Path 4 + 5: unexpected socket faults (direct and IO-wrapped) at the framed read are ERROR,
    // never a clean/invented termination.
    [Theory]
    [Trait("Category", "B0-A-TRACE-Q4")]
    [MemberData(nameof(UnexpectedFaultMemberData))]
    public async Task P01_UnexpectedFault_FirstRead_IsErrorNotClean(SocketError code, bool wrapped)
    {
        await using var listener = Start(observe: true);
        var socket = new SocketException((int)code);
        Exception fault = wrapped ? new IOException($"q4 wrapped {code}", socket) : socket;
        var post = await ObserveFirstMessage(listener, new ScriptedStream(Array.Empty<byte>(), fault));

        Assert.Equal("ERROR", post.Status);
        Assert.NotEqual("CLEAN_EOF", post.Status);
        Assert.NotEqual("CLOSED", post.Status);
        Assert.NotEqual("CANCELLED", post.Status);
    }

    public static IEnumerable<object[]> UnexpectedFaultMemberData()
    {
        foreach (var code in UnexpectedFaults)
        {
            yield return new object[] { code, false };
            yield return new object[] { code, true };
        }
    }

    // Path 4: the six closure codes, with no established local teardown, are ERROR (the
    // TRACEQ3-F01 counterexample); none may become CLEAN_EOF.
    [Theory]
    [Trait("Category", "B0-A-TRACE-Q4")]
    [InlineData(SocketError.ConnectionReset)]
    [InlineData(SocketError.ConnectionAborted)]
    [InlineData(SocketError.OperationAborted)]
    [InlineData(SocketError.NotConnected)]
    [InlineData(SocketError.Shutdown)]
    [InlineData(SocketError.Disconnecting)]
    public async Task P02_ClosureCode_WithoutTeardown_IsErrorNotCleanEof(SocketError code)
    {
        await using var listener = Start(observe: true);
        var post = await ObserveFirstMessage(
            listener,
            new ScriptedStream(
                Array.Empty<byte>(),
                new IOException($"q4 injected {code}", new SocketException((int)code))));

        Assert.Equal("ERROR", post.Status);
        Assert.NotEqual("CLEAN_EOF", post.Status);
    }

    // Path 2 + 9: the same six closure codes, with local teardown established from lifecycle state
    // (listener shutdown requested), are the local operation's own cancellation, not an ERROR and
    // not a clean EOF.
    [Theory]
    [Trait("Category", "B0-A-TRACE-Q4")]
    [InlineData(SocketError.ConnectionReset)]
    [InlineData(SocketError.ConnectionAborted)]
    [InlineData(SocketError.OperationAborted)]
    [InlineData(SocketError.NotConnected)]
    [InlineData(SocketError.Shutdown)]
    [InlineData(SocketError.Disconnecting)]
    public async Task P03_ClosureCode_WithLocalTeardown_IsCancelled(SocketError code)
    {
        await using var listener = Start(observe: true);
        listener.Cancel();
        var post = await ObserveFirstMessage(
            listener,
            new ScriptedStream(
                Array.Empty<byte>(),
                new IOException($"q4 teardown {code}", new SocketException((int)code))));

        Assert.Equal("CANCELLED", post.Status);
        Assert.NotEqual("ERROR", post.Status);
        Assert.NotEqual("CLEAN_EOF", post.Status);
    }

    // Path 4 (additional boundary) + 9: a closure code during the additional framed read, with
    // local teardown established, is CANCELLED on the additional message and must not be recorded
    // as CLEAN_EOF or overwrite the observation with an ERROR.
    [Fact]
    [Trait("Category", "B0-A-TRACE-Q4")]
    public async Task P04_AdditionalRead_ClosureCode_WithTeardown_IsCancelled()
    {
        await using var listener = Start(observe: true);
        listener.Cancel();
        var post = await ObserveFirstMessage(
            listener,
            new ScriptedStream(
                ValidTinyStructMessage,
                new IOException(
                    "q4 additional teardown reset",
                    new SocketException((int)SocketError.ConnectionReset))));

        Assert.Equal("MESSAGE_READ", post.Status);
        var additional = Assert.Single(post.AdditionalMessages);
        Assert.Equal("CANCELLED", additional.Status);
        Assert.NotEqual("CLEAN_EOF", additional.Status);
        Assert.NotEqual("ERROR", additional.Status);
    }

    // Path 4 (additional boundary): an unexpected wrapped network fault during the additional read
    // without local teardown remains observable as ERROR.
    [Fact]
    [Trait("Category", "B0-A-TRACE-Q4")]
    public async Task P05_AdditionalRead_UnexpectedFault_IsError()
    {
        await using var listener = Start(observe: true);
        var post = await ObserveFirstMessage(
            listener,
            new ScriptedStream(
                ValidTinyStructMessage,
                new IOException(
                    "q4 additional unexpected",
                    new SocketException((int)SocketError.NetworkDown))));

        Assert.Equal("ERROR", post.Status);
        Assert.NotEqual("CLEAN_EOF", post.Status);
        Assert.Equal(typeof(IOException).FullName, post.ErrorType);
    }

    // Path 6: framed-read disposal is the documented non-clean, non-error local/stream closure
    // signature (CANCELLED), and is never CLEAN_EOF.
    [Fact]
    [Trait("Category", "B0-A-TRACE-Q4")]
    public async Task P06_FramedReadDisposal_IsCancelledNotCleanEofOrError()
    {
        await using var listener = Start(observe: true);
        var direct = await ObserveFirstMessage(
            listener, new ScriptedStream(Array.Empty<byte>(), new ObjectDisposedException("q4 direct")));
        Assert.Equal("CANCELLED", direct.Status);
        Assert.NotEqual("CLEAN_EOF", direct.Status);
        Assert.NotEqual("ERROR", direct.Status);

        var wrapped = await ObserveFirstMessage(
            listener,
            new ScriptedStream(
                Array.Empty<byte>(),
                new IOException("q4 wrapped ode", new ObjectDisposedException("q4 inner"))));
        Assert.Equal("CANCELLED", wrapped.Status);
        Assert.NotEqual("CLEAN_EOF", wrapped.Status);
        Assert.NotEqual("ERROR", wrapped.Status);
    }

    // Path 1 + 7: a genuine zero-byte read is CLEAN_EOF, while EOF inside an incomplete frame is
    // TRUNCATED; the two remain distinct.
    [Fact]
    [Trait("Category", "B0-A-TRACE-Q4")]
    public async Task P07_GenuineEofAndTruncation_RemainDistinct()
    {
        await using var listener = Start(observe: true);
        var eof = await ObserveFirstMessage(
            listener, new ScriptedStream(Array.Empty<byte>(), null));
        Assert.Equal("CLEAN_EOF", eof.Status);
        Assert.Null(eof.ErrorType);

        var truncated = await ObserveFirstMessage(
            listener, new ScriptedStream(new byte[] { 0x00 }, null));
        Assert.Equal("TRUNCATED", truncated.Status);
        Assert.NotEqual("CLEAN_EOF", truncated.Status);
    }

    // Path 8 + 9: through the real acquisition/negotiation/observation handler path, an unexpected
    // transport failure at the framed read is ERROR, and a later listener disposal must not
    // overwrite that terminal classification.
    [Fact]
    [Trait("Category", "B0-A-TRACE-Q4")]
    public async Task P08_RealHandler_UnexpectedFramedFault_NotOverwrittenByDisposal()
    {
        var listener = Start(observe: true);
        using var pair = await Pair.Create();
        var stream = Install(
            pair.Server,
            new SocketException((int)SocketError.NetworkDown),
            prefix: LegacyPreamble,
            faultIgnoresCancellation: true);

        var handler = Track(listener, pair.Server, InvokeHandler(listener, pair.Server));
        await stream.Entered.Task.WaitAsync(Bound);
        stream.Release.SetResult();
        await handler.WaitAsync(Bound);

        var trace = Assert.Single(listener.Traces);
        Assert.Equal("ACCEPTED", trace.Outcome);
        var post = Assert.IsType<PostHandshakeObservation>(trace.PostHandshake);
        Assert.Equal("ERROR", post.Status);
        Assert.Equal(typeof(SocketException).FullName, post.ErrorType);

        // Cleanup must not conceal the earlier causal failure.
        await listener.DisposeAsync().AsTask().WaitAsync(Bound);
        var after = Assert.Single(listener.Traces);
        Assert.Same(trace, after);
        Assert.Equal("ERROR", ((PostHandshakeObservation)after.PostHandshake!).Status);
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
                    IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, guard.Token);
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
    /// Deterministic fault schedule over a real loopback socket: a prefix lets the real negotiator
    /// complete; the configured exception is thrown at the framed-read boundary.
    /// </summary>
    private sealed class ControlledNetworkStream : NetworkStream
    {
        private readonly NetworkStream _native;
        private readonly Exception? _fault;
        private readonly byte[]? _prefix;
        private readonly bool _faultIgnoresCancellation;
        private int _prefixOffset;
        private int _faultThrown;

        internal ControlledNetworkStream(Socket socket, Exception? fault, byte[]? prefix, bool ignoreCancellation)
            : base(socket, ownsSocket: true)
        {
            _native = new NetworkStream(socket, ownsSocket: false);
            _fault = fault;
            _prefix = prefix;
            _faultIgnoresCancellation = ignoreCancellation;
        }

        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
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

    /// <summary>Serves bytes then either reports a clean EOF or throws a configured fault.</summary>
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

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
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

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
