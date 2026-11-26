using Xunit;

namespace BoltSharp.Tests.Transport;

/// <summary>
/// Tests that observe process-wide or timing-sensitive transport behaviour (unobserved task
/// faults, accept-loop reset accounting) run sequentially with respect to every other test
/// collection, so their measurements are not perturbed by concurrently running tests.
/// </summary>
[CollectionDefinition("NonParallelTransport", DisableParallelization = true)]
public sealed class NonParallelTransportCollection
{
}
