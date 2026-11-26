using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using BoltSharp.Framing;
using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Transport;

namespace BoltSharp.AotHarness;

internal sealed class Harness
{
    private int _exitCode = 0;

    public int Run()
    {
        Console.WriteLine("=== BoltSharp Native AOT Harness ===");
        Console.WriteLine($"Runtime: {Environment.Version}");
        Console.WriteLine($"OS: {Environment.OSVersion}");
        Console.WriteLine();

        RunTest("PackStream Encoding/Decoding", TestPackStream);
        RunTest("Bolt Version Negotiation", TestVersionNegotiation);
        RunTest("Message Codec (Encode/Decode)", TestMessageCodec);
        RunTest("Invalid Input Handling", TestInvalidInput);
        RunTestAsync("Server Startup/Shutdown (Plaintext)", TestServerLifecyclePlaintext);
        RunTestAsync("Server Startup/Shutdown (TLS)", TestServerLifecycleTls);
        RunTestAsync("Server Cancellation", TestServerCancellation);
        RunTest("Public API Surface", TestPublicApi);
        RunTestAsync("Diagnostic Event Delivery", TestDiagnosticEventDelivery);

        Console.WriteLine();
        Console.WriteLine($"=== Harness Complete: Exit Code {_exitCode} ===");
        return _exitCode;
    }

    private void RunTest(string name, Action test)
    {
        Console.Write($"[{name}] ");
        try
        {
            test();
            Console.WriteLine("PASS");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: {ex.Message}");
            _exitCode = 1;
        }
    }

    private void RunTestAsync(string name, Func<Task> test)
    {
        Console.Write($"[{name}] ");
        try
        {
            test().GetAwaiter().GetResult();
            Console.WriteLine("PASS");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: {ex.Message}");
            _exitCode = 1;
        }
    }

