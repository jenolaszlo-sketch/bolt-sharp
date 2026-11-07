using BoltSharp.Messages;
using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.Messages;

/// <summary>
/// Positive decoding tests for every supported Bolt 5.8 request, over both the independent
/// spec-derived fixtures and the authentic Neo4j.Driver 6.4.0 captures (see
/// <see cref="B0BFixtureComparisonTests"/> for the capture-focused set).
/// </summary>
public sealed class BoltRequestDecodeTests
{
    private readonly BoltMessageCodec _codec = new();

    [Fact]
    public void Hello_Minimal_DecodesUserAgent()
    {
        var request = _codec.DecodeRequest(BoltMessageFixtures.Spec5_8.HelloMinimal);

        var hello = Assert.IsType<HelloRequest>(request);
        Assert.Equal(BoltMessageSignatures.Hello, hello.Signature);
        Assert.Equal("Example/1.0", hello.UserAgent);
        Assert.Null(hello.Routing);
        Assert.Null(hello.BoltAgent);
    }

    [Fact]
    public void Hello_Full_DecodesAllKnownFieldsWithTypesPreserved()
    {
        var request = _codec.DecodeRequest(BoltMessageFixtures.Spec5_8.HelloFull);

        var hello = Assert.IsType<HelloRequest>(request);
        Assert.Equal("Example/1.0", hello.UserAgent);

        var routing = Assert.IsType<PackStreamMap>(hello.Routing);
        Assert.Equal(
            "x.example.com:9001",
            Assert.IsType<PackStreamString>(BoltMessageFieldsTestAccess.GetValue(routing, "address")).Value);

        var agent = Assert.IsType<PackStreamMap>(hello.BoltAgent);
        Assert.Equal(
            "neo4j-example/1.0",
            Assert.IsType<PackStreamString>(BoltMessageFieldsTestAccess.GetValue(agent, "product")).Value);

        // Unknown/unvalidated metadata is preserved verbatim in wire order.
        Assert.Equal(5, hello.Extra.Entries.Count);
        Assert.Equal(
            "WARNING",
            Assert.IsType<PackStreamString>(
                BoltMessageFieldsTestAccess.GetValue(hello.Extra, "notifications_minimum_severity")).Value);
        var classifications = Assert.IsType<PackStreamList>(
            BoltMessageFieldsTestAccess.GetValue(hello.Extra, "notifications_disabled_classifications"));
        Assert.Equal(2, classifications.Items.Count);
    }

    [Fact]
    public void Hello_ExplicitNullOptionals_AreAcceptedAndPreserved()
    {
        var request = _codec.DecodeRequest(BoltMessageFixtures.Spec5_8.HelloNullOptional);

        var hello = Assert.IsType<HelloRequest>(request);
        Assert.Null(hello.Routing);
        // Null is a distinct wire value from absence: the key is present with a null value.
        var entry = hello.Extra.Entries.Single(e => e.Key == "routing");
        Assert.Null(entry.Value);
        Assert.True(hello.Extra.Entries.Any(e => e.Key == "notifications_minimum_severity"));
        Assert.True(hello.Extra.Entries.Any(e => e.Key == "notifications_disabled_classifications"));
    }

    [Fact]
    public void Hello_FullAgentShape_PreservesNullAgentEntries()
    {
        var request = _codec.DecodeRequest(BoltMessageFixtures.Spec5_8.HelloAgent);

        var hello = Assert.IsType<HelloRequest>(request);
        var agent = Assert.IsType<PackStreamMap>(hello.BoltAgent);
        Assert.Equal(4, agent.Entries.Count);
        var platform = agent.Entries.Single(e => e.Key == "platform");
        Assert.Null(platform.Value);
    }

