using System.IO;
using SpaceSweeper.Core.Scanning;

namespace SpaceSweeper.Windows.Scanning;

public sealed class NtfsFastScanProvider : IStorageScanProvider
{
    public string Name => "ntfs-fast";

    public ValueTask<StorageScanProviderStatus> GetStatusAsync(
        ScanOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(options.RootPath))
        {
            return ValueTask.FromResult(StorageScanProviderStatus.Unavailable("NTFS fast scan requires a directory root."));
        }

        var root = Path.GetPathRoot(Path.GetFullPath(options.RootPath));
        if (string.IsNullOrWhiteSpace(root))
        {
            return ValueTask.FromResult(StorageScanProviderStatus.Unavailable("Cannot resolve a volume root."));
        }

        var drive = DriveInfo.GetDrives()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, root, StringComparison.OrdinalIgnoreCase));

        if (drive is null || !drive.IsReady)
        {
            return ValueTask.FromResult(StorageScanProviderStatus.Unavailable("The volume is not ready."));
        }

        if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(StorageScanProviderStatus.Unavailable("The volume is not NTFS."));
        }

        return ValueTask.FromResult(StorageScanProviderStatus.Unavailable(
            "The NTFS MFT/USN provider boundary is present, but the low-level reader is not enabled in this MVP."));
    }

    public Task<ScanResult> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "The NTFS fast scan provider is reserved for the MFT/USN implementation branch.");
    }
}
