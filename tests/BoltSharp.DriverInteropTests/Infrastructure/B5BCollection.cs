using Xunit;

namespace BoltSharp.DriverInteropTests.Infrastructure;

/// <summary>
/// Serializes the interoperability scenarios. Each scenario owns an ephemeral-port server and a
/// real driver; running them sequentially keeps the wire captures, timing observations and
/// counter assertions deterministic.
/// </summary>
[CollectionDefinition("B5-B")]
public sealed class B5BCollection
{
}
