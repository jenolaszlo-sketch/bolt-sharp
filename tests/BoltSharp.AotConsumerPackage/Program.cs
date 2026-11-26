using System;
using System.Buffers;
using BoltSharp.PackStream;
using BoltSharp.Framing;
using BoltSharp.Messages;
using BoltSharp.Handshake;

internal class Program
{
    static int Main()
    {
        Console.WriteLine("=== BoltSharp AOT Consumer Test ===");
        Console.WriteLine($"Runtime: {Environment.Version}");

        try
        {
            // Test PackStream
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PackStreamWriter(buffer);
            writer.WriteInteger(42);
            writer.WriteString("hello");
            
            var reader = new PackStreamReader(buffer.WrittenMemory);
            var val1 = reader.ReadValue() as PackStreamInteger;
            var val2 = reader.ReadValue() as PackStreamString;
            
            if (val1?.Value != 42L) throw new Exception("Integer mismatch");
            if (val2?.Value != "hello") throw new Exception("String mismatch");
            
            // Test Message Codec
            var codec = new BoltMessageCodec();
            var hello = new HelloRequest(new PackStreamMap(new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, PackStreamValue?>>
            {
                new("user_agent", new PackStreamString("Test/1.0")),
                new("bolt_agent", new PackStreamMap(new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, PackStreamValue?>>
                {
                    new("product", new PackStreamString("Test"))
                }))
            }));
            
            var payload = new ArrayBufferWriter<byte>();
            var pw = new PackStreamWriter(payload);
            pw.WriteStructure(BoltMessageSignatures.Hello, new PackStreamValue?[] { hello.Extra });
            
            var decoded = codec.DecodeRequest(payload.WrittenMemory);
            if (decoded.Signature != BoltMessageSignatures.Hello) throw new Exception("Decode failed");
            
            // Test Version Range
            var range = new BoltVersionRange(5, 8, 8);
            var v58 = new BoltProtocolVersion(5, 8);
            if (!range.Contains(v58)) throw new Exception("Version range failed");
            
            Console.WriteLine("All tests passed!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: {ex.Message}");
            return 1;
        }
    }
}