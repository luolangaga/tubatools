import AppKit
import ApplicationServices
@testable import ComputerUseProviderKit
import Testing

private func orderedIdentityApp() -> RunningAppDescriptor {
    RunningAppDescriptor(
        name: "Identity Test",
        bundleIdentifier: nil,
        pid: getpid(),
        runningApplication: NSRunningApplication.current
    )
}

private func orderedAppIdentity() -> [String: Any] {
    [
        "providerBusName": "mac:\(getpid())",
        "processGeneration": macProcessGeneration(pid: getpid()),
    ]
}

private func orderedIdentitySnapshot(
    app: RunningAppDescriptor = orderedIdentityApp(),
    windowHandle: String = "bound-window"
) -> AppSnapshot {
    AppSnapshot(
        app: app,
        windowTitle: "Identity Test",
        windowBounds: nil,
        rootWindowElement: AXUIElementCreateSystemWide(),
        targetWindowID: nil,
        targetWindowHandle: windowHandle,
        targetWindowLayer: nil,
        treeLines: [],
        focusedSummary: nil,
        focusedElement: nil,
        selectedText: nil,
        elements: [:]
    )
}

private func orderedIdentityFailure(
    _ operation: () throws -> Void
) -> [String: Any] {
    do {
        try operation()
        Issue.record("Expected an identity or observation failure")
        return [:]
    } catch {
        return runtimeFailureResult(error)
    }
}

@Test
func requestedAppIdentityMismatchIsTypedForPIDAndGeneration() {
    let app = orderedIdentityApp()
    for field in ["providerBusName", "processGeneration"] {
        var identity = orderedAppIdentity()
        identity[field] = "replaced"
        let failure = orderedIdentityFailure {
            try validateRequestedIdentity(["appIdentity": identity], app: app)
        }

        #expect(failure["failureCode"] as? String == "stale_app_ref")
        #expect(failure["failureSubject"] as? String == "app")
        #expect(failure["input_events_emitted"] as? Bool == false)
    }
}

@Test
func requestedWindowMismatchIsTyped() {
    let failure = orderedIdentityFailure {
        try validateRequestedWindow(
            ["targetWindowHandle": "bound-window"],
            snapshot: orderedIdentitySnapshot(windowHandle: "replacement")
        )
    }

    #expect(failure["failureCode"] as? String == "stale_window_ref")
    #expect(failure["failureSubject"] as? String == "window")
}

@Test
func orderedIdentityResolutionRejectsMissingBoundPIDWithoutDiscovery() {
    var lookedUp: [pid_t] = []
    let failure = orderedIdentityFailure {
        _ = try resolveOrderedInputApp(orderedAppIdentity()) { pid in
            lookedUp.append(pid)
            return nil
        }
    }

    #expect(lookedUp == [getpid()])
    #expect(failure["failureCode"] as? String == "stale_app_ref")
    #expect(failure["input_events_emitted"] as? Bool == false)
}

@Test
func orderedIdentityResolutionRejectsReusedPIDGeneration() {
    var identity = orderedAppIdentity()
    identity["processGeneration"] = "previous-generation"
    let failure = orderedIdentityFailure {
        _ = try resolveOrderedInputApp(identity) { _ in
            NSRunningApplication.current
        }
    }

    #expect(failure["failureCode"] as? String == "stale_app_ref")
}

@Test
func orderedFinalSnapshotRejectsMissingProcessBeforeCapture() {
    var captures = 0
    let failure = orderedIdentityFailure {
        _ = try orderedInputFinalSnapshot(
            appIdentity: orderedAppIdentity(),
            windowHandle: "bound-window",
            resolveApp: {
                try resolveOrderedInputApp(orderedAppIdentity()) { _ in nil }
            },
            capture: { _ in
                captures += 1
                return orderedIdentitySnapshot()
            }
        )
    }

    #expect(captures == 0)
    #expect(failure["failureCode"] as? String == "stale_app_ref")
}

