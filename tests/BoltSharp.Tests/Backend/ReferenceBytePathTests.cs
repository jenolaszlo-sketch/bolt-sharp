using BoltSharp.Backend;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Tests.Messages;
using BoltSharp.Tests.Sessions;

using Xunit;

namespace BoltSharp.Tests.Backend;

/// <summary>
/// B3-C5 (Level C): protocol-byte integration where the existing public APIs support it. Requests
/// are decoded from authentic B3-A/B0-B bytes (B3-A has no request encoder); responses are encoded
/// with the B3-A codec and compared byte-for-byte against the retained B0-B golden fixtures.
/// No transport listener is involved.
/// </summary>
public sealed class ReferenceBytePathTests
{
    private static readonly BoltMessageCodec Codec = new();

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    // Authentic RUN "RETURN 42 AS answer" with params {} and extra {"mode":"r"} (t03 capture).
    private const string RunScalarHex = "B310D01352455455524E20343220415320616E73776572A0A1846D6F64658172";

    // Authentic PULL {n: 1000}.
    private const string PullHex = "B13FA1816EC903E8";

    [Fact]
    public void AuthenticScalarRunAndPull_RespondWithGoldenBytes()
    {
        var session = BackendTestSupport.Authenticated(new CountingProvider());

        var run = session.ReceiveBytes(Hex(RunScalarHex));

        var success = SessionHarness.SingleResponse<SuccessResponse>(run);
        Assert.Equal(new[] { "answer" }, success.Fields);
        Assert.Equal(BoltConnectionState.Streaming, run.State);
        Assert.Equal(BoltMessageFixtures.B0B.RunSuccessFieldsAnswer, Codec.EncodeResponse(success));

        var pull = session.ReceiveBytes(Hex(PullHex));

        Assert.Equal(2, pull.Responses.Count);
        var record = Assert.IsType<RecordResponse>(pull.Responses[0]);
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(Assert.Single(record.Data.Items)).Value);

        // Byte-for-byte equal to the retained B0-B golden RECORD [42].
        Assert.Equal(BoltMessageFixtures.B0B.Record42, Codec.EncodeResponse(record));

        // The accepted B3-B final summary is SUCCESS {} (no has_more field at exhaustion); the
        // retained real-server t03 golden SuccessHasMoreFalse explicitly carries has_more:false.
        // B3-C does not change B3-B emission semantics; the distinction is recorded as a B5
        // driver-interoperability observation in the B3-C report.
        Assert.Equal("B170A0", Convert.ToHexString(Codec.EncodeResponse(pull.Responses[1])));
        Assert.Equal(BoltConnectionState.Ready, pull.State);

        // Round-trip: the encoded bytes decode back to the same values through the accepted codec.
        var decoded = Codec.DecodeResponse(Codec.EncodeResponse(record));
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(
            Assert.Single(Assert.IsType<RecordResponse>(decoded).Data.Items)).Value);
    }

    [Fact]
    public void GraphQuery_ResponsesMatchRetainedB0BGoldenGraphBytes()
    {
        // No authentic RUN bytes exist for this query shape; the request is typed, while the
        // responses are compared byte-for-byte with the authentic t04 golden fixture.
        var session = BackendTestSupport.Authenticated(new CountingProvider());
        var run = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.GraphQueryText));

        var success = SessionHarness.SingleResponse<SuccessResponse>(run);
        Assert.Equal(new[] { "a", "r", "b" }, success.Fields);
        Assert.Equal(BoltMessageFixtures.B0B.RunSuccessFieldsGraph, Codec.EncodeResponse(success));

        var pull = session.Receive(new PullRequest(-1, null));
        var record = Assert.IsType<RecordResponse>(pull.Responses[0]);

        Assert.Equal(BoltMessageFixtures.B0B.RecordGraph, Codec.EncodeResponse(record));

        // Decode the golden bytes back and verify the graph values survive the full codec path.
        var decoded = Assert.IsType<RecordResponse>(Codec.DecodeResponse(Codec.EncodeResponse(record)));
        var alice = Assert.IsType<PackStreamNode>(decoded.Data.Items[0]);
        var knows = Assert.IsType<PackStreamRelationship>(decoded.Data.Items[1]);
        var bob = Assert.IsType<PackStreamNode>(decoded.Data.Items[2]);
        Assert.Equal(1, alice.Id);
        Assert.Equal("n:1", alice.ElementId);
        Assert.Equal("Person", Assert.Single(alice.Labels));
        Assert.Equal("Alice", BoltMessageFieldsTestAccess.GetString(alice.Properties, "name"));
        Assert.Equal(1, knows.Id);
        Assert.Equal(1, knows.StartNodeId);
        Assert.Equal(2, knows.EndNodeId);
        Assert.Equal("KNOWS", knows.Type);
        Assert.Equal("r:1", knows.ElementId);
        Assert.Equal("n:1", knows.StartNodeElementId);
        Assert.Equal("n:2", knows.EndNodeElementId);
        Assert.Equal(2024, BoltMessageFieldsTestAccess.GetInteger(knows.Properties, "since"));
        Assert.Equal(2, bob.Id);
        Assert.Equal("n:2", bob.ElementId);
    }

    [Fact]
    public void PropertyQuery_PullBytes_AreWellFormedAndDecodable()
    {
        var session = BackendTestSupport.Authenticated(new CountingProvider());
        session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText));

        var pull = session.Receive(new PullRequest(-1, null));

        foreach (var response in pull.Responses)
        {
            var bytes = Codec.EncodeResponse(response);
            var decoded = Codec.DecodeResponse(bytes);
            Assert.Equal(response.GetType(), decoded.GetType());
        }

        var first = Assert.IsType<RecordResponse>(Codec.DecodeResponse(Codec.EncodeResponse(pull.Responses[0])));
        Assert.Equal("Alice", Assert.IsType<PackStreamString>(Assert.Single(first.Data.Items)).Value);
    }
}