    [Theory]
    [InlineData("none", BoltAuthScheme.None)]
    [InlineData("basic", BoltAuthScheme.Basic)]
    [InlineData("bearer", BoltAuthScheme.Bearer)]
    [InlineData("kerberos", BoltAuthScheme.Kerberos)]
    public void Logon_PredefinedSchemes_AreRecognized(string scheme, BoltAuthScheme expected)
    {
        var bytes = scheme switch
        {
            "none" => BoltMessageFixtures.Spec5_8.LogonNone,
            "basic" => BoltMessageFixtures.Spec5_8.LogonBasic,
            "bearer" => BoltMessageFixtures.Spec5_8.LogonBearer,
            _ => BoltMessageFixtures.Spec5_8.LogonKerberos,
        };

        var request = _codec.DecodeRequest(bytes);

        var logon = Assert.IsType<LogonRequest>(request);
        Assert.Equal(BoltMessageSignatures.Logon, logon.Signature);
        Assert.Equal(scheme, logon.Scheme);
        Assert.Equal(expected, logon.RecognizedScheme);
    }

    [Fact]
    public void Logon_UnknownScheme_IsPreservedAsStructurallyValid()
    {
        var request = _codec.DecodeRequest(BoltMessageFixtures.Spec5_8.LogonUnknownScheme);

        var logon = Assert.IsType<LogonRequest>(request);
        Assert.Equal("custom", logon.Scheme);
        Assert.Equal(BoltAuthScheme.Unknown, logon.RecognizedScheme);
    }

    [Fact]
    public void Run_FullExtra_DecodesAllKnownFieldsWithTypesPreserved()
    {
        var request = _codec.DecodeRequest(BoltMessageFixtures.Spec5_8.RunFullExtra);

        var run = Assert.IsType<RunRequest>(request);
        Assert.Equal(BoltMessageSignatures.Run, run.Signature);
        Assert.Equal("RETURN 1", run.Query);
        Assert.Empty(run.Parameters.Entries);
        Assert.Equal(8, run.Extra.Entries.Count);

        Assert.Equal(123, BoltMessageFieldsTestAccess.GetInteger(run.Extra, "tx_timeout"));
        Assert.Equal("r", BoltMessageFieldsTestAccess.GetString(run.Extra, "mode"));
        Assert.Equal("neo4j", BoltMessageFieldsTestAccess.GetString(run.Extra, "db"));
        Assert.Equal("u", BoltMessageFieldsTestAccess.GetString(run.Extra, "imp_user"));

        var bookmarks = Assert.IsType<PackStreamList>(
            BoltMessageFieldsTestAccess.GetValue(run.Extra, "bookmarks"));
        Assert.Equal("bm:1", Assert.IsType<PackStreamString>(bookmarks.Items[0]).Value);
    }

    [Fact]
    public void Run_NestedParameterValues_ArePreserved()
    {
        var request = _codec.DecodeRequest(BoltMessageFixtures.Spec5_8.RunWithParameter);

        var run = Assert.IsType<RunRequest>(request);
        Assert.Equal("RETURN $x AS x", run.Query);
        var x = Assert.IsType<PackStreamInteger>(
            BoltMessageFieldsTestAccess.GetValue(run.Parameters, "x"));
        Assert.Equal(1, x.Value);
    }

    [Fact]
    public void Pull_FiniteN_AndQid_Decode()
    {
        var request = _codec.DecodeRequest(BoltMessageFixtures.Spec5_8.PullNAndQid);

        var pull = Assert.IsType<PullRequest>(request);
        Assert.Equal(BoltMessageSignatures.Pull, pull.Signature);
        Assert.Equal(5, pull.N);
        Assert.Equal(7, pull.Qid);
    }

    [Fact]
    public void Pull_NMinus1_FetchAllRemainingRecords()
    {
        var request = _codec.DecodeRequest(BoltMessageFixtures.Spec5_8.PullNMinus1);

        var pull = Assert.IsType<PullRequest>(request);
        Assert.Equal(-1, pull.N);
        Assert.Null(pull.Qid);
    }

    [Fact]
    public void Discard_NAndQid_Decode()
    {
        var request = _codec.DecodeRequest(BoltMessageFixtures.Spec5_8.DiscardNAndQid);

        var discard = Assert.IsType<DiscardRequest>(request);
        Assert.Equal(BoltMessageSignatures.Discard, discard.Signature);
        Assert.Equal(-1, discard.N);
        Assert.Equal(-1, discard.Qid);
    }

