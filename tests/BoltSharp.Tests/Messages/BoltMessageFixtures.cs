using System.Buffers.Binary;
using System.Text;

namespace BoltSharp.Tests.Messages;

/// <summary>
/// A tiny PackStream 1.0 encoder written directly from the published PackStream specification for
/// test purposes. It is deliberately independent of the production
/// <see cref="BoltSharp.PackStream.PackStreamWriter"/> (the same independent-oracle pattern the
/// accepted B0-B harness established with its <c>ProtocolFixtures</c>): a shared encoder mistake
/// cannot make the production codec and the expected bytes agree at once. Only the subset needed
/// for the message fixtures is implemented.
/// </summary>
internal static class IndependentPackStream
{
    public static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var part in parts)
        {
            total += part.Length;
        }

        var result = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    /// <summary>Prepends a header to a sequence of already-encoded parts.</summary>
    public static byte[] Concat(byte[] first, byte[][] rest)
    {
        var all = new byte[rest.Length + 1][];
        all[0] = first;
        Array.Copy(rest, 0, all, 1, rest.Length);
        return Concat(all);
    }

    public static byte[] Null() => new[] { (byte)0xC0 };

    public static byte[] Bool(bool value) => new[] { value ? (byte)0xC3 : (byte)0xC2 };

    public static byte[] Int(long value)
    {
        if (value is >= -16 and <= 127)
        {
            return new[] { unchecked((byte)value) };
        }

        if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
        {
            return new[] { (byte)0xC8, unchecked((byte)value) };
        }

        if (value is >= short.MinValue and <= short.MaxValue)
        {
            var b = new byte[3];
            b[0] = 0xC9;
            BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(1), (short)value);
            return b;
        }

        if (value is >= int.MinValue and <= int.MaxValue)
        {
            var b = new byte[5];
            b[0] = 0xCA;
            BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(1), (int)value);
            return b;
        }

        var o = new byte[9];
        o[0] = 0xCB;
        BinaryPrimitives.WriteInt64BigEndian(o.AsSpan(1), value);
        return o;
    }

    public static byte[] Str(string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        if (utf8.Length <= 15)
        {
            return Concat(new[] { (byte)(0x80 | utf8.Length) }, utf8);
        }

        if (utf8.Length <= byte.MaxValue)
        {
            return Concat(new[] { (byte)0xD0, (byte)utf8.Length }, utf8);
        }

        var header = new byte[3];
        header[0] = 0xD1;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(1), (ushort)utf8.Length);
        return Concat(header, utf8);
    }

    public static byte[] List(params byte[][] items)
    {
        if (items.Length <= 15)
        {
            return Concat(new[] { (byte)(0x90 | items.Length) }, items);
        }

        return Concat(new[] { (byte)0xD4, (byte)items.Length }, items);
    }

    public static byte[] Map(params (string Key, byte[] Value)[] entries)
    {
        if (entries.Length == 0)
        {
            return new[] { (byte)0xA0 };
        }

        var parts = new List<byte[]> { new[] { (byte)(0xA0 | entries.Length) } };
        foreach (var (key, value) in entries)
        {
            parts.Add(Str(key));
            parts.Add(value);
        }

        return Concat(parts.ToArray());
    }

    public static byte[] Struct(byte tag, params byte[][] fields) =>
        Concat(new[] { (byte)(0xB0 | fields.Length), tag }, fields);
}

