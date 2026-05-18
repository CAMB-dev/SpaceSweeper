namespace SpaceSweeper.Core.Cleanup;

public sealed record CleanupPreview(
    IReadOnlyList<CleanupTarget> AllowedItems,
    IReadOnlyList<CleanupBlockedItem> BlockedItems)
{
    public bool CanExecute => AllowedItems.Count > 0;

    public long TotalBytes => AllowedItems.Sum(static item => item.Length);
}