    [Fact]
    public void Telemetry_Zero_Decodes()
    {
        var request = _codec.DecodeRequest(BoltMessageFixtures.Spec5_8.TelemetryZero);

        var telemetry = Assert.IsType<TelemetryRequest>(request);
        Assert.Equal(BoltMessageSignatures.Telemetry, telemetry.Signature);
        Assert.Equal(0, telemetry.Api);
    }

    [Fact]
    public void ZeroArityRequests_DecodeWithoutSideEffects()
    {
        Assert.IsType<ResetRequest>(_codec.DecodeRequest(BoltMessageFixtures.B0B.Reset));
        Assert.IsType<GoodbyeRequest>(_codec.DecodeRequest(BoltMessageFixtures.B0B.Goodbye));
    }

    [Fact]
    public void Decode_IsDeterministic_AndCodecIsStateless()
    {
        // The same payload decodes to equal values on repeated calls and across interleaved
        // decodes of different messages: the codec holds no connection state.
        var first = _codec.DecodeRequest(BoltMessageFixtures.Spec5_8.HelloMinimal);
        _ = _codec.DecodeRequest(BoltMessageFixtures.Spec5_8.RunFullExtra);
        var second = _codec.DecodeRequest(BoltMessageFixtures.Spec5_8.HelloMinimal);

        var a = Assert.IsType<HelloRequest>(first);
        var b = Assert.IsType<HelloRequest>(second);
        Assert.Equal(a.UserAgent, b.UserAgent);
        Assert.Equal(a.Extra.Entries.Count, b.Extra.Entries.Count);
    }

    [Fact]
    public void Record_TwoValuesForOneRunField_IsValidAtCodecLevel()
    {
        // NC-05b boundary: the stateless codec enforces the outer RECORD structure arity only.
        // Verifying that the value count matches the RUN SUCCESS 'fields' list requires
        // cross-message context and is a B3-B/B3-C responsibility. This two-value list therefore
        // decodes successfully here.
        var response = _codec.DecodeResponse(IndependentPackStream.Struct(
            0x71,
            IndependentPackStream.List(IndependentPackStream.Int(42), IndependentPackStream.Int(43))));

        var record = Assert.IsType<RecordResponse>(response);
        Assert.Equal(2, record.Data.Items.Count);
    }
}

/// <summary>
/// Test access to the internal map accessors, so assertions can inspect preserved metadata without
/// going through the codec's validation.
/// </summary>
internal static class BoltMessageFieldsTestAccess
{
    public static PackStreamValue? GetValue(PackStreamMap map, string key) =>
        BoltMessageFieldsProxy.GetValue(map, key);

    public static bool TryGet(PackStreamMap map, string key, out PackStreamValue? value) =>
        BoltMessageFieldsProxy.TryGet(map, key, out value);

    public static string? GetString(PackStreamMap map, string key) =>
        BoltMessageFieldsProxy.GetString(map, key);

    public static long? GetInteger(PackStreamMap map, string key) =>
        BoltMessageFieldsProxy.GetInteger(map, key);

    private static class BoltMessageFieldsProxy
    {
        // The real helpers are internal to the production assembly; the tests re-implement the same
        // last-wins lookup independently to avoid coupling assertions to the implementation.
        public static PackStreamValue? GetValue(PackStreamMap map, string key)
        {
            PackStreamValue? result = null;
            foreach (var entry in map.Entries)
            {
                if (string.Equals(entry.Key, key, StringComparison.Ordinal))
                {
                    result = entry.Value;
                }
            }

            return result;
        }

        public static bool TryGet(PackStreamMap map, string key, out PackStreamValue? value)
        {
            value = GetValue(map, key);
            return value is not null;
        }

        public static string? GetString(PackStreamMap map, string key) =>
            GetValue(map, key) as PackStreamString is { } value ? value.Value : null;

        public static long? GetInteger(PackStreamMap map, string key) =>
            GetValue(map, key) as PackStreamInteger is { } value ? value.Value : null;
    }
}
