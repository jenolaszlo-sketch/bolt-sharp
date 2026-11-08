using System.Collections.ObjectModel;

using BoltSharp.PackStream;
using BoltSharp.Sessions;

namespace BoltSharp.Backend;

/// <summary>
/// A finite, forward-only result cursor over records produced by the reference backend. It
/// implements the accepted <see cref="IBoltOperation"/> seam: the owning session validates the
/// field metadata once at acquisition (B3-B-R2), reads records incrementally through
/// <see cref="ReadRow"/>, and calls <see cref="Dispose"/> at most once when the result completes,
/// fails, is reset or the connection closes.
/// <para>
/// <b>Ownership.</b> A new instance is returned for every RUN; instances are never shared between
/// operations or sessions and never reused after disposal. Field names and records are
/// defensively copied at construction, so the operation cannot be mutated through the evaluator's
/// collections. <see cref="ReadRow"/> is incremental (an index over an immutable record list, no
/// queue, no background work) and stable after exhaustion: it keeps returning
/// <see langword="null"/> rather than resurrecting records. Use after <see cref="Dispose"/>
/// throws <see cref="ObjectDisposedException"/> deterministically.
/// </para>
/// <para>
/// <b>Disposal.</b> <see cref="Dispose"/> is idempotent and never throws. The B3-B session
/// enforces the accepted fail-closed contract: if a provider disposal ever threw, the connection
/// would become permanently unusable; this operation cannot trigger that path.
/// </para>
/// </summary>
public sealed class ReferenceResultOperation : IBoltOperation
{
    private readonly IReadOnlyList<IReadOnlyList<PackStreamValue?>> _records;
    private int _position;
    private bool _disposed;

    /// <summary>Creates a result cursor over the given field names and evaluated records.</summary>
    public ReferenceResultOperation(
        IReadOnlyList<string> fields,
        IReadOnlyList<IReadOnlyList<PackStreamValue?>> records)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(records);
        Fields = new ReadOnlyCollection<string>(CopyFields(fields));
        _records = new ReadOnlyCollection<IReadOnlyList<PackStreamValue?>>(CopyRecords(records));
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Fields { get; }

    /// <inheritdoc/>
    public IReadOnlyList<PackStreamValue?>? ReadRow()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(ReferenceResultOperation),
                "The result operation has been disposed and cannot produce more records.");
        }

        return _position < _records.Count ? _records[_position++] : null;
    }

    /// <inheritdoc/>
    public void Dispose() => _disposed = true;

    private static string[] CopyFields(IReadOnlyList<string> fields)
    {
        var copy = new string[fields.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = fields[i]
                ?? throw new ArgumentException("Result field names must not be null.", nameof(fields));
        }

        return copy;
    }

    private static IReadOnlyList<PackStreamValue?>[] CopyRecords(
        IReadOnlyList<IReadOnlyList<PackStreamValue?>> records)
    {
        var copy = new IReadOnlyList<PackStreamValue?>[records.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            var row = records[i]
                ?? throw new ArgumentException("Result records must not be null.", nameof(records));
            var rowCopy = new PackStreamValue?[row.Count];
            for (var j = 0; j < rowCopy.Length; j++)
            {
                rowCopy[j] = row[j];
            }

            copy[i] = rowCopy;
        }

        return copy;
    }
}