    private void TestPackStream()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);

        // Write various PackStream types
        writer.WriteNull();
        writer.WriteBoolean(true);
        writer.WriteBoolean(false);
        writer.WriteInteger(42);
        writer.WriteInteger(-1);
        writer.WriteInteger(long.MaxValue);
        writer.WriteFloat(3.14);
        writer.WriteFloat(-2.71);
        writer.WriteString("hello");
        writer.WriteString("");
        writer.WriteBytes(new byte[] { 1, 2, 3 });

        var listItems = new List<PackStreamValue?>
        {
            new PackStreamInteger(1),
            new PackStreamInteger(2),
            new PackStreamInteger(3)
        };
        writer.WriteList(listItems);

        var mapEntries = new List<KeyValuePair<string, PackStreamValue?>>
        {
            new("key1", new PackStreamInteger(1)),
            new("key2", new PackStreamInteger(2))
        };
        writer.WriteMap(mapEntries);

        // Read back using ReadValue
        var reader = new PackStreamReader(buffer.WrittenMemory);
        var nullVal = reader.ReadValue();
        AssertNull(nullVal);

        var boolTrue = reader.ReadValue() as PackStreamBoolean;
        AssertNotNull(boolTrue);
        AssertTrue(boolTrue.Value);

        var boolFalse = reader.ReadValue() as PackStreamBoolean;
        AssertNotNull(boolFalse);
        AssertFalse(boolFalse.Value);

        var int1 = reader.ReadValue() as PackStreamInteger;
        AssertNotNull(int1);
        AssertEqual(int1.Value, 42L);

        var int2 = reader.ReadValue() as PackStreamInteger;
        AssertNotNull(int2);
        AssertEqual(int2.Value, -1L);

        var int3 = reader.ReadValue() as PackStreamInteger;
        AssertNotNull(int3);
        AssertEqual(int3.Value, long.MaxValue);

        var float1 = reader.ReadValue() as PackStreamFloat;
        AssertNotNull(float1);
        AssertApprox(float1.Value, 3.14);

        var float2 = reader.ReadValue() as PackStreamFloat;
        AssertNotNull(float2);
        AssertApprox(float2.Value, -2.71);

        var str1 = reader.ReadValue() as PackStreamString;
        AssertNotNull(str1);
        AssertEqual(str1.Value, "hello");

        var str2 = reader.ReadValue() as PackStreamString;
        AssertNotNull(str2);
        AssertEqual(str2.Value, "");

        var bytesVal = reader.ReadValue() as PackStreamBytes;
        AssertNotNull(bytesVal);
        var bytes = bytesVal.Value;
        AssertEqual(bytes.Length, 3, "bytes.Length");
        AssertEqual(bytes.Span[0], (byte)1, "bytes.Span[0]");

        var list = reader.ReadValue() as PackStreamList;
        AssertNotNull(list);
        AssertEqual(list.Items.Count, 3, "list.Items.Count");
        var item0 = list.Items[0] as PackStreamInteger;
        AssertNotNull(item0);
        AssertEqual(item0.Value, 1L, "item0.Value");

        var map = reader.ReadValue() as PackStreamMap;
        AssertNotNull(map);
        AssertEqual(map.Entries.Count, 2, "map.Entries.Count");
        var mapVal1 = map.Entries[0].Value as PackStreamInteger;
        var mapVal2 = map.Entries[1].Value as PackStreamInteger;
        AssertNotNull(mapVal1);
        AssertNotNull(mapVal2);
        AssertEqual(mapVal1.Value, 1L, "mapVal1.Value");
        AssertEqual(mapVal2.Value, 2L, "mapVal2.Value");
    }

    private void TestVersionNegotiation()
    {
        var versions = new[]
        {
            new BoltProtocolVersion(5, 0),
            new BoltProtocolVersion(5, 1),
            new BoltProtocolVersion(5, 2),
            new BoltProtocolVersion(5, 3),
            new BoltProtocolVersion(5, 4),
            new BoltProtocolVersion(5, 5),
            new BoltProtocolVersion(5, 6),
            new BoltProtocolVersion(5, 7),
            new BoltProtocolVersion(5, 8),
        };

        var range = new BoltVersionRange(5, 8, 8); // 5.8 down to 5.0
        foreach (var v in versions)
        {
            AssertTrue(range.Contains(v));
        }

        AssertFalse(range.Contains(new BoltProtocolVersion(4, 0)));
        AssertFalse(range.Contains(new BoltProtocolVersion(6, 0)));
    }

    private void TestMessageCodec()
    {
        var codec = new BoltMessageCodec();

        // Test encoding a HELLO request
        var extraEntries = new List<KeyValuePair<string, PackStreamValue?>>
        {
            new("user_agent", new PackStreamString("BoltSharp.AotHarness/1.0")),
            new("bolt_agent", new PackStreamMap(new List<KeyValuePair<string, PackStreamValue?>>
            {
                new("product", new PackStreamString("BoltSharp"))
            }))
        };
        var hello = new HelloRequest(new PackStreamMap(extraEntries));

        // We need to encode using the codec - but there's no EncodeRequest. Let's test decoding.
        // Create a HELLO message manually using PackStreamWriter
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        writer.WriteStructure(BoltMessageSignatures.Hello, new PackStreamValue?[]
        {
            new PackStreamMap(extraEntries)
        });

        var payload = buffer.WrittenMemory;

        // Decode it back
        var decoded = codec.DecodeRequest(payload);
        AssertEqual(decoded.Signature, BoltMessageSignatures.Hello);
        var helloDecoded = decoded as HelloRequest;
        AssertNotNull(helloDecoded);
        AssertEqual(helloDecoded.UserAgent, "BoltSharp.AotHarness/1.0");

        // Test encoding a SUCCESS response
        var successMetadata = new List<KeyValuePair<string, PackStreamValue?>>
        {
            new("fields", new PackStreamList(new List<PackStreamValue?>
            {
                new PackStreamString("test")
            }))
        };
        var success = new SuccessResponse(new PackStreamMap(successMetadata));
        var encoded = codec.EncodeResponse(success);
        AssertTrue(encoded.Length > 0);

        // Decode it back
        var decodedSuccess = codec.DecodeResponse(encoded);
        AssertEqual(decodedSuccess.Signature, BoltMessageSignatures.Success);
    }

    private void TestInvalidInput()
    {
        // Test PackStream reader with invalid marker (0xC4 is not a defined marker)
        var invalidData = new byte[] { 0xC4 };
        var reader = new PackStreamReader(invalidData);
        AssertThrows<PackStreamException>(() => reader.ReadValue());

        // Test truncated data - STRING8 marker but no length bytes
        var truncated = new byte[] { 0xD0 }; // String8 marker requires 1 byte length
        reader = new PackStreamReader(truncated);
        AssertThrows<PackStreamException>(() => reader.ReadValue());

        // Test codec with invalid payload (empty)
        var codec = new BoltMessageCodec();
        AssertThrows<BoltMessageException>(() => codec.DecodeRequest(new byte[] { }));
    }

    private async Task TestServerLifecyclePlaintext()
    {
        var options = new BoltTcpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            MaxConnections = 10,
            HandshakeTimeout = TimeSpan.FromSeconds(5),
            MessageReadTimeout = TimeSpan.FromSeconds(5),
            WriteTimeout = TimeSpan.FromSeconds(5),
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        };

        var server = BoltTcpServer.Create(options);
        await server.StartAsync();
        AssertNotNull(server.BoundEndPoint);
        AssertTrue(server.IsRunning);

        // Stop should work cleanly
        await server.StopAsync();
        AssertEqual(server.ShutdownOutcome, BoltShutdownOutcome.Completed);
        AssertFalse(server.IsRunning);

        // Double-stop should be safe
        await server.StopAsync();
    }

    private async Task TestServerLifecycleTls()
    {
        // Create a self-signed certificate for testing
        using var cert = CreateSelfSignedCertificate();

        var options = new BoltTcpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            MaxConnections = 10,
            HandshakeTimeout = TimeSpan.FromSeconds(5),
            TlsHandshakeTimeout = TimeSpan.FromSeconds(10),
            MessageReadTimeout = TimeSpan.FromSeconds(5),
            WriteTimeout = TimeSpan.FromSeconds(5),
            ShutdownTimeout = TimeSpan.FromSeconds(5),
            Tls = new BoltTlsServerOptions(cert)
            {
                ClientCertificateRequired = false,
                // SslProtocols.Tls12 = 3072, SslProtocols.Tls13 = 12288 (from System.Net.Security)
EnabledProtocols = (System.Security.Authentication.SslProtocols)3072 | (System.Security.Authentication.SslProtocols)12288,
            },
        };

        var server = BoltTcpServer.Create(options);
        await server.StartAsync();
        AssertNotNull(server.BoundEndPoint);
        AssertTrue(server.IsRunning);

        await server.StopAsync();
        AssertEqual(server.ShutdownOutcome, BoltShutdownOutcome.Completed);
    }

    private async Task TestServerCancellation()
    {
        var options = new BoltTcpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            MaxConnections = 10,
            HandshakeTimeout = TimeSpan.FromSeconds(5),
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        };

        var server = BoltTcpServer.Create(options);
        await server.StartAsync();

        // Test that StopAsync completes successfully
        await server.StopAsync();
        AssertEqual(server.ShutdownOutcome, BoltShutdownOutcome.Completed);

        // Test idempotency
        await server.StopAsync();
        AssertEqual(server.ShutdownOutcome, BoltShutdownOutcome.Completed);

        // Test cancellation of StopAsync wait (shutdown already complete, should not throw)
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await server.StopAsync(cts.Token); // Should complete, not throw
    }

    private void TestPublicApi()
    {
        // Verify key public types are accessible
        var t1 = typeof(BoltTcpServer);
        var t2 = typeof(BoltTcpServerOptions);
        var t3 = typeof(BoltTlsServerOptions);
        var t4 = typeof(BoltVersionRange);
        var t5 = typeof(BoltProtocolVersion);
        var t6 = typeof(PackStreamValue);
        var t7 = typeof(PackStreamWriter);
        var t7b = typeof(PackStreamReader);
        var t8 = typeof(BoltMessageWriter);
        var t9 = typeof(BoltMessageReader);
        var t10 = typeof(BoltHandshakeNegotiator);
        var t11 = typeof(BoltSessionOptions);
        var t12 = typeof(BoltConnectionSession);
        var t13 = typeof(HelloRequest);
        var t14 = typeof(GoodbyeRequest);
        var t15 = typeof(LogonRequest);
        var t16 = typeof(BoltMessageSignatures);
        var t17 = typeof(BoltShutdownOutcome);
        var t18 = typeof(BoltTransportEventKind);
        var t19 = typeof(BoltMessageCodec);
        var t20 = typeof(SuccessResponse);
        var t21 = typeof(RecordResponse);
        var t22 = typeof(FailureResponse);
        var t23 = typeof(BoltAuthScheme);
        var t24 = typeof(BoltMessageErrorCategory);
        var t25 = typeof(BoltFramingLimits);
        var t26 = typeof(PackStreamLimits);
        var t27 = typeof(BoltHandshakeOptions);
        var t28 = typeof(BoltHandshakeResult);

        // Verify they're all non-null
        AssertNotNull(t1); AssertNotNull(t2); AssertNotNull(t3); AssertNotNull(t4);
        AssertNotNull(t5); AssertNotNull(t6); AssertNotNull(t7); AssertNotNull(t7b);
        AssertNotNull(t8); AssertNotNull(t9); AssertNotNull(t10); AssertNotNull(t11);
        AssertNotNull(t12); AssertNotNull(t13); AssertNotNull(t14); AssertNotNull(t15);
        AssertNotNull(t16); AssertNotNull(t17); AssertNotNull(t18); AssertNotNull(t19);
        AssertNotNull(t20); AssertNotNull(t21); AssertNotNull(t22); AssertNotNull(t23);
        AssertNotNull(t24); AssertNotNull(t25); AssertNotNull(t26); AssertNotNull(t27);
        AssertNotNull(t28);
    }

    private async Task TestDiagnosticEventDelivery()
    {
        // Verify that diagnostic events are delivered correctly under Native AOT
        var options = new BoltTcpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            MaxConnections = 10,
            HandshakeTimeout = TimeSpan.FromSeconds(5),
            MessageReadTimeout = TimeSpan.FromSeconds(5),
            WriteTimeout = TimeSpan.FromSeconds(5),
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        };

        var server = BoltTcpServer.Create(options);
        var eventReceived = false;
        var eventConnectionId = "";
        var eventDetail = "";

        // Subscribe to diagnostic events
        server.Diagnostics.Event += evt =>
        {
            if (evt.Kind == BoltTransportEventKind.ListenerStarted ||
                evt.Kind == BoltTransportEventKind.ListenerStopped)
            {
                eventReceived = true;
                eventConnectionId = evt.ConnectionId;
                eventDetail = evt.Detail ?? "";
            }
        };

        await server.StartAsync();
        AssertNotNull(server.BoundEndPoint);

        // Stop the server - this should trigger listener stopped diagnostic
        await server.StopAsync();
        AssertEqual(server.ShutdownOutcome, BoltShutdownOutcome.Completed);

        // Verify diagnostic event was delivered
        AssertTrue(eventReceived);
        AssertNotNull(eventConnectionId);
    }

    private X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            new X500DistinguishedName("CN=localhost"),
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddHours(1));

