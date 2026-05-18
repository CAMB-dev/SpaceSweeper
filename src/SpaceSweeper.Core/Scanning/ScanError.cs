namespace SpaceSweeper.Core.Scanning;

public sealed record ScanError(
    string Path,
    ScanErrorKind Kind,
    string Message);
