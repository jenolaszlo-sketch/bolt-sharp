using System.Buffers.Binary;
using System.Text;

namespace BoltSharp.IntegrationTests.Infrastructure;

/// <summary>
/// Independent PackStream/Bolt byte fixtures for B0-B-DRV-INT-01.
/// <para>
/// These encoders are written directly from the published Bolt 5.8 / PackStream 1.0 specification
/// (structure semantics, message signatures and marker byte ranges) and deliberately do <b>not</b>
/// call BoltSharp's <c>PackStreamWriter</c> or <c>BoltMessageWriter</c>. They are therefore an
/// independent byte oracle: a shared serializer mistake cannot make both the fake responder and
/// BoltSharp appear correct at once.
/// </para>
/// <para>
/// Only the subset of PackStream needed for the controlled scenarios is implemented: null, boolean,
/// signed integers, strings, lists, maps and tiny structures (including Node and Relationship).
/// </para>
/// </summary>
internal static class ProtocolFixtures
{
    // ---- PackStream 1.0 markers (spec-sourced) ----
    public const byte Null = 0xC0;
    public const byte False = 0xC2;
    public const byte True = 0xC3;
    public const byte Int8 = 0xC8;
    public const byte Int16 = 0xC9;
    public const byte Int32 = 0xCA;
    public const byte Int64 = 0xCB;
    public const byte String8 = 0xD0;
    public const byte String16 = 0xD1;
    public const byte String32 = 0xD2;
    public const byte List8 = 0xD4;
    public const byte List16 = 0xD5;
    public const byte Map8 = 0xD8;
    public const byte Map16 = 0xD9;

    // ---- Bolt message signatures (request) ----
    public const byte MsgHello = 0x01;
    public const byte MsgGoodbye = 0x02;
    public const byte MsgReset = 0x0F;
    public const byte MsgRun = 0x10;
    public const byte MsgBegin = 0x11;
    public const byte MsgCommit = 0x12;
    public const byte MsgRollback = 0x13;
    public const byte MsgDiscard = 0x2F;
    public const byte MsgPull = 0x3F;
    public const byte MsgTelemetry = 0x54;
    public const byte MsgRoute = 0x66;
    public const byte MsgLogon = 0x6A;
    public const byte MsgLogoff = 0x6B;

    // ---- Bolt message signatures (response) ----
    public const byte MsgSuccess = 0x70;
    public const byte MsgRecord = 0x71;
    public const byte MsgIgnored = 0x7E;
    public const byte MsgFailure = 0x7F;

    // ---- Graph structure tags ----
    public const byte NodeTag = 0x4E;
    public const byte RelationshipTag = 0x52;

    // ---- Scalar encoders ----

    public static byte[] NullValue() => new[] { Null };

    public static byte[] Bool(bool value) => new[] { value ? True : False };

    public static byte[] Int(long value)
    {
        if (value is >= -16 and <= 127)
        {
            return new[] { (byte)value };
        }

        if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
        {
            return new[] { Int8, (byte)value };
        }

        if (value is >= short.MinValue and <= short.MaxValue)
        {
            var b = new byte[3];
            b[0] = Int16;
            BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(1), (short)value);
            return b;
        }

