using Microsoft.VisualBasic.FileIO;
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
            else
            {
                allowed.Add(target);
            }
        }

        return Task.FromResult(new CleanupPreview(allowed, blocked));
    }

    public async Task<CleanupResult> CleanupAsync(
        IReadOnlyList<CleanupTarget> targets,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(targets, cancellationToken).ConfigureAwait(false);
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

        return new CleanupResult(
            targets.Count,
            completed,
            preview.TotalBytes,
            failures);
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

        return true;
    }
}