/// <summary>
/// Independent byte fixtures for the B3-A message codec.
/// <para>
/// Three provenance classes, kept strictly separate:
/// <list type="bullet">
/// <item><see cref="B0B"/> — authentic <c>Neo4j.Driver</c> 6.4.0 request captures and qualified
/// response fixtures, copied byte-for-byte from the committed, hash-verified B0-B evidence
/// (<c>docs/reviews/evidence/B0-B-DRV-INT-01/*.json</c> frame hex). HELLO/LOGON request bytes are
/// absent from that evidence by design (the capture layer redacts sensitive messages), so no
/// authentic fixture exists for them.</item>
/// <item><see cref="Spec5_8"/> — fixtures built with <see cref="IndependentPackStream"/> from the
/// published Bolt 5.8 message specification (preserved copy:
/// <c>docs/reviews/evidence/B3-A-IMPL-01/spec/message.adoc</c>), independent of both the
/// production writer and the B0-B encoder.</item>
/// <item><see cref="Negative"/> — malformed inputs for the negative tests.</item>
/// </list>
/// No fixture is produced by the production encoder under test.
/// </para>
/// </summary>
internal static class BoltMessageFixtures
{
    internal static byte[] Hex(string hex) =>
        Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));

    /// <summary>Authentic and B0-B-qualified byte fixtures.</summary>
    internal static class B0B
    {
        // ---- Authentic client → server captures (Neo4j.Driver 6.4.0, B0-B evidence) ----

        /// <summary>t03-query-scalar: RUN("RETURN 42 AS answer", {}, {"mode":"r"}).</summary>
        public static readonly byte[] RunScalar = Hex(
            "B310D01352455455524E20343220415320616E73776572A0A1846D6F64658172");

        /// <summary>t05/t06: RUN("RETURN n", {}, {}).</summary>
        public static readonly byte[] RunReturnN = Hex("B3108852455455524E206EA0A0");

        /// <summary>t03/t06: PULL {"n": 1000} (the driver's default fetch size).</summary>
        public static readonly byte[] PullN1000 = Hex("B13FA1816EC903E8");

        /// <summary>t05: PULL {"n": 1} (finite fetch size).</summary>
        public static readonly byte[] PullN1 = Hex("B13FA1816E01");

        /// <summary>t05-discard/t05-early-disposal: DISCARD {"n": -1} (discard all remaining records).</summary>
        public static readonly byte[] DiscardNMinus1 = Hex("B12FA1816EFF");

        /// <summary>All flows: RESET.</summary>
        public static readonly byte[] Reset = Hex("B00F");

        /// <summary>All flows: GOODBYE.</summary>
        public static readonly byte[] Goodbye = Hex("B002");

        // ---- B0-B-qualified response fixtures (server → client, spec-checked in B0-B-Q1) ----

        /// <summary>HELLO SUCCESS {"server": "Neo4j/5.26.0", "connection_id": "bolt-b0b-1", "hints": {}}.</summary>
        public static readonly byte[] HelloSuccess = Hex(
            "B170A3867365727665728C4E656F346A2F352E32362E308D636F6E6E656374696F6E5F69648A626F6C742D6230622D318568696E7473A0");

        /// <summary>LOGON/RESET SUCCESS {} (empty metadata).</summary>
        public static readonly byte[] SuccessEmpty = Hex("B170A0");

        /// <summary>RUN SUCCESS {"fields": ["answer"]}.</summary>
        public static readonly byte[] RunSuccessFieldsAnswer = Hex("B170A1866669656C64739186616E73776572");

        /// <summary>RUN SUCCESS {"fields": ["a", "r", "b"]}.</summary>
        public static readonly byte[] RunSuccessFieldsGraph = Hex("B170A1866669656C647393816181728162");

        /// <summary>RECORD [42].</summary>
        public static readonly byte[] Record42 = Hex("B171912A");

        /// <summary>
        /// t04 RECORD [Node(1, "n:1", [Person], name=Alice), Relationship(1, 1→2, KNOWS, "r:1",
        /// n:1→n:2, since=2024), Node(2, "n:2", [Person], name=Bob)] — independently decoded in
        /// B0-B-Q1 (23/23 checks).
        /// </summary>
        public static readonly byte[] RecordGraph = Hex(
            "B17193B44E019186506572736F6EA1846E616D6585416C696365836E3A31" +
            "B852010102854B4E4F5753A18573696E6365C907E883723A31836E3A31836E3A32" +
            "B44E029186506572736F6EA1846E616D6583426F62836E3A32");

        /// <summary>PULL terminal SUCCESS {"has_more": false}.</summary>
        public static readonly byte[] SuccessHasMoreFalse = Hex("B170A1886861735F6D6F7265C2");

        /// <summary>
        /// t06 FAILURE with 5.7+ GQL metadata: {"gql_status": "42001",
        /// "neo4j_code": "Neo.ClientError.Statement.SyntaxError",
        /// "description": "Invalid input for the deliberately unsupported query.",
        /// "diagnostic_record": {}}.
        /// </summary>
        public static readonly byte[] FailureGql = Hex(
            "B17FA48A67716C5F737461747573853432303031" +
            "8A6E656F346A5F636F6465D0254E656F2E436C69656E744572726F722E53746174656D656E742E53796E7461784572726F72" +
            "8B6465736372697074696F6ED035496E76616C696420696E70757420666F72207468652064656C696265726174656C7920756E737570706F727465642071756572792E" +
            "D011646961676E6F737469635F7265636F7264A0");

        /// <summary>
        /// The B0-B producer IGNORED fixture: a one-field structure (B1 7E A0). The official Bolt 5.8
        /// specification defines IGNORED with <b>no fields</b>; the qualified driver tolerates the
        /// one-field form because its IGNORED deserializer reads no fields and the pipelined reader
        /// discards unconsumed structure bytes. Recorded as a fixture discrepancy — this byte string
        /// is <b>not</b> a golden fixture for B3-A.
        /// </summary>
        public static readonly byte[] ProducerIgnoredOneField = Hex("B17EA0");
    }

    /// <summary>Fixtures built with <see cref="IndependentPackStream"/> from the Bolt 5.8 specification.</summary>
    internal static class Spec5_8
    {
        private static byte[] Map(params (string Key, byte[] Value)[] entries) =>
            IndependentPackStream.Map(entries);

        // ---- Requests ----

        /// <summary>HELLO {"user_agent": "Example/1.0"} — minimal schema-valid form.</summary>
        public static readonly byte[] HelloMinimal =
            IndependentPackStream.Struct(0x01, Map(("user_agent", IndependentPackStream.Str("Example/1.0"))));

        /// <summary>
        /// HELLO {"user_agent": "Example/1.0", "routing": {"address": "x.example.com:9001"},
        /// "bolt_agent": {"product": "neo4j-example/1.0"}, "notifications_minimum_severity":
        /// "WARNING", "notifications_disabled_classifications": ["HINT", "GENERIC"]}.
        /// </summary>
        public static readonly byte[] HelloFull = IndependentPackStream.Struct(0x01, Map(
            ("user_agent", IndependentPackStream.Str("Example/1.0")),
            ("routing", Map(("address", IndependentPackStream.Str("x.example.com:9001")))),
            ("bolt_agent", Map(("product", IndependentPackStream.Str("neo4j-example/1.0")))),
            ("notifications_minimum_severity", IndependentPackStream.Str("WARNING")),
            ("notifications_disabled_classifications", IndependentPackStream.List(
                IndependentPackStream.Str("HINT"), IndependentPackStream.Str("GENERIC")))));

        /// <summary>HELLO with explicit nulls for the optional fields (valid "use the default" values).</summary>
        public static readonly byte[] HelloNullOptional = IndependentPackStream.Struct(0x01, Map(
            ("user_agent", IndependentPackStream.Str("X")),
            ("routing", IndependentPackStream.Null()),
            ("notifications_minimum_severity", IndependentPackStream.Null()),
            ("notifications_disabled_classifications", IndependentPackStream.Null())));

        /// <summary>HELLO with the full bolt_agent shape (product mandatory; platform/language/language_details null-able).</summary>
        public static readonly byte[] HelloAgent = IndependentPackStream.Struct(0x01, Map(
            ("user_agent", IndependentPackStream.Str("X")),
            ("bolt_agent", Map(
                ("product", IndependentPackStream.Str("neo4j-example/1.0")),
                ("platform", IndependentPackStream.Null()),
                ("language", IndependentPackStream.Str("C#/8.0")),
                ("language_details", IndependentPackStream.Null())))));

        public static readonly byte[] LogonNone =
            IndependentPackStream.Struct(0x6A, Map(("scheme", IndependentPackStream.Str("none"))));

        public static readonly byte[] LogonBasic = IndependentPackStream.Struct(0x6A, Map(
            ("scheme", IndependentPackStream.Str("basic")),
            ("principal", IndependentPackStream.Str("user")),
            ("credentials", IndependentPackStream.Str("password"))));

        public static readonly byte[] LogonBearer = IndependentPackStream.Struct(0x6A, Map(
            ("scheme", IndependentPackStream.Str("bearer")),
            ("credentials", IndependentPackStream.Str("token"))));

        public static readonly byte[] LogonKerberos = IndependentPackStream.Struct(0x6A, Map(
            ("scheme", IndependentPackStream.Str("kerberos")),
            ("credentials", IndependentPackStream.Str("ticket"))));

        /// <summary>A structurally valid scheme the codec does not recognize.</summary>
        public static readonly byte[] LogonUnknownScheme =
            IndependentPackStream.Struct(0x6A, Map(("scheme", IndependentPackStream.Str("custom"))));

        /// <summary>RUN "RETURN $x AS x" {"x": 1} {} — nested parameter values preserved.</summary>
        public static readonly byte[] RunWithParameter = IndependentPackStream.Struct(
            0x10,
            IndependentPackStream.Str("RETURN $x AS x"),
            Map(("x", IndependentPackStream.Int(1))),
            Map());

        /// <summary>RUN with the full 5.8 extra schema.</summary>
        public static readonly byte[] RunFullExtra = IndependentPackStream.Struct(
            0x10,
            IndependentPackStream.Str("RETURN 1"),
            Map(),
            Map(
                ("bookmarks", IndependentPackStream.List(IndependentPackStream.Str("bm:1"))),
                ("tx_timeout", IndependentPackStream.Int(123)),
                ("tx_metadata", Map(("log", IndependentPackStream.Str("m")))),
                ("mode", IndependentPackStream.Str("r")),
                ("db", IndependentPackStream.Str("neo4j")),
                ("imp_user", IndependentPackStream.Str("u")),
                ("notifications_minimum_severity", IndependentPackStream.Str("WARNING")),
                ("notifications_disabled_classifications", IndependentPackStream.List(
                    IndependentPackStream.Str("HINT")))));

        /// <summary>PULL {"n": 5, "qid": 7}.</summary>
        public static readonly byte[] PullNAndQid = IndependentPackStream.Struct(
            0x3F, Map(("n", IndependentPackStream.Int(5)), ("qid", IndependentPackStream.Int(7))));

        /// <summary>PULL {"n": -1} — fetch all remaining records.</summary>
        public static readonly byte[] PullNMinus1 = IndependentPackStream.Struct(
            0x3F, Map(("n", IndependentPackStream.Int(-1))));

        /// <summary>DISCARD {"n": -1, "qid": -1} — the specification's own example form.</summary>
        public static readonly byte[] DiscardNAndQid = IndependentPackStream.Struct(
            0x2F, Map(("n", IndependentPackStream.Int(-1)), ("qid", IndependentPackStream.Int(-1))));

        /// <summary>TELEMETRY 0.</summary>
        public static readonly byte[] TelemetryZero =
            IndependentPackStream.Struct(0x54, IndependentPackStream.Int(0));

        // ---- Responses (the codec must encode exactly these bytes) ----

        /// <summary>IGNORED — zero fields, the Bolt 5.8 specification form.</summary>
        public static readonly byte[] Ignored = IndependentPackStream.Struct(0x7E);

        /// <summary>SUCCESS {"has_more": true}.</summary>
        public static readonly byte[] SuccessHasMoreTrue =
            IndependentPackStream.Struct(0x70, Map(("has_more", IndependentPackStream.Bool(true))));

        /// <summary>RUN SUCCESS {"fields": ["x"], "t_first": 123}.</summary>
        public static readonly byte[] RunSuccessFieldsAndTFirst = IndependentPackStream.Struct(
            0x70,
            Map(
                ("fields", IndependentPackStream.List(IndependentPackStream.Str("x"))),
                ("t_first", IndependentPackStream.Int(123))));

        /// <summary>FAILURE with a bounded nested cause map (5.7+ shape).</summary>
        public static readonly byte[] FailureWithCause = IndependentPackStream.Struct(
            0x7F,
            Map(
                ("gql_status", IndependentPackStream.Str("50N42")),
                ("cause", Map(
                    ("gql_status", IndependentPackStream.Str("50N09")),
                    ("neo4j_code", IndependentPackStream.Str("Neo.ClientError.Request.Invalid"))))));

        /// <summary>LOGON SUCCESS {"advertised_address": "graphz.example.com:7687"} (5.8).</summary>
        public static readonly byte[] LogonSuccessAdvertisedAddress = IndependentPackStream.Struct(
            0x70, Map(("advertised_address", IndependentPackStream.Str("graphz.example.com:7687"))));

        /// <summary>LOGON SUCCESS {"credentials_expired": true}.</summary>
        public static readonly byte[] LogonSuccessCredentialsExpired = IndependentPackStream.Struct(
            0x70, Map(("credentials_expired", IndependentPackStream.Bool(true))));
    }

    /// <summary>Malformed inputs for the negative tests (built with the independent encoder or hand-written hex).</summary>
    internal static class Negative
    {
        private static byte[] Map(params (string Key, byte[] Value)[] entries) =>
            IndependentPackStream.Map(entries);

        /// <summary>An empty payload.</summary>
        public static readonly byte[] Empty = Array.Empty<byte>();

        /// <summary>A top-level integer instead of a structure.</summary>
        public static readonly byte[] NotAStructure = Hex("2A");

        /// <summary>A structure with a tag no Bolt 5.8 message uses.</summary>
        public static readonly byte[] UnknownRequestSignature =
            IndependentPackStream.Struct(0x5A, Map(("user_agent", IndependentPackStream.Str("X"))));

        /// <summary>A graph Node structure used as a top-level message.</summary>
        public static readonly byte[] NodeAsMessage = Hex("B44E019186506572736F6EA0836E3A31");

        /// <summary>BEGIN — known for 5.8, unsupported by the B3-A codec.</summary>
        public static readonly byte[] UnsupportedBegin =
            IndependentPackStream.Struct(0x11, Map());

        /// <summary>COMMIT — known for 5.8, unsupported by the B3-A codec.</summary>
        public static readonly byte[] UnsupportedCommit = IndependentPackStream.Struct(0x12);

        /// <summary>ROLLBACK — known for 5.8, unsupported by the B3-A codec.</summary>
        public static readonly byte[] UnsupportedRollback = IndependentPackStream.Struct(0x13);

        /// <summary>ROUTE — known for 5.8, unsupported by the B3-A codec.</summary>
        public static readonly byte[] UnsupportedRoute = IndependentPackStream.Struct(
            0x66, Map(), IndependentPackStream.List(), Map());

        /// <summary>HELLO with two fields instead of one.</summary>
        public static readonly byte[] HelloWrongArity = IndependentPackStream.Struct(
            0x01,
            Map(("user_agent", IndependentPackStream.Str("Example/1.0"))),
            Map());

        /// <summary>HELLO with zero fields.</summary>
        public static readonly byte[] HelloEmpty = IndependentPackStream.Struct(0x01);

        /// <summary>HELLO whose extra field is a list, not a map.</summary>
        public static readonly byte[] HelloExtraNotMap =
            IndependentPackStream.Struct(0x01, IndependentPackStream.List());

        /// <summary>HELLO without user_agent.</summary>
        public static readonly byte[] HelloMissingUserAgent =
            IndependentPackStream.Struct(0x01, Map());

        /// <summary>HELLO whose user_agent is an integer.</summary>
        public static readonly byte[] HelloUserAgentWrongType =
            IndependentPackStream.Struct(0x01, Map(("user_agent", IndependentPackStream.Int(1))));

        /// <summary>HELLO carrying the prohibited pre-5.1 authentication field 'scheme'.</summary>
        public static readonly byte[] HelloProhibitedScheme = IndependentPackStream.Struct(0x01, Map(
            ("user_agent", IndependentPackStream.Str("X")),
            ("scheme", IndependentPackStream.Str("basic"))));

        /// <summary>HELLO carrying the prohibited 'patch_bolt' field (removed after Bolt 4.4).</summary>
        public static readonly byte[] HelloProhibitedPatchBolt = IndependentPackStream.Struct(0x01, Map(
            ("user_agent", IndependentPackStream.Str("X")),
            ("patch_bolt", IndependentPackStream.List(IndependentPackStream.Str("utc")))));

        /// <summary>HELLO whose bolt_agent is a string instead of a map.</summary>
        public static readonly byte[] HelloBoltAgentWrongType = IndependentPackStream.Struct(0x01, Map(
            ("user_agent", IndependentPackStream.Str("X")),
            ("bolt_agent", IndependentPackStream.Str("agent"))));

        /// <summary>HELLO whose bolt_agent lacks the mandatory 'product' entry.</summary>
        public static readonly byte[] HelloBoltAgentMissingProduct = IndependentPackStream.Struct(0x01, Map(
            ("user_agent", IndependentPackStream.Str("X")),
            ("bolt_agent", Map())));

        /// <summary>HELLO whose routing value is an integer instead of a string.</summary>
        public static readonly byte[] HelloRoutingWrongValueType = IndependentPackStream.Struct(0x01, Map(
            ("user_agent", IndependentPackStream.Str("X")),
            ("routing", Map(("address", IndependentPackStream.Int(1))))));

        /// <summary>HELLO whose notifications_disabled_classifications contains a non-string.</summary>
        public static readonly byte[] HelloClassificationsWrongItemType = IndependentPackStream.Struct(0x01, Map(
            ("user_agent", IndependentPackStream.Str("X")),
            ("notifications_disabled_classifications", IndependentPackStream.List(
                IndependentPackStream.Int(1)))));

        /// <summary>LOGON whose auth field is a list, not a map.</summary>
        public static readonly byte[] LogonAuthNotMap =
            IndependentPackStream.Struct(0x6A, IndependentPackStream.List());

        /// <summary>LOGON without a scheme.</summary>
        public static readonly byte[] LogonMissingScheme =
            IndependentPackStream.Struct(0x6A, Map());

        /// <summary>LOGON whose scheme is an integer.</summary>
        public static readonly byte[] LogonSchemeWrongType =
            IndependentPackStream.Struct(0x6A, Map(("scheme", IndependentPackStream.Int(1))));

        /// <summary>LOGON basic without credentials.</summary>
        public static readonly byte[] LogonBasicMissingCredentials = IndependentPackStream.Struct(0x6A, Map(
            ("scheme", IndependentPackStream.Str("basic")),
            ("principal", IndependentPackStream.Str("user"))));

        /// <summary>LOGON basic whose principal is an integer.</summary>
        public static readonly byte[] LogonBasicPrincipalWrongType = IndependentPackStream.Struct(0x6A, Map(
            ("scheme", IndependentPackStream.Str("basic")),
            ("principal", IndependentPackStream.Int(1)),
            ("credentials", IndependentPackStream.Str("password"))));

        /// <summary>LOGON bearer without credentials.</summary>
        public static readonly byte[] LogonBearerMissingCredentials = IndependentPackStream.Struct(
            0x6A, Map(("scheme", IndependentPackStream.Str("bearer"))));

        /// <summary>RUN with two fields instead of three.</summary>
        public static readonly byte[] RunWrongArity = IndependentPackStream.Struct(
            0x10, IndependentPackStream.Str("query"), Map());

        /// <summary>RUN whose query field is an integer.</summary>
        public static readonly byte[] RunQueryWrongType =
            IndependentPackStream.Struct(0x10, IndependentPackStream.Int(1), Map(), Map());

        /// <summary>RUN whose parameters field is a list, not a map.</summary>
        public static readonly byte[] RunParametersWrongType = IndependentPackStream.Struct(
            0x10, IndependentPackStream.Str("query"), IndependentPackStream.List(), Map());

        /// <summary>RUN whose extra carries a wrong-typed tx_timeout.</summary>
        public static readonly byte[] RunTxTimeoutWrongType = IndependentPackStream.Struct(
            0x10,
            IndependentPackStream.Str("query"),
            Map(),
            Map(("tx_timeout", IndependentPackStream.Str("later"))));

        /// <summary>RUN carrying the prohibited legacy 'notifications_disabled_categories' field.</summary>
        public static readonly byte[] RunProhibitedCategories = IndependentPackStream.Struct(
            0x10,
            IndependentPackStream.Str("query"),
            Map(),
            Map(("notifications_disabled_categories", IndependentPackStream.List())));

        /// <summary>PULL without the required 'n' field.</summary>
        public static readonly byte[] PullMissingN = IndependentPackStream.Struct(0x3F, Map());

        /// <summary>PULL whose 'n' is a string.</summary>
        public static readonly byte[] PullNWrongType =
            IndependentPackStream.Struct(0x3F, Map(("n", IndependentPackStream.Str("not!"))));

        /// <summary>PULL with n = -2 (below the permitted -1).</summary>
        public static readonly byte[] PullNOutOfRange =
            IndependentPackStream.Struct(0x3F, Map(("n", IndependentPackStream.Int(-2))));

        /// <summary>PULL with qid = -2 (below the permitted -1).</summary>
        public static readonly byte[] PullQidOutOfRange = IndependentPackStream.Struct(
            0x3F, Map(("n", IndependentPackStream.Int(1)), ("qid", IndependentPackStream.Int(-2))));

        /// <summary>DISCARD with n = -2.</summary>
        public static readonly byte[] DiscardNOutOfRange =
            IndependentPackStream.Struct(0x2F, Map(("n", IndependentPackStream.Int(-2))));

        /// <summary>RESET with an extra field — zero-arity messages reject fields.</summary>
        public static readonly byte[] ResetWithField = IndependentPackStream.Struct(0x0F, Map());

        /// <summary>GOODBYE with an extra field.</summary>
        public static readonly byte[] GoodbyeWithField =
            IndependentPackStream.Struct(0x02, IndependentPackStream.Int(1));

        /// <summary>LOGOFF with an extra field.</summary>
        public static readonly byte[] LogoffWithField =
            IndependentPackStream.Struct(0x6B, IndependentPackStream.Int(1));

        /// <summary>TELEMETRY with no field.</summary>
        public static readonly byte[] TelemetryNoField = IndependentPackStream.Struct(0x54);

        /// <summary>TELEMETRY whose api field is a string.</summary>
        public static readonly byte[] TelemetryApiWrongType =
            IndependentPackStream.Struct(0x54, IndependentPackStream.Str("driver"));

        /// <summary>SUCCESS whose metadata field is a list, not a map.</summary>
        public static readonly byte[] SuccessMetadataNotMap =
            IndependentPackStream.Struct(0x70, IndependentPackStream.List());

        /// <summary>SUCCESS whose 'fields' metadata is an integer.</summary>
        public static readonly byte[] SuccessFieldsWrongType =
            IndependentPackStream.Struct(0x70, Map(("fields", IndependentPackStream.Int(1))));

        /// <summary>SUCCESS whose 'has_more' metadata is a string.</summary>
        public static readonly byte[] SuccessHasMoreWrongType =
            IndependentPackStream.Struct(0x70, Map(("has_more", IndependentPackStream.Str("true"))));

        /// <summary>SUCCESS carrying the prohibited 'patch_bolt' metadata.</summary>
        public static readonly byte[] SuccessProhibitedPatchBolt = IndependentPackStream.Struct(
            0x70, Map(("patch_bolt", IndependentPackStream.List(IndependentPackStream.Str("utc")))));

        /// <summary>RECORD whose field is a map, not a list.</summary>
        public static readonly byte[] RecordFieldNotList =
            IndependentPackStream.Struct(0x71, Map());

        /// <summary>RECORD with two fields instead of one — wrong outer arity (not an NC-05b column mismatch, which the stateless codec cannot see).</summary>
        public static readonly byte[] RecordWrongOuterArity = Hex("B271912A01");

        /// <summary>FAILURE carrying the prohibited pre-5.7 'code' field.</summary>
        public static readonly byte[] FailureProhibitedCode = IndependentPackStream.Struct(0x7F, Map(
            ("code", IndependentPackStream.Str("ABCDE")),
            ("gql_status", IndependentPackStream.Str("42001"))));

        /// <summary>FAILURE whose cause is a string, not a map.</summary>
        public static readonly byte[] FailureCauseNotMap = IndependentPackStream.Struct(0x7F, Map(
            ("gql_status", IndependentPackStream.Str("42001")),
            ("cause", IndependentPackStream.Str("nope!"))));

        /// <summary>A truncated RUN: the declared 19-byte query string is cut off.</summary>
        public static readonly byte[] TruncatedRun = Hex(
            "B310D01352455455524E203432204153");

        /// <summary>A complete RESET structure followed by trailing bytes.</summary>
        public static readonly byte[] TrailingBytesAfterReset = Hex("B00F01");

        /// <summary>An invalid nested PackStream value (marker 0x7F is not a value marker).</summary>
        public static readonly byte[] InvalidNestedValue = Hex(
            "B101A1818A757365725F6167656E747F");
    }
}
