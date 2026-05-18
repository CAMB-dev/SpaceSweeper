namespace SpaceSweeper.Core.Scanning;

public interface IStorageScanner
{
    Task<ScanResult> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
