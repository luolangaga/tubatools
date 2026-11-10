namespace ComputerUse.Windows.Runtime;

internal static class PostActionObservationRetry
{
    private const int OwnedWindowTransitionDetectionPollLimit = 5;
    private const int OwnedWindowRetirementPollLimit = 20;
    private static readonly TimeSpan OwnedWindowRetirementPollDelay =
        TimeSpan.FromMilliseconds(100);
    internal static TResult Execute<TWindow, TResult>(
        Func<IReadOnlyList<TWindow>> listWindows,
        Func<TWindow, long> windowHandle,
        Func<TWindow, bool> windowActive,
        long originalWindowHandle,
        Func<TWindow, bool, TResult> buildSnapshot,
        Func<TResult> buildWindowlessSnapshot,
        Action<TimeSpan>? delay = null,
        Action<int, TWindow?, Exception>? onFailure = null,
        Func<TWindow, long>? windowOwnerHandle = null,
        long originalOwnerHandle = 0)
        where TWindow : class
    {
        ArgumentNullException.ThrowIfNull(listWindows);
        ArgumentNullException.ThrowIfNull(windowHandle);
        ArgumentNullException.ThrowIfNull(windowActive);
        ArgumentNullException.ThrowIfNull(buildSnapshot);
        ArgumentNullException.ThrowIfNull(buildWindowlessSnapshot);
        delay ??= Thread.Sleep;

        TWindow? attemptedWindow = null;
        var ownedTransitionChecked = false;
        return BoundedObservationRetry.Execute(
            () =>
            {
                attemptedWindow = null;
                var windows = listWindows();
                if (windows.Count == 0)
                {
                    return buildWindowlessSnapshot();
                }

                attemptedWindow = windows.FirstOrDefault(windowActive) ??
                    windows.FirstOrDefault(
                        value => windowHandle(value) == originalWindowHandle) ??
                    windows[0];
                var originalRetired = false;
                if (!ownedTransitionChecked &&
                    (originalOwnerHandle != 0 ||
                        windowOwnerHandle is not null))
                {
                    var originalWindow = windows.FirstOrDefault(
                        value => windowHandle(value) == originalWindowHandle);
                    var ownerHandle = originalOwnerHandle;
                    if (ownerHandle == 0 &&
                        originalWindow is not null &&
                        windowOwnerHandle is not null)
                    {
                        ownerHandle = windowOwnerHandle(originalWindow);
                    }
                    if (ownerHandle != 0)
                    {
                        ownedTransitionChecked = true;
                        originalRetired = originalWindow is null;
                        var transitionObserved = originalRetired ||
                            windowHandle(attemptedWindow) == ownerHandle;
                        for (var poll = 0;
                            !transitionObserved &&
                            poll < OwnedWindowTransitionDetectionPollLimit;
                            poll++)
                        {
                            delay(OwnedWindowRetirementPollDelay);
                            windows = listWindows();
                            if (windows.Count == 0)
                            {
                                attemptedWindow = null;
                                return buildWindowlessSnapshot();
                            }
                            originalWindow = windows.FirstOrDefault(
                                value =>
                                    windowHandle(value) ==
                                        originalWindowHandle);
                            attemptedWindow =
                                windows.FirstOrDefault(windowActive) ??
                                originalWindow ??
                                windows[0];
                            originalRetired = originalWindow is null;
                            transitionObserved =
                                originalRetired ||
                                windowHandle(attemptedWindow) == ownerHandle;
                        }
                        for (var poll = 0;
                            transitionObserved &&
                            !originalRetired &&
                            poll < OwnedWindowRetirementPollLimit;
                            poll++)
                        {
                            delay(OwnedWindowRetirementPollDelay);
                            windows = listWindows();
                            if (windows.Count == 0)
                            {
                                attemptedWindow = null;
                                return buildWindowlessSnapshot();
                            }
                            originalWindow = windows.FirstOrDefault(
                                value =>
                                    windowHandle(value) ==
                                        originalWindowHandle);
                            attemptedWindow =
                                windows.FirstOrDefault(windowActive) ??
                                originalWindow ??
                                windows[0];
                            originalRetired = originalWindow is null;
                        }
                    }
                }
                return buildSnapshot(attemptedWindow, originalRetired);
            },
            delay,
            (attempt, error) =>
                onFailure?.Invoke(attempt, attemptedWindow, error));
    }
}
