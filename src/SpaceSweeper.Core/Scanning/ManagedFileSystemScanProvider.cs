using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;

namespace SpaceSweeper.Core.Scanning;

public sealed class ManagedFileSystemScanProvider : IStorageScanProvider
{
    public string Name => "managed-enumeration";

    public ValueTask<StorageScanProviderStatus> GetStatusAsync(
        ScanOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(
            Directory.Exists(options.RootPath) || File.Exists(options.RootPath)
                ? StorageScanProviderStatus.Available
                : StorageScanProviderStatus.Unavailable("The path does not exist or cannot be reached."));
    }

    public async Task<ScanResult> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);

        var stopwatch = Stopwatch.StartNew();
        var rootPath = Path.GetFullPath(options.RootPath);
        var context = new ScanBuildContext(options with { RootPath = rootPath }, progress, Name);

        progress.Report(new ScanProgress(
            rootPath,
            rootPath,
            0,
            0,
            0,
            0,
            ScanPhase.Scanning,
            Name));

        StorageNode root;
        if (File.Exists(rootPath))
        {
            try
            {
                root = ScanRootFile(rootPath, context);
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                var error = CreateError(rootPath, ex);
                context.AddError(error);
                root = new StorageNode(
                    rootPath,
                    StorageNode.GetDisplayName(rootPath),
                    StorageNodeKind.File,
                    0,
                    null,
                    0,
                    0,
                    null,
                    0,
                    Array.Empty<StorageNode>(),
                    new[] { error });
            }
        }
        else
        {
            var rootBuilder = CreateDirectoryBuilder(rootPath, parent: null);
            await ScanDirectoryTreeAsync(rootBuilder, context, cancellationToken).ConfigureAwait(false);
            root = rootBuilder.ToStorageNode(maxChildrenPerNode: int.MaxValue);
        }

        stopwatch.Stop();
        progress.Report(new ScanProgress(
            rootPath,
            rootPath,
            context.ItemsScanned,
            context.BytesScanned,
            context.DirectoriesScanned,
            context.ErrorCount,
            ScanPhase.Completed,
            Name,
            root));

        return new ScanResult(root, Name, stopwatch.Elapsed, context.GetErrors());
    }

    private static StorageNode ScanRootFile(string path, ScanBuildContext context)
    {
        var info = new FileInfo(path);
        context.RecordFile(path, info.Length);
        return StorageNode.File(path, info.Length, info.LastWriteTimeUtc, info.Attributes);
    }

    private static async Task ScanDirectoryTreeAsync(
        NodeBuilder root,
        ScanBuildContext context,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<NodeBuilder>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
        var state = new WorkQueueState { PendingDirectories = 1 };
        channel.Writer.TryWrite(root);

        var workerCount = Math.Max(1, context.Options.MaxDegreeOfParallelism);
        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var directory in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        await ProcessDirectoryAsync(directory, context, channel.Writer, state, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        if (Interlocked.Decrement(ref state.PendingDirectories) == 0)
                        {
                            channel.Writer.TryComplete();
                        }
                    }
                }
            }, cancellationToken))
            .ToArray();

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private static async Task ProcessDirectoryAsync(
        NodeBuilder directory,
        ScanBuildContext context,
        ChannelWriter<NodeBuilder> writer,
        WorkQueueState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.RecordDirectory(directory.Path);
        context.ReportSnapshotIfDue(directory.Path, directory.Root, force: false);

        var visitKey = context.Options.FollowReparsePoints ? GetDirectoryVisitKey(directory.Path) : null;
        if (visitKey is not null && !context.TryEnterDirectory(visitKey))
        {
            AddDirectoryError(directory, context, new ScanError(directory.Path, ScanErrorKind.Unsupported, "Directory cycle detected through a reparse point."));
            return;
        }

        try
        {
            IEnumerable<FileSystemInfo> children;
            try
            {
                children = new DirectoryInfo(directory.Path).EnumerateFileSystemInfos();
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                AddDirectoryError(directory, context, CreateError(directory.Path, ex));
                return;
            }

            try
            {
                foreach (var child in children)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ProcessChildAsync(directory, child, context, writer, state, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                AddDirectoryError(directory, context, CreateError(directory.Path, ex));
            }
        }
        finally
        {
            if (visitKey is not null)
            {
                context.LeaveDirectory(visitKey);
            }
        }

        context.ReportSnapshotIfDue(directory.Path, directory.Root, force: false);
    }

    private static async Task ProcessChildAsync(
        NodeBuilder parent,
        FileSystemInfo child,
        ScanBuildContext context,
        ChannelWriter<NodeBuilder> writer,
        WorkQueueState state,
        CancellationToken cancellationToken)
    {
        try
        {
            var attributes = SafeGetAttributes(child, 0);
            var isDirectory = attributes.HasFlag(FileAttributes.Directory);

            if (!isDirectory)
            {
                if (!context.Options.IncludeFiles)
                {
                    return;
                }

                var fileInfo = child as FileInfo ?? new FileInfo(child.FullName);
                var file = NodeBuilder.CreateFile(parent, fileInfo.FullName, fileInfo.Length, fileInfo.LastWriteTimeUtc, attributes);
                parent.AddChild(file);
                parent.AddFileToAncestors(fileInfo.Length);
                context.RecordFile(fileInfo.FullName, fileInfo.Length);
                context.ReportSnapshotIfDue(parent.Path, parent.Root, force: false);
                return;
            }

            var directoryInfo = child as DirectoryInfo ?? new DirectoryInfo(child.FullName);
            var directory = CreateDirectoryBuilder(directoryInfo.FullName, parent, directoryInfo.LastWriteTimeUtc, attributes);
            parent.AddChild(directory);
            directory.AddDirectoryToAncestors();

            if (attributes.HasFlag(FileAttributes.ReparsePoint) && !context.Options.FollowReparsePoints)
            {
                context.RecordDirectory(directory.Path);
                context.ReportSnapshotIfDue(parent.Path, parent.Root, force: false);
                return;
            }

            Interlocked.Increment(ref state.PendingDirectories);
            if (!writer.TryWrite(directory))
            {
                if (Interlocked.Decrement(ref state.PendingDirectories) == 0)
                {
                    writer.TryComplete();
                }
            }
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            AddDirectoryError(parent, context, CreateError(child.FullName, ex));
        }
    }

    private static NodeBuilder CreateDirectoryBuilder(
        string path,
        NodeBuilder? parent,
        DateTimeOffset? lastWriteTime = null,
        FileAttributes attributes = FileAttributes.Directory)
    {
        return NodeBuilder.CreateDirectory(
            parent,
            path,
            GetDirectoryKind(path),
            lastWriteTime,
            attributes | FileAttributes.Directory);
    }

    private static void AddDirectoryError(NodeBuilder directory, ScanBuildContext context, ScanError error)
    {
        directory.AddError(error);
        context.AddError(error);
    }

    private static StorageNodeKind GetDirectoryKind(string path)
    {
        var root = Path.GetPathRoot(path);
        return string.Equals(
            root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase)
            ? StorageNodeKind.Drive
            : StorageNodeKind.Directory;
    }

    private static FileAttributes SafeGetAttributes(FileSystemInfo info, FileAttributes fallback)
    {
        try
        {
            return info.Attributes;
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            return fallback;
        }
    }

    private static string GetDirectoryVisitKey(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            var target = info.LinkTarget is not null ? info.ResolveLinkTarget(returnFinalTarget: true) : null;
            return Normalize(target?.FullName ?? info.FullName);
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            return Normalize(path);
        }
    }

    private static bool IsExpectedFileSystemException(Exception ex)
    {
        return ex is UnauthorizedAccessException
            or IOException
            or PathTooLongException
            or DirectoryNotFoundException
            or FileNotFoundException
            or NotSupportedException;
    }

    private static ScanError CreateError(string path, Exception ex)
    {
        return new ScanError(path, GetErrorKind(ex), ex.Message);
    }

    private static ScanErrorKind GetErrorKind(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => ScanErrorKind.AccessDenied,
            DirectoryNotFoundException or FileNotFoundException => ScanErrorKind.NotFound,
            PathTooLongException => ScanErrorKind.PathTooLong,
            IOException => ScanErrorKind.InputOutput,
            NotSupportedException => ScanErrorKind.Unsupported,
            _ => ScanErrorKind.Unknown
        };
    }

    private static string Normalize(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private sealed class NodeBuilder
    {
        private readonly ConcurrentBag<NodeBuilder> _children = [];
        private readonly ConcurrentBag<ScanError> _errors = [];
        private long _length;
        private int _fileCount;
        private int _directoryCount;

        private NodeBuilder(
            NodeBuilder? parent,
            string path,
            StorageNodeKind kind,
            long length,
            int fileCount,
            int directoryCount,
            DateTimeOffset? lastWriteTime,
            FileAttributes attributes)
        {
            Parent = parent;
            Path = path;
            Kind = kind;
            _length = length;
            _fileCount = fileCount;
            _directoryCount = directoryCount;
            LastWriteTime = lastWriteTime;
            Attributes = attributes;
        }

        public NodeBuilder? Parent { get; }

        public NodeBuilder Root => Parent?.Root ?? this;

        public string Path { get; }

        public StorageNodeKind Kind { get; }

        public DateTimeOffset? LastWriteTime { get; }

        public FileAttributes Attributes { get; }

        public long Length => Interlocked.Read(ref _length);

        public static NodeBuilder CreateDirectory(
            NodeBuilder? parent,
            string path,
            StorageNodeKind kind,
            DateTimeOffset? lastWriteTime,
            FileAttributes attributes)
        {
            return new NodeBuilder(parent, path, kind, 0, 0, 1, lastWriteTime, attributes);
        }

        public static NodeBuilder CreateFile(
            NodeBuilder parent,
            string path,
            long length,
            DateTimeOffset? lastWriteTime,
            FileAttributes attributes)
        {
            return new NodeBuilder(parent, path, StorageNodeKind.File, Math.Max(0, length), 1, 0, lastWriteTime, attributes);
        }

        public void AddChild(NodeBuilder child)
        {
            _children.Add(child);
        }

        public void AddError(ScanError error)
        {
            _errors.Add(error);
        }

        public void AddFileToAncestors(long length)
        {
            var current = this;
            while (current is not null)
            {
                Interlocked.Add(ref current._length, Math.Max(0, length));
                Interlocked.Increment(ref current._fileCount);
                current = current.Parent;
            }
        }

        public void AddDirectoryToAncestors()
        {
            var current = Parent;
            while (current is not null)
            {
                Interlocked.Increment(ref current._directoryCount);
                current = current.Parent;
            }
        }

        public StorageNode ToStorageNode(int maxChildrenPerNode)
        {
            if (Kind == StorageNodeKind.File)
            {
                return StorageNode.File(Path, Length, LastWriteTime, Attributes);
            }

            var children = _children
                .OrderByDescending(static child => child.Length)
                .ThenBy(static child => child.Kind)
                .ThenBy(static child => StorageNode.GetDisplayName(child.Path), StringComparer.CurrentCultureIgnoreCase)
                .Take(maxChildrenPerNode)
                .Select(child => child.ToStorageNode(maxChildrenPerNode))
                .ToArray();

            return new StorageNode(
                Path,
                StorageNode.GetDisplayName(Path),
                Kind,
                Length,
                null,
                Volatile.Read(ref _fileCount),
                Volatile.Read(ref _directoryCount),
                LastWriteTime,
                Attributes,
                children,
                _errors.ToArray());
        }
    }

    private sealed class WorkQueueState
    {
        public int PendingDirectories;
    }

    private sealed class ScanBuildContext
    {
        private readonly object _errorsLock = new();
        private readonly object _activeDirectoriesLock = new();
        private readonly object _snapshotLock = new();
        private readonly IProgress<ScanProgress> _progress;
        private readonly string _providerName;
        private readonly List<ScanError> _errors = [];
        private readonly HashSet<string> _activeDirectories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch _snapshotStopwatch = Stopwatch.StartNew();
        private long _itemsScanned;
        private long _bytesScanned;
        private int _directoriesScanned;

        public ScanBuildContext(
            ScanOptions options,
            IProgress<ScanProgress> progress,
            string providerName)
        {
            Options = options;
            _progress = progress;
            _providerName = providerName;
        }

        public ScanOptions Options { get; }

        public long ItemsScanned => Interlocked.Read(ref _itemsScanned);

        public long BytesScanned => Interlocked.Read(ref _bytesScanned);

        public int DirectoriesScanned => Volatile.Read(ref _directoriesScanned);

        public int ErrorCount
        {
            get
            {
                lock (_errorsLock)
                {
                    return _errors.Count;
                }
            }
        }

        public IReadOnlyList<ScanError> GetErrors()
        {
            lock (_errorsLock)
            {
                return _errors.ToArray();
            }
        }

        public void RecordFile(string path, long bytes)
        {
            var items = Interlocked.Increment(ref _itemsScanned);
            var scannedBytes = Interlocked.Add(ref _bytesScanned, Math.Max(0, bytes));
            ReportCounterProgressIfDue(path, items, scannedBytes);
        }

        public void RecordDirectory(string path)
        {
            var items = Interlocked.Increment(ref _itemsScanned);
            var scannedBytes = BytesScanned;
            Interlocked.Increment(ref _directoriesScanned);
            ReportCounterProgressIfDue(path, items, scannedBytes);
        }

        public void AddError(ScanError error)
        {
            lock (_errorsLock)
            {
                _errors.Add(error);
            }
        }

        public void ReportSnapshotIfDue(string currentPath, NodeBuilder root, bool force)
        {
            lock (_snapshotLock)
            {
                if (!force
                    && (ItemsScanned % Math.Max(1, Options.SnapshotItemInterval) != 0
                        || _snapshotStopwatch.Elapsed < Options.SnapshotMinimumInterval))
                {
                    return;
                }

                _snapshotStopwatch.Restart();
                _progress.Report(new ScanProgress(
                    Options.RootPath,
                    currentPath,
                    ItemsScanned,
                    BytesScanned,
                    DirectoriesScanned,
                    ErrorCount,
                    ScanPhase.Scanning,
                    _providerName,
                    root.ToStorageNode(Math.Max(1, Options.MaximumSnapshotChildren))));
            }
        }

        public bool TryEnterDirectory(string key)
        {
            lock (_activeDirectoriesLock)
            {
                return _activeDirectories.Add(key);
            }
        }

        public void LeaveDirectory(string key)
        {
            lock (_activeDirectoriesLock)
            {
                _activeDirectories.Remove(key);
            }
        }

        private void ReportCounterProgressIfDue(string path, long items, long scannedBytes)
        {
            if (items % Math.Max(1, Options.ProgressItemInterval) != 0)
            {
                return;
            }

            _progress.Report(new ScanProgress(
                Options.RootPath,
                path,
                items,
                scannedBytes,
                DirectoriesScanned,
                ErrorCount,
                ScanPhase.Scanning,
                _providerName));
        }
    }
}
