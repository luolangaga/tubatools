import ApplicationServices
import CoreGraphics
@testable import ComputerUseProviderKit
import Testing

@Test
func exactAXWindowIDWinsOverFrontmostDuplicateTitle() {
    let parent = WindowCaptureCandidate(
        windowID: 41,
        layer: 0,
        bounds: CGRect(x: 100, y: 100, width: 800, height: 600),
        title: "Untitled",
        area: 480_000,
        frontToBackIndex: 1
    )
    let sheet = WindowCaptureCandidate(
        windowID: 42,
        layer: 0,
        bounds: CGRect(x: 250, y: 180, width: 458, height: 267),
        title: "Untitled",
        area: 122_286,
        frontToBackIndex: 0
    )

    let selected = preferredWindowCaptureCandidate(
        [sheet, parent],
        windowIDHint: parent.windowID,
        titleHint: "Untitled"
    )

    #expect(selected?.windowID == parent.windowID)
}

@Test
func titleHeuristicRemainsWhenAXWindowIDIsUnavailable() {
    let parent = WindowCaptureCandidate(
        windowID: 41,
        layer: 0,
        bounds: CGRect(x: 100, y: 100, width: 800, height: 600),
        title: "Untitled",
        area: 480_000,
        frontToBackIndex: 1
    )
    let sheet = WindowCaptureCandidate(
        windowID: 42,
        layer: 0,
        bounds: CGRect(x: 250, y: 180, width: 458, height: 267),
        title: "Untitled",
        area: 122_286,
        frontToBackIndex: 0
    )

    let selected = preferredWindowCaptureCandidate(
        [sheet, parent],
        windowIDHint: nil,
        titleHint: "Untitled"
    )

    #expect(selected?.windowID == sheet.windowID)
}

@Test
func zeroSizedFramesDoNotProduceNearbyClickPoints() {
    let frame = CGRect(x: -627, y: 768, width: 0, height: 0)

    #expect(!hasUsableClickGeometry(frame))
    #expect(localClickActionPoints(frame: frame, isSyntheticText: false).isEmpty)
}

@Test
func finitePositiveFramesRemainClickable() {
    let frame = CGRect(x: 193, y: 234, width: 222, height: 24)

    #expect(hasUsableClickGeometry(frame))
    #expect(localClickActionPoints(frame: frame, isSyntheticText: false).count == 2)
}

@Test
func leafMenuItemSelectionIsNotReportedAsCompletedClick() {
    #expect(
        !canUseContainingListSelectionAsClick(
            requestedTargetRole: "AXMenuItem",
            candidateRole: "AXMenuItem",
            hasMenuItemAncestor: false,
            hasChildMenu: false,
            hasPopoverAncestor: false
        )
    )
}

@Test
func submenuOwnerSelectionCanOpenItsChildMenu() {
    #expect(
        canUseContainingListSelectionAsClick(
            requestedTargetRole: "AXMenuItem",
            candidateRole: "AXMenuItem",
            hasMenuItemAncestor: false,
            hasChildMenu: true,
            hasPopoverAncestor: false
        )
    )
}

@Test
func ordinaryListSelectionRemainsAvailable() {
    #expect(
        canUseContainingListSelectionAsClick(
            requestedTargetRole: "AXRow",
            candidateRole: "AXRow",
            hasMenuItemAncestor: false,
            hasChildMenu: false,
            hasPopoverAncestor: false
        )
    )
}

@Test
func checkboxListSelectionIsNotReportedAsActivation() {
    #expect(
        !canUseContainingListSelectionAsClick(
            requestedTargetRole: "AXCheckBox",
            candidateRole: "AXCheckBox",
            hasMenuItemAncestor: false,
            hasChildMenu: false,
            hasPopoverAncestor: false
        )
    )
}

@Test
func checkboxRequestCannotBecomeContainingRowSelectionAfterHitTesting() {
    #expect(
        !canUseContainingListSelectionAsClick(
            requestedTargetRole: "AXCheckBox",
            candidateRole: "AXRow",
            hasMenuItemAncestor: false,
            hasChildMenu: false,
            hasPopoverAncestor: false
        )
    )
}

@Test
func checkboxBooleanValuesToggleWithoutTreatingMixedAsBinary() {
    #expect(toggledCheckboxValue(NSNumber(value: 0)) == NSNumber(value: 1))
    #expect(toggledCheckboxValue(NSNumber(value: 1)) == NSNumber(value: 0))
    #expect(toggledCheckboxValue(NSNumber(value: 2)) == nil)
    #expect(toggledCheckboxValue("1" as CFString) == nil)
}

@Test
func checkboxValueWriteRequiresExactNumericReadback() {
    #expect(
        checkboxValueReadbackMatches(
            expected: NSNumber(value: 1),
            observed: NSNumber(value: 1)
        )
    )
    #expect(
        !checkboxValueReadbackMatches(
            expected: NSNumber(value: 1),
            observed: NSNumber(value: 0)
        )
    )
    #expect(
        !checkboxValueReadbackMatches(
            expected: NSNumber(value: 1),
            observed: "1" as CFString
        )
    )
}