#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(
            cert.Export(X509ContentType.Pfx), password: null, keyStorageFlags: X509KeyStorageFlags.EphemeralKeySet);
#else
        return new X509Certificate2(cert.Export(X509ContentType.Pfx), "", X509KeyStorageFlags.EphemeralKeySet);
#endif
    }

    private static void AssertEqual(object actual, object expected, string context = "")
    {
        if (!Equals(actual, expected))
            throw new Exception($"Expected {expected}, got {actual}" + (context != "" ? $" ({context})" : ""));
    }

    private static void AssertNull(object? obj)
    {
        if (obj is not null) throw new Exception("Expected null");
    }

    private static void AssertTrue(bool condition)
    {
        if (!condition) throw new Exception("Expected true");
    }

    private static void AssertFalse(bool condition)
    {
        if (condition) throw new Exception("Expected false");
    }

    private static void AssertNotNull(object? obj)
    {
        if (obj is null) throw new Exception("Expected non-null");
    }

    private static void AssertApprox(double actual, double expected, double epsilon = 0.001)
    {
        if (Math.Abs(actual - expected) > epsilon)
            throw new Exception($"Expected ~{expected}, got {actual}");
    }

    private static void AssertThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
            throw new Exception($"Expected {typeof(T).Name}");
        }
        catch (T) { }
    }

    private static async Task AssertThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
            throw new Exception($"Expected {typeof(T).Name}");
        }
        catch (T) { }
    }
}

internal class Program
{
    static int Main()
    {
        var harness = new Harness();
        return harness.Run();
    }
}