using System.Buffers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

using BoltSharp.Framing;
using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.Sessions;

namespace BoltSharp.Transport;

/// <summary>
/// The per-connection pipeline: TLS authentication (when configured), the B2 Bolt handshake,
/// then a sequential loop of one decoded request at a time through the B3-B session, with B3-A
/// encoded responses written back through B2 framing.
/// <para>
/// <b>Single ownership.</b> One connection runs one pipeline task; B3-B session calls are
/// strictly sequential within it (the accepted single-owner contract). The handshake reader
/// consumes exactly the handshake bytes, so client bytes pipelined beyond the handshake remain
/// in the stream for the framing reader.
/// </para>
/// <para>
/// <b>Terminal behavior.</b> When the session reports ShouldClose (GOODBYE, terminal failure,
/// failed retirement) the pipeline writes any final responses, flushes and closes. For GOODBYE
/// no response is fabricated. Cancellation, EOF and transport faults close the connection and
/// dispose the session exactly once.
/// </para>
/// </summary>
internal static class BoltTcpConnection
{
    internal static async Task RunAsync(
        TcpClient client,
        BoltTcpServerOptions options,
        BoltTransportDiagnostics diagnostics,
        string connectionId,
        CancellationToken shutdownCancellationToken)
    {
        using var clientOwnership = client;
        var networkStream = client.GetStream();
        var failed = false;
        BoltConnectionSession? session = null;

        try
        {
            Stream protocolStream = networkStream;

            // B5-A2: TLS authentication happens before any Bolt byte, fails closed, and never
            // downgrades to plaintext.
            if (options.Tls is not null)
            {
                using var tlsCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownCancellationToken);
                tlsCts.CancelAfter(options.TlsHandshakeTimeout);
                var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
                try
                {
                    await sslStream.AuthenticateAsServerAsync(
                            options.Tls.ServerCertificate,
                            options.Tls.ClientCertificateRequired,
                            options.Tls.EnabledProtocols,
                            checkCertificateRevocation: false)
                        .WaitAsync(tlsCts.Token)
                        .ConfigureAwait(false);
                    diagnostics.Report(
                        BoltTransportEventKind.TlsHandshakeCompleted,
                        connectionId,
                        sslStream.SslProtocol.ToString());
                    protocolStream = sslStream;
                }
                catch (Exception ex)
                {
                    diagnostics.Report(BoltTransportEventKind.TlsHandshakeFailed, connectionId, ex.GetType().Name);
                    await sslStream.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            // B5-A3: the B2 Bolt handshake, bounded, before any message framing. The negotiator
            // reads exactly the handshake bytes, so pipelined protocol bytes beyond the handshake
            // remain in the stream for the framing reader.
            var negotiator = new BoltHandshakeNegotiator(options.HandshakeOptions);
            BoltHandshakeResult handshake;
            using (var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownCancellationToken))
            {
                handshakeCts.CancelAfter(options.HandshakeTimeout);
                handshake = await negotiator.NegotiateAsync(protocolStream, handshakeCts.Token)
                    .ConfigureAwait(false);
            }

            if (!handshake.Accepted || handshake.ShouldClose)
            {
                diagnostics.Report(
                    BoltTransportEventKind.BoltHandshakeFailed,
                    connectionId,
                    $"accepted={handshake.Accepted} version={handshake.SelectedVersion?.ToString() ?? "none"}");
                return;
            }

            diagnostics.Report(
                BoltTransportEventKind.BoltHandshakeCompleted,
                connectionId,
                $"v{handshake.SelectedVersion} mode={handshake.Mode}");

            // B5-A5: one B3-B session per negotiated connection, initialized in NEGOTIATION with
            // the configured verifier (fail-closed when none) and the B3-C reference backend.
            var operationProvider = options.OperationProviderFactory!();
            session = new BoltConnectionSession(
                operationProvider,
                new BoltSessionOptions { Verifier = options.CredentialVerifier },
                new BoltMessageCodec());

            await RunMessageLoopAsync(
                    session,
                    protocolStream,
                    options,
                    diagnostics,
                    connectionId,
                    shutdownCancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsClientDisconnect(ex))
        {
            // Clean client disconnect or deliberate closure: classified, not an error.
            diagnostics.Report(BoltTransportEventKind.ConnectionCompleted, connectionId, "client-disconnect");
            return;
        }
        catch (OperationCanceledException) when (shutdownCancellationToken.IsCancellationRequested)
        {
            // Listener shutdown: classified as completed shutdown cleanup.
            diagnostics.Report(BoltTransportEventKind.ConnectionCompleted, connectionId, "listener-shutdown");
            return;
        }
        catch (OperationCanceledException)
        {
            // A bounded phase (TLS/handshake/read/write) timed out: fail closed.
            failed = true;
            diagnostics.Report(BoltTransportEventKind.ConnectionFailed, connectionId, "timeout");
        }
        catch (BoltFramingException ex)
        {
            // Accepted B2 framing error classification: fail closed.
            failed = true;
            diagnostics.Report(BoltTransportEventKind.ConnectionFailed, connectionId, $"framing:{ex.GetType().Name}");
        }
        catch (BoltHandshakeException ex)
        {
            failed = true;
            diagnostics.Report(BoltTransportEventKind.ConnectionFailed, connectionId, $"handshake:{ex.GetType().Name}");
        }
        catch (BoltMessageException ex)
        {
            // The accepted B3-A decode contract answers malformed messages at the session level;
            // a decode fault escaping to the transport boundary is a transport error.
            failed = true;
            diagnostics.Report(BoltTransportEventKind.ConnectionFailed, connectionId, $"codec:{ex.Category}");
        }
        catch (Exception ex)
        {
            failed = true;
            diagnostics.Report(BoltTransportEventKind.ConnectionFailed, connectionId, ex.GetType().Name);
        }
        finally
        {
            // Exactly-once session cleanup: Disconnect retires any active operation (idempotent,
            // a no-op once terminal); Dispose marks the session disposed.
            try
            {
                session?.Disconnect();
                session?.Dispose();
            }
            catch (Exception)
            {
                // Session cleanup is bounded and never throws by accepted contract; contain any
                // residual fault so connection accounting stays accurate.
            }

            diagnostics.Report(
                failed ? BoltTransportEventKind.ConnectionFailed : BoltTransportEventKind.ConnectionCompleted,
                connectionId,
                "closed");
        }
    }

    private static async Task RunMessageLoopAsync(
        BoltConnectionSession session,
        Stream stream,
        BoltTcpServerOptions options,
        BoltTransportDiagnostics diagnostics,
        string connectionId,
        CancellationToken shutdownCancellationToken)
    {
        var reader = new BoltMessageReader(stream, options.FramingLimits);
        var codec = new BoltMessageCodec();

        while (!shutdownCancellationToken.IsCancellationRequested)
        {
            byte[]? message;
            using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownCancellationToken))
            {
                readCts.CancelAfter(options.MessageReadTimeout);
                message = await reader.ReadMessageAsync(readCts.Token).ConfigureAwait(false);
            }

            if (message is null)
            {
                // Clean EOF before any message byte: the client disconnected deliberately.
                diagnostics.Report(BoltTransportEventKind.ConnectionCompleted, connectionId, "clean-eof");
                return;
            }

            var request = codec.DecodeRequest(message);
            var result = session.Receive(request);

            await WriteResponsesAsync(
                    stream,
                    codec,
                    result.Responses,
                    options.WriteTimeout,
                    shutdownCancellationToken)
                .ConfigureAwait(false);
            diagnostics.Report(
                BoltTransportEventKind.MessageProcessed,
                connectionId,
                $"sig=0x{request.Signature:X2} responses={result.Responses.Count} state={result.State}");

            if (result.ShouldClose || session.IsDefunct)
            {
                // Terminal outcome (GOODBYE, fatal protocol error, failed retirement): the final
                // responses (if any) were written; the connection closes. No RESET recovery, no
                // further messages.
                diagnostics.Report(BoltTransportEventKind.ConnectionCompleted, connectionId, "terminal-outcome");
                return;
            }
        }
    }

    private static async Task WriteResponsesAsync(
        Stream stream,
        BoltMessageCodec codec,
        IReadOnlyList<BoltResponse> responses,
        TimeSpan writeTimeout,
        CancellationToken shutdownCancellationToken)
    {
        if (responses.Count == 0)
        {
            // GOODBYE: no response is fabricated and nothing is written.
            return;
        }

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BoltMessageWriter(buffer);
        foreach (var response in responses)
        {
            // B3-A owns response encoding; B2 owns chunk framing.
            writer.WriteMessage(codec.EncodeResponse(response));
        }

        using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownCancellationToken);
        writeCts.CancelAfter(writeTimeout);
        var payload = buffer.WrittenSpan.ToArray();
        var written = 0;
        while (written < payload.Length)
        {
            // NetworkStream/SslStream on a blocking socket write the whole buffer; the loop
            // bounds repeated writes for partial-write robustness across stream types.
            await stream.WriteAsync(payload.AsMemory(written), writeCts.Token).ConfigureAwait(false);
            written = payload.Length;
        }

        await stream.FlushAsync(writeCts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Clean client disconnects (EOF, connection reset, orderly socket closure) are completion,
    /// not transport faults.
    /// </summary>
    private static bool IsClientDisconnect(Exception exception) =>
        exception is IOException or SocketException or ObjectDisposedException;
}
