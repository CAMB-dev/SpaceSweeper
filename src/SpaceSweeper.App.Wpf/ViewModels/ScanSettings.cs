namespace SpaceSweeper.App.Wpf.ViewModels;

public sealed class ScanSettings : ObservableObject
{
    private int _maxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 6);
    private int _progressItemInterval = 256;
    private int _snapshotItemInterval = 1024;
    private int _snapshotMinimumIntervalMilliseconds = 350;
    private int _maximumSnapshotChildren = 512;

    public int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        set => SetProperty(ref _maxDegreeOfParallelism, Math.Clamp(value, 1, 32));
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
        set => SetProperty(ref _snapshotMinimumIntervalMilliseconds, Math.Clamp(value, 100, 5000));
    }

    public int MaximumSnapshotChildren
    {
        get => _maximumSnapshotChildren;
        set => SetProperty(ref _maximumSnapshotChildren, Math.Clamp(value, 64, 5000));
    }
}
