import CoreGraphics
import Testing
@testable import ComputerUseProviderKit

@Test
func explicitSnapshotWindowDoesNotConsultTheFocusedSibling() throws {
    var focusedReads = 0
    let selected = try selectSnapshotWindow(
        requestedHandle: "window-a",
        inventory: { ["window-b", "window-a"] },
        handleOf: { $0 },
        unboundWindow: { focusedReads += 1; return "window-b" }
    )
    #expect(selected == "window-a")
    #expect(focusedReads == 0)
}

@Test
func missingAmbiguousAndIncompleteWindowInventoriesDoNotRebind() {
    for inventory in [nil, [], ["window-b"], ["window-a", "window-a"]] as [[String]?] {
        var focusedReads = 0
        do {
            _ = try selectSnapshotWindow(
                requestedHandle: "window-a", inventory: { inventory },
                handleOf: { $0 },
                unboundWindow: { focusedReads += 1; return "window-b" }
            )
            Issue.record("An unresolved explicit window must fail")
        } catch {
            let result = runtimeFailureResult(error)
            #expect(result["failureCode"] as? String == "state_unavailable")
            #expect(result["input_events_emitted"] as? Bool == false)
        }
        #expect(focusedReads == 0)
    }
}

@Test
func unboundSnapshotStillUsesNormalWindowDiscovery() throws {
    for handle in [nil, ""] as [String?] {
        var inventoryReads = 0
        let selected = try selectSnapshotWindow(
            requestedHandle: handle,
            inventory: { inventoryReads += 1; return [] as [String] },
            handleOf: { $0 }, unboundWindow: { "focused-window" }
        )
        #expect(selected == "focused-window")
        #expect(inventoryReads == 0)
    }
}

@Test
func snapshotFocusCannotExposeAnotherWindowsSelection() {
    #expect(focusedElementInSnapshotWindow(
        "editor-b", selectedHandle: "window-a",
        windowHandleOf: { _ in "window-b" }
    ) == nil)
    #expect(focusedElementInSnapshotWindow(
        "editor-a", selectedHandle: "window-a",
        windowHandleOf: { _ in "window-a" }
    ) == "editor-a")
    #expect(focusedElementInSnapshotWindow(
        "unowned", selectedHandle: "window-a",
        windowHandleOf: { _ in nil }
    ) == nil)
}

@Test
func exactWindowCaptureNeverBorrowsSiblingGeometry() {
    let sibling = WindowCaptureCandidate(
        windowID: 20, layer: 0, bounds: CGRect(x: 0, y: 0, width: 900, height: 700),
        title: "Same title", area: 630_000, frontToBackIndex: 0
    )
    let smallTarget = WindowCaptureCandidate(
        windowID: 21, layer: 1, bounds: CGRect(x: 0, y: 0, width: 50, height: 50),
        title: "Same title", area: 2_500, frontToBackIndex: 1
    )
    #expect(preferredWindowCaptureCandidate(
        [sibling], windowIDHint: 21, titleHint: "Same title"
    ) == nil)
    #expect(preferredWindowCaptureCandidate(
        [sibling, smallTarget], windowIDHint: 21, titleHint: "Same title"
    )?.windowID == 21)
    #expect(preferredWindowCaptureCandidate(
        [smallTarget, smallTarget], windowIDHint: 21, titleHint: "Same title"
    ) == nil)
}
