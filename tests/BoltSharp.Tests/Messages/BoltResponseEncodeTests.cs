using BoltSharp.Messages;
using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.Messages;

/// <summary>
/// Golden-byte encoding tests. Expected bytes come from the independent oracle set — the
/// B0-B-qualified fixtures (server→client frame hex committed in the B0-B evidence) and the
/// test-local <see cref="IndependentPackStream"/> spec encoder — never from the production writer.
/// </summary>
public sealed class BoltResponseEncodeTests
{
    private readonly BoltMessageCodec _codec = new();

    private static PackStreamMap Map(params (string Key, PackStreamValue? Value)[] entries) =>
        BoltMessageMapsProxy.FromEntries(entries);

    [Fact]
    public void Success_Empty_EncodesToGoldenBytes()
    {
        var bytes = _codec.EncodeResponse(SuccessResponse.Empty);

        Assert.Equal(BoltMessageFixtures.B0B.SuccessEmpty, bytes);
    }

    [Fact]
    public void Success_HelloMetadata_EncodesToB0BQualifiedBytes()
    {
        // The exact metadata set the B0-B harness served to the qualified driver.
        var response = SuccessResponse.FromEntries(
            ("server", new PackStreamString("Neo4j/5.26.0")),
            ("connection_id", new PackStreamString("bolt-b0b-1")),
            ("hints", new PackStreamMap(Array.Empty<KeyValuePair<string, PackStreamValue?>>())));

        var bytes = _codec.EncodeResponse(response);

        Assert.Equal(BoltMessageFixtures.B0B.HelloSuccess, bytes);
    }

    [Fact]
    public void Success_RunFields_EncodesToB0BQualifiedBytes()
    {
        var response = SuccessResponse.FromEntries(
            ("fields", new PackStreamList(new PackStreamValue?[] { new PackStreamString("answer") })));

        Assert.Equal(BoltMessageFixtures.B0B.RunSuccessFieldsAnswer, _codec.EncodeResponse(response));

        var graphFields = SuccessResponse.FromEntries(
            ("fields", new PackStreamList(new PackStreamValue?[]
            {
                new PackStreamString("a"), new PackStreamString("r"), new PackStreamString("b"),
            })));

        Assert.Equal(BoltMessageFixtures.B0B.RunSuccessFieldsGraph, _codec.EncodeResponse(graphFields));
    }

    [Fact]
    public void Success_HasMore_EncodesToGoldenBytes()
    {
        var hasMoreFalse = SuccessResponse.FromEntries(("has_more", new PackStreamBoolean(false)));
        Assert.Equal(BoltMessageFixtures.B0B.SuccessHasMoreFalse, _codec.EncodeResponse(hasMoreFalse));

        var hasMoreTrue = SuccessResponse.FromEntries(("has_more", new PackStreamBoolean(true)));
        Assert.Equal(BoltMessageFixtures.Spec5_8.SuccessHasMoreTrue, _codec.EncodeResponse(hasMoreTrue));
    }

    [Fact]
    public void Success_RunFieldsAndTFirst_EncodesToSpecBytes()
    {
        var response = SuccessResponse.FromEntries(
            ("fields", new PackStreamList(new PackStreamValue?[] { new PackStreamString("x") })),
            ("t_first", new PackStreamInteger(123)));

        Assert.Equal(BoltMessageFixtures.Spec5_8.RunSuccessFieldsAndTFirst, _codec.EncodeResponse(response));
    }

    [Fact]
    public void Success_LogonMetadata_EncodesToSpecBytes()
    {
        var advertised = SuccessResponse.FromEntries(
            ("advertised_address", new PackStreamString("graphz.example.com:7687")));
        Assert.Equal(
            BoltMessageFixtures.Spec5_8.LogonSuccessAdvertisedAddress,
            _codec.EncodeResponse(advertised));

        var expired = SuccessResponse.FromEntries(("credentials_expired", new PackStreamBoolean(true)));
        Assert.Equal(
            BoltMessageFixtures.Spec5_8.LogonSuccessCredentialsExpired,
            _codec.EncodeResponse(expired));
    }

