using BoltSharp.Handshake;

using Xunit;

namespace BoltSharp.Tests.Handshake;

/// <summary>
/// Version-honesty and normalization of <see cref="BoltHandshakeOptions"/>.
/// </summary>
public sealed class BoltHandshakeOptionsTests
{
    [Fact]
    public void Default_AdvertisesNothing()
    {
        Assert.Empty(BoltHandshakeOptions.Default.SupportedVersions);
        Assert.False(BoltHandshakeOptions.Default.ManifestV1Enabled);
        Assert.Equal(0UL, BoltHandshakeOptions.Default.OfferedCapabilities);
    }

    [Fact]
    public void SupportedVersions_AreDeduplicatedAndSortedDescending()
    {
        var options = new BoltHandshakeOptions(
            new[]
            {
                new BoltProtocolVersion(5, 7),
                new BoltProtocolVersion(4, 0),
                new BoltProtocolVersion(5, 8),
                new BoltProtocolVersion(5, 7),
            },
            manifestV1Enabled: false);

        Assert.Equal(
            new[]
            {
                new BoltProtocolVersion(5, 8),
                new BoltProtocolVersion(5, 7),
                new BoltProtocolVersion(4, 0),
            },
            options.SupportedVersions);
    }

    [Fact]
    public void Create_EnablesManifestByDefault()
    {
        var options = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) });

        Assert.True(options.ManifestV1Enabled);
        Assert.Equal(new[] { new BoltProtocolVersion(5, 8) }, options.SupportedVersions);
    }

    [Fact]
    public void Constructor_RequiresVersions()
    {
        Assert.Throws<ArgumentNullException>(
            () => new BoltHandshakeOptions(null!, manifestV1Enabled: false));
    }

    [Fact]
    public void DefaultLimits_AreDocumentedValues()
    {
        Assert.Equal(256, BoltHandshakeLimits.DefaultMaxManifestVersions);
        Assert.Equal(4_096, BoltHandshakeLimits.DefaultMaxHandshakeBytes);
        Assert.Equal(10, BoltVarInt.MaxEncodedLength);
    }
}
