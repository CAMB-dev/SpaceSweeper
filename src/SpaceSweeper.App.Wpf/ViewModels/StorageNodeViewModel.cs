using System.Collections.ObjectModel;
using SpaceSweeper.Core.Analysis;
using SpaceSweeper.Core.Scanning;
using SpaceSweeper.Core.Utilities;

namespace SpaceSweeper.App.Wpf.ViewModels;

public sealed class StorageNodeViewModel
{
    private const int MaxVisibleChildren = 2000;
    private static readonly Func<StorageNode, StorageNodeTag?> NoTag = static _ => null;
    private ObservableCollection<StorageNodeViewModel>? _children;
    private IReadOnlyList<StorageNode>? _visibleChildren;
    private readonly StorageNodeVisibility _visibility;

    public StorageNodeViewModel(
        StorageNode node,
        StorageNodeFilter? filter = null,
        Func<StorageNode, StorageNodeTag?>? tagResolver = null)
    {
        Node = node;
        _visibility = new StorageNodeVisibility(filter ?? StorageNodeFilter.Empty, tagResolver ?? NoTag);
    }

    public StorageNodeViewModel(StorageNode node, StorageNodeVisibility visibility)
    {
        Node = node;
        _visibility = visibility;
    }

    public StorageNode Node { get; }

    public string Name => Node.Name;

    public long Length => Node.Length;

    public string LengthText => SizeFormatter.FormatBytes(Node.Length);

    public int FileCount => Node.FileCount;

    public int DirectoryCount => Node.DirectoryCount;

    public StorageNodeTag? Tag => _visibility.GetTag(Node);

    public string TagText => Tag?.ToString() ?? string.Empty;

    public ObservableCollection<StorageNodeViewModel> Children
    {
        get
        {
                _children ??= new ObservableCollection<StorageNodeViewModel>(
                    VisibleChildren
                    .Take(MaxVisibleChildren)
                    .Select(child => new StorageNodeViewModel(child, _visibility)));
            return _children;
        }
    }

    public int HiddenChildCount => Math.Max(0, VisibleChildren.Count - MaxVisibleChildren);

    public bool HasHiddenChildren => HiddenChildCount > 0;

    private IReadOnlyList<StorageNode> VisibleChildren => _visibleChildren ??= _visibility.GetVisibleChildren(Node);
}
