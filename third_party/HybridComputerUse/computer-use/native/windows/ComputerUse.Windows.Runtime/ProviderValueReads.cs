namespace ComputerUse.Windows.Runtime;

internal static class ProviderValueReads
{
    internal static string OptionalString(Func<string> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return string.Empty;
        }
    }
}
