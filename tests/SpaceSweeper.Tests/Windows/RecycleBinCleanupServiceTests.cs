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
        var targets = new[]
        {
            new CleanupTarget(@"C:\blocked", StorageNodeKind.Directory, 10),
            new CleanupTarget(Path.Combine(Path.GetTempPath(), "allowed.bin"), StorageNodeKind.File, 20),
            new CleanupTarget(Path.Combine(Path.GetTempPath(), "link"), StorageNodeKind.Directory, 0, FileAttributes.ReparsePoint)
        };

        var preview = await service.PreviewAsync(targets);

        Assert.Single(preview.AllowedItems);
        Assert.Equal(2, preview.BlockedItems.Count);
        Assert.Equal(20, preview.TotalBytes);
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
}
