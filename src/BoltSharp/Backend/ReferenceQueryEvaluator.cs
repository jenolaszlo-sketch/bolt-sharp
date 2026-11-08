using System.Text;

using BoltSharp.Messages;
using BoltSharp.PackStream;

namespace BoltSharp.Backend;

/// <summary>
/// The outcome of evaluating a query against the reference graph. When <see cref="IsSupported"/>
/// is <see langword="false"/>, <see cref="RefusalReason"/> identifies the reviewer-visible reason
/// (the accepted B3-B provider seam has no way to convey it on the wire; every refusal maps to
/// the session's deterministic 50N42 <c>Request.Invalid</c> FAILURE).
/// </summary>
public sealed class ReferenceQueryEvaluation
{
    private static readonly IReadOnlyList<string> NoFields = Array.Empty<string>();
    private static readonly IReadOnlyList<IReadOnlyList<PackStreamValue?>> NoRecords =
        Array.Empty<IReadOnlyList<PackStreamValue?>>();

    private ReferenceQueryEvaluation(
        bool isSupported,
        IReadOnlyList<string> fields,
        IReadOnlyList<IReadOnlyList<PackStreamValue?>> records,
        string? refusalReason)
    {
        IsSupported = isSupported;
        Fields = fields;
        Records = records;
        RefusalReason = refusalReason;
    }

    /// <summary>True when the query matched a supported form and evaluated successfully.</summary>
    public bool IsSupported { get; }

    /// <summary>The RUN field names ("answer", "n.name", "a"..."r"..."b"). Empty when refused.</summary>
    public IReadOnlyList<string> Fields { get; }

    /// <summary>The finite evaluated records. Empty when refused.</summary>
    public IReadOnlyList<IReadOnlyList<PackStreamValue?>> Records { get; }

    /// <summary>Diagnostic refusal reason, or <see langword="null"/> when supported.</summary>
    public string? RefusalReason { get; }

    /// <summary>Creates a supported evaluation.</summary>
    public static ReferenceQueryEvaluation Supported(
        IReadOnlyList<string> fields,
        IReadOnlyList<IReadOnlyList<PackStreamValue?>> records)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(records);
        return new ReferenceQueryEvaluation(true, fields, records, null);
    }

    /// <summary>Creates a refused evaluation with a diagnostic reason.</summary>
    public static ReferenceQueryEvaluation Refused(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new ReferenceQueryEvaluation(false, NoFields, NoRecords, reason);
    }
}

/// <summary>
/// Recognizes and evaluates the deliberately small, explicitly allowlisted query surface of the
/// reference backend. This is <b>not</b> a Cypher parser: the only normalization is whitespace
/// collapsing (all whitespace runs become single spaces, surrounding whitespace is removed),
/// and the normalized text must match one of the documented forms exactly. Keyword case is
/// significant; lowercase variants are refused as unsupported. Unsupported text (writes,
/// multi-statement text, substring tricks, unknown forms) is refused — never silently executed
/// and never answered with a fabricated empty result.
/// </summary>
public sealed class ReferenceQueryEvaluator
{
    /// <summary>Refusal reason: the normalized query matched no supported form.</summary>
    public const string UnsupportedQueryReason = "unsupported-query";

    /// <summary>Refusal reason: the query needs the <c>value</c> parameter, which was absent.</summary>
    public const string MissingParameterReason = "missing-parameter";

    /// <summary>Supported form: <c>RETURN 42 AS answer</c>.</summary>
    public const string ScalarQueryText = "RETURN 42 AS answer";

    /// <summary>Supported form: <c>RETURN $value AS answer</c>.</summary>
    public const string ParameterQueryText = "RETURN $value AS answer";

    /// <summary>Supported form: <c>MATCH (n:Person) RETURN n.name</c>.</summary>
    public const string PropertyQueryText = "MATCH (n:Person) RETURN n.name";

    /// <summary>Supported form: <c>MATCH (n:Person) RETURN n.name AS name ORDER BY name</c>.</summary>
    public const string OrderedPropertyQueryText = "MATCH (n:Person) RETURN n.name AS name ORDER BY name";

    /// <summary>Supported form: <c>MATCH (a:Person)-[r:KNOWS]-&gt;(b:Person) RETURN a, r, b</c>.</summary>
    public const string GraphQueryText = "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b";

    /// <summary>The parameter name read by <see cref="ParameterQueryText"/>.</summary>
    public const string ValueParameterName = "value";

    private readonly ReferenceGraph _graph;

    /// <summary>Creates an evaluator over the given graph (defaults to <see cref="ReferenceGraph.Default"/>).</summary>
    public ReferenceQueryEvaluator(ReferenceGraph? graph = null) => _graph = graph ?? ReferenceGraph.Default;

    /// <summary>
    /// Collapses all whitespace runs to single spaces and removes surrounding whitespace. This is
    /// the only accepted normalization; no token rewriting, case folding, comment stripping or
    /// semicolon handling occurs.
    /// </summary>
    public static string Normalize(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var builder = new StringBuilder(query.Length);
        var pendingSpace = false;
        foreach (var ch in query)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Evaluates one query. Supported forms are matched against the normalized text with ordinal
    /// (case-sensitive) comparison; everything else is refused with
    /// <see cref="UnsupportedQueryReason"/>.
    /// </summary>
    public ReferenceQueryEvaluation Evaluate(string query, PackStreamMap parameters)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(parameters);

        switch (Normalize(query))
        {
            case ScalarQueryText:
                return ReferenceQueryEvaluation.Supported(
                    new[] { "answer" },
                    new[] { Row(new PackStreamInteger(42)) });

            case ParameterQueryText:
                if (!TryGetParameter(parameters, ValueParameterName, out var value))
                {
                    return ReferenceQueryEvaluation.Refused(MissingParameterReason);
                }

                // The typed value is passed through unchanged: never stringified, interpolated or
                // re-parsed. An explicit null parameter survives as a null record value.
                return ReferenceQueryEvaluation.Supported(
                    new[] { "answer" },
                    new[] { Row(value) });

            case PropertyQueryText:
                return ReferenceQueryEvaluation.Supported(
                    new[] { "n.name" },
                    ProjectPersonNames(orderByName: false, aliased: false));

            case OrderedPropertyQueryText:
                return ReferenceQueryEvaluation.Supported(
                    new[] { "name" },
                    ProjectPersonNames(orderByName: true, aliased: true));

            case GraphQueryText:
                return ReferenceQueryEvaluation.Supported(new[] { "a", "r", "b" }, ProjectKnowsPattern());

            default:
                return ReferenceQueryEvaluation.Refused(UnsupportedQueryReason);
        }
    }