@Test
func failedAdvertisedNativeActionStopsWithPostEmissionEvidence() {
    let error = nativeAXActionAttemptError(
        action: kAXIncrementAction as String,
        result: .cannotComplete
    )

    #expect(error.inputEventsEmitted)
    #expect(error.partialInputPossible == true)
    #expect(error.readbackSampled == false)
    #expect(error.readbackVerified == nil)
    #expect(error.actionRoute == "ax.action.increment")
    #expect(
        error.message.contains(
            "Observe the current state before retrying."
        )
    )

    let failure = runtimeFailureResult(error)
    #expect(failure["failurePhase"] as? String == "emission")
    #expect(failure["mutation_unknown"] as? Bool == true)
    #expect(failure["input_events_emitted"] as? Bool == true)
}

@Test
func listContainedButtonSelectionIsNotReportedAsActivation() {
    #expect(
        !canUseContainingListSelectionAsClick(
            requestedTargetRole: "AXButton",
            candidateRole: "AXButton",
            hasMenuItemAncestor: false,
            hasChildMenu: false,
            hasPopoverAncestor: false
        )
    )
}

@Test
func popoverRowsRequireRealActivationInsteadOfSelectionOnly() {
    #expect(
        !canUseContainingListSelectionAsClick(
            requestedTargetRole: "AXRow",
            candidateRole: "AXRow",
            hasMenuItemAncestor: false,
            hasChildMenu: false,
            hasPopoverAncestor: true
        )
    )
}

@Test
func snapshotRuntimeIDsIdentifyTransientPopoverRows() {
    let popover = ElementRecord(
        index: 2,
        runtimeID: [0, 1, 2],
        identifier: nil,
        element: nil,
        localFrame: nil,
        rawActions: [],
        prettyActions: [],
        providerRole: "popover"
    )
    let table = ElementRecord(
        index: 3,
        runtimeID: [0, 1, 2, 3],
        identifier: nil,
        element: nil,
        localFrame: nil,
        rawActions: [],
        prettyActions: [],
        providerRole: "table"
    )
    let row = ElementRecord(
        index: 4,
        runtimeID: [0, 1, 2, 3, 4],
        identifier: nil,
        element: nil,
        localFrame: nil,
        rawActions: [],
        prettyActions: [],
        providerRole: "listitem"
    )

    #expect(
        transientPopoverRowRecord(
            for: row,
            records: [popover, table, row]
        ) === row
    )
}

@Test
func siblingPopoversDoNotCaptureOrdinaryRows() {
    let popover = ElementRecord(
        index: 2,
        runtimeID: [0, 1, 2],
        identifier: nil,
        element: nil,
        localFrame: nil,
        rawActions: [],
        prettyActions: [],
        providerRole: "popover"
    )
    let ordinaryRow = ElementRecord(
        index: 4,
        runtimeID: [0, 4],
        identifier: nil,
        element: nil,
        localFrame: nil,
        rawActions: [],
        prettyActions: [],
        providerRole: "listitem"
    )

    #expect(
        transientPopoverRowRecord(
            for: ordinaryRow,
            records: [popover, ordinaryRow]
        ) == nil
    )
}

@Test
func onlySingleLeftActionlessTransientRowsUseGlobalPointer() {
    let actionlessRow = ElementRecord(
        index: 4,
        identifier: nil,
        element: nil,
        localFrame: nil,
        rawActions: [],
        prettyActions: [],
        providerRole: "listitem"
    )
    let pressableRow = ElementRecord(
        index: 5,
        identifier: nil,
        element: nil,
        localFrame: nil,
        rawActions: [kAXPressAction as String],
        prettyActions: [],
        providerRole: "listitem"
    )
    let confirmOnlyRow = ElementRecord(
        index: 6,
        identifier: nil,
        element: nil,
        localFrame: nil,
        rawActions: [kAXConfirmAction as String],
        prettyActions: [],
        providerRole: "listitem"
    )

    #expect(
        canUseGlobalPointerForTransientRow(
            row: actionlessRow,
            button: .left,
            clickCount: 1
        )
    )
    #expect(
        !canUseGlobalPointerForTransientRow(
            row: actionlessRow,
            button: .right,
            clickCount: 1
        )
    )
    #expect(
        !canUseGlobalPointerForTransientRow(
            row: actionlessRow,
            button: .left,
            clickCount: 2
        )
    )
    #expect(
        !canUseGlobalPointerForTransientRow(
            row: pressableRow,
            button: .left,
            clickCount: 1
        )
    )
    #expect(
        canUseGlobalPointerForTransientRow(
            row: confirmOnlyRow,
            button: .left,
            clickCount: 1
        )
    )
    #expect(hasPrimaryClickAction([kAXPressAction as String]))
    #expect(!hasPrimaryClickAction([kAXConfirmAction as String]))
}

@Test
func listContainersUseSelectedChildren() {
    #expect(
        selectionAttributeForContainerRole("AXList")
            == kAXSelectedChildrenAttribute as String
    )
}

