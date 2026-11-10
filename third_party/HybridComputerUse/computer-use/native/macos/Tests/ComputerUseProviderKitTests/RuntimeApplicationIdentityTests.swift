@testable import ComputerUseProviderKit
import AppKit
import Testing

private func runtimeIdentityApp() -> RunningAppDescriptor {
    RunningAppDescriptor(name: "Real App", bundleIdentifier: "test.real-app", pid: getpid(), runningApplication: NSRunningApplication.current)
}

private func runtimeIdentity(generation: String? = nil) -> [String: Any] {
    ["providerBusName": "mac:\(getpid())", "processGeneration": generation ?? macProcessGeneration(pid: getpid())]
}

@Test func shortAppRefUsesOnlyBoundProcessIdentity() throws {
    var names: [String] = []
    var identities = 0
    let descriptor = try resolveRuntimeApplication(query: "a1", identity: runtimeIdentity(), resolveByName: { query in
        names.append(query)
        return runtimeIdentityApp()
    }, resolveBoundApp: { identity in
        identities += 1
        #expect(identity["providerBusName"] as? String == "mac:\(getpid())")
        return runtimeIdentityApp()
    })
    #expect(descriptor.pid == getpid())
    #expect(identities == 1)
    #expect(names.isEmpty)
}

@Test func boundProcessMismatchAndExitNeverFallBackToName() {
    for missing in [false, true] {
        var names = 0
        do {
            _ = try resolveRuntimeApplication(query: "a1", identity: runtimeIdentity(generation: "old-process"), resolveByName: { _ in
                names += 1
                return runtimeIdentityApp()
            }, resolveBoundApp: { _ in
                if missing { throw ComputerUseError.staleAppRef("process exited") }
                return runtimeIdentityApp()
            })
            Issue.record("Expected stale app refusal")
        } catch {
            #expect(runtimeFailureResult(error)["failureCode"] as? String == "stale_app_ref")
        }
        #expect(names == 0)
    }
}

@Test func unboundAppDiscoveryStillUsesName() throws {
    var names: [String] = []
    _ = try resolveRuntimeApplication(query: "TextEdit", identity: nil, resolveByName: { name in
        names.append(name)
        return runtimeIdentityApp()
    }, resolveBoundApp: { _ in
        Issue.record("Unexpected identity resolution")
        return runtimeIdentityApp()
    })
    #expect(names == ["TextEdit"])
}

@Test func appBindingCoversActionsWindowsAndServiceRefresh() async {
    let bridge = ComputerUseRuntimeBridge()
    for tool in ["select", "set_value", "get_app_state", "list_app_windows", "activate", "close"] {
        let result = await bridge.execute(["tool": tool, "app": "a1", "appIdentity": runtimeIdentity(generation: "old-process")])
        #expect(result["failureCode"] as? String == "stale_app_ref")
        #expect(result["input_events_emitted"] as? Bool == false)
        #expect(bridge.service.activeAppIdentity == nil)
    }
    bridge.service.activeAppIdentity = runtimeIdentity(generation: "old-process")
    do {
        _ = try bridge.service.refreshSnapshot(for: "a1")
        Issue.record("Expected service capture to refuse stale binding")
    } catch {
        #expect(runtimeFailureResult(error)["failureCode"] as? String == "stale_app_ref")
    }
    bridge.service.activeAppIdentity = nil
}

@Test func requestAppBindingRestoresAfterSuccessAndFailure() async {
    let bridge = ComputerUseRuntimeBridge()
    let success = bridge.service.withAppIdentity(runtimeIdentity()) {
        #expect(bridge.service.activeAppIdentity != nil)
        return true
    }
    #expect(success)
    #expect(bridge.service.activeAppIdentity == nil)
    do {
        try bridge.service.withAppIdentity(runtimeIdentity()) {
            #expect(bridge.service.activeAppIdentity != nil)
            throw ComputerUseError.stateUnavailable("capture failed")
        }
    } catch {}
    #expect(bridge.service.activeAppIdentity == nil)
    let failure = await bridge.execute(["tool": "select", "app": "a1", "appIdentity": runtimeIdentity(generation: "old-process")])
    #expect(failure["ok"] as? Bool == false)
    #expect(bridge.service.activeAppIdentity == nil)
    var discovered: [String] = []
    _ = try? resolveRuntimeApplication(query: "TextEdit", identity: bridge.service.activeAppIdentity, resolveByName: { name in
        discovered.append(name)
        return runtimeIdentityApp()
    })
    #expect(discovered == ["TextEdit"])
    #expect(bridge.service.activeAppIdentity == nil)
}
