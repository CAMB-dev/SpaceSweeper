using System.Diagnostics;
using System.IO;

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

    public Task<ScanResult> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);

        var stopwatch = Stopwatch.StartNew();
        var context = new ScanBuildContext(options, progress, Name);

        progress.Report(new ScanProgress(
            options.RootPath,
            options.RootPath,
            0,
            0,
            0,
            0,
            ScanPhase.Scanning,
            Name));

        var root = ScanPath(options.RootPath, context, cancellationToken);
        stopwatch.Stop();

        progress.Report(new ScanProgress(
            options.RootPath,
            options.RootPath,
            context.ItemsScanned,
            context.BytesScanned,
            context.DirectoriesScanned,
            context.Errors.Count,
            ScanPhase.Completed,
            Name));

        return Task.FromResult(new ScanResult(root, Name, stopwatch.Elapsed, context.Errors));
    }

    private static StorageNode ScanPath(
        string path,
        ScanBuildContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (File.Exists(path))
            {
                return ScanFile(path, context);
            }

            return ScanDirectory(path, context, cancellationToken);
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            var error = CreateError(path, ex);
            context.AddError(error);
            return StorageNode.Directory(
                path,
                StorageNodeKind.Directory,
                null,
                FileAttributes.Directory,
                Array.Empty<StorageNode>(),
                new[] { error });
        }
    }

    private static StorageNode ScanFile(string path, ScanBuildContext context)
    {
        var info = new FileInfo(path);
        context.RecordItem(path, info.Length, isDirectory: false);
        return StorageNode.File(path, info.Length, info.LastWriteTimeUtc, info.Attributes);
    }

    private static StorageNode ScanDirectory(
        string path,
        ScanBuildContext context,
        CancellationToken cancellationToken)
    {
        var info = new DirectoryInfo(path);
        var attributes = SafeGetAttributes(info, FileAttributes.Directory);
        var isReparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint);

        context.RecordItem(path, 0, isDirectory: true);

        if (isReparsePoint && !context.Options.FollowReparsePoints)
        {
            return StorageNode.Directory(
                path,
                GetDirectoryKind(path),
                SafeGetLastWriteTime(info),
                attributes,
                Array.Empty<StorageNode>(),
                Array.Empty<ScanError>());
        }

        var children = new List<StorageNode>();
        var localErrors = new List<ScanError>();
        var visitKey = context.Options.FollowReparsePoints ? GetDirectoryVisitKey(info) : null;

        if (visitKey is not null && !context.TryEnterDirectory(visitKey))
        {
            var error = new ScanError(path, ScanErrorKind.Unsupported, "Directory cycle detected through a reparse point.");
            localErrors.Add(error);
            context.AddError(error);

            return StorageNode.Directory(
                path,
                GetDirectoryKind(path),
                SafeGetLastWriteTime(info),
                attributes,
                Array.Empty<StorageNode>(),
                localErrors);
        }

        try
        {
            try
            {
                foreach (var child in info.EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (!context.Options.IncludeFiles && child is FileInfo)
                        {
                            continue;
                        }

                        children.Add(ScanPath(child.FullName, context, cancellationToken));
                    }
                    catch (Exception ex) when (IsExpectedFileSystemException(ex))
                    {
                        var error = CreateError(child.FullName, ex);
                        localErrors.Add(error);
                        context.AddError(error);
                    }
                }
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                var error = CreateError(path, ex);
                localErrors.Add(error);
                context.AddError(error);
            }
        }
        finally
        {
            if (visitKey is not null)
            {
                context.LeaveDirectory(visitKey);
            }
        }

        var orderedChildren = children
            .OrderByDescending(static child => child.Length)
            .ThenBy(static child => child.Kind)
            .ThenBy(static child => child.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return StorageNode.Directory(
            path,
            GetDirectoryKind(path),
            SafeGetLastWriteTime(info),
            attributes,
            orderedChildren,
            localErrors);
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

    private static string GetDirectoryVisitKey(DirectoryInfo info)
    {
        try
        {
            var target = info.LinkTarget is not null ? info.ResolveLinkTarget(returnFinalTarget: true) : null;
            return Path.GetFullPath(target?.FullName ?? info.FullName)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            return Path.GetFullPath(info.FullName)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static DateTimeOffset? SafeGetLastWriteTime(FileSystemInfo info)
    {
        try
        {
            return info.LastWriteTimeUtc;
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            return null;
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

    private sealed class ScanBuildContext
    {
        private readonly IProgress<ScanProgress> _progress;
        private readonly string _providerName;

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

        public long ItemsScanned { get; private set; }

        public long BytesScanned { get; private set; }

        public int DirectoriesScanned { get; private set; }

        public List<ScanError> Errors { get; } = [];

        private HashSet<string> ActiveDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void RecordItem(string path, long bytes, bool isDirectory)
        {
            ItemsScanned++;
            BytesScanned += Math.Max(0, bytes);

            if (isDirectory)
            {
                DirectoriesScanned++;
            }

            if (ItemsScanned % Math.Max(1, Options.ProgressItemInterval) == 0)
            {
                _progress.Report(new ScanProgress(
                    Options.RootPath,
                    path,
                    ItemsScanned,
                    BytesScanned,
                    DirectoriesScanned,
                    Errors.Count,
                    ScanPhase.Scanning,
                    _providerName));
            }
        }

        public void AddError(ScanError error)
        {
            Errors.Add(error);
        }

        public bool TryEnterDirectory(string key)
        {
            return ActiveDirectories.Add(key);
        }

        public void LeaveDirectory(string key)
        {
            ActiveDirectories.Remove(key);
        }
    }
}
