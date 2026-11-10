using Xunit;

namespace BoltSharp.B5BQ1.Probes.Infrastructure;

/// <summary>
/// All Q1 probes run sequentially in one collection: the scenarios share nothing, but sequential
/// execution keeps loopback timing, counters and evidence deterministic.
/// </summary>
[CollectionDefinition("Q1", DisableParallelization = true)]
public sealed class Q1Collection;
