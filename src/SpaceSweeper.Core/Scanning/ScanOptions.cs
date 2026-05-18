namespace SpaceSweeper.Core.Scanning;

public sealed record ScanOptions(string RootPath)
{
    public bool IncludeFiles { get; init; } = true;

    public bool FollowReparsePoints { get; init; }

    public int ProgressItemInterval { get; init; } = 256;

    public int SnapshotItemInterval { get; init; } = 1024;

    public TimeSpan SnapshotMinimumInterval { get; init; } = TimeSpan.FromMilliseconds(350);

    public int MaximumSnapshotChildren { get; init; } = 512;

    public int MaxDegreeOfParallelism { get; init; } = Math.Clamp(Environment.ProcessorCount, 2, 6);
}
