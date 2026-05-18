using System.IO;

namespace SpaceSweeper.Core.Scanning;

public sealed record StorageNode(
    string Path,
    string Name,
    StorageNodeKind Kind,
    long Length,
    long? AllocatedLength,
    int FileCount,
    int DirectoryCount,
    DateTimeOffset? LastWriteTime,
    FileAttributes Attributes,
    IReadOnlyList<StorageNode> Children,
    IReadOnlyList<ScanError> Errors)
{
    public bool IsContainer => Kind is StorageNodeKind.Directory or StorageNodeKind.Drive;

    public bool HasErrors => Errors.Count > 0 || Children.Any(static child => child.HasErrors);

    public static StorageNode File(
        string path,
        long length,
        DateTimeOffset? lastWriteTime,
        FileAttributes attributes)
    {
        return new StorageNode(
            path,
            GetDisplayName(path),
            StorageNodeKind.File,
            Math.Max(0, length),
            null,
            1,
            0,
            lastWriteTime,
            attributes,
            Array.Empty<StorageNode>(),
            Array.Empty<ScanError>());
    }

    public static StorageNode Directory(
        string path,
        StorageNodeKind kind,
        DateTimeOffset? lastWriteTime,
        FileAttributes attributes,
        IReadOnlyList<StorageNode> children,
        IReadOnlyList<ScanError> errors)
    {
        var length = children.Sum(static child => child.Length);
        var fileCount = children.Sum(static child => child.FileCount);
        var directoryCount = 1 + children.Sum(static child => child.DirectoryCount);

        return new StorageNode(
            path,
            GetDisplayName(path),
            kind,
            length,
            null,
            fileCount,
            directoryCount,
            lastWriteTime,
            attributes,
            children,
            errors);
    }

    public static string GetDisplayName(string path)
    {
        var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var name = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }
}
