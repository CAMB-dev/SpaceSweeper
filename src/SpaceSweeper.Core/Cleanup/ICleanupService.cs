namespace SpaceSweeper.Core.Cleanup;

public interface ICleanupService
{
    Task<CleanupPreview> PreviewAsync(
        IReadOnlyList<CleanupTarget> targets,
        CancellationToken cancellationToken = default);

    Task<CleanupResult> CleanupAsync(
        IReadOnlyList<CleanupTarget> targets,
        CancellationToken cancellationToken = default);
}
