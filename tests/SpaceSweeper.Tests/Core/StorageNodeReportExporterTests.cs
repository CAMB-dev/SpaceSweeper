using System.Globalization;
using SpaceSweeper.Core.Analysis;
using SpaceSweeper.Core.Scanning;

namespace SpaceSweeper.Tests.Core;

public sealed class StorageNodeReportExporterTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 5, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WriteCsv_ExportsVisibleNodesAndTags()
    {
        var tagged = StorageNode.File(@"C:\scan\keep.iso", 2048, Timestamp, FileAttributes.Normal);
        var skipped = StorageNode.File(@"C:\scan\skip.tmp", 1024, Timestamp, FileAttributes.Normal);
        var root = StorageNode.Directory(
            @"C:\scan",
            StorageNodeKind.Directory,
            Timestamp,
            FileAttributes.Directory,
            [tagged, skipped],
            []);
        var filter = StorageNodeFilter.Compile("*.iso");
        using var writer = new StringWriter();

        StorageNodeReportExporter.WriteCsv(
            writer,
            root,
            filter,
            node => string.Equals(node.Path, tagged.Path, StringComparison.OrdinalIgnoreCase) ? StorageNodeTag.Green : null);

        var csv = writer.ToString();
        Assert.Contains("keep.iso", csv);
        Assert.Contains("Green", csv);
        Assert.DoesNotContain("skip.tmp", csv);
    }

    [Fact]
    public void WriteCsv_EncodesFormulaLikeTextCells()
    {
        var dangerous = StorageNode.File(@"C:\scan\=cmd.txt", 42, Timestamp, FileAttributes.Normal);
        var root = StorageNode.Directory(@"C:\scan", StorageNodeKind.Directory, Timestamp, FileAttributes.Directory, [dangerous], []);
        using var writer = new StringWriter();

        StorageNodeReportExporter.WriteCsv(writer, root, StorageNodeFilter.Empty, static _ => null);

        var csv = writer.ToString();
        Assert.Contains("'=cmd.txt", csv);
    }

    [Fact]
    public void WriteCsv_UsesInvariantSizeFormatting()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var node = StorageNode.File(@"C:\scan\large.bin", 1536, Timestamp, FileAttributes.Normal);
            var root = StorageNode.Directory(@"C:\scan", StorageNodeKind.Directory, Timestamp, FileAttributes.Directory, [node], []);
            using var writer = new StringWriter();

            StorageNodeReportExporter.WriteCsv(writer, root, StorageNodeFilter.Empty, static _ => null);

            Assert.Contains("1.5 KB", writer.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
