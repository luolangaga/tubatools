namespace ComputerUse.Windows.Runtime;

internal static class ResolutionCandidateScope
{
    internal static void Visit<TScope, TCandidate>(
        Func<TScope> activate,
        Action<Func<TCandidate, bool>> enumerate,
        Func<TCandidate, bool> visit)
        where TScope : IDisposable
    {
        using var scope = activate();
        enumerate(visit);
    }
}
