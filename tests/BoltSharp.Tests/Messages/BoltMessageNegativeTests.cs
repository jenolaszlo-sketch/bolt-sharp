using BoltSharp.Messages;

using Xunit;

namespace BoltSharp.Tests.Messages;

/// <summary>
/// Negative decoding tests. Each case asserts the exact <see cref="BoltMessageErrorCategory"/> —
/// not merely that some exception occurred — so the deterministic error contract is pinned.
/// </summary>
public sealed class BoltMessageNegativeTests
{
    private readonly BoltMessageCodec _codec = new();

    private void AssertCategory(
        byte[] payload,
        BoltMessageErrorCategory expected,
        Func<BoltMessageCodec, byte[], object?> decode)
    {
        var exception = Assert.Throws<BoltMessageException>(() => decode(_codec, payload));
        Assert.Equal(expected, exception.Category);
    }

    // ---- Payload-level failures ----

    [Fact]
    public void EmptyPayload_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.Empty,
            BoltMessageErrorCategory.EmptyPayload,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void NonStructureTopLevel_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.NotAStructure,
            BoltMessageErrorCategory.NotAStructure,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void TruncatedPayload_IsRejectedAsTruncated()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.TruncatedRun,
            BoltMessageErrorCategory.Truncated,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void TrailingBytes_AfterCompleteStructure_AreRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.TrailingBytesAfterReset,
            BoltMessageErrorCategory.TrailingBytes,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void InvalidNestedPackStreamValue_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.InvalidNestedValue,
            BoltMessageErrorCategory.InvalidPackStreamValue,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void OversizedNestedList_ViolatingConfiguredLimit_IsRejected()
    {
        var limits = new BoltSharp.PackStream.PackStreamLimits(maxListItems: 8);
        var codec = new BoltMessageCodec(limits);
        var items = Enumerable.Range(0, 9)
            .Select(_ => BoltSharp.Tests.Messages.IndependentPackStream.Int(1))
            .ToArray();
        var payload = BoltSharp.Tests.Messages.IndependentPackStream.Struct(
            0x01,
            BoltSharp.Tests.Messages.IndependentPackStream.Map(
                ("user_agent", BoltSharp.Tests.Messages.IndependentPackStream.List(items))));

        var exception = Assert.Throws<BoltMessageException>(() => codec.DecodeRequest(payload));
        Assert.Equal(BoltMessageErrorCategory.InvalidPackStreamValue, exception.Category);
    }

    // ---- Signature classification ----

    [Fact]
    public void UnknownSignature_IsRejectedAsUnknown()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.UnknownRequestSignature,
            BoltMessageErrorCategory.UnknownSignature,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void GraphStructureAsTopLevelMessage_IsRejected()
    {
        // A Bolt graph structure (here: Node, tag 0x4E) is not a message signature. The PackStream
        // layer decodes it into its typed graph value before message dispatch, so the codec
        // classifies it as NotAStructure rather than UnknownSignature — the distinction is
        // deterministic either way and pinned here.
        AssertCategory(
            BoltMessageFixtures.Negative.NodeAsMessage,
            BoltMessageErrorCategory.NotAStructure,
            (c, p) => c.DecodeRequest(p));
    }

    [Theory]
    [InlineData(nameof(BoltMessageFixtures.Negative.UnsupportedBegin))]
    [InlineData(nameof(BoltMessageFixtures.Negative.UnsupportedCommit))]
    [InlineData(nameof(BoltMessageFixtures.Negative.UnsupportedRollback))]
    [InlineData(nameof(BoltMessageFixtures.Negative.UnsupportedRoute))]
    public void KnownButUnsupportedMessages_AreClassifiedAsUnsupported(string fixtureName)
    {
        var fixture = typeof(BoltMessageFixtures.Negative)
            .GetField(fixtureName)!
            .GetValue(null) as byte[] ?? throw new InvalidOperationException(fixtureName);

        AssertCategory(fixture, BoltMessageErrorCategory.UnsupportedMessage, (c, p) => c.DecodeRequest(p));
    }

    // ---- HELLO ----

