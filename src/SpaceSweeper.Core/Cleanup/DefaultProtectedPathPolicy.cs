using System.IO;

namespace SpaceSweeper.Core.Cleanup;

public sealed class DefaultProtectedPathPolicy : IProtectedPathPolicy
{
    private readonly IReadOnlyList<string> _protectedSubtrees;
    private readonly IReadOnlyList<string> _protectedExactPaths;

    public DefaultProtectedPathPolicy()
    {
        _protectedSubtrees = GetProtectedSubtrees().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _protectedExactPaths = GetProtectedExactPaths().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public ProtectedPathDecision Evaluate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ProtectedPathDecision.Deny("Path is empty.");
        }

        if (UsesUnsupportedWindowsNamespace(path))
        {
            return ProtectedPathDecision.Deny("Extended or device paths are not supported for cleanup.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ProtectedPathDecision.Deny("Path is invalid.");
        }

        if (UsesUnsupportedWindowsNamespace(fullPath))
        {
            return ProtectedPathDecision.Deny("Extended or device paths are not supported for cleanup.");
        }

        var root = Path.GetPathRoot(fullPath);
        if (IsSamePath(fullPath, root))
        {
            return ProtectedPathDecision.Deny("Drive roots cannot be cleaned.");
        }

        foreach (var protectedPath in _protectedExactPaths)
        {
            if (IsSamePath(fullPath, protectedPath))
            {
                return ProtectedPathDecision.Deny($"Path is protected by policy: {protectedPath}");
            }
        }

        foreach (var protectedRoot in _protectedSubtrees)
        {
            if (IsSameOrChildPath(fullPath, protectedRoot))
            {
                return ProtectedPathDecision.Deny($"Path is protected by policy: {protectedRoot}");
            }
        }

        return ProtectedPathDecision.Allow;
    }

    private static IEnumerable<string> GetProtectedSubtrees()
    {
        yield return AppContext.BaseDirectory;

        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.Windows,
                     Environment.SpecialFolder.System,
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                     Environment.SpecialFolder.CommonApplicationData
                 })
        {
            var path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> GetProtectedExactPaths()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            yield return userProfile;
        }
    }

    private static bool IsSamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrChildPath(string path, string possibleParent)
    {
        var normalizedPath = Normalize(path);
        var normalizedParent = Normalize(possibleParent);

        return string.Equals(normalizedPath, normalizedParent, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool UsesUnsupportedWindowsNamespace(string path)
    {
        return OperatingSystem.IsWindows()
            && (path.StartsWith(@"\\?\", StringComparison.Ordinal)
                || path.StartsWith(@"\\.\", StringComparison.Ordinal)
                || path.StartsWith(@"\??\", StringComparison.Ordinal));
    }
}
