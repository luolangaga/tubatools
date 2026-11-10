using System.Drawing;

namespace ComputerUse.Windows.Runtime;

internal static class UiaRootProjectionTests
{
    internal static void Run()
    {
        using (var automation = WindowsUiaAutomationFactory.Create())
        {
            if (automation.TransactionTimeout != TimeSpan.FromSeconds(3))
            {
                throw new InvalidDataException(
                    "Windows UIA transaction timeout must be exactly three seconds.");
            }
        }

        var ownerProjectionCandidates = new[]
        {
            new OwnedNativeWindowCandidate(
                Handle: 0x10,
                OwnerHandle: 0,
                ProcessId: 41,
                IndependentWindow: true,
                Bounds: Rectangle.Empty),
            new OwnedNativeWindowCandidate(
                Handle: 0x20,
                OwnerHandle: 0x10,
                ProcessId: 41,
                IndependentWindow: false,
                Bounds: Rectangle.Empty),
            new OwnedNativeWindowCandidate(
                Handle: 0x21,
                OwnerHandle: 0x20,
                ProcessId: 41,
                IndependentWindow: false,
                Bounds: Rectangle.Empty),
            new OwnedNativeWindowCandidate(
                Handle: 0x30,
                OwnerHandle: 0,
                ProcessId: 41,
                IndependentWindow: true,
                Bounds: Rectangle.Empty),
            new OwnedNativeWindowCandidate(
                Handle: 0x31,
                OwnerHandle: 0x30,
                ProcessId: 41,
                IndependentWindow: false,
                Bounds: Rectangle.Empty),
            new OwnedNativeWindowCandidate(
                Handle: 0x40,
                OwnerHandle: 0x10,
                ProcessId: 41,
                IndependentWindow: true,
                Bounds: Rectangle.Empty),
            new OwnedNativeWindowCandidate(
                Handle: 0x50,
                OwnerHandle: 0x10,
                ProcessId: 42,
                IndependentWindow: false,
                Bounds: Rectangle.Empty),
        };
        if (OwnedNativeWindowProjection.ResolveLogicalHandle(
                ownerProjectionCandidates,
                0x20) != 0x10 ||
            OwnedNativeWindowProjection.ResolveLogicalHandle(
                ownerProjectionCandidates,
                0x21) != 0x10 ||
            !OwnedNativeWindowProjection.Members(
                    ownerProjectionCandidates,
                    0x10)
                .SequenceEqual(new long[] { 0x10, 0x20, 0x21 }) ||
            OwnedNativeWindowProjection.ResolveLogicalHandle(
                ownerProjectionCandidates,
                0x31) != 0x30 ||
            OwnedNativeWindowProjection.ResolveLogicalHandle(
                ownerProjectionCandidates,
                0x40) != 0x40 ||
            OwnedNativeWindowProjection.ResolveLogicalHandle(
                ownerProjectionCandidates,
                0x50) != 0x50)
        {
            throw new InvalidDataException(
                "Owned native popup projection crossed an owner, process, or independent-window boundary.");
        }
        var cyclicOwnerCandidates = new[]
        {
            new OwnedNativeWindowCandidate(
                0x60, 0x61, 51, false, Rectangle.Empty),
            new OwnedNativeWindowCandidate(
                0x61, 0x60, 51, false, Rectangle.Empty),
        };
        if (OwnedNativeWindowProjection.ResolveLogicalHandle(
                cyclicOwnerCandidates,
                0x60) != 0x60)
        {
            throw new InvalidDataException(
                "Owned native popup projection did not fail closed on an owner cycle.");
        }

        var ownerlessPopupCandidates = new[]
        {
            new OwnedNativeWindowCandidate(
                0x70, 0, 61, true, new Rectangle(100, 100, 1000, 700)),
            new OwnedNativeWindowCandidate(
                0x71, 0, 61, false, new Rectangle(200, 200, 300, 400)),
        };
        if (OwnedNativeWindowProjection.ResolveLogicalHandle(
                ownerlessPopupCandidates,
                0x71) != 0x70 ||
            !OwnedNativeWindowProjection.Members(
                    ownerlessPopupCandidates,
                    0x70)
                .SequenceEqual(new long[] { 0x70, 0x71 }))
        {
            throw new InvalidDataException(
                "A uniquely contained ownerless transient was not projected into its managed window.");
        }

        var ambiguousOwnerlessPopupCandidates = ownerlessPopupCandidates.Append(
            new OwnedNativeWindowCandidate(
                0x72, 0, 61, true, new Rectangle(150, 150, 600, 500))
        ).ToArray();
        if (OwnedNativeWindowProjection.ResolveLogicalHandle(
                ambiguousOwnerlessPopupCandidates,
                0x71) != 0x72)
        {
            throw new InvalidDataException(
                "An ownerless transient did not select its nearest containing managed window.");
        }
        if (OwnedNativeWindowProjection.ResolveLogicalHandle(
                ambiguousOwnerlessPopupCandidates,
                0x71,
                0x70) != 0x72 ||
            !OwnedNativeWindowProjection.Members(
                    ambiguousOwnerlessPopupCandidates,
                    0x70,
                    0x70)
                .SequenceEqual(new long[] { 0x70 }))
        {
            throw new InvalidDataException(
                "A requested window changed the uniquely projected transient owner.");
        }
        if (OwnedNativeWindowProjection.ResolveLogicalHandle(
                ambiguousOwnerlessPopupCandidates,
                0x72,
                0x70) != 0x72)
        {
            throw new InvalidDataException(
                "A bound logical window absorbed an independent managed window.");
        }
        if (OwnedNativeWindowProjection.ResolveLogicalHandle(
                ambiguousOwnerlessPopupCandidates,
                0x71,
                0x71) != 0x72)
        {
            throw new InvalidDataException(
                "A non-window preferred root displaced the nearest managed window.");
        }

        var tiedOwnerlessPopupCandidates = ownerlessPopupCandidates.Append(
            new OwnedNativeWindowCandidate(
                0x75, 0, 61, true, new Rectangle(100, 100, 1000, 700))
        ).ToArray();
        if (OwnedNativeWindowProjection.ResolveLogicalHandle(
                tiedOwnerlessPopupCandidates,
                0x71) != 0x71)
        {
            throw new InvalidDataException(
                "An ownerless transient crossed a tied spatial ownership boundary.");
        }
        if (OwnedNativeWindowProjection.ResolveLogicalHandle(
                tiedOwnerlessPopupCandidates,
                0x71,
                0x70) != 0x71)
        {
            throw new InvalidDataException(
                "A requested window incorrectly resolved an ambiguous spatial tie.");
        }

        var uncontainedOwnerlessPopupCandidates = new[]
        {
            ownerlessPopupCandidates[0],
            new OwnedNativeWindowCandidate(
                0x73, 0, 61, false, new Rectangle(1200, 200, 300, 400)),
        };
        if (OwnedNativeWindowProjection.ResolveLogicalHandle(
                uncontainedOwnerlessPopupCandidates,
                0x73) != 0x73)
        {
            throw new InvalidDataException(
                "An uncontained ownerless transient was projected into a managed window.");
        }

        var foreignOwnerlessPopupCandidates = new[]
        {
            ownerlessPopupCandidates[0],
            new OwnedNativeWindowCandidate(
                0x74, 0, 62, false, new Rectangle(200, 200, 300, 400)),
        };
        if (OwnedNativeWindowProjection.ResolveLogicalHandle(
                foreignOwnerlessPopupCandidates,
                0x74,
                0x70) != 0x74)
        {
            throw new InvalidDataException(
                "A bound logical window crossed a process boundary.");
        }

        var sameThreadOverflowCandidates = new[]
        {
            new OwnedNativeWindowCandidate(
                0x76,
                0,
                61,
                true,
                new Rectangle(100, 100, 1000, 700),
                ThreadId: 701),
            new OwnedNativeWindowCandidate(
                0x77,
                0,
                61,
                false,
                new Rectangle(300, 650, 320, 480),
                ThreadId: 701),
            new OwnedNativeWindowCandidate(
                0x78,
                0,
                61,
                true,
                new Rectangle(0, 0, 1920, 1080),
                ThreadId: 702),
        };
        if (OwnedNativeWindowProjection.ResolveLogicalHandle(
                sameThreadOverflowCandidates,
                0x77) != 0x76 ||
            OwnedNativeWindowProjection.ResolveLogicalHandle(
                sameThreadOverflowCandidates,
                0x77,
                0x76) != 0x76)
        {
            throw new InvalidDataException(
                "A same-process ownerless transient escaped its unique GUI-thread window when its bounds overflowed that window.");
        }

        var ambiguousSameThreadCandidates = sameThreadOverflowCandidates.Append(
            new OwnedNativeWindowCandidate(
                0x79,
                0,
                61,
                true,
                new Rectangle(150, 150, 600, 500),
                ThreadId: 701)
        ).ToArray();
        if (OwnedNativeWindowProjection.ResolveLogicalHandle(
                ambiguousSameThreadCandidates,
                0x77) != 0x77 ||
            OwnedNativeWindowProjection.ResolveLogicalHandle(
                ambiguousSameThreadCandidates,
                0x77,
                0x76) != 0x77 ||
            OwnedNativeWindowProjection.ResolveLogicalHandle(
                ambiguousSameThreadCandidates,
                0x79,
                0x76) != 0x79)
        {
            throw new InvalidDataException(
                "GUI-thread ownership crossed an ambiguous or independent-window boundary.");
        }

        var foreignSameThreadCandidates = new[]
        {
            sameThreadOverflowCandidates[0],
            new OwnedNativeWindowCandidate(
                0x7a,
                0,
                62,
                false,
                new Rectangle(300, 650, 320, 480),
                ThreadId: 701),
        };
        if (OwnedNativeWindowProjection.ResolveLogicalHandle(
                foreignSameThreadCandidates,
                0x7a,
                0x76) != 0x7a)
        {
            throw new InvalidDataException(
                "GUI-thread ownership crossed a process boundary.");
        }

        var projectedRootCandidates = new[]
        {
            new ProjectedUiaRootCandidate(71, 0x80, "runtime:owner"),
            // ControlView exposes the tooltip but can omit the interactive popup.
            new ProjectedUiaRootCandidate(71, 0x81, "runtime:tooltip"),
            // RawView repeats the tooltip and contributes the otherwise absent menu.
            new ProjectedUiaRootCandidate(71, 0x81, "runtime:tooltip"),
            new ProjectedUiaRootCandidate(71, 0x81, "runtime:menu"),
            new ProjectedUiaRootCandidate(71, 0x81, "runtime:tooltip"),
            new ProjectedUiaRootCandidate(72, 0x81, "runtime:foreign-process"),
            new ProjectedUiaRootCandidate(71, 0x82, "runtime:foreign-hwnd"),
            new ProjectedUiaRootCandidate(71, 0x81, string.Empty),
            new ProjectedUiaRootCandidate(71, 0x81, string.Empty),
        };
        var selectedProjectedRoots = ProjectedUiaRootSelection.SelectIndices(
            projectedRootCandidates,
            processId: 71,
            projectedHandles: new long[] { 0x80, 0x81 });
        if (!selectedProjectedRoots.SequenceEqual(new[] { 0, 1, 3, 7, 8 }))
        {
            throw new InvalidDataException(
                "Projected UIA root selection lost a RawView-only sibling root, retained a cross-view duplicate runtime identity, or crossed a process/HWND boundary.");
        }

        var independentDialogRoots = ProjectedUiaRootSelection.SelectIndices(
            projectedRootCandidates,
            processId: 71,
            projectedHandles: new long[] { 0x82 });
        if (!independentDialogRoots.SequenceEqual(new[] { 6 }))
        {
            throw new InvalidDataException(
                "Projected UIA root selection absorbed or lost an independent dialog HWND.");
        }

        var handlelessTransientRoots = new[]
        {
            new ProjectedUiaRootCandidate(
                71,
                0x80,
                "runtime:owner",
                new Rectangle(100, 100, 1000, 700),
                true),
            new ProjectedUiaRootCandidate(
                71,
                0,
                "runtime:contained-menu",
                new Rectangle(300, 300, 320, 480),
                SpatialOwnershipEligible: true),
            new ProjectedUiaRootCandidate(
                0,
                0,
                "runtime:process-neutral-contained-menu",
                new Rectangle(325, 325, 300, 450),
                SpatialOwnershipEligible: true),
            new ProjectedUiaRootCandidate(
                71,
                0x99,
                "runtime:unlisted-contained-menu",
                new Rectangle(350, 350, 300, 400),
                SpatialOwnershipEligible: true),
            new ProjectedUiaRootCandidate(
                71,
                0,
                "runtime:contained-pane",
                new Rectangle(250, 250, 500, 300)),
            new ProjectedUiaRootCandidate(
                0,
                0,
                "runtime:process-neutral-contained-pane",
                new Rectangle(275, 275, 450, 250)),
            new ProjectedUiaRootCandidate(
                71,
                0,
                "runtime:independent-dialog",
                new Rectangle(400, 400, 250, 200),
                true),
            new ProjectedUiaRootCandidate(
                71,
                0,
                "runtime:outside-menu",
                new Rectangle(1200, 300, 320, 480)),
            new ProjectedUiaRootCandidate(
                72,
                0,
                "runtime:foreign-menu",
                new Rectangle(300, 300, 320, 480),
                SpatialOwnershipEligible: true),
        };
        var selectedHandlelessTransientRoots =
            ProjectedUiaRootSelection.SelectIndices(
                handlelessTransientRoots,
                processId: 71,
                projectedHandles: new long[] { 0x80 },
                logicalBounds: new Rectangle(100, 100, 1000, 700));
        if (!selectedHandlelessTransientRoots.SequenceEqual(new[] { 0, 1, 2, 3 }))
        {
            throw new InvalidDataException(
                "Projected UIA root selection lost an eligible same-process or process-neutral spatial transient, or absorbed an ordinary pane across an independent-window, foreign-process, or geometry boundary.");
        }
    }
}