    [Fact]
    public void Hello_WrongArity_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.HelloWrongArity,
            BoltMessageErrorCategory.StructureArity,
            (c, p) => c.DecodeRequest(p));
        AssertCategory(
            BoltMessageFixtures.Negative.HelloEmpty,
            BoltMessageErrorCategory.StructureArity,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void Hello_ExtraNotMap_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.HelloExtraNotMap,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void Hello_MissingUserAgent_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.HelloMissingUserAgent,
            BoltMessageErrorCategory.MissingRequiredField,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void Hello_UserAgentWrongType_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.HelloUserAgentWrongType,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void Hello_ProhibitedLegacyFields_AreRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.HelloProhibitedScheme,
            BoltMessageErrorCategory.ProhibitedField,
            (c, p) => c.DecodeRequest(p));
        AssertCategory(
            BoltMessageFixtures.Negative.HelloProhibitedPatchBolt,
            BoltMessageErrorCategory.ProhibitedField,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void Hello_BoltAgentShapeViolations_AreRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.HelloBoltAgentWrongType,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeRequest(p));
        AssertCategory(
            BoltMessageFixtures.Negative.HelloBoltAgentMissingProduct,
            BoltMessageErrorCategory.MissingRequiredField,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void Hello_RoutingWrongValueType_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.HelloRoutingWrongValueType,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void Hello_ClassificationsWrongItemType_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.HelloClassificationsWrongItemType,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeRequest(p));
    }

    // ---- LOGON ----

    [Fact]
    public void Logon_AuthNotMap_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.LogonAuthNotMap,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void Logon_MissingOrWrongTypedScheme_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.LogonMissingScheme,
            BoltMessageErrorCategory.MissingRequiredField,
            (c, p) => c.DecodeRequest(p));
        AssertCategory(
            BoltMessageFixtures.Negative.LogonSchemeWrongType,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void Logon_SchemeShapeViolations_AreRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.LogonBasicMissingCredentials,
            BoltMessageErrorCategory.MissingRequiredField,
            (c, p) => c.DecodeRequest(p));
        AssertCategory(
            BoltMessageFixtures.Negative.LogonBasicPrincipalWrongType,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeRequest(p));
        AssertCategory(
            BoltMessageFixtures.Negative.LogonBearerMissingCredentials,
            BoltMessageErrorCategory.MissingRequiredField,
            (c, p) => c.DecodeRequest(p));
    }

    // ---- RUN ----

    [Fact]
    public void Run_WrongArity_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.RunWrongArity,
            BoltMessageErrorCategory.StructureArity,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void Run_WrongQueryOrParameterTypes_AreRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.RunQueryWrongType,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeRequest(p));
        AssertCategory(
            BoltMessageFixtures.Negative.RunParametersWrongType,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void Run_WrongExtraFieldType_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.RunTxTimeoutWrongType,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void Run_ProhibitedLegacyField_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.RunProhibitedCategories,
            BoltMessageErrorCategory.ProhibitedField,
            (c, p) => c.DecodeRequest(p));
    }

    // ---- PULL / DISCARD ----

    [Fact]
    public void Pull_MissingRequiredN_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.PullMissingN,
            BoltMessageErrorCategory.MissingRequiredField,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void Pull_WrongNType_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.PullNWrongType,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeRequest(p));
    }

    [Fact]
    public void Pull_OutOfRangeValues_AreRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.PullNOutOfRange,
            BoltMessageErrorCategory.InvalidFieldValue,
            (c, p) => c.DecodeRequest(p));
        AssertCategory(
            BoltMessageFixtures.Negative.PullQidOutOfRange,
            BoltMessageErrorCategory.InvalidFieldValue,
            (c, p) => c.DecodeRequest(p));
        AssertCategory(
            BoltMessageFixtures.Negative.DiscardNOutOfRange,
            BoltMessageErrorCategory.InvalidFieldValue,
            (c, p) => c.DecodeRequest(p));
    }

    // ---- Zero-arity messages ----

    [Fact]
    public void ZeroArityMessages_WithExtraFields_AreRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.ResetWithField,
            BoltMessageErrorCategory.StructureArity,
            (c, p) => c.DecodeRequest(p));
        AssertCategory(
            BoltMessageFixtures.Negative.GoodbyeWithField,
            BoltMessageErrorCategory.StructureArity,
            (c, p) => c.DecodeRequest(p));
        AssertCategory(
            BoltMessageFixtures.Negative.LogoffWithField,
            BoltMessageErrorCategory.StructureArity,
            (c, p) => c.DecodeRequest(p));
    }

    // ---- TELEMETRY ----

    [Fact]
    public void Telemetry_WrongArityOrType_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.TelemetryNoField,
            BoltMessageErrorCategory.StructureArity,
            (c, p) => c.DecodeRequest(p));
        AssertCategory(
            BoltMessageFixtures.Negative.TelemetryApiWrongType,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeRequest(p));
    }

    // ---- Responses ----

    [Fact]
    public void Success_MetadataNotMap_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.SuccessMetadataNotMap,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeResponse(p));
    }

    [Fact]
    public void Success_WrongMetadataTypes_AreRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.SuccessFieldsWrongType,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeResponse(p));
        AssertCategory(
            BoltMessageFixtures.Negative.SuccessHasMoreWrongType,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeResponse(p));
    }

    [Fact]
    public void Success_ProhibitedMetadata_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.SuccessProhibitedPatchBolt,
            BoltMessageErrorCategory.ProhibitedField,
            (c, p) => c.DecodeResponse(p));
    }

    [Fact]
    public void Record_FieldNotList_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.RecordFieldNotList,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeResponse(p));
    }

    [Fact]
    public void Record_WrongOuterArity_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.RecordWrongOuterArity,
            BoltMessageErrorCategory.StructureArity,
            (c, p) => c.DecodeResponse(p));
    }

    [Fact]
    public void Failure_ProhibitedPre57CodeField_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.FailureProhibitedCode,
            BoltMessageErrorCategory.ProhibitedField,
            (c, p) => c.DecodeResponse(p));
    }

    [Fact]
    public void Failure_CauseNotMap_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.FailureCauseNotMap,
            BoltMessageErrorCategory.InvalidFieldType,
            (c, p) => c.DecodeResponse(p));
    }

    [Fact]
    public void Response_UnknownSignature_IsRejected()
    {
        AssertCategory(
            BoltMessageFixtures.Negative.UnknownRequestSignature,
            BoltMessageErrorCategory.UnknownSignature,
            (c, p) => c.DecodeResponse(p));
    }

    // ---- Encoding-side schema validation ----

    [Fact]
    public void Encode_WrongTypedSuccessMetadata_IsRejected()
    {
        var metadata = new BoltSharp.PackStream.PackStreamMap(new[]
        {
            new KeyValuePair<string, BoltSharp.PackStream.PackStreamValue?>(
                "fields", new BoltSharp.PackStream.PackStreamInteger(1)),
        });
        var response = new SuccessResponse(metadata);

        var exception = Assert.Throws<BoltMessageException>(() => _codec.EncodeResponse(response));
        Assert.Equal(BoltMessageErrorCategory.InvalidFieldType, exception.Category);
    }

    [Fact]
    public void Encode_ProhibitedFailureCode_IsRejected()
    {
        var metadata = new BoltSharp.PackStream.PackStreamMap(new[]
        {
            new KeyValuePair<string, BoltSharp.PackStream.PackStreamValue?>(
                "code", new BoltSharp.PackStream.PackStreamString("Neo.DatabaseError.General.UnknownError")),
        });
        var response = new FailureResponse(metadata);

        var exception = Assert.Throws<BoltMessageException>(() => _codec.EncodeResponse(response));
        Assert.Equal(BoltMessageErrorCategory.ProhibitedField, exception.Category);
    }

    [Fact]
    public void Encode_DeeplyNestedCause_BeyondConfiguredDepth_IsRejected()
    {
        // Build a cause chain deeper than the configured PackStream depth limit (100).
        var metadata = new BoltSharp.PackStream.PackStreamMap(Array.Empty<KeyValuePair<string, BoltSharp.PackStream.PackStreamValue?>>());
        for (var i = 0; i < 105; i++)
        {
            metadata = new BoltSharp.PackStream.PackStreamMap(new[]
            {
                new KeyValuePair<string, BoltSharp.PackStream.PackStreamValue?>("cause", metadata),
            });
        }

        var response = new FailureResponse(metadata);

        var exception = Assert.Throws<BoltMessageException>(() => _codec.EncodeResponse(response));
        Assert.Equal(BoltMessageErrorCategory.InvalidPackStreamValue, exception.Category);
    }

    [Fact]
    public void Encode_UnknownResponseType_IsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => _codec.EncodeResponse(new UnknownResponse()));
        Assert.Contains("Unknown Bolt response type", exception.Message);
    }

    private sealed class UnknownResponse : BoltResponse
    {
        public override byte Signature => 0x99;
    }
}
