namespace ComputerUse.Windows.Runtime;

internal static class UiaSemanticWindowPredicate
{
    // Unsupported role metadata is distinct from a known non-window and from a read error.
    internal static bool? Read<T>(
        Func<(bool Supported, T Value)> controlType,
        T windowType,
        Func<bool> windowPattern)
    {
        var role = controlType();
        if (!role.Supported) { return null; }
        return EqualityComparer<T>.Default.Equals(role.Value, windowType) && windowPattern();
    }
}
