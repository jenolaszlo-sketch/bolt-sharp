using BoltSharp.Messages;
using BoltSharp.Transport;
using BoltSharp.PackStream;
using BoltSharp.Tests.Sessions;

using Xunit;

namespace BoltSharp.Tests.Transport;

/// <summary>§13.2: the mandatory Alice/KNOWS/Bob reference-graph conversation over real TCP.</summary>
public sealed class TcpGraphIntegrationTests
{
    public const string GraphQuery = "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b";

    /// <summary>The authentic B0-B/B3-C graph RECORD message bytes (golden, blob-verified).</summary>
    public const string GoldenRecordHex =
        "B17193B44E019186506572736F6EA1846E616D6585416C696365836E3A31B852010102854B4E4F5753A18573696E6365C907E883723A31836E3A31836E3A32" +
        "B44E029186506572736F6EA1846E616D6583426F62836E3A32";

    [Fact]
    public async Task GraphConversation_ReturnsAliceKnowsBob_WithExactGoldenBytes()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TransportTestSupport.ConnectAndAuthorize(server, GraphQuery);

        client.SendRequest(new PullRequest(1000, null));

        // Read the RECORD at the byte level first: exact framing/payload match with the accepted
        // golden vector (one chunked message, B1 71 ...).
        var raw = client.ReadFramedMessage();
        Assert.NotNull(raw);
        Assert.Equal(GoldenRecordHex, Convert.ToHexString(raw));

        // Decode through the accepted B3-A response decoder and verify structure.
        var record = Assert.IsType<RecordResponse>(client.Decode(raw));
        Assert.Equal(3, record.Data.Items.Count); // record arity: a, r, b

        var alice = Assert.IsType<PackStreamNode>(record.Data.Items[0]);
        var knows = Assert.IsType<PackStreamRelationship>(record.Data.Items[1]);
        var bob = Assert.IsType<PackStreamNode>(record.Data.Items[2]);

        Assert.Equal(1, alice.Id);
        Assert.Equal("Person", Assert.Single(alice.Labels));
        Assert.Equal("Alice", TransportTestSupport.GetStringEntry(alice.Properties, "name"));
        Assert.Equal("n:1", alice.ElementId);

        Assert.Equal(1, knows.Id);
        Assert.Equal("KNOWS", knows.Type);
        Assert.Equal(1, knows.StartNodeId);
        Assert.Equal(2, knows.EndNodeId);
        Assert.Equal("n:1", knows.StartNodeElementId);
        Assert.Equal("n:2", knows.EndNodeElementId);
        Assert.Equal("r:1", knows.ElementId);
        Assert.Equal(2024, Assert.IsType<PackStreamInteger>(
            KnowsProperty(knows, "since")).Value);

        Assert.Equal(2, bob.Id);
        Assert.Equal("Person", Assert.Single(bob.Labels));
        Assert.Equal("Bob", TransportTestSupport.GetStringEntry(bob.Properties, "name"));
        Assert.Equal("n:2", bob.ElementId);

        // Result completion: terminal SUCCESS without has_more (accepted B3-B semantics).
        var summary = Assert.IsType<SuccessResponse>(client.ReadResponse());
        Assert.NotEqual(true, summary.HasMore);
    }

    [Fact]
    public async Task GraphConversation_DecodedDirectionIsAliceToBob()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TransportTestSupport.ConnectAndAuthorize(server, GraphQuery);

        client.SendRequest(new PullRequest(1000, null));
        var responses = client.ReadResult();
        var record = Assert.IsType<RecordResponse>(responses[0]);
        var alice = Assert.IsType<PackStreamNode>(record.Data.Items[0]);
        var knows = Assert.IsType<PackStreamRelationship>(record.Data.Items[1]);
        var bob = Assert.IsType<PackStreamNode>(record.Data.Items[2]);

        // Correct direction: the relationship points from the first returned node to the second.
        Assert.Equal(alice.Id, knows.StartNodeId);
        Assert.Equal(bob.Id, knows.EndNodeId);
        Assert.Equal(alice.ElementId, knows.StartNodeElementId);
        Assert.Equal(bob.ElementId, knows.EndNodeElementId);
    }

    private static PackStreamValue? KnowsProperty(PackStreamRelationship knows, string key)
    {
        foreach (var entry in knows.Properties.Entries)
        {
            if (string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        throw new InvalidOperationException("missing property " + key);
    }
}
