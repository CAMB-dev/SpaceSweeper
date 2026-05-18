using SpaceSweeper.Core.Cleanup;
using SpaceSweeper.Core.Scanning;
using SpaceSweeper.Windows.Cleanup;

namespace SpaceSweeper.Tests.Windows;

public sealed class RecycleBinCleanupServiceTests
{
    [Fact]
    public async Task PreviewAsync_SeparatesBlockedTargets()
    {
        var service = new RecycleBinCleanupService(new BlockingPolicy());
        var allowedPath = Path.Combine(Path.GetTempPath(), "SpaceSweeper.Tests", Guid.NewGuid().ToString("N"), "allowed.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(allowedPath)!);
        await File.WriteAllTextAsync(allowedPath, "01234567890123456789");

        var targets = new[]
        {
            new CleanupTarget(@"C:\blocked", StorageNodeKind.Directory, 10),
            new CleanupTarget(allowedPath, StorageNodeKind.File, 20),
            new CleanupTarget(Path.Combine(Path.GetTempPath(), "link"), StorageNodeKind.Directory, 0, FileAttributes.ReparsePoint)
        };

        try
        {
            var preview = await service.PreviewAsync(targets);

            Assert.Single(preview.AllowedItems);
            Assert.Equal(2, preview.BlockedItems.Count);
            Assert.Equal(20, preview.TotalBytes);
            Assert.NotNull(preview.AllowedItems[0].ProviderIdentity);
        }
        finally
        {
            DeleteDirectoryWithRetry(Path.GetDirectoryName(allowedPath)!);
        }
    }

    [Fact]
    public async Task CleanupAsync_BlocksTargetsWithoutPreviewIdentity()
    {
        var service = new RecycleBinCleanupService(new BlockingPolicy());
        var root = Path.Combine(Path.GetTempPath(), "SpaceSweeper.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "target.bin");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(path, "data");

        try
        {
            var target = new CleanupTarget(path, StorageNodeKind.File, 4);
            var result = await service.CleanupAsync(new[] { target });

            Assert.Equal(0, result.CompletedCount);
            Assert.Single(result.Failures);
            Assert.True(File.Exists(path));
        }
        finally
        {
            DeleteDirectoryWithRetry(root);
        }
    }

    [Fact]
    public async Task CleanupAsync_FailsWhenPreviewedIdentityChanges()
    {
        var service = new RecycleBinCleanupService(new BlockingPolicy());
        var root = Path.Combine(Path.GetTempPath(), "SpaceSweeper.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "target.bin");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(path, "a");

        try
        {
            var preview = await service.PreviewAsync(new[] { new CleanupTarget(path, StorageNodeKind.File, 1) });
            Assert.Single(preview.AllowedItems);

            File.Delete(path);
            await File.WriteAllTextAsync(path, "b");

            var result = await service.CleanupAsync(preview.AllowedItems);

            Assert.Equal(0, result.CompletedCount);
            Assert.Single(result.Failures);
            Assert.True(File.Exists(path));
        }
        finally
        {
            DeleteDirectoryWithRetry(root);
        }
    }

    [Fact]
    public async Task CleanupAsync_FailsWhenDirectoryContentsChangeAfterPreview()
    {
        var service = new RecycleBinCleanupService(new BlockingPolicy());
        var root = Path.Combine(Path.GetTempPath(), "SpaceSweeper.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "before.bin"), "1234");

        try
        {
            var target = new CleanupTarget(root, StorageNodeKind.Directory, 4, FileAttributes.Directory, FileCount: 1, DirectoryCount: 1);
            var preview = await service.PreviewAsync(new[] { target });
            Assert.Single(preview.AllowedItems);

            await File.WriteAllTextAsync(Path.Combine(root, "after.bin"), "5678");

            var result = await service.CleanupAsync(preview.AllowedItems);

            Assert.Equal(0, result.CompletedCount);
            Assert.Single(result.Failures);
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            DeleteDirectoryWithRetry(root);
        }
    }

    private sealed class BlockingPolicy : IProtectedPathPolicy
    {
        public ProtectedPathDecision Evaluate(string path)
        {
            return path.Contains("blocked", StringComparison.OrdinalIgnoreCase)
                ? ProtectedPathDecision.Deny("blocked")
                : ProtectedPathDecision.Allow;
        }
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
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
