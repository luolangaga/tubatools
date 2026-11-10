import AppKit
import ApplicationServices
@testable import ComputerUseProviderKit
import Testing

@Test
func boundWindowInventoryRequiresCompleteComparableAbsence() {
    #expect(windowHandleInventoryLiveness(nil, requestedHandle: "target") == .unknown)
    #expect(windowHandleInventoryLiveness([], requestedHandle: "target") == .absent)
    #expect(windowHandleInventoryLiveness(["other"], requestedHandle: "target") == .absent)
    #expect(windowHandleInventoryLiveness(["other", nil], requestedHandle: "target") == .unknown)
    #expect(windowHandleInventoryLiveness([""], requestedHandle: "target") == .unknown)
    #expect(windowHandleInventoryLiveness([nil, "target"], requestedHandle: "target") == .present)
    #expect(windowHandleInventoryLiveness([], requestedHandle: "") == .unknown)
}

@Test
func boundWindowAbsenceRequiresBothSourcesAndComparableIdentity() {
    let states: [BoundWindowLiveness] = [.present, .absent, .unknown]
    for ax in states {
        for cg in states {
            let result = corroboratedWindowLiveness(accessibility: ax, windowServer: cg)
            #expect((result == .absent) == (ax == .absent && cg == .absent))
        }
    }
    #expect(corroboratedWindowLiveness(accessibility: .absent, windowServer: .present) == .unknown)
    #expect(corroboratedWindowLiveness(accessibility: .present, windowServer: .absent) == .unknown)
    #expect(corroboratedWindowLiveness(
        accessibility: .absent, windowServer: .absent,
        nativeIdentityComparable: false, nativeInventoryEmpty: false
    ) == .unknown)
    #expect(corroboratedWindowLiveness(
        accessibility: .absent, windowServer: .absent,
        nativeIdentityComparable: false, nativeInventoryEmpty: true
    ) == .absent)
    #expect(corroboratedWindowLiveness(
        accessibility: .unknown, windowServer: .present,
        nativeIdentityComparable: false
    ) == .present)
}

@Test
func boundWindowServerInventoryPreservesHiddenAndMalformedRecords() {
    let owner = kCGWindowOwnerPID as String
    let number = kCGWindowNumber as String
    let onscreen = kCGWindowIsOnscreen as String
    let handle: (CGWindowID) -> String = { "window-\($0)" }
    #expect(windowServerHandleInventory(nil, targetPID: 10, handle: handle) == nil)
    #expect(windowServerHandleInventory([], targetPID: 10, handle: handle) == [])
    let records: [[String: Any]] = [
        [owner: NSNumber(value: 10), number: NSNumber(value: 7), onscreen: false],
        [owner: NSNumber(value: 11), number: NSNumber(value: 8)],
        [owner: NSNumber(value: 10)],
        [number: NSNumber(value: 9)],
        [owner: NSNumber(value: 10), number: NSNumber(value: 0)],
    ]
    let result = windowServerHandleInventory(records, targetPID: 10, handle: handle)
    #expect(result == ["window-7", nil, nil, nil])
    #expect(windowHandleInventoryLiveness(result, requestedHandle: "window-7") == .present)
    #expect(windowHandleInventoryLiveness(result, requestedHandle: "missing") == .unknown)
}

@Test
func boundWindowAXInventoryPreservesUnreadableIdentityAndRootReplacement() {
    let collect: ([Int]?, String) -> [String?]? = { nodes, target in
        accessibilityWindowHandleInventory(
            nodes, requestedHandle: target,
            ownedReadableWindow: { $0 != 0 },
            axHandle: { "ax-\($0)" },
            nativeHandle: { $0 == 1 ? nil : "native-7" }
        )
    }
    #expect(collect(nil, "native-7") == nil)
    #expect(collect([], "native-7") == [])
    #expect(collect([0, 1], "native-7") == [nil, nil])
    #expect(collect([1], "ax-1") == ["ax-1"])
    #expect(collect([2, 3], "native-7") == ["native-7", "native-7"])
    #expect(windowHandleInventoryLiveness(
        collect([3], "native-7"), requestedHandle: "native-7"
    ) == .present)
    #expect(windowHandleInventoryLiveness(
        collect([0, 2], "other"), requestedHandle: "other"
    ) == .unknown)
}

private func livenessApp() -> RunningAppDescriptor {
    RunningAppDescriptor(
        name: "Liveness Test", bundleIdentifier: nil, pid: getpid(),
        runningApplication: NSRunningApplication.current
    )
}

private func livenessIdentity() -> [String: Any] {
    ["providerBusName": "mac:\(getpid())",
     "processGeneration": macProcessGeneration(pid: getpid())]
}

private func livenessSnapshot(windowID: CGWindowID) -> AppSnapshot {
    AppSnapshot(
        app: livenessApp(), windowTitle: "Document", windowBounds: nil,
        rootWindowElement: AXUIElementCreateSystemWide(),
        targetWindowID: windowID,
        targetWindowHandle: macWindowHandle(pid: getpid(), windowID: windowID),
        targetWindowLayer: nil, treeLines: [],
        focusedSummary: nil, focusedElement: nil, selectedText: nil,
        elements: [:]
    )
}