    private IReadOnlyList<IReadOnlyList<PackStreamValue?>> ProjectPersonNames(bool orderByName, bool aliased)
    {
        _ = aliased; // the field-name choice is applied by the caller; kept for readable call sites
        var projected = new List<IReadOnlyList<PackStreamValue?>>();
        foreach (var node in _graph.Nodes)
        {
            if (!node.Labels.Contains(ReferenceGraph.PersonLabel))
            {
                continue;
            }

            projected.Add(Row(ReadProperty(node.Properties, ReferenceGraph.NameProperty)));
        }

        if (!orderByName)
        {
            // Deterministic fixture enumeration order (graph construction order), not a Cypher
            // row-order guarantee.
            return projected;
        }

        // Ascending ordinal ordering, matching the documented ORDER BY name variant only.
        return projected
            .OrderBy(row => (row[0] as PackStreamString)?.Value, StringComparer.Ordinal)
            .ToList();
    }

    private IReadOnlyList<IReadOnlyList<PackStreamValue?>> ProjectKnowsPattern()
    {
        var records = new List<IReadOnlyList<PackStreamValue?>>();
        foreach (var relationship in _graph.Relationships)
        {
            if (!string.Equals(relationship.Type, ReferenceGraph.KnowsType, StringComparison.Ordinal))
            {
                continue;
            }

            var start = FindNode(relationship.StartNodeId);
            var end = FindNode(relationship.EndNodeId);
            if (start is null || end is null
                || !start.Labels.Contains(ReferenceGraph.PersonLabel)
                || !end.Labels.Contains(ReferenceGraph.PersonLabel))
            {
                continue;
            }

            // The record references the graph's own node and relationship instances: the result is
            // derived from the graph model, not from a detached hard-coded record.
            records.Add(Row(start, relationship, end));
        }

        return records;
    }

    private PackStreamNode? FindNode(long id)
    {
        foreach (var node in _graph.Nodes)
        {
            if (node.Id == id)
            {
                return node;
            }
        }

        return null;
    }

    private static PackStreamValue? ReadProperty(PackStreamMap properties, string key)
    {
        // Last-wins lookup, consistent with the accepted map read semantics.
        PackStreamValue? value = null;
        foreach (var entry in properties.Entries)
        {
            if (string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                value = entry.Value;
            }
        }

        return value;
    }

    private static bool TryGetParameter(PackStreamMap parameters, string key, out PackStreamValue? value)
    {
        var found = false;
        value = null;
        foreach (var entry in parameters.Entries)
        {
            if (string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                value = entry.Value;
                found = true;
            }
        }

        return found;
    }

    private static IReadOnlyList<PackStreamValue?> Row(params PackStreamValue?[] values) => values;
}

/// <summary>
/// The reference backend's RUN-option (capability) policy. Only the default auto-commit read
/// behavior is accepted: no options, or <c>mode = "r"</c>. Explicit null <c>db</c>/<c>imp_user</c>
/// entries are treated as "not requested"; any other key or value (write mode, database
/// selection, impersonation, transaction metadata, timeouts, bookmarks, unknown options) is
/// refused, because silently ignoring them could change execution semantics or access scope.
/// </summary>
public static class ReferenceCapabilityPolicy
{
    /// <summary>RUN extra key selecting the access mode.</summary>
    public const string ModeOption = "mode";

    /// <summary>RUN extra key selecting a database.</summary>
    public const string DatabaseOption = "db";

    /// <summary>RUN extra key selecting impersonation.</summary>
    public const string ImpersonationOption = "imp_user";

    /// <summary>The only accepted mode value (read).</summary>
    public const string ReadOnlyMode = "r";

    /// <summary>Refusal reason: an unsupported or unsafe RUN option was supplied.</summary>
    public const string UnsupportedOptionReason = "unsupported-run-option";

    /// <summary>
    /// Returns true when the RUN extra map requests only the supported read-only auto-commit
    /// behavior. Every entry is inspected; duplicates cannot smuggle an unsafe value past the
    /// policy.
    /// </summary>
    public static bool TryAcceptExtras(PackStreamMap extra, out string? refusalReason)
    {
        ArgumentNullException.ThrowIfNull(extra);
        foreach (var entry in extra.Entries)
        {
            switch (entry.Key)
            {
                case ModeOption:
                    if (entry.Value is PackStreamString mode
                        && string.Equals(mode.Value, ReadOnlyMode, StringComparison.Ordinal))
                    {
                        break;
                    }

                    refusalReason = UnsupportedOptionReason;
                    return false;

                case DatabaseOption:
                case ImpersonationOption:
                    if (entry.Value is null)
                    {
                        break;
                    }

                    refusalReason = UnsupportedOptionReason;
                    return false;

                default:
                    refusalReason = UnsupportedOptionReason;
                    return false;
            }
        }

        refusalReason = null;
        return true;
    }
}
