import AppKit
import Testing
@testable import ComputerUseProviderKit

@Test
func requestWindowBindingRestoresOnSuccessFailureAndNestedRequests() throws {
    let service = ComputerUseService()
    service.activeAppIdentity = ["providerBusName": "outer-app"]
    service.activeWindowHandle = "outer-window"
    try service.withAppIdentity(["providerBusName": "app-a"], windowHandle: "window-a") {
        let inherited = try service.snapshotWindowHandle(requested: nil)
        let empty = try service.snapshotWindowHandle(requested: "")
        #expect(inherited == "window-a")
        #expect(empty == "window-a")
        try service.withAppIdentity(["providerBusName": "app-b"], windowHandle: "window-b") {
            let nested = try service.snapshotWindowHandle(requested: nil)
            #expect(nested == "window-b")
        }
        #expect(service.activeAppIdentity?["providerBusName"] as? String == "app-a")
        #expect(service.activeWindowHandle == "window-a")
        do {
            try service.withAppIdentity(nil, windowHandle: "window-c") {
                throw ComputerUseError.stateUnavailable("capture failed")
            }
            Issue.record("The injected error must escape")
        } catch {}
        #expect(service.activeAppIdentity?["providerBusName"] as? String == "app-a")
        #expect(service.activeWindowHandle == "window-a")
    }
    #expect(service.activeAppIdentity?["providerBusName"] as? String == "outer-app")
    #expect(service.activeWindowHandle == "outer-window")
}

@Test
func conflictingSnapshotWindowFailsBeforeAppDiscovery() throws {
    let service = ComputerUseService()
    service.withAppIdentity(nil, windowHandle: "window-a") {
        do {
            _ = try service.refreshSnapshot(
                for: "This application must not be discovered",
                expectedWindowHandle: "window-b"
            )
            Issue.record("An explicit window conflict must fail")
        } catch {
            let result = runtimeFailureResult(error)
            #expect(result["failureCode"] as? String == "state_unavailable")
            #expect(result["input_events_emitted"] as? Bool == false)
            #expect((result["error"] as? String ?? "").contains("conflicts"))
        }
    }
    #expect(service.activeWindowHandle == nil)
}

@Test
func unboundRequestDoesNotInheritAnEarlierRequestWindow() throws {
    let service = ComputerUseService()
    try service.withAppIdentity(nil, windowHandle: "window-a") {
        try service.withAppIdentity(nil) {
            #expect(service.activeWindowHandle == nil)
            let unbound = try service.snapshotWindowHandle(requested: nil)
            let explicit = try service.snapshotWindowHandle(requested: "explicit")
            #expect(unbound == nil)
            #expect(explicit == "explicit")
        }
        #expect(service.activeWindowHandle == "window-a")
    }
    #expect(service.activeWindowHandle == nil)
}

@Test
func postInputWindowDisappearanceCannotFallBackToSibling() {
    let service = ComputerUseService()
    let app = RunningAppDescriptor(
        name: "Binding Test", bundleIdentifier: "test.binding", pid: getpid(),
        runningApplication: NSRunningApplication.current
    )
    for absenceProven in [false, true] {
        var unboundReads = 0
        var validations = 0
        let result = service.withAppIdentity(nil, windowHandle: "window-a") {
            windowActionFinalObservation(
                tool: "close", app: app,
                appIdentity: ["providerBusName": "test", "processGeneration": "generation"],
                windowHandle: "window-a",
                capture: {
                    let selected = try selectSnapshotWindow(
                        requestedHandle: service.snapshotWindowHandle(requested: nil),
                        inventory: { ["window-b"] }, handleOf: { $0 },
                        unboundWindow: { unboundReads += 1; return "window-b" }
                    )
                    return ["windowHandle": selected ?? ""]
                },
                validateWindow: {
                    validations += 1
                    if absenceProven {
                        throw ComputerUseError.staleWindowRef("bound window disappeared")
                    }
                }
            )
        }
        #expect(result["input_events_emitted"] as? Bool == true)
        #expect(unboundReads == 0)
        #expect(validations == 1)
        let snapshot = result["snapshot"] as? [String: Any]
        #expect(snapshot?["windowHandle"] == nil)
        if absenceProven {
            #expect(result["ok"] as? Bool == true)
            #expect(snapshot?["windowAbsent"] as? Bool == true)
            #expect(snapshot?["closedWindowHandle"] as? String == "window-a")
        } else {
            #expect(result["ok"] as? Bool == false)
            #expect(result["failureCode"] as? String == "effect_unknown")
            #expect(result["mutation_unknown"] as? Bool == true)
            #expect(snapshot == nil)
        }
        #expect(service.activeWindowHandle == nil)
    }
}
