import AppKit
import ApplicationServices
@testable import ComputerUseProviderKit
import Testing

private func stateReadApp(pid: pid_t = getpid()) -> RunningAppDescriptor {
    RunningAppDescriptor(
        name: "State Read Test",
        bundleIdentifier: "test.state-read",
        pid: pid,
        runningApplication: NSRunningApplication.current
    )
}

private func stateReadOperation() -> [String: Any] {
    [
        "app": "State Read Alias",
        "appIdentity": [
            "providerBusName": "mac:\(getpid())",
            "processGeneration": macProcessGeneration(pid: getpid()),
        ],
        "targetWindowHandle": "bound-window",
    ]
}

private func stateReadSnapshot(
    app: RunningAppDescriptor = stateReadApp(),
    window: String = "bound-window"
) -> AppSnapshot {
    AppSnapshot(
        app: app,
        windowTitle: "State Read Test",
        windowBounds: nil,
        rootWindowElement: AXUIElementCreateSystemWide(),
        targetWindowID: nil,
        targetWindowHandle: window,
        targetWindowLayer: nil,
        treeLines: [],
        focusedSummary: nil,
        focusedElement: nil,
        selectedText: nil,
        elements: [:]
    )
}

@Test
func boundGetAppStateNeverUsesEitherNameLookupPath() throws {
    let bridge = ComputerUseRuntimeBridge()
    var nameResolves = 0
    var nameRefreshes = 0
    var identityReads = 0
    var captures = 0
    let snapshot = try bridge.getAppStateSnapshot(
        stateReadOperation(),
        resolveByName: { _ in
            nameResolves += 1
            return stateReadApp(pid: getpid() + 1)
        },
        refreshByName: { _ in
            nameRefreshes += 1
            return stateReadSnapshot(app: stateReadApp(pid: getpid() + 1))
        },
        resolveBoundApp: { _ in
            identityReads += 1
            return stateReadApp()
        },
        captureBound: { descriptor in
            captures += 1
            #expect(descriptor.pid == getpid())
            return stateReadSnapshot(app: descriptor)
        }
    )

    #expect(nameResolves == 0)
    #expect(nameRefreshes == 0)
    #expect(identityReads == 2)
    #expect(captures == 1)
    #expect(snapshot.app.pid == getpid())
    #expect(Set(bridge.service.snapshotsByApp.keys) == Set([
        "state read alias", "state read test", "test.state-read",
    ]))
    #expect(bridge.service.snapshotsByApp.values.allSatisfy {
        $0.app.pid == getpid() && $0.targetWindowHandle == "bound-window"
    })
}

@Test
func boundGetAppStateRejectsExitBeforeOrDuringCaptureWithoutRelaunch() {
    for exitAtRead in [1, 2] {
        for captureFails in [false, true] {
            let bridge = ComputerUseRuntimeBridge()
            bridge.service.snapshotsByApp["existing"] =
                stateReadSnapshot(window: "cached-window")
            var nameResolves = 0
            var nameRefreshes = 0
            var identityReads = 0
            var captures = 0
            do {
                _ = try bridge.getAppStateSnapshot(
                    stateReadOperation(),
                    resolveByName: { _ in
                        nameResolves += 1
                        return stateReadApp()
                    },
                    refreshByName: { _ in
                        nameRefreshes += 1
                        return stateReadSnapshot()
                    },
                    resolveBoundApp: { identity in
                        identityReads += 1
                        if identityReads == exitAtRead {
                            return try resolveOrderedInputApp(identity) { _ in nil }
                        }
                        return stateReadApp()
                    },
                    captureBound: { _ in
                        captures += 1
                        if captureFails {
                            throw ComputerUseError.stateUnavailable("AX read failed")
                        }
                        return stateReadSnapshot()
                    }
                )
                Issue.record("Expected bound process exit to reject the read")
            } catch {
                let result = runtimeFailureResult(error)
                #expect(result["failureCode"] as? String == "stale_app_ref")
                #expect(result["failureSubject"] as? String == "app")
                #expect(result["input_events_emitted"] as? Bool == false)
            }
            #expect(nameResolves == 0)
            #expect(nameRefreshes == 0)
            #expect(identityReads == exitAtRead)
            #expect(captures == (exitAtRead == 1 ? 0 : 1))
            #expect(Array(bridge.service.snapshotsByApp.keys) == ["existing"])
            #expect(bridge.service.snapshotsByApp["existing"]?.targetWindowHandle
                == "cached-window")
        }
    }
}

