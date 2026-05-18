using SpaceSweeper.Core.Cleanup;
using SpaceSweeper.Core.Scanning;
using SpaceSweeper.Windows.Cleanup;

namespace SpaceSweeper.Windows.Scanning;

public static class WindowsStorageServices
{
    public static IStorageScanner CreateDefaultScanner()
    {
        return new StorageScanner(new IStorageScanProvider[]
        {
            new ManagedFileSystemScanProvider()
        });
    }

    public static ICleanupService CreateDefaultCleanupService()
    {
        return new RecycleBinCleanupService(new DefaultProtectedPathPolicy());
    }
}
