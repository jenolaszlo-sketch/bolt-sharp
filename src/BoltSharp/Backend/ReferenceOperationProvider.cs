using BoltSharp.Messages;
using BoltSharp.Sessions;

namespace BoltSharp.Backend;

/// <summary>
/// The B3-C reference backend: a read-only operation provider over the deterministic
/// <see cref="ReferenceGraph"/>. It implements the accepted provisional
/// <see cref="IBoltOperationProvider"/> seam without changing its shape.
/// <para>
/// <b>Behavior.</b> RUN options are checked against <see cref="ReferenceCapabilityPolicy"/>;
/// the query must match one of <see cref="ReferenceQueryEvaluator"/>'s explicitly documented,
/// whitespace-normalized forms. A successful preparation returns a fresh
/// <see cref="ReferenceResultOperation"/>; every refusal (unsupported query, missing parameter,
/// unsafe RUN option) returns <see langword="null"/>, which the B3-B session deterministically
/// maps to a recoverable 50N42 <c>Request.Invalid</c> FAILURE. The seam cannot convey a refusal
/// reason on the wire (bounded B3-C qualification).
/// </para>
/// <para>
/// <b>Ownership.</b> Operations are produced per RUN, are never shared or reused, own their
/// records immutably, and are retired by the session under the accepted exactly-once,
/// fail-closed contract. The provider retains no operations and mutates no graph state.
/// </para>
/// </summary>
public sealed class ReferenceOperationProvider : IBoltOperationProvider
{
    private readonly ReferenceQueryEvaluator _evaluator;

    /// <summary>Creates a provider over the given graph (defaults to <see cref="ReferenceGraph.Default"/>).</summary>
    public ReferenceOperationProvider(ReferenceGraph? graph = null) =>
        _evaluator = new ReferenceQueryEvaluator(graph);

    /// <inheritdoc/>
    public IBoltOperation? Prepare(RunRequest run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (!ReferenceCapabilityPolicy.TryAcceptExtras(run.Extra, out _))
        {
            return null;
        }

        var evaluation = _evaluator.Evaluate(run.Query, run.Parameters);
        return evaluation.IsSupported
            ? new ReferenceResultOperation(evaluation.Fields, evaluation.Records)
            : null;
    }
}
