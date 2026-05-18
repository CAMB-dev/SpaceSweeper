namespace SpaceSweeper.Core.Scanning;

public sealed record ScanProgress(
    string RootPath,
    string CurrentPath,
    long ItemsScanned,
    long BytesScanned,
    int DirectoriesScanned,
    int ErrorCount,
    ScanPhase Phase,
    string ProviderName);
