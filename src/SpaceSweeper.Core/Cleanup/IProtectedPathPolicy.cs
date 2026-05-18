namespace SpaceSweeper.Core.Cleanup;

public interface IProtectedPathPolicy
{
    ProtectedPathDecision Evaluate(string path);
}
