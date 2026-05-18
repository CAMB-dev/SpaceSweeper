using SpaceSweeper.Core.Analysis;
using SpaceSweeper.Core.Scanning;

namespace SpaceSweeper.Tests.Core;

public sealed class StorageNodeFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Matches_AppliesNameSizeAgeAndExcludeRules()
    {
        var keep = File(@"C:\scan\photo.jpg", 2L * 1024 * 1024 * 1024, Now.AddDays(-40));
        var excluded = File(@"C:\scan\cache.tmp", 3L * 1024 * 1024 * 1024, Now.AddDays(-40));
        var small = File(@"C:\scan\small.jpg", 4 * 1024, Now.AddDays(-40));
        var recent = File(@"C:\scan\recent.jpg", 2L * 1024 * 1024 * 1024, Now.AddDays(-2));

        var filter = StorageNodeFilter.Compile("*.jpg;>1gb;>30days;|*.tmp", Now);

        Assert.True(filter.Matches(keep, null));
        Assert.False(filter.Matches(excluded, null));
        Assert.False(filter.Matches(small, null));
        Assert.False(filter.Matches(recent, null));
    }

    [Fact]
    public void MatchesSubtree_KeepsDirectoryWhenDescendantMatches()
    {
        var root = Directory(@"C:\scan", Directory(@"C:\scan\nested", File(@"C:\scan\nested\match.iso", 2048, Now)));
        var filter = StorageNodeFilter.Compile("*.iso", Now);

        Assert.True(filter.MatchesSubtree(root, static _ => null));
        Assert.True(filter.MatchesSubtree(root.Children[0], static _ => null));
    }

    [Fact]
    public void StorageNodeVisibility_EnumeratesVisibleAncestorsOnce()
    {
        var root = Directory(
            @"C:\scan",
            Directory(@"C:\scan\nested", File(@"C:\scan\nested\match.iso", 2048, Now)),
            File(@"C:\scan\skip.tmp", 128, Now));
        var visibility = new StorageNodeVisibility(StorageNodeFilter.Compile("*.iso", Now), static _ => null);

        var visibleNames = visibility.EnumerateVisible(root, includeRoot: true).Select(static node => node.Name).ToArray();

        Assert.Contains("scan", visibleNames);
        Assert.Contains("nested", visibleNames);
        Assert.Contains("match.iso", visibleNames);
        Assert.DoesNotContain("skip.tmp", visibleNames);
    }

    [Fact]
    public void Matches_SupportsTagCriteria()
    {
        var node = File(@"C:\scan\large.bin", 42, Now);
        var filter = StorageNodeFilter.Compile(":red", Now);

        Assert.True(filter.Matches(node, StorageNodeTag.Red));
        Assert.False(filter.Matches(node, StorageNodeTag.Blue));
    }

    private static StorageNode File(string path, long length, DateTimeOffset lastWriteTime)
    {
        return StorageNode.File(path, length, lastWriteTime, FileAttributes.Normal);
    }

    private static StorageNode Directory(string path, params StorageNode[] children)
    {
        return StorageNode.Directory(path, StorageNodeKind.Directory, Now, FileAttributes.Directory, children, []);
    }
}
