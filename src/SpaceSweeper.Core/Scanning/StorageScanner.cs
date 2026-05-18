using System.IO;

namespace SpaceSweeper.Core.Scanning;

public sealed class StorageScanner : IStorageScanner
{
    private readonly IReadOnlyList<IStorageScanProvider> _providers;

    public StorageScanner(IEnumerable<IStorageScanProvider> providers)
    {
        _providers = providers.ToArray();
        if (_providers.Count == 0)
        {
            throw new ArgumentException("At least one scan provider is required.", nameof(providers));
        }
    }

    public async Task<ScanResult> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            throw new ArgumentException("A root path is required.", nameof(options));
        }

        return await Task.Run(
            () => RunProvidersAsync(options, progress ?? new Progress<ScanProgress>(), cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ScanResult> RunProvidersAsync(
        ScanOptions options,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        var fullRoot = Path.GetFullPath(options.RootPath);
        var normalizedOptions = options with { RootPath = fullRoot };
        var providerErrors = new List<ScanError>();

        progress.Report(new ScanProgress(
            fullRoot,
            fullRoot,
            0,
            0,
            0,
            0,
            ScanPhase.Preparing,
            "selector"));

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status = await provider.GetStatusAsync(normalizedOptions, cancellationToken).ConfigureAwait(false);
            if (!status.IsAvailable)
            {
                providerErrors.Add(new ScanError(
                    fullRoot,
                    ScanErrorKind.Unsupported,
                    $"{provider.Name}: {status.Reason ?? "Provider unavailable."}"));
                continue;
            }

            try
            {
                return await provider.ScanAsync(normalizedOptions, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                progress.Report(new ScanProgress(
                    fullRoot,
                    fullRoot,
                    0,
                    0,
                    0,
                    providerErrors.Count,
                    ScanPhase.Cancelled,
                    provider.Name));
                throw;
            }
            catch (Exception ex) when (provider != _providers[^1])
            {
                providerErrors.Add(new ScanError(fullRoot, ScanErrorKind.Unknown, $"{provider.Name}: {ex.Message}"));
            }
        }

        throw new InvalidOperationException(
            "No scan provider could scan the requested path. " +
            string.Join(" ", providerErrors.Select(static error => error.Message)));
    }
}
