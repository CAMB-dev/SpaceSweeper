using SpaceSweeper.Core.Scanning;

namespace SpaceSweeper.App.Wpf.ViewModels;

public sealed class ScanSettings : ObservableObject
{
    private int _maxDegreeOfParallelism = ScanOptionDefaults.MaxDegreeOfParallelism;
    private int _progressItemInterval = ScanOptionDefaults.ProgressItemInterval;
    private int _snapshotItemInterval = ScanOptionDefaults.SnapshotItemInterval;
    private int _snapshotMinimumIntervalMilliseconds = ScanOptionDefaults.SnapshotMinimumIntervalMilliseconds;
    private int _maximumSnapshotChildren = ScanOptionDefaults.MaximumSnapshotChildren;

    public int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        set => SetProperty(
            ref _maxDegreeOfParallelism,
            Math.Clamp(value, ScanOptionDefaults.MinimumMaxDegreeOfParallelism, ScanOptionDefaults.MaximumMaxDegreeOfParallelism));
    }

    public int ProgressItemInterval
    {
        get => _progressItemInterval;
        set => SetProperty(ref _progressItemInterval, Math.Max(1, value));
    }

    public int SnapshotItemInterval
    {
        get => _snapshotItemInterval;
        set => SetProperty(ref _snapshotItemInterval, Math.Max(1, value));
    }

    public int SnapshotMinimumIntervalMilliseconds
    {
        get => _snapshotMinimumIntervalMilliseconds;
        set => SetProperty(
            ref _snapshotMinimumIntervalMilliseconds,
            Math.Clamp(
                value,
                ScanOptionDefaults.MinimumSnapshotIntervalMilliseconds,
                ScanOptionDefaults.MaximumSnapshotIntervalMilliseconds));
    }

    public int MaximumSnapshotChildren
    {
        get => _maximumSnapshotChildren;
        set => SetProperty(
            ref _maximumSnapshotChildren,
            Math.Clamp(value, ScanOptionDefaults.MinimumSnapshotChildren, ScanOptionDefaults.MaximumSnapshotChildrenLimit));
    }

    public ScanOptions ToScanOptions(string rootPath)
    {
        return new ScanOptions(rootPath)
        {
            FollowReparsePoints = false,
            ProgressItemInterval = ProgressItemInterval,
            SnapshotItemInterval = SnapshotItemInterval,
            SnapshotMinimumInterval = TimeSpan.FromMilliseconds(SnapshotMinimumIntervalMilliseconds),
            MaximumSnapshotChildren = MaximumSnapshotChildren,
            MaxDegreeOfParallelism = MaxDegreeOfParallelism
        };
    }
}
