using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using SpaceSweeper.App.Wpf.Localization;
using SpaceSweeper.Core.Analysis;
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
    private readonly Dictionary<string, StorageNodeTag> _nodeTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private CancellationTokenSource? _scanCancellation;
    private string _rootPath;
    private string _filterText = string.Empty;
    private string _statusText;
    private bool _isScanning;
    private double _progressValue;
    private long _scanGeneration;
    private long _activeScanGeneration;
    private StorageNodeViewModel? _rootNode;
    private StorageNodeViewModel? _viewNode;
    private StorageNodeViewModel? _selectedNode;
    private StorageNodeViewModel? _selectedChild;
    private DriveOption? _selectedDrive;
    private LanguageOption _selectedLanguage;
    private bool _updatingDriveSelection;
    private bool _isCleaning;
    private bool _isExporting;
    private StorageNodeFilter _filter = StorageNodeFilter.Empty;
    private StorageNodeVisibility _visibility = StorageNodeVisibility.Empty;
    private IReadOnlyDictionary<string, StorageNodeTag> _nodeTagSnapshot =
        new ReadOnlyDictionary<string, StorageNodeTag>(new Dictionary<string, StorageNodeTag>(StringComparer.OrdinalIgnoreCase));
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

        BrowseCommand = new RelayCommand(Browse, () => !IsScanning && !IsCleaning);
        ScanCommand = new RelayCommand(ScanAsync, CanScan);
        CancelCommand = new RelayCommand(Cancel, () => IsScanning);
        CleanupCommand = new RelayCommand(CleanupAsync, CanCleanupSelectedNode);
        SettingsCommand = new RelayCommand(() => _dialogs.ShowSettings(Settings), () => !IsScanning && !IsCleaning);
        EnterCommand = new RelayCommand(EnterSelectedNode, CanEnterSelectedNode);
        BackCommand = new RelayCommand(GoBack, () => _backStack.Count > 0);
        ForwardCommand = new RelayCommand(GoForward, () => _forwardStack.Count > 0);
        UpCommand = new RelayCommand(GoUp, CanGoUp);
        RootCommand = new RelayCommand(GoRoot, () => RootNode is not null && ViewNode is not null && !IsSameNode(RootNode, ViewNode));
        ApplyFilterCommand = new RelayCommand(ApplyFilter, () => RootNode is not null);
        ClearFilterCommand = new RelayCommand(ClearFilter, () => RootNode is not null && Filter.HasCriteria);
        ExportCommand = new RelayCommand(ExportCurrentViewAsync, () => ViewNode is not null && !IsScanning && !IsCleaning && !IsExporting);
        TagRedCommand = new RelayCommand(() => SetSelectedTag(StorageNodeTag.Red), CanTagSelectedNode);
        TagYellowCommand = new RelayCommand(() => SetSelectedTag(StorageNodeTag.Yellow), CanTagSelectedNode);
        TagGreenCommand = new RelayCommand(() => SetSelectedTag(StorageNodeTag.Green), CanTagSelectedNode);
        TagBlueCommand = new RelayCommand(() => SetSelectedTag(StorageNodeTag.Blue), CanTagSelectedNode);
        ClearTagCommand = new RelayCommand(ClearSelectedTag, () => IsNodeVisible(SelectedNode) && ResolveTag(SelectedNode!.Node) is not null);
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

    public RelayCommand BackCommand { get; }

    public RelayCommand ForwardCommand { get; }

    public RelayCommand UpCommand { get; }

    public RelayCommand RootCommand { get; }

    public RelayCommand ApplyFilterCommand { get; }

    public RelayCommand ClearFilterCommand { get; }

    public RelayCommand ExportCommand { get; }

    public RelayCommand TagRedCommand { get; }

    public RelayCommand TagYellowCommand { get; }

    public RelayCommand TagGreenCommand { get; }

    public RelayCommand TagBlueCommand { get; }

    public RelayCommand ClearTagCommand { get; }

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

    public string FilterText
    {
        get => _filterText;
        set => SetProperty(ref _filterText, value);
    }

    public StorageNodeFilter Filter
    {
        get => _filter;
        private set
        {
            if (!ReferenceEquals(_filter, value))
            {
                _filter = value;
                RebuildVisibility();
                OnPropertyChanged();
                ClearFilterCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IReadOnlyDictionary<string, StorageNodeTag> NodeTags => _nodeTagSnapshot;

    public StorageNodeVisibility NodeVisibility => _visibility;

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
                ExportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsCleaning
    {
        get => _isCleaning;
        private set
        {
            if (SetProperty(ref _isCleaning, value))
            {
                BrowseCommand.NotifyCanExecuteChanged();
                ScanCommand.NotifyCanExecuteChanged();
                CleanupCommand.NotifyCanExecuteChanged();
                SettingsCommand.NotifyCanExecuteChanged();
                ExportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetProperty(ref _isExporting, value))
            {
                BrowseCommand.NotifyCanExecuteChanged();
                ScanCommand.NotifyCanExecuteChanged();
                ExportCommand.NotifyCanExecuteChanged();
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
                ApplyFilterCommand.NotifyCanExecuteChanged();
                ClearFilterCommand.NotifyCanExecuteChanged();
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
                SyncSelectedChildToView();
                BackCommand.NotifyCanExecuteChanged();
                ForwardCommand.NotifyCanExecuteChanged();
                UpCommand.NotifyCanExecuteChanged();
                RootCommand.NotifyCanExecuteChanged();
                ExportCommand.NotifyCanExecuteChanged();
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
                SyncSelectedChildToView();
                CleanupCommand.NotifyCanExecuteChanged();
                EnterCommand.NotifyCanExecuteChanged();
                TagRedCommand.NotifyCanExecuteChanged();
                TagYellowCommand.NotifyCanExecuteChanged();
                TagGreenCommand.NotifyCanExecuteChanged();
                TagBlueCommand.NotifyCanExecuteChanged();
                ClearTagCommand.NotifyCanExecuteChanged();
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
        SelectedNode = CreateNode(node);
    }

    public void EnterSelectedNode()
    {
        if (!CanEnterSelectedNode() || SelectedNode is null)
        {
            return;
        }

        NavigateTo(SelectedNode, recordHistory: true);
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
        var scanGeneration = ++_scanGeneration;
        _activeScanGeneration = scanGeneration;
        _lastUiProgressUpdate = DateTimeOffset.MinValue;
        RootItems.Clear();
        RootNode = null;
        ViewNode = null;
        SelectedNode = null;
        _nodeTags.Clear();
        RefreshTagSnapshot();
        ClearNavigationHistory();
        StatusText = _text.Get("StatusPreparing");

        try
        {
            _scanCancellation = new CancellationTokenSource();
            var progress = new Progress<ScanProgress>(progress => OnScanProgressChanged(scanGeneration, progress));
            var result = await _scanner.ScanAsync(Settings.ToScanOptions(RootPath), progress, _scanCancellation.Token).ConfigureAwait(true);
            _activeScanGeneration = 0;
            ReplaceRoot(result.Root, preserveSelection: false);
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
            _activeScanGeneration = 0;
            StatusText = _text.Get("StatusCancelled");
        }
        catch (Exception ex)
        {
            _activeScanGeneration = 0;
            StatusText = _text.Get("StatusFailed");
            _dialogs.ShowError(_text.Get("ScanFailedTitle"), ex.Message);
        }
        finally
        {
            if (_activeScanGeneration == scanGeneration)
            {
                _activeScanGeneration = 0;
            }

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
        if (!CanCleanupSelectedNode())
        {
            return;
        }

        IsCleaning = true;
        try
        {
            var selected = SelectedNode;
            if (selected is null)
            {
                return;
            }

            var targets = new[] { CleanupTarget.FromNode(selected.Node) };
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
                    preview.BlockedItems.Count,
                    preview.AllowedItems[0].Path));

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
                _nodeTags.Clear();
                RefreshTagSnapshot();
                ClearNavigationHistory();
                StatusText = _text.Get("StatusRescanRequired");
            }
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(_text.Get("CleanupBlockedTitle"), ex.Message);
        }
        finally
        {
            IsCleaning = false;
        }
    }

    private void OnScanProgressChanged(long scanGeneration, ScanProgress progress)
    {
        if (scanGeneration != _activeScanGeneration || !IsScanning)
        {
            return;
        }

        if (progress.Phase != ScanPhase.Scanning)
        {
            return;
        }

        if (progress.SnapshotRoot is not null)
        {
            ReplaceRoot(progress.SnapshotRoot, preserveSelection: true);
        }

        if (DateTimeOffset.UtcNow - _lastUiProgressUpdate < TimeSpan.FromMilliseconds(100))
        {
            return;
        }

        _lastUiProgressUpdate = DateTimeOffset.UtcNow;
        ProgressValue = progress.ItemsScanned % 100;

        StatusText = string.Format(
            _text.Get("StatusScanningFormat"),
            progress.ItemsScanned,
            SizeFormatter.FormatBytes(progress.BytesScanned),
            progress.ErrorCount);
    }

    private bool CanScan()
    {
        return !IsScanning
            && !IsCleaning
            && !IsExporting
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
        return IsNodeVisible(SelectedNode) && SelectedNode!.Node.IsContainer;
    }

    private bool CanTagSelectedNode()
    {
        return IsNodeVisible(SelectedNode);
    }

    private bool CanCleanupSelectedNode()
    {
        if (SelectedNode is null || IsScanning || IsCleaning || IsExporting || !IsNodeVisible(SelectedNode))
        {
            return false;
        }

        return !Filter.HasCriteria || Filter.Matches(SelectedNode.Node, ResolveTag(SelectedNode.Node));
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

        NavigateTo(CreateNode(parent), recordHistory: true);
    }

    private void GoRoot()
    {
        if (RootNode is null)
        {
            return;
        }

        NavigateTo(RootNode, recordHistory: true);
    }

    private void GoBack()
    {
        NavigateHistory(_backStack, _forwardStack);
    }

    private void GoForward()
    {
        NavigateHistory(_forwardStack, _backStack);
    }

    private void ApplyFilter()
    {
        Filter = StorageNodeFilter.Compile(FilterText);
        RefreshCurrentTree(preserveSelection: true);
    }

    private void ClearFilter()
    {
        FilterText = string.Empty;
        Filter = StorageNodeFilter.Empty;
        RefreshCurrentTree(preserveSelection: true);
    }

    private async Task ExportCurrentViewAsync()
    {
        if (ViewNode is null || IsExporting)
        {
            return;
        }

        IsExporting = true;
        try
        {
            var fileName = MakeSafeFileName($"{ViewNode.Node.Name}-spacesweeper.csv");
            var path = _dialogs.PickSaveFile(fileName, _text.Get("CsvReportFilter"));
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var node = ViewNode.Node;
            var filter = Filter;
            var tags = _nodeTagSnapshot;
            await Task.Run(() =>
            {
                using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                StorageNodeReportExporter.WriteCsv(writer, node, filter, item => tags.TryGetValue(item.Path, out var tag) ? tag : null);
            }).ConfigureAwait(true);

            _dialogs.ShowInfo(_text.Get("ExportCompleteTitle"), _text.Get("ExportCompleteMessage"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _dialogs.ShowError(_text.Get("ExportFailedTitle"), ex.Message);
        }
        finally
        {
            IsExporting = false;
        }
    }

    private void SetSelectedTag(StorageNodeTag tag)
    {
        if (!IsNodeVisible(SelectedNode))
        {
            return;
        }

        _nodeTags[SelectedNode!.Node.Path] = tag;
        RefreshTagSnapshot();
        RefreshCurrentTree(preserveSelection: true);
    }

    private void ClearSelectedTag()
    {
        if (!IsNodeVisible(SelectedNode))
        {
            return;
        }

        _nodeTags.Remove(SelectedNode!.Node.Path);
        RefreshTagSnapshot();
        RefreshCurrentTree(preserveSelection: true);
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

    private StorageNodeViewModel CreateNode(StorageNode node)
    {
        return new StorageNodeViewModel(node, _visibility);
    }

    private StorageNodeTag? ResolveTag(StorageNode node)
    {
        return _nodeTagSnapshot.TryGetValue(node.Path, out var tag) ? tag : null;
    }

    private void RefreshTagSnapshot()
    {
        _nodeTagSnapshot = new ReadOnlyDictionary<string, StorageNodeTag>(
            new Dictionary<string, StorageNodeTag>(_nodeTags, StringComparer.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(NodeTags));
        RebuildVisibility();
    }

    private void RebuildVisibility()
    {
        var tags = _nodeTagSnapshot;
        _visibility = new StorageNodeVisibility(Filter, node => tags.TryGetValue(node.Path, out var tag) ? tag : null);
        OnPropertyChanged(nameof(NodeVisibility));
    }

    private bool IsNodeVisible(StorageNodeViewModel? node)
    {
        if (node is null)
        {
            return false;
        }

        return (RootNode is not null && IsSameNode(RootNode, node)) || _visibility.IsSubtreeVisible(node.Node);
    }

    private void RefreshCurrentTree(bool preserveSelection)
    {
        if (RootNode is null)
        {
            return;
        }

        ReplaceRoot(RootNode.Node, preserveSelection);
    }

    private void NavigateTo(StorageNodeViewModel node, bool recordHistory)
    {
        if (recordHistory && ViewNode is not null && !IsSameNode(ViewNode, node))
        {
            _backStack.Push(ViewNode.Node.Path);
            _forwardStack.Clear();
        }

        ViewNode = node;
        SelectedNode = ViewNode;
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    private void NavigateHistory(Stack<string> source, Stack<string> destination)
    {
        if (RootNode is null || ViewNode is null || source.Count == 0)
        {
            return;
        }

        while (source.Count > 0)
        {
            var targetPath = source.Pop();
            var target = FindStorageNode(RootNode.Node, targetPath);
            if (target is null || !_visibility.IsSubtreeVisible(target))
            {
                continue;
            }

            destination.Push(ViewNode.Node.Path);
            ViewNode = CreateNode(target);
            SelectedNode = ViewNode;
            break;
        }

        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    private void ClearNavigationHistory()
    {
        _backStack.Clear();
        _forwardStack.Clear();
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    private static string MakeSafeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(fileName.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "spacesweeper-report.csv" : safe;
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

    private void SyncSelectedChildToView()
    {
        var selectedPath = SelectedNode?.Node.Path;
        var selectedChild = selectedPath is null || ViewNode is null
            ? null
            : ViewNode.Children.FirstOrDefault(child =>
                string.Equals(child.Node.Path, selectedPath, StringComparison.OrdinalIgnoreCase));

        if (!ReferenceEquals(_selectedChild, selectedChild))
        {
            _selectedChild = selectedChild;
            OnPropertyChanged(nameof(SelectedChild));
        }
    }

    private void ReplaceRoot(StorageNode rootNode, bool preserveSelection)
    {
        var selectedPath = preserveSelection ? SelectedNode?.Node.Path : null;
        var viewPath = ViewNode?.Node.Path;
        var root = CreateNode(rootNode);
        RootNode = root;
        RootItems.Clear();
        RootItems.Add(root);
        var viewStorageNode = preserveSelection && viewPath is not null
            ? FindNearestVisibleStorageNode(rootNode, viewPath)
            : rootNode;
        ViewNode = viewStorageNode is null ? root : CreateNode(viewStorageNode);
        var selectedStorageNode = selectedPath is null ? null : FindStorageNode(rootNode, selectedPath);
        SelectedNode = selectedStorageNode is not null && _visibility.IsSubtreeVisible(selectedStorageNode)
            ? CreateNode(selectedStorageNode)
            : ViewNode;
        PruneNavigationHistory(rootNode);
    }

    private StorageNode? FindNearestVisibleStorageNode(StorageNode root, string path)
    {
        var currentPath = path;
        while (!string.IsNullOrWhiteSpace(currentPath))
        {
            var node = FindStorageNode(root, currentPath);
            if (node is not null && (string.Equals(node.Path, root.Path, StringComparison.OrdinalIgnoreCase) || _visibility.IsSubtreeVisible(node)))
            {
                return node;
            }

            var trimmed = currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetDirectoryName(trimmed);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            currentPath = parent;
        }

        return root;
    }

    private void PruneNavigationHistory(StorageNode root)
    {
        PruneNavigationHistory(root, _backStack);
        PruneNavigationHistory(root, _forwardStack);
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    private void PruneNavigationHistory(StorageNode root, Stack<string> history)
    {
        var validPaths = history
            .Reverse()
            .Where(path =>
            {
                var node = FindStorageNode(root, path);
                return node is not null && _visibility.IsSubtreeVisible(node);
            })
            .ToArray();

        history.Clear();
        foreach (var path in validPaths)
        {
            history.Push(path);
        }
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
