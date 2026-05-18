namespace SpaceSweeper.Core.Utilities;

public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string FormatBytes(long bytes)
    {
        var value = Math.Max(0, bytes);
        var unitIndex = 0;
        var display = (double)value;

        while (display >= 1024 && unitIndex < Units.Length - 1)
        {
            display /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value} {Units[unitIndex]}"
            : $"{display:0.##} {Units[unitIndex]}";
    }
}
