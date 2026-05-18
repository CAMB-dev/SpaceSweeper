using System.Collections.ObjectModel;
using SpaceSweeper.Core.Scanning;
using SpaceSweeper.Core.Utilities;

namespace SpaceSweeper.App.Wpf.ViewModels;

public sealed class StorageNodeViewModel
{
    private const int MaxVisibleChildren = 2000;
    private ObservableCollection<StorageNodeViewModel>? _children;

    public StorageNodeViewModel(StorageNode node)
    {
        Node = node;
    }

    public StorageNode Node { get; }

    public string Name => Node.Name;

    public long Length => Node.Length;

    public string LengthText => SizeFormatter.FormatBytes(Node.Length);

    public int FileCount => Node.FileCount;

    public int DirectoryCount => Node.DirectoryCount;

    public ObservableCollection<StorageNodeViewModel> Children
    {
        get
        {
            _children ??= new ObservableCollection<StorageNodeViewModel>(
                Node.Children
                    .Take(MaxVisibleChildren)
                    .Select(static child => new StorageNodeViewModel(child)));
            return _children;
        }
    }

    public int HiddenChildCount => Math.Max(0, Node.Children.Count - MaxVisibleChildren);

    public bool HasHiddenChildren => HiddenChildCount > 0;
}
