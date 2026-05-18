namespace SpaceSweeper.Core.Scanning;

public sealed record ScanOptions(string RootPath)
{
    public bool IncludeFiles { get; init; } = true;

    public bool FollowReparsePoints { get; init; }

    public int ProgressItemInterval { get; init; } = ScanOptionDefaults.ProgressItemInterval;

    public int SnapshotItemInterval { get; init; } = ScanOptionDefaults.SnapshotItemInterval;

    public TimeSpan SnapshotMinimumInterval { get; init; } =
        TimeSpan.FromMilliseconds(ScanOptionDefaults.SnapshotMinimumIntervalMilliseconds);

    public int MaximumSnapshotChildren { get; init; } = ScanOptionDefaults.MaximumSnapshotChildren;

    public int MaxDegreeOfParallelism { get; init; } = ScanOptionDefaults.MaxDegreeOfParallelism;
}
