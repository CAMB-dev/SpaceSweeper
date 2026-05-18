namespace SpaceSweeper.Core.Cleanup;

public sealed record CleanupResult(
    int RequestedCount,
    int CompletedCount,
    long RequestedBytes,
    IReadOnlyList<CleanupFailure> Failures)
{
    public bool Succeeded => Failures.Count == 0;
}
