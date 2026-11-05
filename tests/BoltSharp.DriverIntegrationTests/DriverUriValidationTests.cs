using Xunit;

namespace BoltSharp.DriverIntegrationTests;

/// <summary>
/// B2-C-R1 regression coverage for B2-C-Q1 finding Q1-F01: the driver scaffold must accept only
/// direct Bolt schemes and reject routing, unsupported or malformed URIs. This is URI validation
/// only; it does not establish or attempt connectivity.
/// </summary>
public sealed class DriverUriValidationTests
{
    [Theory]
    [InlineData("bolt://127.0.0.1:7687")]
    [InlineData("bolt://localhost")]
    [InlineData("bolt://localhost:7687/")]
    [InlineData("bolt+s://db.example.com:7687")]
    [InlineData("bolt+ssc://127.0.0.1:7687")]
    public void ValidateUri_Accepts_Direct_Bolt_Schemes(string value)
    {
        var uri = DriverTestSettings.ValidateUri(value);
        Assert.StartsWith("bolt", uri.Scheme, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("neo4j://127.0.0.1:7687")]
    [InlineData("neo4j+s://127.0.0.1:7687")]
    [InlineData("neo4j+ssc://127.0.0.1:7687")]
    [InlineData("http://127.0.0.1:7687")]
    [InlineData("https://127.0.0.1:7687")]
    [InlineData("ws://127.0.0.1:7687")]
    [InlineData("wss://127.0.0.1:7687")]
    [InlineData("bolt://")]
    [InlineData("bolt://host/path")]
    [InlineData("bolt://host?x=1")]
    [InlineData("bolt://host#fragment")]
    [InlineData("bolt://user:secret@host")]
    [InlineData("not a uri")]
    [InlineData("")]
    public void ValidateUri_Rejects_Routing_Unsupported_Or_Malformed(string value)
    {
        Assert.Throws<ArgumentException>(() => DriverTestSettings.ValidateUri(value));
    }
}