@Test
func orderedFinalSnapshotRechecksIdentityAfterFailedRead() {
    for processExited in [false, true] {
        var resolutions = 0
        var captures = 0
        let failure = orderedIdentityFailure {
            _ = try orderedInputFinalSnapshot(
                appIdentity: orderedAppIdentity(),
                windowHandle: "bound-window",
                resolveApp: {
                    resolutions += 1
                    if processExited && resolutions == 2 {
                        return try resolveOrderedInputApp(orderedAppIdentity()) { _ in nil }
                    }
                    return orderedIdentityApp()
                },
                capture: { _ in
                    captures += 1
                    throw ComputerUseError.stateUnavailable("AX temporarily unreadable")
                },
                inspectWindow: { _, _ in .unknown }
            )
        }

        #expect(resolutions == (processExited ? 2 : 3))
        #expect(captures == 1)
        #expect(failure["failureCode"] as? String ==
            (processExited ? "stale_app_ref" : "state_unavailable"))
    }
}

@Test
func orderedFinalSnapshotPreservesTypedCaptureFailureAndProviderReset() {
    for (error, code) in [
        (ComputerUseError.staleWindowRef("window replaced"), "stale_window_ref"),
        (ComputerUseError.staleAppRef("app replaced"), "stale_app_ref"),
        (ComputerUseError.providerResetRequired("read timed out"), "state_unavailable"),
    ] {
        var resolutions = 0
        let failure = orderedIdentityFailure {
            _ = try orderedInputFinalSnapshot(
                appIdentity: orderedAppIdentity(),
                windowHandle: "bound-window",
                resolveApp: {
                    resolutions += 1
                    return orderedIdentityApp()
                },
                capture: { _ in throw error }
            )
        }

        #expect(resolutions == 1)
        #expect(failure["failureCode"] as? String == code)
        #expect((failure["providerResetRequired"] as? Bool ?? false)
            == requiresProviderReset(error))
    }
}

@Test
func orderedFinalSnapshotRejectsExitAfterSuccessfulCapture() {
    var resolutions = 0
    var captures = 0
    let failure = orderedIdentityFailure {
        _ = try orderedInputFinalSnapshot(
            appIdentity: orderedAppIdentity(),
            windowHandle: "bound-window",
            resolveApp: {
                resolutions += 1
                if resolutions == 2 {
                    return try resolveOrderedInputApp(orderedAppIdentity()) { _ in nil }
                }
                return orderedIdentityApp()
            },
            capture: { _ in
                captures += 1
                return orderedIdentitySnapshot()
            }
        )
    }

    #expect(resolutions == 2)
    #expect(captures == 1)
    #expect(failure["failureCode"] as? String == "stale_app_ref")
}

@Test
func orderedFinalSnapshotRejectsReplacementSnapshotIdentity() {
    for replacedApp in [false, true] {
        let otherApp = RunningAppDescriptor(
            name: orderedIdentityApp().name,
            bundleIdentifier: nil,
            pid: getpid() + 1,
            runningApplication: NSRunningApplication.current
        )
        let failure = orderedIdentityFailure {
            _ = try orderedInputFinalSnapshot(
                appIdentity: orderedAppIdentity(),
                windowHandle: "bound-window",
                resolveApp: { orderedIdentityApp() },
                capture: { _ in
                    orderedIdentitySnapshot(
                        app: replacedApp ? otherApp : orderedIdentityApp(),
                        windowHandle: replacedApp ? "bound-window" : "replacement"
                    )
                },
                inspectWindow: { _, _ in .absent }
            )
        }

        #expect(failure["failureCode"] as? String ==
            (replacedApp ? "stale_app_ref" : "stale_window_ref"))
    }
}

@Test
func orderedFinalSnapshotReturnsOnlyMatchingIdentity() throws {
    let snapshot = try orderedInputFinalSnapshot(
        appIdentity: orderedAppIdentity(),
        windowHandle: "bound-window",
        resolveApp: { orderedIdentityApp() },
        capture: { _ in orderedIdentitySnapshot() }
    )

    #expect(snapshot.app.pid == getpid())
    #expect(snapshot.targetWindowHandle == "bound-window")
}
