namespace ComputerUse.Windows.Runtime;

internal static class WindowsToggleState
{
    internal static string Canonicalize(string state)
    {
        return state.Trim().ToLowerInvariant() switch
        {
            "on" => "checked",
            "off" => "unchecked",
            "indeterminate" => "mixed",
            _ => string.Empty,
        };
    }
}