    [Fact]
    public void Success_PreservesMetadataOrder_Deterministically()
    {
        // No canonical map ordering is claimed: the caller's order is preserved, and encoding the
        // same value twice produces identical bytes.
        var response = SuccessResponse.FromEntries(
            ("server", new PackStreamString("Neo4j/5.26.0")),
            ("connection_id", new PackStreamString("bolt-b0b-1")));

        var first = _codec.EncodeResponse(response);
        var second = _codec.EncodeResponse(response);

        Assert.Equal(first, second);
        Assert.Equal(
            BoltMessageFixtures.Hex("B170A2" + "86736572766572" + "8C4E656F346A2F352E32362E30" + "8D636F6E6E656374696F6E5F6964" + "8A626F6C742D6230622D31"),
            first);
    }

    [Fact]
    public void Record_Scalar_EncodesToB0BQualifiedBytes()
    {
        var response = RecordResponse.FromValues(new PackStreamInteger(42));

        Assert.Equal(BoltMessageFixtures.B0B.Record42, _codec.EncodeResponse(response));
    }

    [Fact]
    public void Record_TypedGraphValues_EncodeToB0BQualifiedBytes()
    {
        // Built from typed graph values (accepted B1 representations); the encoded bytes must match
        // the B0-B fixture that the real driver reconstructed and B0-B-Q1 independently decoded.
        var alice = new PackStreamNode(
            1,
            new[] { "Person" },
            Map(("name", new PackStreamString("Alice"))),
            "n:1");
        var knows = new PackStreamRelationship(
            1, 1, 2, "KNOWS",
            Map(("since", new PackStreamInteger(2024))),
            "r:1", "n:1", "n:2");
        var bob = new PackStreamNode(
            2,
            new[] { "Person" },
            Map(("name", new PackStreamString("Bob"))),
            "n:2");

        var response = RecordResponse.FromValues(alice, knows, bob);

        Assert.Equal(BoltMessageFixtures.B0B.RecordGraph, _codec.EncodeResponse(response));
    }

    [Fact]
    public void Failure_GqlMetadata_EncodesToB0BQualifiedBytes()
    {
        var response = FailureResponse.FromEntries(
            ("gql_status", new PackStreamString("42001")),
            ("neo4j_code", new PackStreamString("Neo.ClientError.Statement.SyntaxError")),
            ("description", new PackStreamString("Invalid input for the deliberately unsupported query.")),
            ("diagnostic_record", new PackStreamMap(Array.Empty<KeyValuePair<string, PackStreamValue?>>())));

        Assert.Equal(BoltMessageFixtures.B0B.FailureGql, _codec.EncodeResponse(response));
    }

    [Fact]
    public void Failure_NestedCause_EncodesToSpecBytes()
    {
        var response = FailureResponse.FromEntries(
            ("gql_status", new PackStreamString("50N42")),
            ("cause", FailureResponse.FromEntries(
                ("gql_status", new PackStreamString("50N09")),
                ("neo4j_code", new PackStreamString("Neo.ClientError.Request.Invalid"))).Metadata));

        Assert.Equal(BoltMessageFixtures.Spec5_8.FailureWithCause, _codec.EncodeResponse(response));
    }

    [Fact]
    public void Ignored_EncodesAsZeroFieldSpecStructure()
    {
        // Bolt 5.8: "IGNORED — Summary — (no fields)". The B0-B producer fixture's one-field form
        // (B1 7E A0) is a recorded discrepancy, not a golden fixture.
        var bytes = _codec.EncodeResponse(IgnoredResponse.Instance);

        Assert.Equal(BoltMessageFixtures.Spec5_8.Ignored, bytes);
        Assert.Equal(new byte[] { 0xB0, 0x7E }, bytes);
    }

    [Fact]
    public void Encode_NullResponse_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => _codec.EncodeResponse(null!));
    }
}

/// <summary>
/// Bridge to the internal ordered-map construction helper, keeping test metadata construction on
/// the same ownership conventions as the production code.
/// </summary>
internal static class BoltMessageMapsProxy
{
    public static PackStreamMap FromEntries((string Key, PackStreamValue? Value)[] entries)
    {
        var list = new List<KeyValuePair<string, PackStreamValue?>>(entries.Length);
        foreach (var (key, value) in entries)
        {
            list.Add(new KeyValuePair<string, PackStreamValue?>(key, value));
        }

        return new PackStreamMap(list);
    }
}
