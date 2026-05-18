using System.Globalization;
using SpaceSweeper.Core.Scanning;

namespace SpaceSweeper.Core.Analysis;

public static class StorageNodeReportExporter
{
    public static void WriteCsv(
        TextWriter writer,
        StorageNode root,
        StorageNodeFilter filter,
        Func<StorageNode, StorageNodeTag?> tagResolver)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(tagResolver);

        var visibility = new StorageNodeVisibility(filter, tagResolver);
        writer.WriteLine("Path,Name,Kind,Bytes,Size,Files,Folders,LastWriteTime,Tag");
        foreach (var node in visibility.EnumerateVisible(root, includeRoot: true))
        {
            var tag = tagResolver(node)?.ToString() ?? string.Empty;
            writer.Write(EncodeCell(node.Path));
            writer.Write(',');
            writer.Write(EncodeCell(node.Name));
            writer.Write(',');
            writer.Write(node.Kind);
            writer.Write(',');
            writer.Write(node.Length);
            writer.Write(',');
            writer.Write(EncodeCell(FormatBytesInvariant(node.Length)));
            writer.Write(',');
            writer.Write(node.FileCount);
            writer.Write(',');
            writer.Write(node.DirectoryCount);
            writer.Write(',');
            writer.Write(EncodeCell(node.LastWriteTime?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty));
            writer.Write(',');
            writer.WriteLine(EncodeCell(tag));
        }
    }

    private static string EncodeCell(string value)
    {
        if (CanBeSpreadsheetFormula(value))
        {
            value = "'" + value;
        }

        return value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static bool CanBeSpreadsheetFormula(string value)
    {
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                return character is '=' or '+' or '-' or '@';
            }

            if (character is '\t' or '\r' or '\n')
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatBytesInvariant(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var value = Math.Max(0, bytes);
        var unitIndex = 0;
        var display = (double)value;

        while (display >= 1024 && unitIndex < units.Length - 1)
        {
            display /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{value} {units[unitIndex]}")
            : string.Create(CultureInfo.InvariantCulture, $"{display:0.##} {units[unitIndex]}");
    }
}
