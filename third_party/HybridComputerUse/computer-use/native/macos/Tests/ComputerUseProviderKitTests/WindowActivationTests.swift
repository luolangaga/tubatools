@testable import ComputerUseProviderKit
import CoreGraphics
import Darwin
import Testing

@Test
func windowActionMatchesAnAXFallbackHandle() {
    let windows = [10, 11]
    let selected = matchingWindowCandidate(
        windows,
        requestedHandle: "w2:ax-fallback"
    ) {
        $0 == 11 ? "w2:ax-fallback" : "w2:native"
    }

    #expect(selected == 11)
}

@Test
func windowActionDoesNotSubstituteAnotherWindowHandle() {
    let selected = matchingWindowCandidate(
        [10, 11],
        requestedHandle: "w2:missing"
    ) {
        "w2:\($0)"
    }

    #expect(selected == nil)
}

@Test
func activationReadbackRequiresForegroundAndExactFocusedWindow() {
    let foreground = WindowServerForegroundObservation(matchesTarget: true)

    #expect(activatedWindowMatchesRequest(
        foreground: foreground,
        focusedWindowHandle: "w2:target",
        requestedWindowHandle: "w2:target"
    ))
    #expect(!activatedWindowMatchesRequest(
        foreground: foreground,
        focusedWindowHandle: "w2:sibling",
        requestedWindowHandle: "w2:target"
    ))
    #expect(!activatedWindowMatchesRequest(
        foreground: WindowServerForegroundObservation(matchesTarget: false),
        focusedWindowHandle: "w2:target",
        requestedWindowHandle: "w2:target"
    ))
    #expect(!activatedWindowMatchesRequest(
        foreground: WindowServerForegroundObservation(matchesTarget: nil),
        focusedWindowHandle: "w2:target",
        requestedWindowHandle: "w2:target"
    ))
}

@Test
func activationVerificationWaitsUntilTargetIsFrontmost() {
    var samples: [pid_t?] = [101, 101, 202]
    var waits = 0
    let activated = waitForFrontmostApplication(
        timeout: 1,
        pollInterval: 0.01,
        foreground: {
            WindowServerForegroundObservation(
                matchesTarget: samples.removeFirst() == 202
            )
        },
        wait: { _ in
            waits += 1
        }
    )

    #expect(activated)
    #expect(waits == 2)
}

@Test
func activationVerificationFailsWhenTargetNeverBecomesFrontmost() {
    var waits = 0
    let activated = waitForFrontmostApplication(
        timeout: 0,
        pollInterval: 0.01,
        foreground: {
            WindowServerForegroundObservation(matchesTarget: false)
        },
        wait: { _ in
            waits += 1
        }
    )

    #expect(!activated)
    #expect(waits == 0)
}

@Test
func globalPointerPreparationSkipsActivationWhenAlreadyFrontmost() {
    var raised = 0
    var activated = 0
    var waits = 0

    let prepared = prepareApplicationForGlobalPointerInput(
        timeout: 1,
        pollInterval: 0.01,
        raiseWindow: { raised += 1 },
        activateBoundWindow: {
            activated += 1
            return true
        },
        activateApplication: {
            activated += 1
            return true
        },
        windowServerForeground: {
            WindowServerForegroundObservation(matchesTarget: true)
        },
        wait: { _ in waits += 1 }
    )

    #expect(prepared)
    #expect(raised == 0)
    #expect(activated == 0)
    #expect(waits == 0)
}

@Test
func boundWindowReadinessRejectsFrontmostProcessWithTransformedWindow() {
    let ready = boundWindowMatchesSnapshot(
        foreground: WindowServerForegroundObservation(matchesTarget: true),
        window: WindowServerBoundWindowObservation(
            matchesOwner: true,
            isOnScreen: true,
            bounds: CGRect(x: -1012, y: -396, width: 1024, height: 800)
        ),
        snapshotBounds: CGRect(x: 239, y: 76, width: 1024, height: 800)
    )

    #expect(!ready)
}

@Test
func boundWindowReadinessRequiresOwnerOnScreenGeometryAndForeground() {
    let snapshot = CGRect(x: 239, y: 76, width: 1024, height: 800)
    let matchingWindow = WindowServerBoundWindowObservation(
        matchesOwner: true,
        isOnScreen: true,
        bounds: CGRect(x: 240, y: 75, width: 1024, height: 800)
    )

    #expect(
        boundWindowMatchesSnapshot(
            foreground: WindowServerForegroundObservation(matchesTarget: true),
            window: matchingWindow,
            snapshotBounds: snapshot
        )
    )
    #expect(
        !boundWindowMatchesSnapshot(
            foreground: WindowServerForegroundObservation(matchesTarget: false),
            window: matchingWindow,
            snapshotBounds: snapshot
        )
    )
    #expect(
        !boundWindowMatchesSnapshot(
            foreground: WindowServerForegroundObservation(matchesTarget: true),
            window: WindowServerBoundWindowObservation(
                matchesOwner: false,
                isOnScreen: true,
                bounds: snapshot
            ),
            snapshotBounds: snapshot
        )
    )
}

@Test
func boundWindowPreparationWaitsForPublicActivationBeforePrivateFallback() {
    var activatedAndRaised = 0
    var fallbackActivations = 0
    var samples = [false, false, true]
    var waits = 0

    let prepared = prepareBoundWindowForPointerInput(
        activationTimeout: 1,
        fallbackTimeout: 1,
        pollInterval: 0.01,
        activateAndRaise: { activatedAndRaised += 1 },
        activateBoundWindow: {
            fallbackActivations += 1
            return true
        },
        ready: { samples.removeFirst() },
        wait: { _ in waits += 1 }
    )

    #expect(prepared)
    #expect(activatedAndRaised == 1)
    #expect(fallbackActivations == 0)
    #expect(waits == 1)
}

