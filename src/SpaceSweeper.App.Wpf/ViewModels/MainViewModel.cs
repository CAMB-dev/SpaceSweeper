using System.Collections.ObjectModel;
using System.IO;
using SpaceSweeper.App.Wpf.Localization;
using SpaceSweeper.Core.Cleanup;
using SpaceSweeper.Core.Scanning;
using SpaceSweeper.Core.Utilities;

namespace SpaceSweeper.App.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IStorageScanner _scanner;
    private readonly ICleanupService _cleanupService;
    private readonly IAppDialogService _dialogs;
    private readonly ITextProvider _text;
    private CancellationTokenSource? _scanCancellation;
    private string _rootPath;
    private string _statusText;
    private bool _isScanning;
    private StorageNodeViewModel? _rootNode;
    private StorageNodeViewModel? _selectedNode;
    private StorageNodeViewModel? _selectedChild;
    private LanguageOption _selectedLanguage;

    public MainViewModel(
        IStorageScanner scanner,
        ICleanupService cleanupService,
        IAppDialogService dialogs,
        ITextProvider text)
    {
        _scanner = scanner;
        _cleanupService = cleanupService;
        _dialogs = dialogs;
        _text = text;

        _rootPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _statusText = _text.Get("StatusReady");
        Languages = _text.Languages;
        _selectedLanguage = Languages[0];

        BrowseCommand = new RelayCommand(Browse, () => !IsScanning);
        ScanCommand = new RelayCommand(ScanAsync, CanScan);
        CancelCommand = new RelayCommand(Cancel, () => IsScanning);
        CleanupCommand = new RelayCommand(CleanupAsync, () => SelectedNode is not null && !IsScanning);
    }

    public ObservableCollection<StorageNodeViewModel> RootItems { get; } = [];

    public IReadOnlyList<LanguageOption> Languages { get; }

    public RelayCommand BrowseCommand { get; }

    public RelayCommand ScanCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand CleanupCommand { get; }

    public string RootPath
    {
        get => _rootPath;
        set
        {
            if (SetProperty(ref _rootPath, value))
            {
                ScanCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                BrowseCommand.NotifyCanExecuteChanged();
                ScanCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                CleanupCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public StorageNodeViewModel? RootNode
    {
        get => _rootNode;
        private set
        {
            if (SetProperty(ref _rootNode, value))
            {
                OnPropertyChanged(nameof(RootStorageNode));
            }
        }
    }

    public StorageNode? RootStorageNode => RootNode?.Node;

    public StorageNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        private set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                OnPropertyChanged(nameof(SelectedStorageNode));
                OnPropertyChanged(nameof(SelectedChildren));
                _selectedChild = null;
                OnPropertyChanged(nameof(SelectedChild));
                CleanupCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public StorageNode? SelectedStorageNode => SelectedNode?.Node;

    public IEnumerable<StorageNodeViewModel> SelectedChildren =>
        SelectedNode?.Children ?? Enumerable.Empty<StorageNodeViewModel>();

    public StorageNodeViewModel? SelectedChild
    {
        get => _selectedChild;
        set
        {
            if (SetProperty(ref _selectedChild, value) && value is not null)
            {
                SelectedNode = value;
            }
        }
    }

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value))
            {
                _text.Apply(value.CultureName);
                if (!IsScanning)
                {
                    StatusText = _text.Get("StatusReady");
                }
            }
        }
    }

    public void SelectNode(StorageNodeViewModel node)
    {
        SelectedNode = node;
    }

    public void SelectNode(StorageNode node)
    {
        var match = FindNode(RootNode, node.Path);
        if (match is not null)
        {
            SelectedNode = match;
        }
    }

    private void Browse()
    {
        var path = _dialogs.PickFolder(RootPath);
        if (!string.IsNullOrWhiteSpace(path))
        {
            RootPath = path;
        }
    }

    private async Task ScanAsync()
    {
        if (!CanScan())
        {
            return;
        }

        IsScanning = true;
        RootItems.Clear();
        RootNode = null;
        SelectedNode = null;
        StatusText = _text.Get("StatusPreparing");

        try
        {
            _scanCancellation = new CancellationTokenSource();
            var progress = new Progress<ScanProgress>(OnScanProgressChanged);
            var result = await _scanner.ScanAsync(new ScanOptions(RootPath)
            {
                FollowReparsePoints = false,
                ProgressItemInterval = 256
            }, progress, _scanCancellation.Token).ConfigureAwait(true);
            var root = new StorageNodeViewModel(result.Root);
            RootNode = root;
            RootItems.Add(root);
            SelectedNode = root;
            StatusText = string.Format(
                _text.Get("StatusCompletedFormat"),
                result.ProviderName,
                SizeFormatter.FormatBytes(result.Root.Length),
                result.Root.FileCount,
                result.Root.DirectoryCount,
                result.Errors.Count,
                result.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            StatusText = _text.Get("StatusCancelled");
        }
        catch (Exception ex)
        {
            StatusText = _text.Get("StatusFailed");
            _dialogs.ShowError(_text.Get("ScanFailedTitle"), ex.Message);
        }
        finally
        {
            _scanCancellation?.Dispose();
            _scanCancellation = null;

            IsScanning = false;
        }
    }

    private void Cancel()
    {
        _scanCancellation?.Cancel();
    }

    private async Task CleanupAsync()
    {
        if (SelectedNode is null)
        {
            return;
        }

        var targets = new[] { CleanupTarget.FromNode(SelectedNode.Node) };
        var preview = await _cleanupService.PreviewAsync(targets).ConfigureAwait(true);

        if (!preview.CanExecute)
        {
            var reason = preview.BlockedItems.FirstOrDefault()?.Reason ?? _text.Get("CleanupBlockedDefault");
            _dialogs.ShowError(_text.Get("CleanupBlockedTitle"), reason);
            return;
        }

        var confirmed = _dialogs.Confirm(
            _text.Get("CleanupConfirmTitle"),
            string.Format(
                _text.Get("CleanupConfirmFormat"),
                preview.AllowedItems.Count,
                SizeFormatter.FormatBytes(preview.TotalBytes),
                preview.BlockedItems.Count));

        if (!confirmed)
        {
            return;
        }

        var result = await _cleanupService.CleanupAsync(preview.AllowedItems).ConfigureAwait(true);
        _dialogs.ShowInfo(
            _text.Get("CleanupCompleteTitle"),
            string.Format(
                _text.Get("CleanupCompleteFormat"),
                result.CompletedCount,
                result.Failures.Count));

        if (result.CompletedCount > 0)
        {
            RootItems.Clear();
            RootNode = null;
            SelectedNode = null;
            StatusText = _text.Get("StatusRescanRequired");
        }
    }

    private void OnScanProgressChanged(ScanProgress progress)
    {
        if (progress.Phase != ScanPhase.Scanning)
        {
            return;
        }

        StatusText = string.Format(
            _text.Get("StatusScanningFormat"),
            progress.ItemsScanned,
            SizeFormatter.FormatBytes(progress.BytesScanned),
            progress.ErrorCount);
    }

    private bool CanScan()
    {
        return !IsScanning
            && !string.IsNullOrWhiteSpace(RootPath)
            && (Directory.Exists(RootPath) || File.Exists(RootPath));
    }

    private static StorageNodeViewModel? FindNode(StorageNodeViewModel? current, string path)
    {
        if (current is null)
        {
            return null;
        }

        if (string.Equals(current.Node.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        foreach (var child in current.Children)
        {
            var match = FindNode(child, path);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
