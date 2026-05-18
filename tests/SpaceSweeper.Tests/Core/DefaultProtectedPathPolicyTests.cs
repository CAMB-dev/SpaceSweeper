using SpaceSweeper.Core.Cleanup;

namespace SpaceSweeper.Tests.Core;

public sealed class DefaultProtectedPathPolicyTests
{
    [Fact]
    public void Evaluate_BlocksDriveRoot()
    {
        var policy = new DefaultProtectedPathPolicy();
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;

        var decision = policy.Evaluate(root);

        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void Evaluate_AllowsTempChildPath()
    {
        var policy = new DefaultProtectedPathPolicy();
        var tempChild = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "SpaceSweeper.Tests", Guid.NewGuid().ToString("N"));

        var decision = policy.Evaluate(tempChild);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Evaluate_BlocksExtendedLengthAlias()
    {
        var policy = new DefaultProtectedPathPolicy();

        var decision = policy.Evaluate(@"\\?\C:\Windows");

        Assert.False(decision.IsAllowed);
    }
}
