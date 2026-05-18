namespace SpaceSweeper.Core.Scanning;

public sealed record ScanResult(
    StorageNode Root,
    string ProviderName,
    TimeSpan Elapsed,
    IReadOnlyList<ScanError> Errors);
