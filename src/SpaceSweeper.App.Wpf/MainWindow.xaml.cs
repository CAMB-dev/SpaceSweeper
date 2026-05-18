using System.Windows;
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
}
