using System.Text;
using SpaceSweeper.Core.Scanning;

namespace SpaceSweeper.Tests.Core;

public sealed class ManagedFileSystemScanProviderTests : IDisposable
{
    private readonly string _root;

    public ManagedFileSystemScanProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SpaceSweeper.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ScanAsync_AggregatesNestedFiles()
    {
        var nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(_root, "root.txt"), "12345", Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(nested, "child.txt"), "1234567890", Encoding.UTF8);

        var provider = new ManagedFileSystemScanProvider();
        var result = await provider.ScanAsync(
            new ScanOptions(_root),
            new Progress<ScanProgress>());

        Assert.Equal("managed-enumeration", result.ProviderName);
        Assert.Equal(2, result.Root.FileCount);
        Assert.Equal(2, result.Root.DirectoryCount);
        Assert.True(result.Root.Length >= 15);
        Assert.Contains(result.Root.Children, child => child.Name == "nested");
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsUnavailableForMissingPath()
    {
        var provider = new ManagedFileSystemScanProvider();
        var status = await provider.GetStatusAsync(new ScanOptions(Path.Combine(_root, "missing")));

        Assert.False(status.IsAvailable);
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                Thread.Sleep(50);
            }
        }
    }
}
