using SpaceSweeper.Core.Scanning;

namespace SpaceSweeper.Core.Cleanup;

public sealed record CleanupTarget(
    string Path,
    StorageNodeKind Kind,
    long Length,
    FileAttributes Attributes = 0,
    string? ProviderIdentity = null)
{
    public static CleanupTarget FromNode(StorageNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new CleanupTarget(node.Path, node.Kind, node.Length, node.Attributes);
    }
}
