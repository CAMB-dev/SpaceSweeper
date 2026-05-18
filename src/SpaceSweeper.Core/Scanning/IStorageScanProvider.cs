namespace SpaceSweeper.Core.Scanning;

public interface IStorageScanProvider
{
    string Name { get; }

    ValueTask<StorageScanProviderStatus> GetStatusAsync(
        ScanOptions options,
        CancellationToken cancellationToken = default);

    Task<ScanResult> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken = default);
}
