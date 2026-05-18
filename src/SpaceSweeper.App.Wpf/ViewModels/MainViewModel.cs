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
    private double _progressValue;
    private StorageNodeViewModel? _rootNode;
    private StorageNodeViewModel? _viewNode;
    private StorageNodeViewModel? _selectedNode;
    private StorageNodeViewModel? _selectedChild;
    private DriveOption? _selectedDrive;
    private LanguageOption _selectedLanguage;
    private bool _updatingDriveSelection;
    private DateTimeOffset _lastUiProgressUpdate = DateTimeOffset.MinValue;

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
        SettingsCommand = new RelayCommand(() => _dialogs.ShowSettings(Settings), () => !IsScanning);
        EnterCommand = new RelayCommand(EnterSelectedNode, CanEnterSelectedNode);
        UpCommand = new RelayCommand(GoUp, CanGoUp);
        RootCommand = new RelayCommand(GoRoot, () => RootNode is not null && ViewNode is not null && !IsSameNode(RootNode, ViewNode));
        LoadAvailableDrives();
        SelectDriveForRootPath();
    }

    public ObservableCollection<StorageNodeViewModel> RootItems { get; } = [];

    public ObservableCollection<DriveOption> AvailableDrives { get; } = [];

    public ScanSettings Settings { get; } = new();

    public IReadOnlyList<LanguageOption> Languages { get; }

    public RelayCommand BrowseCommand { get; }

    public RelayCommand ScanCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand CleanupCommand { get; }

    public RelayCommand SettingsCommand { get; }

    public RelayCommand EnterCommand { get; }

    public RelayCommand UpCommand { get; }

    public RelayCommand RootCommand { get; }

    public string RootPath
    {
        get => _rootPath;
        set
        {
            if (SetProperty(ref _rootPath, value))
            {
                SelectDriveForRootPath();
                ScanCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public DriveOption? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (!SetProperty(ref _selectedDrive, value) || value is null || _updatingDriveSelection)
            {
                return;
            }

            RootPath = value.RootPath;
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
                SettingsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
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

    public StorageNodeViewModel? ViewNode
    {
        get => _viewNode;
        private set
        {
            if (SetProperty(ref _viewNode, value))
            {
                OnPropertyChanged(nameof(ViewStorageNode));
                OnPropertyChanged(nameof(ViewPath));
                OnPropertyChanged(nameof(SelectedChildren));
                UpCommand.NotifyCanExecuteChanged();
                RootCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public StorageNode? ViewStorageNode => ViewNode?.Node;

    public string ViewPath => ViewNode?.Node.Path ?? string.Empty;

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
                EnterCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public StorageNode? SelectedStorageNode => SelectedNode?.Node;

    public IEnumerable<StorageNodeViewModel> SelectedChildren =>
        ViewNode?.Children ?? Enumerable.Empty<StorageNodeViewModel>();

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
        SelectedNode = new StorageNodeViewModel(node);
    }

    public void EnterSelectedNode()
    {
        if (!CanEnterSelectedNode() || SelectedNode is null)
        {
            return;
        }

        ViewNode = SelectedNode;
        SelectedNode = ViewNode;
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
        ViewNode = null;
        SelectedNode = null;
        StatusText = _text.Get("StatusPreparing");

        try
        {
            _scanCancellation = new CancellationTokenSource();
            var progress = new Progress<ScanProgress>(OnScanProgressChanged);
            var result = await _scanner.ScanAsync(new ScanOptions(RootPath)
            {
                FollowReparsePoints = false,
                ProgressItemInterval = Settings.ProgressItemInterval,
                SnapshotItemInterval = Settings.SnapshotItemInterval,
                SnapshotMinimumInterval = TimeSpan.FromMilliseconds(Settings.SnapshotMinimumIntervalMilliseconds),
                MaximumSnapshotChildren = Settings.MaximumSnapshotChildren,
                MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism
            }, progress, _scanCancellation.Token).ConfigureAwait(true);
            var root = new StorageNodeViewModel(result.Root);
            ReplaceRoot(root, preserveSelection: false);
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
            ProgressValue = 0;
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
            ViewNode = null;
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

        if (DateTimeOffset.UtcNow - _lastUiProgressUpdate < TimeSpan.FromMilliseconds(100))
        {
            return;
        }

        _lastUiProgressUpdate = DateTimeOffset.UtcNow;
        ProgressValue = progress.ItemsScanned % 100;

        if (progress.SnapshotRoot is not null)
        {
            ReplaceRoot(new StorageNodeViewModel(progress.SnapshotRoot), preserveSelection: true);
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

    private void LoadAvailableDrives()
    {
        AvailableDrives.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(static drive => drive.IsReady))
        {
            var freeSpace = SizeFormatter.FormatBytes(drive.AvailableFreeSpace);
            var totalSpace = SizeFormatter.FormatBytes(drive.TotalSize);
            AvailableDrives.Add(new DriveOption(drive.RootDirectory.FullName, $"{drive.RootDirectory.FullName}  {freeSpace} / {totalSpace}"));
        }
    }

    private bool CanEnterSelectedNode()
    {
        return SelectedNode?.Node.IsContainer == true;
    }

    private bool CanGoUp()
    {
        return RootNode is not null && ViewNode is not null && !IsSameNode(RootNode, ViewNode);
    }

    private void GoUp()
    {
        if (!CanGoUp() || RootNode is null || ViewNode is null)
        {
            return;
        }

        var parent = FindParentStorageNode(RootNode.Node, ViewNode.Node.Path);
        if (parent is null)
        {
            GoRoot();
            return;
        }

        ViewNode = new StorageNodeViewModel(parent);
        SelectedNode = ViewNode;
    }

    private void GoRoot()
    {
        if (RootNode is null)
        {
            return;
        }

        ViewNode = RootNode;
        SelectedNode = RootNode;
    }

    private void SelectDriveForRootPath()
    {
        _updatingDriveSelection = true;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(RootPath));
            SelectedDrive = AvailableDrives.FirstOrDefault(drive =>
                string.Equals(drive.RootPath, root, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            SelectedDrive = null;
        }
        finally
        {
            _updatingDriveSelection = false;
        }
    }

    private static StorageNode? FindStorageNode(StorageNode? current, string path)
    {
        if (current is null)
        {
            return null;
        }

        if (string.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        foreach (var child in current.Children)
        {
            var match = FindStorageNode(child, path);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private void ReplaceRoot(StorageNodeViewModel root, bool preserveSelection)
    {
        var selectedPath = preserveSelection ? SelectedNode?.Node.Path : null;
        RootNode = root;
        RootItems.Clear();
        RootItems.Add(root);
        var selectedStorageNode = selectedPath is null ? null : FindStorageNode(root.Node, selectedPath);
        SelectedNode = selectedStorageNode is null ? root : new StorageNodeViewModel(selectedStorageNode);
        var viewPath = ViewNode?.Node.Path;
        var viewStorageNode = viewPath is null ? null : FindStorageNode(root.Node, viewPath);
        ViewNode = viewStorageNode is null ? root : new StorageNodeViewModel(viewStorageNode);
    }

    private static StorageNode? FindParentStorageNode(StorageNode current, string childPath)
    {
        foreach (var child in current.Children)
        {
            if (string.Equals(child.Path, childPath, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            var match = FindParentStorageNode(child, childPath);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static bool IsSameNode(StorageNodeViewModel left, StorageNodeViewModel right)
    {
        return string.Equals(left.Node.Path, right.Node.Path, StringComparison.OrdinalIgnoreCase);
    }
}
