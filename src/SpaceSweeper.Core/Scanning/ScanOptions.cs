namespace SpaceSweeper.Core.Scanning;

public sealed record ScanOptions(string RootPath)
{
    public bool IncludeFiles { get; init; } = true;

    public bool FollowReparsePoints { get; init; }

    public int ProgressItemInterval { get; init; } = 256;
}
