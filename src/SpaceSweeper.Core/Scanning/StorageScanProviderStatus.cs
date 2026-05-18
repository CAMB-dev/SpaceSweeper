namespace SpaceSweeper.Core.Scanning;

public sealed record StorageScanProviderStatus(bool IsAvailable, string? Reason = null)
{
    public static StorageScanProviderStatus Available { get; } = new(true);

    public static StorageScanProviderStatus Unavailable(string reason) => new(false, reason);
}
