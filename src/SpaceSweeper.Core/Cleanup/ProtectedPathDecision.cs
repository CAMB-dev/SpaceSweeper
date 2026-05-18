namespace SpaceSweeper.Core.Cleanup;

public sealed record ProtectedPathDecision(bool IsAllowed, string? Reason)
{
    public static ProtectedPathDecision Allow { get; } = new(true, null);

    public static ProtectedPathDecision Deny(string reason) => new(false, reason);
}
