using BoltSharp.Messages;
using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.Messages;

/// <summary>
/// Response decoding (diagnostics and test verification). Also pins the recorded B0-B IGNORED
/// fixture discrepancy: the producer emitted a one-field IGNORED that the qualified driver
/// tolerates, while the Bolt 5.8 specification — and this codec — define IGNORED as zero-field.
/// </summary>
public sealed class BoltResponseDecodeTests
{
    private readonly BoltMessageCodec _codec = new();

    [Fact]
    public void Success_Empty_Decodes()
    {
        var response = _codec.DecodeResponse(BoltMessageFixtures.B0B.SuccessEmpty);

        var success = Assert.IsType<SuccessResponse>(response);
        Assert.Equal(BoltMessageSignatures.Success, success.Signature);
        Assert.Empty(success.Metadata.Entries);
        Assert.Null(success.Server);
        Assert.Null(success.ConnectionId);
    }

    [Fact]
    public void Success_HelloMetadata_DecodesTypedAccessors()
    {
        var response = _codec.DecodeResponse(BoltMessageFixtures.B0B.HelloSuccess);

        var success = Assert.IsType<SuccessResponse>(response);
        Assert.Equal("Neo4j/5.26.0", success.Server);
        Assert.Equal("bolt-b0b-1", success.ConnectionId);
        var hints = Assert.IsType<PackStreamMap>(success.Hints);
        Assert.Empty(hints.Entries);
    }

    [Fact]
    public void Success_RunMetadata_DecodesFieldsAndHasMore()
    {
        var response = _codec.DecodeResponse(BoltMessageFixtures.B0B.RunSuccessFieldsAnswer);

        var success = Assert.IsType<SuccessResponse>(response);
        Assert.Equal(new[] { "answer" }, success.Fields);
        Assert.Null(success.HasMore);
        Assert.Null(success.Qid);

        var terminal = Assert.IsType<SuccessResponse>(
            _codec.DecodeResponse(BoltMessageFixtures.B0B.SuccessHasMoreFalse));
        Assert.False(terminal.HasMore!.Value);
    }

    [Fact]
    public void Success_LogonMetadata_Decodes5_8Fields()
    {
        var advertised = Assert.IsType<SuccessResponse>(
            _codec.DecodeResponse(BoltMessageFixtures.Spec5_8.LogonSuccessAdvertisedAddress));
        Assert.Equal("graphz.example.com:7687", advertised.AdvertisedAddress);

        var expired = Assert.IsType<SuccessResponse>(
            _codec.DecodeResponse(BoltMessageFixtures.Spec5_8.LogonSuccessCredentialsExpired));
        Assert.True(expired.CredentialsExpired!.Value);
    }

    [Fact]
    public void Record_Scalar_Decodes()
    {
        var response = _codec.DecodeResponse(BoltMessageFixtures.B0B.Record42);

        var record = Assert.IsType<RecordResponse>(response);
        Assert.Equal(BoltMessageSignatures.Record, record.Signature);
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(record.Data.Items[0]).Value);
    }

    [Fact]
    public void Record_TypedGraphValues_DecodeWithDistinctIdentities()
    {
        var response = _codec.DecodeResponse(BoltMessageFixtures.B0B.RecordGraph);

        var record = Assert.IsType<RecordResponse>(response);
        Assert.Equal(3, record.Data.Items.Count);

        var alice = Assert.IsType<PackStreamNode>(record.Data.Items[0]);
        var relationship = Assert.IsType<PackStreamRelationship>(record.Data.Items[1]);
        var bob = Assert.IsType<PackStreamNode>(record.Data.Items[2]);

        Assert.Equal(1, alice.Id);
        Assert.Equal("n:1", alice.ElementId);
        Assert.Equal(new[] { "Person" }, alice.Labels.ToArray());
        Assert.Equal("Alice", BoltMessageFieldsTestAccess.GetString(alice.Properties, "name"));

        Assert.Equal(1, relationship.Id);
        Assert.Equal(1, relationship.StartNodeId);
        Assert.Equal(2, relationship.EndNodeId);
        Assert.Equal("KNOWS", relationship.Type);
        Assert.Equal("r:1", relationship.ElementId);
        Assert.Equal("n:1", relationship.StartNodeElementId);
        Assert.Equal("n:2", relationship.EndNodeElementId);
        Assert.Equal(2024, BoltMessageFieldsTestAccess.GetInteger(relationship.Properties, "since"));

        Assert.Equal(2, bob.Id);
        Assert.Equal("n:2", bob.ElementId);
        Assert.Equal("Bob", BoltMessageFieldsTestAccess.GetString(bob.Properties, "name"));

        // Node and relationship intentionally share the numeric id 1 but are distinct typed values.
        Assert.IsType<PackStreamNode>(record.Data.Items[0]);
        Assert.IsType<PackStreamRelationship>(record.Data.Items[1]);
    }

    [Fact]
    public void Failure_GqlMetadata_DecodesTypedAccessors()
    {
        var response = _codec.DecodeResponse(BoltMessageFixtures.B0B.FailureGql);

        var failure = Assert.IsType<FailureResponse>(response);
        Assert.Equal(BoltMessageSignatures.Failure, failure.Signature);
        Assert.Equal("42001", failure.GqlStatus);
        Assert.Equal("Neo.ClientError.Statement.SyntaxError", failure.Neo4jCode);
        Assert.Equal("Invalid input for the deliberately unsupported query.", failure.Description);
        Assert.NotNull(failure.DiagnosticRecord);
        Assert.Empty(failure.DiagnosticRecord!.Entries);
        Assert.Null(failure.Cause);
    }

    [Fact]
    public void Failure_NestedCause_DecodesRecursively()
    {
        var response = _codec.DecodeResponse(BoltMessageFixtures.Spec5_8.FailureWithCause);

        var failure = Assert.IsType<FailureResponse>(response);
        Assert.Equal("50N42", failure.GqlStatus);
        var cause = Assert.IsType<PackStreamMap>(failure.Cause);
        Assert.Equal("50N09", BoltMessageFieldsTestAccess.GetString(cause, "gql_status"));
        Assert.Equal(
            "Neo.ClientError.Request.Invalid",
            BoltMessageFieldsTestAccess.GetString(cause, "neo4j_code"));
    }

    [Fact]
    public void Ignored_SpecZeroFieldForm_Decodes()
    {
        Assert.Same(IgnoredResponse.Instance, _codec.DecodeResponse(BoltMessageFixtures.Spec5_8.Ignored));
    }

    [Fact]
    public void Ignored_ProducerOneFieldForm_IsRejectedAsArityError()
    {
        // Recorded B0-B fixture discrepancy: the producer emitted B1 7E A0 (one empty-map field).
        // The Bolt 5.8 specification defines IGNORED with no fields; the qualified driver tolerates
        // the one-field form because its deserializer reads no IGNORED fields and the pipelined
        // reader discards unconsumed structure bytes. This codec follows the specification.
        var exception = Assert.Throws<BoltMessageException>(
            () => _codec.DecodeResponse(BoltMessageFixtures.B0B.ProducerIgnoredOneField));

        Assert.Equal(BoltMessageErrorCategory.StructureArity, exception.Category);
    }
}
