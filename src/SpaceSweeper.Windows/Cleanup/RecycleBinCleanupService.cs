using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using SpaceSweeper.Core.Cleanup;
using SpaceSweeper.Core.Scanning;

namespace SpaceSweeper.Windows.Cleanup;

public sealed class RecycleBinCleanupService : ICleanupService
{
    private readonly IProtectedPathPolicy _protectedPathPolicy;

    public RecycleBinCleanupService(IProtectedPathPolicy protectedPathPolicy)
    {
        _protectedPathPolicy = protectedPathPolicy;
    }

    public Task<CleanupPreview> PreviewAsync(
        IReadOnlyList<CleanupTarget> targets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var allowed = new List<CleanupTarget>();
        var blocked = new List<CleanupBlockedItem>();

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var decision = _protectedPathPolicy.Evaluate(target.Path);
            if (!decision.IsAllowed)
            {
                blocked.Add(new CleanupBlockedItem(target.Path, decision.Reason ?? "Path is protected."));
            }
            else if (target.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                blocked.Add(new CleanupBlockedItem(target.Path, "Reparse points are blocked by cleanup policy."));
            }
            else if (!TryGetFileIdentity(target.Path, out var identity, out var identityError))
            {
                blocked.Add(new CleanupBlockedItem(target.Path, identityError));
            }
            else
            {
                allowed.Add(target with { ProviderIdentity = identity });
            }
        }

        return Task.FromResult(new CleanupPreview(allowed, blocked));
    }

    public Task<CleanupResult> CleanupAsync(
        IReadOnlyList<CleanupTarget> targets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var preview = ValidateApprovedTargets(targets, cancellationToken);
        var failures = new List<CleanupFailure>();
        var completed = 0;

        foreach (var target in preview.AllowedItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!TargetStillMatches(target, out var mismatchReason))
                {
                    failures.Add(new CleanupFailure(target.Path, mismatchReason));
                    continue;
                }

                if (target.Kind is StorageNodeKind.Directory or StorageNodeKind.Drive)
                {
                    FileSystem.DeleteDirectory(
                        target.Path,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin,
                        UICancelOption.ThrowException);
                }
                else
                {
                    FileSystem.DeleteFile(
                        target.Path,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin,
                        UICancelOption.ThrowException);
                }

                completed++;
            }
            catch (Exception ex)
            {
                failures.Add(new CleanupFailure(target.Path, ex.Message));
            }
        }

        foreach (var blocked in preview.BlockedItems)
        {
            failures.Add(new CleanupFailure(blocked.Path, blocked.Reason));
        }

        return Task.FromResult(new CleanupResult(
            targets.Count,
            completed,
            preview.TotalBytes,
            failures));
    }

    private CleanupPreview ValidateApprovedTargets(
        IReadOnlyList<CleanupTarget> targets,
        CancellationToken cancellationToken)
    {
        var allowed = new List<CleanupTarget>();
        var blocked = new List<CleanupBlockedItem>();

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var decision = _protectedPathPolicy.Evaluate(target.Path);
            if (!decision.IsAllowed)
            {
                blocked.Add(new CleanupBlockedItem(target.Path, decision.Reason ?? "Path is protected."));
            }
            else if (target.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                blocked.Add(new CleanupBlockedItem(target.Path, "Reparse points are blocked by cleanup policy."));
            }
            else if (target.ProviderIdentity is null)
            {
                blocked.Add(new CleanupBlockedItem(target.Path, "Cleanup target identity was not captured during preview."));
            }
            else
            {
                allowed.Add(target);
            }
        }

        return new CleanupPreview(allowed, blocked);
    }

    private static bool TargetStillMatches(CleanupTarget target, out string reason)
    {
        reason = string.Empty;

        if (target.Kind is StorageNodeKind.Directory or StorageNodeKind.Drive)
        {
            if (!Directory.Exists(target.Path))
            {
                reason = "Path no longer exists.";
                return false;
            }

            var attributes = File.GetAttributes(target.Path);
            if (!attributes.HasFlag(FileAttributes.Directory))
            {
                reason = "Path type changed after preview.";
                return false;
            }

            if (attributes.HasFlag(FileAttributes.ReparsePoint) != target.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                reason = "Path reparse-point state changed after preview.";
                return false;
            }

            if (!IdentityStillMatches(target, out reason))
            {
                return false;
            }

            return true;
        }

        if (!File.Exists(target.Path))
        {
            reason = "Path no longer exists.";
            return false;
        }

        var info = new FileInfo(target.Path);
        if (info.Attributes.HasFlag(FileAttributes.Directory))
        {
            reason = "Path type changed after preview.";
            return false;
        }

        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) != target.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            reason = "Path reparse-point state changed after preview.";
            return false;
        }

        if (info.Length != target.Length)
        {
            reason = "File size changed after preview.";
            return false;
        }

        if (!IdentityStillMatches(target, out reason))
        {
            return false;
        }

        return true;
    }

    private static bool IdentityStillMatches(CleanupTarget target, out string reason)
    {
        reason = string.Empty;

        if (target.ProviderIdentity is null)
        {
            reason = "Cleanup target identity was not captured during preview.";
            return false;
        }

        if (!TryGetFileIdentity(target.Path, out var currentIdentity, out var identityError))
        {
            reason = identityError;
            return false;
        }

        if (!string.Equals(target.ProviderIdentity, currentIdentity, StringComparison.Ordinal))
        {
            reason = "Path identity changed after preview.";
            return false;
        }

        return true;
    }

    private static bool TryGetFileIdentity(string path, out string? identity, out string error)
    {
        identity = null;
        error = string.Empty;

        using var handle = CreateFileW(
            path,
            0,
            FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            FileFlagsAndAttributes.FileFlagBackupSemantics,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            error = $"Cannot verify target identity. Win32 error: {Marshal.GetLastWin32Error()}";
            return false;
        }

        if (!GetFileInformationByHandle(handle, out var info))
        {
            error = $"Cannot read target identity. Win32 error: {Marshal.GetLastWin32Error()}";
            return false;
        }

        identity = $"{info.VolumeSerialNumber:X8}:{info.FileIndexHigh:X8}{info.FileIndexLow:X8}";
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        FileFlagsAndAttributes flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation fileInformation);

    [Flags]
    private enum FileFlagsAndAttributes : uint
    {
        FileFlagBackupSemantics = 0x02000000
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
