namespace ComputerUse.Windows.Runtime;

internal static class UiaSnapshotNodeReads
{
    internal static string RoleOrUnknown(Func<string> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return "unknown";
        }
    }

    internal static T? OptionalPattern<T>(Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }
}
