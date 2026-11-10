namespace ComputerUse.Windows.Runtime;

internal static class AuthorizedCanonicalResolutionPolicy
{
    internal static bool UseCanonicalBeforeProviderIdentity(
        bool semanticRebindAuthorized,
        string providerObjectPath)
    {
        return semanticRebindAuthorized &&
            providerObjectPath.StartsWith(
                "runtime:",
                StringComparison.Ordinal);
    }
}
