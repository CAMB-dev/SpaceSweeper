using SpaceSweeper.Core.Scanning;

namespace SpaceSweeper.Core.Analysis;

public sealed class StorageNodeVisibility
{
    public static readonly StorageNodeVisibility Empty = new(StorageNodeFilter.Empty, static _ => null);

    private readonly Dictionary<string, bool> _subtreeVisibility = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<StorageNode, StorageNodeTag?> _tagResolver;

    public StorageNodeVisibility(StorageNodeFilter filter, Func<StorageNode, StorageNodeTag?> tagResolver)
    {
        Filter = filter;
        _tagResolver = tagResolver;
    }

    public StorageNodeFilter Filter { get; }

    public StorageNodeTag? GetTag(StorageNode node)
    {
        return _tagResolver(node);
    }

    public bool IsSubtreeVisible(StorageNode node)
    {
        if (_subtreeVisibility.TryGetValue(node.Path, out var visible))
        {
            return visible;
        }

        visible = ComputeSubtreeVisibility(node);
        _subtreeVisibility[node.Path] = visible;
        return visible;
    }

    public IReadOnlyList<StorageNode> GetVisibleChildren(StorageNode node)
    {
        return node.Children.Where(IsSubtreeVisible).ToArray();
    }

    public IEnumerable<StorageNode> EnumerateVisible(StorageNode root, bool includeRoot)
    {
        if (includeRoot || IsSubtreeVisible(root))
        {
            yield return root;
        }

        foreach (var child in root.Children)
        {
            if (!IsSubtreeVisible(child))
            {
                continue;
            }

            foreach (var descendant in EnumerateVisible(child, includeRoot: false))
            {
                yield return descendant;
            }
        }
    }

    private bool ComputeSubtreeVisibility(StorageNode node)
    {
        var tag = _tagResolver(node);
        if (Filter.IsExcluded(node, tag))
        {
            return false;
        }

        if (Filter.IsIncluded(node, tag))
        {
            return true;
        }

        return node.Children.Any(IsSubtreeVisible);
    }
}