@Test
func outlineAndTableContainersUseSelectedRows() {
    #expect(
        selectionAttributeForContainerRole("AXOutline")
            == kAXSelectedRowsAttribute as String
    )
    #expect(
        selectionAttributeForContainerRole("AXTable")
            == kAXSelectedRowsAttribute as String
    )
}

@Test
func unrelatedContainersDoNotProvideSelectionFallback() {
    #expect(selectionAttributeForContainerRole("AXGroup") == nil)
    #expect(selectionAttributeForContainerRole("AXScrollArea") == nil)
    #expect(selectionAttributeForContainerRole(nil) == nil)
}

@Test
func cachedWindowFallbackRequiresExactPIDAndWindowHandle() {
    #expect(
        cachedWindowFallbackMatches(
            appPID: 740,
            candidatePID: 740,
            candidateWindowHandle: "w2:current",
            expectedWindowHandle: "w2:current"
        )
    )
    #expect(
        !cachedWindowFallbackMatches(
            appPID: 740,
            candidatePID: 741,
            candidateWindowHandle: "w2:current",
            expectedWindowHandle: "w2:current"
        )
    )
    #expect(
        !cachedWindowFallbackMatches(
            appPID: 740,
            candidatePID: 740,
            candidateWindowHandle: "w2:replacement",
            expectedWindowHandle: "w2:current"
        )
    )
}

@Test
func completeDiscoveryWithoutTargetProvesWindowAbsent() {
    #expect(
        classifyTargetWindowDiscovery(
            hasExpectedWindowHandle: true,
            exactWindowFound: false,
            appWindowInventoryComplete: true,
            systemFocusInventoryComplete: true
        ) == .absent
    )
}

@Test
func incompletePostActionDiscoveryRemainsUnknown() {
    #expect(
        classifyTargetWindowDiscovery(
            hasExpectedWindowHandle: true,
            exactWindowFound: false,
            appWindowInventoryComplete: true,
            systemFocusInventoryComplete: false
        ) == .unknown
    )
}

@Test
func exactWindowInAnyLiveDiscoverySourceRemainsPresent() {
    #expect(
        classifyTargetWindowDiscovery(
            hasExpectedWindowHandle: true,
            exactWindowFound: true,
            appWindowInventoryComplete: true,
            systemFocusInventoryComplete: true
        ) == .present
    )
}

@Test
func missingExpectedWindowHandleCannotProveAbsence() {
    #expect(
        classifyTargetWindowDiscovery(
            hasExpectedWindowHandle: false,
            exactWindowFound: false,
            appWindowInventoryComplete: true,
            systemFocusInventoryComplete: true
        ) == .unknown
    )
}

@Test
func exactDestroyedNotificationProvesWindowTransition() {
    #expect(
        postActionTargetWindowTransitionProven(
            targetDestroyed: true,
            currentLiveness: .unknown
        )
    )
}

@Test
func incompleteDiscoveryWithoutDestructionDoesNotProveTransition() {
    #expect(
        !postActionTargetWindowTransitionProven(
            targetDestroyed: false,
            currentLiveness: .unknown
        )
    )
}

@Test
func guiClickMovesPointerBeforePressAndRelease() {
    let button = mouseButton("left")
    let events = guiClickEventTypes(button: button, clickCount: 1)

    #expect(events.map(\.rawValue) == [
        CGEventType.mouseMoved.rawValue,
        CGEventType.leftMouseDown.rawValue,
        CGEventType.leftMouseUp.rawValue,
    ])
}

@Test
func semanticGeometryClickUsesVerifiedBoundGlobalRoute() {
    let service = ComputerUseService()

    #expect(
        service.semanticGeometryClickRoute(
            hasWindowBounds: true,
            hasWindowID: true
        ) == .boundGlobalHID
    )
}

@Test
func semanticGeometryClickFallsBackBeforeEmissionWithoutWindowBinding() {
    let service = ComputerUseService()

    #expect(
        service.semanticGeometryClickRoute(
            hasWindowBounds: false,
            hasWindowID: true
        ) == .globalHID
    )
    #expect(
        service.semanticGeometryClickRoute(
            hasWindowBounds: true,
            hasWindowID: false
        ) == .globalHID
    )
}

@Test
func semanticAXClickPreparesExactBoundWindowBeforeNativeDispatch() {
    let service = ComputerUseService()

    #expect(
        service.semanticAXClickRequiresBoundWindowPreparation(
            method: .auto,
            hasWindowBounds: true,
            hasWindowID: true
        )
    )
    #expect(
        service.semanticAXClickRequiresBoundWindowPreparation(
            method: .accessibility,
            hasWindowBounds: true,
            hasWindowID: true
        )
    )
}

@Test
func explicitClickRoutesDoNotReceiveAXWindowPreparation() {
    let service = ComputerUseService()

    for method in [ClickMethod.appPost, .skyClick, .global] {
        #expect(
            !service.semanticAXClickRequiresBoundWindowPreparation(
                method: method,
                hasWindowBounds: true,
                hasWindowID: true
            )
        )
    }
    #expect(
        !service.semanticAXClickRequiresBoundWindowPreparation(
            method: .auto,
            hasWindowBounds: false,
            hasWindowID: true
        )
    )
}
