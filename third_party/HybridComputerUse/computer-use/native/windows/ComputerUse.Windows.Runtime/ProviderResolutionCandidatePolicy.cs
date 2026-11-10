namespace ComputerUse.Windows.Runtime;

internal static class ProviderResolutionCandidatePolicy
{
    internal static bool ShouldInclude(
        string providerObjectPath,
        ISet<string> seenProviderObjectPaths)
    {
        return !providerObjectPath.StartsWith(
                   "runtime:",
                   StringComparison.Ordinal) ||
            seenProviderObjectPaths.Add(providerObjectPath);
    }
}
