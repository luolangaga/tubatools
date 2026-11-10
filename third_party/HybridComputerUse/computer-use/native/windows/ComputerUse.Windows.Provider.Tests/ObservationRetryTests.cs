namespace ComputerUse.Windows.Runtime;

internal static class ObservationRetryTests
{
    internal static void Run()
    {
        var observationAttempts = 0;
        var observationDelays = new List<TimeSpan>();
        var observedFailureAttempts = new List<int>();
        var recoveredObservation = BoundedObservationRetry.Execute(
            () =>
            {
                observationAttempts++;
                if (observationAttempts == 1)
                {
                    throw new InvalidOperationException("transient snapshot failure");
                }
                return "current-state";
            },
            observationDelays.Add,
            (attempt, _) => observedFailureAttempts.Add(attempt));
        if (recoveredObservation != "current-state" ||
            observationAttempts != 2 ||
            observationDelays.Count != 1 ||
            observationDelays[0] != TimeSpan.FromMilliseconds(75) ||
            observedFailureAttempts.Count != 1 ||
            observedFailureAttempts[0] != 1)
        {
            throw new InvalidDataException(
                "Bounded post-action observation did not recover exactly once.");
        }

        observationAttempts = 0;
        observationDelays.Clear();
        observedFailureAttempts.Clear();
        try
        {
            _ = BoundedObservationRetry.Execute<string>(
                () =>
                {
                    observationAttempts++;
                    throw new InvalidOperationException(
                        "persistent snapshot failure");
                },
                observationDelays.Add,
                (attempt, _) => observedFailureAttempts.Add(attempt));
            throw new InvalidDataException(
                "Exhausted post-action observation retry unexpectedly succeeded.");
        }
        catch (InvalidOperationException error)
            when (error.Message == "persistent snapshot failure")
        {
        }
        if (observationAttempts != 3 ||
            observationDelays.Count != 2 ||
            observationDelays[0] != TimeSpan.FromMilliseconds(75) ||
            observationDelays[1] != TimeSpan.FromMilliseconds(150) ||
            !observedFailureAttempts.SequenceEqual(new[] { 1, 2, 3 }))
        {
            throw new InvalidDataException(
                "Bounded post-action observation exceeded or shortened its retry budget.");
        }

        var windowEnumerations = 0;
        var selectedWindowHandles = new List<long>();
        observationDelays.Clear();
        observedFailureAttempts.Clear();
        var transitionedObservation = PostActionObservationRetry.Execute(
            () =>
            {
                windowEnumerations++;
                return windowEnumerations == 1
                    ? new[]
                    {
                        new TestWindow(101, true),
                        new TestWindow(202, false),
                    }
                    : new[]
                    {
                        new TestWindow(202, true),
                    };
            },
            window => window.Handle,
            window => window.Active,
            101,
            (window, _) =>
            {
                selectedWindowHandles.Add(window.Handle);
                if (window.Handle == 101)
                {
                    throw new InvalidOperationException(
                        "source window closed during snapshot");
                }
                return $"window:{window.Handle}";
            },
            () => "window-absent",
            observationDelays.Add,
            (attempt, _, _) => observedFailureAttempts.Add(attempt));
        if (transitionedObservation != "window:202" ||
            windowEnumerations != 2 ||
            !selectedWindowHandles.SequenceEqual(new long[] { 101, 202 }) ||
            observationDelays.Count != 1 ||
            observationDelays[0] != TimeSpan.FromMilliseconds(75) ||
            !observedFailureAttempts.SequenceEqual(new[] { 1 }))
        {
            throw new InvalidDataException(
                "Post-action observation did not re-enumerate and follow the current window.");
        }

        selectedWindowHandles.Clear();
        windowEnumerations = 0;
        observationDelays.Clear();
        var activeReplacementObservation = PostActionObservationRetry.Execute(
            () =>
            {
                windowEnumerations++;
                return windowEnumerations < 3
                    ? new[]
                    {
                        new TestWindow(101, false, 202),
                        new TestWindow(202, true),
                    }
                    : new[]
                {
                    new TestWindow(202, true),
                };
            },
            window => window.Handle,
            window => window.Active,
            101,
            (window, originalOwnedWindowRetired) =>
            {
                selectedWindowHandles.Add(window.Handle);
                if (!originalOwnedWindowRetired)
                {
                    throw new InvalidDataException(
                        "Retired owned-window transition was not surfaced to the snapshot.");
                }
                return $"window:{window.Handle}";
            },
            () => "window-absent",
            observationDelays.Add,
            windowOwnerHandle: window => window.OwnerHandle);
        if (activeReplacementObservation != "window:202" ||
            windowEnumerations != 3 ||
            !selectedWindowHandles.SequenceEqual(new long[] { 202 }) ||
            !observationDelays.SequenceEqual(
                new[]
                {
                    TimeSpan.FromMilliseconds(100),
                    TimeSpan.FromMilliseconds(100),
                }))
        {
            throw new InvalidDataException(
                "Post-action observation did not wait for an owned window to retire " +
                "and mark the final snapshot for visual stabilization.");
        }

        selectedWindowHandles.Clear();
        windowEnumerations = 0;
        observationDelays.Clear();
        var delayedOwnedTransitionObservation = PostActionObservationRetry.Execute(
            () =>
            {
                windowEnumerations++;
                return windowEnumerations switch
                {
                    1 or 2 => new[]
                    {
                        new TestWindow(101, true, 202),
                        new TestWindow(202, false),
                    },
                    3 => new[]
                    {
                        new TestWindow(101, false, 202),
                        new TestWindow(202, true),
                    },
                    _ => new[]
                    {
                        new TestWindow(202, true),
                    },
                };
            },
            window => window.Handle,
            window => window.Active,
            101,
            (window, originalOwnedWindowRetired) =>
            {
                selectedWindowHandles.Add(window.Handle);
                if (!originalOwnedWindowRetired)
                {
                    throw new InvalidDataException(
                        "Delayed retired owned-window transition was not surfaced.");
                }
                return $"window:{window.Handle}";
            },
            () => "window-absent",
            observationDelays.Add,
            windowOwnerHandle: window => window.OwnerHandle);
        if (delayedOwnedTransitionObservation != "window:202" ||
            windowEnumerations != 4 ||
            !selectedWindowHandles.SequenceEqual(new long[] { 202 }) ||
            observationDelays.Count != 3 ||
            observationDelays.Any(
                value => value != TimeSpan.FromMilliseconds(100)))
        {
            throw new InvalidDataException(
                "Post-action observation did not detect a delayed owned-window " +
                "transition before waiting for retirement.");
        }

        selectedWindowHandles.Clear();
        windowEnumerations = 0;
        observationDelays.Clear();
        var independentWindowObservation = PostActionObservationRetry.Execute(
            () =>
            {
                windowEnumerations++;
                return new[]
                {
                    new TestWindow(101, false),
                    new TestWindow(202, true),
                };
            },
            window => window.Handle,
            window => window.Active,
            101,
            (window, _) =>
            {
                selectedWindowHandles.Add(window.Handle);
                return $"window:{window.Handle}";
            },
            () => "window-absent",
            observationDelays.Add,
            windowOwnerHandle: window => window.OwnerHandle);
        if (independentWindowObservation != "window:202" ||
            windowEnumerations != 1 ||
            !selectedWindowHandles.SequenceEqual(new long[] { 202 }) ||
            observationDelays.Count != 0)
        {
            throw new InvalidDataException(
                "Post-action observation delayed an unrelated same-process window switch.");
        }

        selectedWindowHandles.Clear();
        windowEnumerations = 0;
        observationDelays.Clear();
        var boundedOwnedWindowObservation = PostActionObservationRetry.Execute(
            () =>
            {
                windowEnumerations++;
                return new[]
                {
                    new TestWindow(101, false, 202),
                    new TestWindow(202, true),
                };
            },
            window => window.Handle,
            window => window.Active,
            101,
            (window, originalOwnedWindowRetired) =>
            {
                selectedWindowHandles.Add(window.Handle);
                if (originalOwnedWindowRetired)
                {
                    throw new InvalidDataException(
                        "Still-present owned window was reported as retired.");
                }
                return $"window:{window.Handle}";
            },
            () => "window-absent",
            observationDelays.Add,
            windowOwnerHandle: window => window.OwnerHandle);
        if (boundedOwnedWindowObservation != "window:202" ||
            windowEnumerations != 21 ||
            !selectedWindowHandles.SequenceEqual(new long[] { 202 }) ||
            observationDelays.Count != 20 ||
            observationDelays.Any(
                value => value != TimeSpan.FromMilliseconds(100)))
        {
            throw new InvalidDataException(
                "Owned-window retirement polling was not bounded to two seconds.");
        }

        selectedWindowHandles.Clear();
        windowEnumerations = 0;
        observationDelays.Clear();
        var crossProcessOwnerObservation = PostActionObservationRetry.Execute(
            () =>
            {
                windowEnumerations++;
                return new[]
                {
                    new TestWindow(202, true, ProcessId: 22),
                };
            },
            window => window.Handle,
            window => window.Active,
            101,
            (window, originalOwnedWindowRetired) =>
            {
                selectedWindowHandles.Add(window.Handle);
                if (!originalOwnedWindowRetired)
                {
                    throw new InvalidDataException(
                        "An already-retired cross-process owned window was not marked retired.");
                }
                return $"window:{window.Handle}";
            },
            () => "window-absent",
            observationDelays.Add,
            windowOwnerHandle: window => window.OwnerHandle,
            originalOwnerHandle: 202);
        if (crossProcessOwnerObservation != "window:202" ||
            windowEnumerations != 1 ||
            !selectedWindowHandles.SequenceEqual(new long[] { 202 }) ||
            observationDelays.Count != 0)
        {
            throw new InvalidDataException(
                "Post-action observation did not follow an exact owner after the " +
                "cross-process source window had already retired.");
        }

        var ownerIdentity = new PostActionOwnerIdentity(
            (nint)202,
            22,
            "pid:22:start:100",
            7);
        if (!PostActionOwnerTransitionPolicy.Matches(
                ownerIdentity,
                (nint)202,
                22,
                "pid:22:start:100",
                7))
        {
            throw new InvalidDataException(
                "The exact cross-process owner identity was rejected.");
        }
        var capturedOwnerHandle = (nint)0;
        var capturedOwner = PostActionOwnerTransitionPolicy.Capture(
            (nint)202,
            handle =>
            {
                capturedOwnerHandle = handle;
                return 22;
            },
            processId =>
                processId == 22 ? "pid:22:start:100" : null,
            handle => handle == (nint)202 ? 7 : -1);
        if (capturedOwner is null ||
            capturedOwnerHandle != (nint)202 ||
            capturedOwner.Handle != (nint)202 ||
            capturedOwner.ProcessId != 22 ||
            capturedOwner.ProcessGeneration != "pid:22:start:100" ||
            capturedOwner.LifecycleGeneration != 7)
        {
            throw new InvalidDataException(
                "Exact owner capture incorrectly required visible-window inventory.");
        }
        if (PostActionOwnerTransitionPolicy.Capture(
                (nint)202,
                _ => null,
                _ => "pid:22:start:100",
                _ => 7) is not null)
        {
            throw new InvalidDataException(
                "Owner capture accepted an unresolved native process identity.");
        }
        foreach (var mismatch in new[]
        {
            ((nint)203, 22, "pid:22:start:100", 7L),
            ((nint)202, 23, "pid:22:start:100", 7L),
            ((nint)202, 22, "pid:22:start:101", 7L),
            ((nint)202, 22, "pid:22:start:100", 8L),
        })
        {
            if (PostActionOwnerTransitionPolicy.Matches(
                ownerIdentity,
                mismatch.Item1,
                mismatch.Item2,
                mismatch.Item3,
                mismatch.Item4))
            {
                throw new InvalidDataException(
                    "A changed cross-process owner identity was accepted.");
            }
        }

        var desktopCaptureAttempts = 0;
        observationDelays.Clear();
        observedFailureAttempts.Clear();
        var recoveredDesktopObservation = DesktopObservationRetry.Execute(
            () =>
            {
                desktopCaptureAttempts++;
                if (desktopCaptureAttempts == 1)
                {
                    throw new InvalidOperationException(
                        "transient desktop capture failure");
                }
                return "current-desktop";
            },
            observationDelays.Add,
            (attempt, _) => observedFailureAttempts.Add(attempt));
        if (recoveredDesktopObservation != "current-desktop" ||
            desktopCaptureAttempts != 2 ||
            observationDelays.Count != 1 ||
            observationDelays[0] != TimeSpan.FromMilliseconds(75) ||
            !observedFailureAttempts.SequenceEqual(new[] { 1 }))
        {
            throw new InvalidDataException(
                "Desktop post-action observation did not recover without replay.");
        }

        desktopCaptureAttempts = 0;
        observationDelays.Clear();
        observedFailureAttempts.Clear();
        try
        {
            _ = DesktopObservationRetry.Execute<string>(
                () =>
                {
                    desktopCaptureAttempts++;
                    throw new InvalidOperationException(
                        "persistent desktop capture failure");
                },
                observationDelays.Add,
                (attempt, _) => observedFailureAttempts.Add(attempt));
            throw new InvalidDataException(
                "Exhausted desktop post-action observation unexpectedly succeeded.");
        }
        catch (InvalidOperationException error)
            when (error.Message == "persistent desktop capture failure")
        {
        }
        if (desktopCaptureAttempts != 3 ||
            observationDelays.Count != 2 ||
            observationDelays[0] != TimeSpan.FromMilliseconds(75) ||
            observationDelays[1] != TimeSpan.FromMilliseconds(150) ||
            !observedFailureAttempts.SequenceEqual(new[] { 1, 2, 3 }))
        {
            throw new InvalidDataException(
                "Desktop post-action observation exceeded or shortened its retry budget.");
        }
    }
}
