using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpaceSweeper.App.Wpf.Localization;
using SpaceSweeper.App.Wpf.ViewModels;
using SpaceSweeper.Core.Scanning;
using SpaceSweeper.Windows.Scanning;

namespace SpaceSweeper.App.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(
            WindowsStorageServices.CreateDefaultScanner(),
            WindowsStorageServices.CreateDefaultCleanupService(),
            new WpfDialogService(this),
            new WpfTextProvider());
        DataContext = _viewModel;
        Treemap.NodeSelected += OnTreemapNodeSelected;
        Treemap.NodeActivated += OnTreemapNodeActivated;
    }

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is StorageNodeViewModel node)
        {
            _viewModel.SelectNode(node);
        }
    }

    private void OnTreemapNodeSelected(object? sender, StorageNode? node)
    {
        if (node is not null)
        {
            _viewModel.SelectNode(node);
        }
    }

    private void OnTreemapNodeActivated(object? sender, StorageNode? node)
    {
        if (node is not null)
        {
            _viewModel.SelectNode(node);
            _viewModel.EnterSelectedNode();
        }
    }

    private void OnChildGridMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FindParent<DataGridRow>((DependencyObject)e.OriginalSource) is not null)
        {
            _viewModel.EnterSelectedNode();
        }
    }

    private void OnChildGridPreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var row = FindParent<DataGridRow>((DependencyObject)e.OriginalSource);
        if (row?.Item is StorageNodeViewModel node)
        {
            row.IsSelected = true;
            row.Focus();
            _viewModel.SelectNode(node);
        }
    }

    private static T? FindParent<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
