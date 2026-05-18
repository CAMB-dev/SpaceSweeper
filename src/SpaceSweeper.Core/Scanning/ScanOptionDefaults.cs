namespace SpaceSweeper.Core.Scanning;

public static class ScanOptionDefaults
{
    public const int ProgressItemInterval = 256;
    public const int SnapshotItemInterval = 1024;
    public const int SnapshotMinimumIntervalMilliseconds = 350;
    public const int MaximumSnapshotChildren = 512;
    public const int MinimumMaxDegreeOfParallelism = 1;
    public const int MaximumMaxDegreeOfParallelism = 32;
    public const int MinimumSnapshotIntervalMilliseconds = 100;
    public const int MaximumSnapshotIntervalMilliseconds = 5000;
    public const int MinimumSnapshotChildren = 64;
    public const int MaximumSnapshotChildrenLimit = 5000;

    public static int MaxDegreeOfParallelism => Math.Clamp(Environment.ProcessorCount, 2, 6);
}