        if (value is >= int.MinValue and <= int.MaxValue)
        {
            var b = new byte[5];
            b[0] = Int32;
            BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(1), (int)value);
            return b;
        }

        var o = new byte[9];
        o[0] = Int64;
        BinaryPrimitives.WriteInt64BigEndian(o.AsSpan(1), value);
        return o;
    }

    public static byte[] Str(string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        if (utf8.Length <= 15)
        {
            var b = new byte[1 + utf8.Length];
            b[0] = (byte)(0x80 | utf8.Length);
            utf8.CopyTo(b, 1);
            return b;
        }

        if (utf8.Length <= byte.MaxValue)
        {
            var b = new byte[2 + utf8.Length];
            b[0] = String8;
            b[1] = (byte)utf8.Length;
            utf8.CopyTo(b, 2);
            return b;
        }

        if (utf8.Length <= ushort.MaxValue)
        {
            var b = new byte[3 + utf8.Length];
            b[0] = String16;
            BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(1), (ushort)utf8.Length);
            utf8.CopyTo(b, 3);
            return b;
        }

        var big = new byte[5 + utf8.Length];
        big[0] = String32;
        BinaryPrimitives.WriteInt32BigEndian(big.AsSpan(1), utf8.Length);
        utf8.CopyTo(big, 5);
        return big;
    }

    public static byte[] List(params byte[][] encodedItems)
    {
        if (encodedItems.Length <= 15)
        {
            return Concat(new[] { (byte)(0x90 | encodedItems.Length) }, encodedItems);
        }

        if (encodedItems.Length <= byte.MaxValue)
        {
            return Concat(new[] { List8, (byte)encodedItems.Length }, encodedItems);
        }

        var header = new byte[3];
        header[0] = List16;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(1), (ushort)encodedItems.Length);
        return Concat(header, encodedItems);
    }

    public static byte[] Map(params (string Key, byte[] Value)[] entries)
    {
        var parts = new List<byte[]>(entries.Length * 2);
        foreach (var (key, value) in entries)
        {
            parts.Add(Str(key));
            parts.Add(value);
        }

        byte[] header;
        if (entries.Length <= 15)
        {
            header = new[] { (byte)(0xA0 | entries.Length) };
        }
        else if (entries.Length <= byte.MaxValue)
        {
            header = new[] { Map8, (byte)entries.Length };
        }
        else
        {
            header = new byte[3];
            header[0] = Map16;
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(1), (ushort)entries.Length);
        }

        return Concat(header, parts);
    }

    public static byte[] Struct(byte tag, params byte[][] fields)
    {
        // Tiny structure: 0xB0 | field count, then the tag byte, then the fields.
        return Concat(new[] { (byte)(0xB0 | fields.Length), tag }, fields);
    }

    // ---- Response messages ----

    public static byte[] Success(params (string Key, byte[] Value)[] metadata) =>
        Struct(MsgSuccess, Map(metadata));

    public static byte[] SuccessEmpty() => Struct(MsgSuccess, Map());

    public static byte[] Record(params byte[][] values) => Struct(MsgRecord, List(values));

    public static byte[] Failure(params (string Key, byte[] Value)[] metadata) =>
        Struct(MsgFailure, Map(metadata));

    public static byte[] Ignored(params (string Key, byte[] Value)[] metadata) =>
        Struct(MsgIgnored, Map(metadata));

    /// <summary>An 5.7+ GQL failure, matching the Bolt 5.8 FAILURE metadata field set.</summary>
    public static byte[] GqlFailure(string gqlStatus, string neo4jCode, string description) =>
        Failure(
            ("gql_status", Str(gqlStatus)),
            ("neo4j_code", Str(neo4jCode)),
            ("description", Str(description)));

    // ---- Graph structures ----

    public static byte[] Node(long id, string elementId, string[] labels, params (string Key, byte[] Value)[] properties) =>
        Struct(NodeTag, Int(id), List(labels.Select(Str).ToArray()), Map(properties), Str(elementId));

    public static byte[] Relationship(
        long id,
        long startNodeId,
        long endNodeId,
        string type,
        string elementId,
        string startNodeElementId,
        string endNodeElementId,
        params (string Key, byte[] Value)[] properties) =>
        Struct(
            RelationshipTag,
            Int(id),
            Int(startNodeId),
            Int(endNodeId),
            Str(type),
            Map(properties),
            Str(elementId),
            Str(startNodeElementId),
            Str(endNodeElementId));

    // ---- Framing ----

    /// <summary>Frame a message payload using the Bolt chunked transfer encoding (one chunk + terminator).</summary>
    public static byte[] Frame(byte[] message)
    {
        if (message.Length == 0)
        {
            throw new ArgumentException("An empty message cannot be framed.", nameof(message));
        }

        var chunks = new List<byte[]>();
        var offset = 0;
        while (offset < message.Length)
        {
            var length = Math.Min(65_535, message.Length - offset);
            var header = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)length);
            chunks.Add(header);
            chunks.Add(message.AsSpan(offset, length).ToArray());
            offset += length;
        }

        chunks.Add(new byte[] { 0x00, 0x00 });
        return Concat(chunks);
    }

    public static byte[] Concat(params byte[][] parts) => Concat((IEnumerable<byte[]>)parts);

    public static byte[] Concat(byte[] first, IEnumerable<byte[]> rest)
    {
        var list = new List<byte[]> { first };
        list.AddRange(rest);
        return Concat((IEnumerable<byte[]>)list);
    }

    public static byte[] Concat(IEnumerable<byte[]> parts)
    {
        var list = parts.ToList();
        var total = list.Sum(p => p.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var part in list)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    public static string MsgName(byte signature) => signature switch
    {
        MsgHello => "HELLO",
        MsgGoodbye => "GOODBYE",
        MsgReset => "RESET",
        MsgRun => "RUN",
        MsgBegin => "BEGIN",
        MsgCommit => "COMMIT",
        MsgRollback => "ROLLBACK",
        MsgDiscard => "DISCARD",
        MsgPull => "PULL",
        MsgTelemetry => "TELEMETRY",
        MsgRoute => "ROUTE",
        MsgLogon => "LOGON",
        MsgLogoff => "LOGOFF",
        _ => $"UNKNOWN_0x{signature:X2}",
    };
}