@Test
func boundGetAppStateRejectsChangedGenerationBeforeCapture() {
    var operation = stateReadOperation()
    operation["appIdentity"] = [
        "providerBusName": "mac:\(getpid())",
        "processGeneration": "previous-generation",
    ]
    let bridge = ComputerUseRuntimeBridge()
    var nameCalls = 0
    var captures = 0
    do {
        _ = try bridge.getAppStateSnapshot(
            operation,
            resolveByName: { _ in
                nameCalls += 1
                return stateReadApp()
            },
            refreshByName: { _ in
                nameCalls += 1
                return stateReadSnapshot()
            },
            resolveBoundApp: { _ in stateReadApp() },
            captureBound: { _ in
                captures += 1
                return stateReadSnapshot()
            }
        )
        Issue.record("Expected generation mismatch")
    } catch {
        #expect(runtimeFailureResult(error)["failureCode"] as? String == "stale_app_ref")
    }
    #expect(nameCalls == 0)
    #expect(captures == 0)
    #expect(bridge.service.snapshotsByApp.isEmpty)
}

@Test
func boundGetAppStateRejectsReplacementAppOrWindowSnapshot() {
    for replacedApp in [false, true] {
        let bridge = ComputerUseRuntimeBridge()
        do {
            _ = try bridge.getAppStateSnapshot(
                stateReadOperation(),
                resolveByName: { _ in
                    Issue.record("Bound read reached discovery")
                    return stateReadApp()
                },
                refreshByName: { _ in
                    Issue.record("Bound read reached name-based refresh")
                    return stateReadSnapshot()
                },
                resolveBoundApp: { _ in stateReadApp() },
                captureBound: { _ in
                    stateReadSnapshot(
                        app: stateReadApp(pid: replacedApp ? getpid() + 1 : getpid()),
                        window: replacedApp ? "bound-window" : "replacement"
                    )
                },
                inspectBoundWindow: { _, _ in .absent }
            )
            Issue.record("Expected snapshot identity rejection")
        } catch {
            #expect(runtimeFailureResult(error)["failureCode"] as? String ==
                (replacedApp ? "stale_app_ref" : "stale_window_ref"))
        }
        #expect(bridge.service.snapshotsByApp.isEmpty)
    }
}

@Test
func boundGetAppStateTemporaryReadFailureDoesNotBecomeStale() {
    let bridge = ComputerUseRuntimeBridge()
    var identityReads = 0
    do {
        _ = try bridge.getAppStateSnapshot(
            stateReadOperation(),
            resolveByName: { _ in
                Issue.record("Temporary failure must not trigger discovery")
                return stateReadApp()
            },
            refreshByName: { _ in
                Issue.record("Temporary failure must not trigger name refresh")
                return stateReadSnapshot()
            },
            resolveBoundApp: { _ in
                identityReads += 1
                return stateReadApp()
            },
            captureBound: { _ in
                throw ComputerUseError.stateUnavailable("AX temporarily unreadable")
            },
            inspectBoundWindow: { _, _ in .unknown }
        )
        Issue.record("Expected temporary read failure")
    } catch {
        let result = runtimeFailureResult(error)
        #expect(result["failureCode"] as? String == "state_unavailable")
        #expect(result["failureSubject"] == nil)
        #expect(result["input_events_emitted"] as? Bool == false)
    }
    #expect(identityReads == 3)
    #expect(bridge.service.snapshotsByApp.isEmpty)
}

@Test
func malformedBoundGetAppStateNeverFallsBackToDiscovery() {
    let malformedIdentities: [Any] = ["invalid", NSNull(), [String: Any]()]
    for identity in malformedIdentities {
        var operation = stateReadOperation()
        operation["appIdentity"] = identity
        var nameCalls = 0
        var captures = 0
        do {
            _ = try ComputerUseRuntimeBridge().getAppStateSnapshot(
                operation,
                resolveByName: { _ in
                    nameCalls += 1
                    return stateReadApp()
                },
                refreshByName: { _ in
                    nameCalls += 1
                    return stateReadSnapshot()
                },
                captureBound: { _ in
                    captures += 1
                    return stateReadSnapshot()
                }
            )
            Issue.record("Expected malformed identity rejection")
        } catch {
            #expect((error as? ComputerUseError)?.staleIdentityFailure == nil)
        }
        #expect(nameCalls == 0)
        #expect(captures == 0)
    }
}

@Test
func unboundGetAppStateKeepsNameDiscoveryAndRefresh() throws {
    var operation = stateReadOperation()
    operation.removeValue(forKey: "appIdentity")
    var calls: [String] = []
    let snapshot = try ComputerUseRuntimeBridge().getAppStateSnapshot(
        operation,
        resolveByName: { _ in
            calls.append("resolve")
            return stateReadApp()
        },
        refreshByName: { _ in
            calls.append("refresh")
            return stateReadSnapshot()
        },
        resolveBoundApp: { _ in
            Issue.record("Unbound discovery reached bound resolver")
            return stateReadApp()
        },
        captureBound: { _ in
            Issue.record("Unbound discovery reached bound capture")
            return stateReadSnapshot()
        }
    )
    #expect(calls == ["resolve", "refresh"])
    #expect(snapshot.targetWindowHandle == "bound-window")
}