@Test
func boundWindowProvenanceSurvivesSiblingSnapshotOverwrite() {
    let service = ComputerUseService()
    let first = livenessSnapshot(windowID: 17)
    let second = livenessSnapshot(windowID: 18)
    service.rememberNativeWindowIdentity(first)
    service.snapshotsByApp["app"] = second
    service.rememberNativeWindowIdentity(second)
    #expect(service.nativeWindowHandles == [
        macWindowHandle(pid: getpid(), windowID: 17),
        macWindowHandle(pid: getpid(), windowID: 18),
    ])
    #expect(service.snapshotsByApp["app"]?.targetWindowHandle == second.targetWindowHandle)
}

@Test
func boundWindowFocusChangeDoesNotClaimDisappearanceOrAllowInput() throws {
    for focus in [OrderedInputWindowContinuity.changed, .unknown] {
        var inspections = 0
        let result = try checkedOrderedWindowContinuity(focus) {
            inspections += 1
        }
        #expect(result == .unknown)
        #expect(inspections == 1)
        do {
            _ = try checkedOrderedWindowContinuity(focus) {
                throw ComputerUseError.staleWindowRef("confirmed absent")
            }
            Issue.record("Expected confirmed absence")
        } catch {
            #expect(runtimeFailureResult(error)["failureCode"] as? String == "stale_window_ref")
        }
    }
    #expect(try checkedOrderedWindowContinuity(.same) {
        Issue.record("Normal matching focus must not enumerate liveness")
    } == .same)
}

@Test
func boundWindowSuccessfulSiblingCaptureRequiresAbsenceProof() {
    for state in [BoundWindowLiveness.absent, .present, .unknown] {
        do {
            _ = try orderedInputFinalSnapshot(
                appIdentity: livenessIdentity(),
                windowHandle: macWindowHandle(pid: getpid(), windowID: 17),
                resolveApp: livenessApp,
                capture: { _ in livenessSnapshot(windowID: 18) },
                inspectWindow: { _, _ in state }
            )
            Issue.record("Must not return the sibling snapshot")
        } catch {
            #expect(runtimeFailureResult(error)["failureCode"] as? String ==
                (state == .absent ? "stale_window_ref" : "state_unavailable"))
        }
    }
}

@Test
func boundWindowLivenessRevalidatesIdentityBeforeClassifyingAbsence() {
    for changed in [false, true] {
        var resolutions = 0
        var inspections = 0
        do {
            try validateBoundWindowLiveness(
                appIdentity: livenessIdentity(), windowHandle: "target",
                resolveApp: {
                    resolutions += 1
                    if changed && resolutions == 2 {
                        throw ComputerUseError.staleAppRef("process changed")
                    }
                    return livenessApp()
                },
                inspectWindow: { pid, handle in
                    #expect(pid == getpid())
                    #expect(handle == "target")
                    inspections += 1
                    return .absent
                }
            )
            Issue.record("Expected typed stale failure")
        } catch {
            #expect(runtimeFailureResult(error)["failureCode"] as? String ==
                (changed ? "stale_app_ref" : "stale_window_ref"))
        }
        #expect(resolutions == 2)
        #expect(inspections == 1)
    }
}

@Test
func boundWindowLivenessPresentOrUnknownNeverClaimsStale() throws {
    for state in [BoundWindowLiveness.present, .unknown] {
        try validateBoundWindowLiveness(
            appIdentity: livenessIdentity(), windowHandle: "target",
            resolveApp: livenessApp, inspectWindow: { _, _ in state }
        )
    }
    try validateBoundWindowLiveness(
        appIdentity: [:], windowHandle: "",
        resolveApp: {
            Issue.record("App-root read must not probe a nonexistent bound window")
            return livenessApp()
        },
        inspectWindow: { _, _ in
            Issue.record("App-root read must skip window liveness")
            return .absent
        }
    )
}

@Test
func boundWindowFinalFailureUsesLivenessWithoutReplayingCapture() {
    for state in [BoundWindowLiveness.absent, .present, .unknown] {
        var captures = 0
        var resolutions = 0
        do {
            _ = try orderedInputFinalSnapshot(
                appIdentity: livenessIdentity(), windowHandle: "target",
                resolveApp: {
                    resolutions += 1
                    return livenessApp()
                },
                capture: { _ in
                    captures += 1
                    throw ComputerUseError.stateUnavailable("temporary capture failure")
                },
                inspectWindow: { _, _ in state }
            )
            Issue.record("Expected capture or identity failure")
        } catch {
            #expect(runtimeFailureResult(error)["failureCode"] as? String ==
                (state == .absent ? "stale_window_ref" : "state_unavailable"))
        }
        #expect(captures == 1)
        #expect(resolutions == 3)
    }
}

@Test
func boundWindowGetAppStateFailureUsesSameNonLaunchingLivenessPath() {
    let bridge = ComputerUseRuntimeBridge()
    var captures = 0
    do {
        _ = try bridge.getAppStateSnapshot(
            ["app": "Liveness Test", "appIdentity": livenessIdentity(),
             "targetWindowHandle": "target"],
            resolveByName: { _ in
                Issue.record("Must not discover or launch by name")
                return livenessApp()
            },
            refreshByName: { _ in
                Issue.record("Must not refresh by name")
                throw ComputerUseError.stateUnavailable("unexpected")
            },
            resolveBoundApp: { _ in livenessApp() },
            captureBound: { _ in
                captures += 1
                throw ComputerUseError.stateUnavailable("capture failed")
            },
            inspectBoundWindow: { _, _ in .absent }
        )
        Issue.record("Expected window stale")
    } catch {
        #expect(runtimeFailureResult(error)["failureCode"] as? String == "stale_window_ref")
    }
    #expect(captures == 1)
    #expect(bridge.service.snapshotsByApp.isEmpty)
}
