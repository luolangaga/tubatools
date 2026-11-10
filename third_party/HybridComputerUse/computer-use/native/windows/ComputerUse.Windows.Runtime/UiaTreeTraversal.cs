using FlaUI.Core;
using FlaUI.Core.AutomationElements;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class UiaProvider
{
    private static AutomationElement[] ReadControlChildren(
        AutomationElement element,
        ITreeWalker walker,
        int limit)
    {
        return RunWithoutUiaCache(
            () => BoundedSiblingTraversal.Collect(
                () => walker.GetFirstChild(element),
                walker.GetNextSibling,
                limit).ToArray());
    }

    internal static T RunWithoutUiaCache<T>(Func<T> operation)
    {
        using (CacheRequest.ForceNoCache())
        {
            return operation();
        }
    }
}
