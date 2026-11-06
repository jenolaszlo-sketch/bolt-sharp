using Xunit;

namespace BoltSharp.IntegrationTests.Infrastructure;

/// <summary>
/// Serializes the B0-B integration scenarios. Each scenario binds its own loopback port, but the
/// official driver creates process-wide background tasks; running the scenarios sequentially keeps
/// timing deterministic and avoids cross-test interference.
/// </summary>
[CollectionDefinition("B0-B", DisableParallelization = true)]
public sealed class B0BCollection
{
}
