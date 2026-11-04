using BoltSharp.Handshake;

using Xunit;

namespace BoltSharp.Tests.Handshake;

/// <summary>
/// Golden encodings, malformed encodings, expansion and range generation for
/// <see cref="BoltVersionRange"/>.
/// </summary>
public sealed class BoltVersionRangeTests
{
    [Fact]
    public void ToUInt32_MatchesSpecificationRangeExample()
    {
        // 00 02 08 05 is Bolt 5.8 down to 5.6.
        Assert.Equal(0x00020805u, new BoltVersionRange(5, 8, 2).ToUInt32());
        // 00 04 04 04 is Bolt 4.4 down to 4.0.
        Assert.Equal(0x00040404u, new BoltVersionRange(4, 4, 4).ToUInt32());
    }

    [Fact]
    public void SingleVersion_UsesVersion40Style()
    {
        Assert.Equal(0x00000104u, new BoltVersionRange(4, 1, 0).ToUInt32());
        Assert.Equal(HandshakeTestHelpers.Version(4, 1), new BoltVersionRange(4, 1, 0).ToUInt32());
    }

    [Theory]
    [InlineData(0x00020805u, 5, 8)]
    [InlineData(0x00000104u, 4, 1)]
    [InlineData(0x00040404u, 4, 4)]
    public void FromUInt32_RoundTrips(uint value, int major, int minor)
    {
        var range = BoltVersionRange.FromUInt32(value);

        Assert.Equal(major, range.Major);
        Assert.Equal(minor, range.Minor);
        Assert.Equal(value, range.ToUInt32());
    }

    [Fact]
    public void Expand_ReturnsVersionsHighestMinorFirst()
    {
        var versions = new BoltVersionRange(5, 8, 2).Expand();

        Assert.Equal(
            new[] { new BoltProtocolVersion(5, 8), new BoltProtocolVersion(5, 7), new BoltProtocolVersion(5, 6) },
            versions);
    }

    [Fact]
    public void Expand_SingleVersion_ReturnsOne()
    {
        Assert.Equal(new[] { new BoltProtocolVersion(5, 7) }, new BoltVersionRange(5, 7, 0).Expand());
    }

    [Fact]
    public void Contains_RespectsBoundsAndMajor()
    {
        var range = new BoltVersionRange(4, 4, 4);

        Assert.True(range.Contains(new BoltProtocolVersion(4, 4)));
        Assert.True(range.Contains(new BoltProtocolVersion(4, 0)));
        Assert.False(range.Contains(new BoltProtocolVersion(4, 5)));
        Assert.False(range.Contains(new BoltProtocolVersion(5, 0)));
    }

    [Fact]
    public void FromUInt32_RejectsReservedFirstByte()
    {
        Assert.Throws<BoltHandshakeFormatException>(() => BoltVersionRange.FromUInt32(0x01020304u));
    }

    [Fact]
    public void FromUInt32_RejectsRangeSpanningMajorVersion()
    {
        // range 2 below 4.1 would cross into major 3.
        Assert.Throws<BoltHandshakeFormatException>(
            () => BoltVersionRange.FromUInt32(HandshakeTestHelpers.Range(4, 1, 2)));
    }

    [Fact]
    public void FromUInt32_AcceptsMaximumSingleVersion()
    {
        var range = BoltVersionRange.FromUInt32(0x0000FFFFu);
        Assert.Equal(255, range.Major);
        Assert.Equal(255, range.Minor);
        Assert.Equal(0, range.Range);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(256, 0, 0)]
    [InlineData(0, 256, 0)]
    [InlineData(0, 0, 256)]
    [InlineData(4, 1, 2)]
    public void Constructor_RejectsInvalid(int major, int minor, int range)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoltVersionRange(major, minor, range));
    }

    [Fact]
    public void FromVersions_Empty_ReturnsEmpty()
    {
        Assert.Empty(BoltVersionRange.FromVersions(Array.Empty<BoltProtocolVersion>()));
    }

    [Fact]
    public void FromVersions_ContiguousRun_FormsOneRange()
    {
        var ranges = BoltVersionRange.FromVersions(new[]
        {
            new BoltProtocolVersion(5, 6),
            new BoltProtocolVersion(5, 8),
            new BoltProtocolVersion(5, 7),
        });

        Assert.Equal(new[] { new BoltVersionRange(5, 8, 2) }, ranges);
    }

    [Fact]
    public void FromVersions_MultipleMajors_MatchesSpecificationOffer()
    {
        var versions = new[]
        {
            new BoltProtocolVersion(4, 1),
            new BoltProtocolVersion(5, 7),
            new BoltProtocolVersion(4, 4),
            new BoltProtocolVersion(5, 6),
            new BoltProtocolVersion(4, 0),
            new BoltProtocolVersion(5, 8),
            new BoltProtocolVersion(4, 2),
            new BoltProtocolVersion(4, 3),
        };

        var ranges = BoltVersionRange.FromVersions(versions);

        Assert.Equal(
            new[] { new BoltVersionRange(5, 8, 2), new BoltVersionRange(4, 4, 4) },
            ranges);
    }

    [Fact]
    public void FromVersions_Gap_ProducesSeparateRanges()
    {
        var ranges = BoltVersionRange.FromVersions(new[]
        {
            new BoltProtocolVersion(5, 8),
            new BoltProtocolVersion(5, 6),
        });

        Assert.Equal(
            new[] { new BoltVersionRange(5, 8, 0), new BoltVersionRange(5, 6, 0) },
            ranges);
    }

    [Fact]
    public void FromVersions_DistinctMajors_OrderedDescending()
    {
        var ranges = BoltVersionRange.FromVersions(new[]
        {
            new BoltProtocolVersion(5, 8),
            new BoltProtocolVersion(6, 0),
        });

        Assert.Equal(
            new[] { new BoltVersionRange(6, 0, 0), new BoltVersionRange(5, 8, 0) },
            ranges);
    }

    [Fact]
    public void FromVersions_IsIndependentOfInputOrder()
    {
        var a = BoltVersionRange.FromVersions(new[]
        {
            new BoltProtocolVersion(5, 8),
            new BoltProtocolVersion(5, 7),
        });
        var b = BoltVersionRange.FromVersions(new[]
        {
            new BoltProtocolVersion(5, 7),
            new BoltProtocolVersion(5, 8),
            new BoltProtocolVersion(5, 8),
        });

        Assert.Equal(a, b);
    }
}