@Test
func boundWindowPreparationUsesFallbackOnlyAfterPublicActivationTimeout() {
    var activatedAndRaised = 0
    var fallbackActivations = 0
    var samples = [false, false, true]

    let prepared = prepareBoundWindowForPointerInput(
        activationTimeout: 0,
        fallbackTimeout: 1,
        pollInterval: 0.01,
        activateAndRaise: { activatedAndRaised += 1 },
        activateBoundWindow: {
            fallbackActivations += 1
            return true
        },
        ready: { samples.removeFirst() },
        wait: { _ in }
    )

    #expect(prepared)
    #expect(activatedAndRaised == 1)
    #expect(fallbackActivations == 1)
}

@Test
func boundWindowPreparationKeepsPollingAfterFallback() {
    var activatedAndRaised = 0
    var fallbackActivations = 0
    var samples = [false, false, false, false, true]
    var waits: [Double] = []

    let prepared = prepareBoundWindowForPointerInput(
        activationTimeout: 0,
        fallbackTimeout: 3,
        pollInterval: 0.05,
        activateAndRaise: { activatedAndRaised += 1 },
        activateBoundWindow: {
            fallbackActivations += 1
            return true
        },
        ready: { samples.removeFirst() },
        wait: { waits.append($0) }
    )

    #expect(prepared)
    #expect(activatedAndRaised == 1)
    #expect(fallbackActivations == 1)
    #expect(waits.count == 2)
    #expect(waits.allSatisfy { $0 > 0 && $0 <= 0.05 })
}

@Test
func boundWindowPreparationDoesNotActivateAnAlreadyReadyWindow() {
    var activatedAndRaised = 0
    var fallbackActivations = 0
    var waits = 0

    let prepared = prepareBoundWindowForPointerInput(
        activationTimeout: 1,
        fallbackTimeout: 1,
        pollInterval: 0.01,
        activateAndRaise: { activatedAndRaised += 1 },
        activateBoundWindow: {
            fallbackActivations += 1
            return true
        },
        ready: { true },
        wait: { _ in waits += 1 }
    )

    #expect(prepared)
    #expect(activatedAndRaised == 0)
    #expect(fallbackActivations == 0)
    #expect(waits == 0)
}

@Test
func globalPointerPreparationActivatesWhenRaiseDoesNotChangeForeground() {
    var samples: [pid_t?] = [101, 101, 101, 202]
    var raised = 0
    var activated = 0
    var waits = 0

    let prepared = prepareApplicationForGlobalPointerInput(
        timeout: 1,
        pollInterval: 0.01,
        raiseWindow: { raised += 1 },
        activateBoundWindow: {
            activated += 1
            return true
        },
        activateApplication: {
            activated += 1
            return true
        },
        windowServerForeground: {
            WindowServerForegroundObservation(
                matchesTarget: samples.removeFirst() == 202
            )
        },
        wait: { _ in waits += 1 }
    )

    #expect(prepared)
    #expect(raised == 1)
    #expect(activated == 1)
    #expect(waits == 1)
}

@Test
func globalPointerPreparationAcceptsForegroundEstablishedByRaise() {
    var samples: [pid_t?] = [101, 202]
    var raised = 0
    var activated = 0

    let prepared = prepareApplicationForGlobalPointerInput(
        timeout: 1,
        pollInterval: 0.01,
        raiseWindow: { raised += 1 },
        activateBoundWindow: {
            activated += 1
            return true
        },
        activateApplication: {
            activated += 1
            return true
        },
        windowServerForeground: {
            WindowServerForegroundObservation(
                matchesTarget: samples.removeFirst() == 202
            )
        },
        wait: { _ in }
    )

    #expect(prepared)
    #expect(raised == 1)
    #expect(activated == 0)
}

@Test
func globalPointerPreparationFailsWhenActivationNeverTakesForeground() {
    var raised = 0
    var activated = 0
    var waits = 0

    let prepared = prepareApplicationForGlobalPointerInput(
        timeout: 0,
        pollInterval: 0.01,
        raiseWindow: { raised += 1 },
        activateBoundWindow: {
            activated += 1
            return false
        },
        activateApplication: {
            activated += 1
            return true
        },
        windowServerForeground: {
            WindowServerForegroundObservation(matchesTarget: false)
        },
        wait: { _ in waits += 1 }
    )

    #expect(!prepared)
    #expect(raised == 1)
    #expect(activated == 2)
    #expect(waits == 0)
}

@Test
func globalPointerPreparationFallsBackWhenBoundWindowActivationIsUnavailable() {
    var boundWindowActivations = 0
    var applicationActivations = 0
    var samples = [false, false, true]

    let prepared = prepareApplicationForGlobalPointerInput(
        timeout: 1,
        pollInterval: 0.01,
        raiseWindow: {},
        activateBoundWindow: {
            boundWindowActivations += 1
            return false
        },
        activateApplication: {
            applicationActivations += 1
            return true
        },
        windowServerForeground: {
            WindowServerForegroundObservation(
                matchesTarget: samples.removeFirst()
            )
        },
        wait: { _ in }
    )

    #expect(prepared)
    #expect(boundWindowActivations == 1)
    #expect(applicationActivations == 1)
}
