namespace ComputerUse.Windows.Runtime;

internal static class BoundedSiblingTraversal
{
    internal static IReadOnlyList<T> Collect<T>(
        Func<T?> first,
        Func<T, T?> next,
        int limit)
        where T : class
    {
        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }
        var values = new List<T>(limit);
        if (limit == 0)
        {
            return values;
        }
        var current = first();
        while (current is not null && values.Count < limit)
        {
            values.Add(current);
            if (values.Count < limit)
            {
                current = next(current);
            }
        }
        return values;
    }
}
