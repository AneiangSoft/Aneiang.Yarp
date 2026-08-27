namespace Aneiang.Yarp.Models;

/// <summary>
/// Per-item outcome of a batch operation (e.g. batch delete, batch enable).
/// Independent items may succeed or fail individually within the same atomic batch.
/// </summary>
/// <param name="Id">The target identifier (cluster id, route id, or binding id).</param>
/// <param name="Success">Whether this single item succeeded.</param>
/// <param name="Message">Human-readable per-item message (success note or failure reason).</param>
public readonly record struct BatchItemResult(string Id, bool Success, string Message);

/// <summary>
/// Aggregate result of an atomic batch operation. All items share one lock,
/// one version bump, one publish, and one persist; per-item outcomes are
/// reported via <see cref="Items"/>.
/// </summary>
public sealed class BatchOperationResult
{
    /// <summary>True only when every item succeeded.</summary>
    public bool Success => Failed == 0 && Items.Count > 0;

    /// <summary>Number of items that succeeded.</summary>
    public int Succeeded { get; init; }

    /// <summary>Number of items that failed (including skipped-as-failure if applicable).</summary>
    public int Failed { get; init; }

    /// <summary>Total items processed.</summary>
    public int Total => Items.Count;

    /// <summary>Per-item results in input order.</summary>
    public IReadOnlyList<BatchItemResult> Items { get; init; } = Array.Empty<BatchItemResult>();

    /// <summary>Aggregate human-readable message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Build a result from raw per-item outcomes.</summary>
    public static BatchOperationResult From(IEnumerable<BatchItemResult> items, string summary)
    {
        var list = items as IReadOnlyList<BatchItemResult> ?? items.ToList();
        var succeeded = list.Count(x => x.Success);
        var failed = list.Count - succeeded;
        var detail = failed == 0
            ? $"{summary} ({succeeded} succeeded)"
            : $"{summary} ({succeeded} succeeded, {failed} failed)";
        return new BatchOperationResult
        {
            Items = list,
            Succeeded = succeeded,
            Failed = failed,
            Message = detail
        };
    }
}
