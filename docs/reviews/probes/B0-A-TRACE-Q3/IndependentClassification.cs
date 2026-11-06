using System.Net.Sockets;
using System.Reflection;
using BoltSharp.DriverIntegrationTests.HandshakeTrace;
using BoltSharp.Handshake;
using Xunit;

public class IndependentClassification {
 static readonly TimeSpan Bound = TimeSpan.FromSeconds(8);
 static LoopbackHandshakeListener Start() => LoopbackHandshakeListener.Start(new BoltHandshakeOptions(new[] { new BoltProtocolVersion(5, 8) }, manifestV1Enabled:false), new LoopbackListenerLimits());
 static async Task<PostHandshakeObservation> Observe(Exception? fault, CancellationToken ct = default) {
   await using var listener = Start();
   using var input = new FaultStream(fault);
   using var tracing = new HandshakeTracingStream(input, 4096, leaveOpen:true);
   using var client = new TcpClient();
   var method = typeof(LoopbackHandshakeListener).GetMethod("ObserveFirstMessageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
   return await ((Task<PostHandshakeObservation>)method.Invoke(listener, new object[] { tracing, client, ct })!).WaitAsync(Bound);
 }
 [Theory]
 [InlineData(SocketError.NetworkDown)] [InlineData(SocketError.NetworkUnreachable)] [InlineData(SocketError.NoBufferSpaceAvailable)] [InlineData(SocketError.TimedOut)] [InlineData(SocketError.AccessDenied)]
 public async Task UnexpectedSocketAndWrappedSocketAreErrors(SocketError code) {
   Assert.Equal("ERROR", (await Observe(new SocketException((int)code))).Status);
   Assert.Equal("ERROR", (await Observe(new IOException("wrapper", new SocketException((int)code)))).Status);
 }
 [Theory]
 [InlineData(SocketError.ConnectionReset)] [InlineData(SocketError.ConnectionAborted)] [InlineData(SocketError.OperationAborted)] [InlineData(SocketError.NotConnected)] [InlineData(SocketError.Shutdown)] [InlineData(SocketError.Disconnecting)]
 public async Task ClosureCodesBecomeCleanEofEvenWithoutCancellation(SocketError code) {
   Assert.Equal("CLEAN_EOF", (await Observe(new IOException("wrapper", new SocketException((int)code)))).Status);
 }
 [Fact] public async Task UnexpectedIoAndNestedIoAreErrors() {
   Assert.Equal("ERROR", (await Observe(new IOException("unexpected"))).Status);
   Assert.Equal("ERROR", (await Observe(new IOException("outer", new IOException("inner")))).Status);
 }
 [Fact] public async Task ArbitraryDisposedObjectIsCancelledWithoutToken() {
   var result = await Observe(new ObjectDisposedException("unrelated resource"));
   Assert.Equal("CANCELLED", result.Status);
   Assert.Equal(typeof(ObjectDisposedException).FullName, result.ErrorType);
 }
 [Fact] public async Task GenuineEofRemainsDistinct() => Assert.Equal("CLEAN_EOF", (await Observe(null)).Status);
 [Fact] public async Task SignalledCancellationIsCancelled() {
   using var cts = new CancellationTokenSource(); cts.Cancel();
   Assert.Equal("CANCELLED", (await Observe(new OperationCanceledException(cts.Token), cts.Token)).Status);
 }
 [Fact] public async Task PeerAbortAttemptCanYieldCleanEof() {
   var server = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); server.Start();
   using var peer = new TcpClient();
   try {
     var accept = server.AcceptTcpClientAsync();
     await peer.ConnectAsync(System.Net.IPAddress.Loopback, ((System.Net.IPEndPoint)server.LocalEndpoint).Port);
     using var accepted = await accept.WaitAsync(Bound);
     await using var listener = Start();
     using var tracing = new HandshakeTracingStream(accepted.GetStream(), 4096, leaveOpen:true);
     peer.Client.LingerState = new LingerOption(true, 0); peer.Close();
     var method = typeof(LoopbackHandshakeListener).GetMethod("ObserveFirstMessageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
     var result = await ((Task<PostHandshakeObservation>)method.Invoke(listener, new object[] { tracing, accepted, CancellationToken.None })!).WaitAsync(Bound);
     Assert.Equal("CLEAN_EOF", result.Status);
     Assert.Null(result.ErrorType);
   } finally { server.Stop(); }
 }
 sealed class FaultStream(Exception? fault) : Stream {
   public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
   public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
   public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => fault is null ? ValueTask.FromResult(0) : ValueTask.FromException<int>(fault);
   public override int Read(byte[] b,int o,int c) => throw new NotSupportedException(); public override void Flush() {} public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException(); public override void SetLength(long v)=>throw new NotSupportedException(); public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
 }
}


