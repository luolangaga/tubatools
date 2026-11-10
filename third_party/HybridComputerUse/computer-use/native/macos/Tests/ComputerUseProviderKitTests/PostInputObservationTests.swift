import AppKit
import ApplicationServices
@testable import ComputerUseProviderKit
import Testing

private func postInputApp() -> RunningAppDescriptor {
    RunningAppDescriptor(
        name: "Post Input Test",
        bundleIdentifier: "org.computer-use.post-input-test",
        pid: getpid(),
        runningApplication: NSRunningApplication.current
    )
}

private func postInputIdentity() -> [String: Any] {
    [
        "providerBusName": "mac:\(getpid())",
        "processGeneration": macProcessGeneration(pid: getpid()),
    ]
}

@Test
func postInputCaptureFailurePreservesDeliveryAndReadback() {
    for (route, sampled, verified) in [
        ("input.cg_event.keyboard", false, nil as Bool?),
        ("input.pasteboard.global_command_v", false, nil),
        ("input.cg_event.scroll", false, nil),
        ("ax.action.scrolldownbypage", false, nil),
        ("ax.attribute.value.set", true, true),
    ] {
        var sent: [String: Any] = ["route": route, "readback_sampled": sampled]
        if let verified {
            sent["readback_verified"] = verified
        }
        let result = postInputObservationFailureResult(
            ComputerUseError.stateUnavailable("provider_identity_conflict"),
            evidence: sent
        )
        let wire = ProviderResponseBuilder.domainResult(result, operation: "resolve_and_act")
        let mutation = wire["mutation"] as? [String: Any]
        let failure = wire["failure"] as? [String: Any]
        let evidence = wire["action_evidence"] as? [String: Any]
        #expect(wire["ok"] as? Bool == false)
        #expect(wire["snapshot"] == nil)
        #expect(mutation?["input_events_emitted"] as? Bool == true)
        #expect(mutation?["partial_input_possible"] as? Bool == false)
        #expect(mutation?["mutation_unknown"] as? Bool == true)
        #expect(failure?["phase"] as? String == "observation")
        #expect(failure?["code"] as? String == "effect_unknown")
        #expect(failure?["facts_complete"] as? Bool == false)
        #expect(evidence?["route"] as? String == route)
        #expect(evidence?["readback_sampled"] as? Bool == sampled)
        #expect(evidence?["readback_verified"] as? Bool == verified)
    }
}

@Test
func pureReadFailureReportsNoInput() {
    let pureRead = runtimeFailureResult(ComputerUseError.stateUnavailable("unavailable"))
    #expect(pureRead["input_events_emitted"] as? Bool == false)
    #expect(pureRead["mutation_unknown"] as? Bool == false)
}

@Test
func expansionObservationFailurePreservesInputEvidence() {
    func capture() throws {
        let root = AXUIElementCreateApplication(-1)
        var renderer = TreeRenderer(
            context: RenderContext(
                windowBounds: nil, focusedElement: nil, focusedAncestry: [],
                textLimit: .defaults, treeLimits: .defaults
            ),
            readExpansion: { _ in
                try readAXExpansion(
                    readNames: { (.cannotComplete, nil) },
                    readValue: {
                        Issue.record("Unknown applicability must not read AXExpanded")
                        return (.success, kCFBooleanFalse)
                    }
                )
            }
        )
        try renderer.render(root)
    }
    do {
        try capture()
        Issue.record("Pure observation must fail")
    } catch {
        let result = runtimeFailureResult(error)
        #expect(result["failureCode"] as? String == "state_unavailable")
        #expect(result["input_events_emitted"] as? Bool == false)
        #expect(result["mutation_unknown"] as? Bool == false)
    }
    do {
        try capture()
        Issue.record("Post-input observation must fail")
    } catch {
        let result = postInputObservationFailureResult(
            error,
            evidence: [
                "route": "ax.attribute.value.set",
                "readback_sampled": true,
                "readback_verified": true,
            ]
        )
        let wire = ProviderResponseBuilder.domainResult(result, operation: "resolve_and_act")
        let mutation = wire["mutation"] as? [String: Any]
        let failure = wire["failure"] as? [String: Any]
        let evidence = wire["action_evidence"] as? [String: Any]
        #expect(wire["ok"] as? Bool == false)
        #expect(wire["snapshot"] == nil)
        #expect(mutation?["input_events_emitted"] as? Bool == true)
        #expect(mutation?["partial_input_possible"] as? Bool == false)
        #expect(mutation?["mutation_unknown"] as? Bool == true)
        #expect(failure?["code"] as? String == "effect_unknown")
        #expect(failure?["phase"] as? String == "observation")
        #expect(failure?["facts_complete"] as? Bool == false)
        #expect(evidence?["route"] as? String == "ax.attribute.value.set")
        #expect(evidence?["readback_sampled"] as? Bool == true)
        #expect(evidence?["readback_verified"] as? Bool == true)
    }
}

@Test
func postInputCaptureFailureRetainsProviderReset() {
    let result = postInputObservationFailureResult(
        ComputerUseError.providerResetRequired("capture timed out"),
        evidence: ["route": "input.cg_event.keyboard"]
    )
    #expect(result["providerResetRequired"] as? Bool == true)
    #expect(result["input_events_emitted"] as? Bool == true)
}

@Test
func postInputWindowCloseRequiresIndependentBoundAbsence() {
    let app = postInputApp()
    let identity = postInputIdentity()
    let handle = macWindowHandle(pid: app.pid, windowID: 17)
    for state in [BoundWindowLiveness.present, .unknown, .absent] {
        var captures = 0
        var inspections = 0
        let result = windowActionFinalObservation(
            tool: "close", app: app, appIdentity: identity, windowHandle: handle,
            capture: {
                captures += 1
                throw ComputerUseError.stateUnavailable("provider_identity_conflict")
            },
            validateWindow: {
                try validateBoundWindowLiveness(
                    appIdentity: identity,
                    windowHandle: handle,
                    resolveApp: { app },
                    inspectWindow: { pid, requested in
                        inspections += 1
                        #expect(pid == app.pid)
                        #expect(requested == handle)
                        return state
                    }
                )
            }
        )
        #expect(captures == 1)
        #expect(inspections == 1)
        #expect(result["input_events_emitted"] as? Bool == true)
        let snapshot = result["snapshot"] as? [String: Any]
        if state == .absent {
            #expect(result["ok"] as? Bool == true)
            #expect(snapshot?["windowAbsent"] as? Bool == true)
            #expect(snapshot?["closedWindowHandle"] as? String == handle)
        } else {
            #expect(result["ok"] as? Bool == false)
            #expect(snapshot == nil)
            #expect(result["mutation_unknown"] as? Bool == true)
        }
    }
}

@Test
func postInputWindowCaptureCannotSupplyItsOwnAbsenceProof() {
    for tool in ["close", "activate"] {
        var validations = 0
        let result = windowActionFinalObservation(
            tool: tool, app: postInputApp(), appIdentity: postInputIdentity(),
            windowHandle: "bound-window",
            capture: { throw ComputerUseError.staleWindowRef("capture failed") },
            validateWindow: { validations += 1 }
        )
        #expect(result["ok"] as? Bool == false)
        #expect(result["snapshot"] == nil)
        #expect(result["input_events_emitted"] as? Bool == true)
        #expect(validations == (tool == "close" ? 1 : 0))
        let evidence = result["actionEvidence"] as? [String: Any]
        #expect(evidence?["route"] as? String == "ax.window.\(tool)")
        #expect(evidence?["readback_verified"] as? Bool == (tool == "activate"))
    }
}

@Test
func postInputWindowCaptureSuccessDoesNotProbeOrReplaceSiblingState() {
    var validations = 0
    let result = windowActionFinalObservation(
        tool: "close", app: postInputApp(), appIdentity: postInputIdentity(),
        windowHandle: "closed-window",
        capture: { ["windowHandle": "remaining-sibling", "elements": []] },
        validateWindow: { validations += 1 }
    )
    let snapshot = result["snapshot"] as? [String: Any]
    #expect(result["ok"] as? Bool == true)
    #expect(snapshot?["windowHandle"] as? String == "remaining-sibling")
    #expect(snapshot?["windowAbsent"] == nil)
    #expect(validations == 0)
}

@Test
func postInputWindowResetAndMissingIdentityCannotProveAbsence() {
    for reset in [false, true] {
        var validations = 0
        let result = windowActionFinalObservation(
            tool: "close", app: postInputApp(), appIdentity: postInputIdentity(),
            windowHandle: reset ? "bound-window" : "",
            capture: {
                if reset {
                    throw ComputerUseError.providerResetRequired("timed out")
                }
                throw ComputerUseError.stateUnavailable("unavailable")
            },
            validateWindow: {
                validations += 1
                throw ComputerUseError.staleWindowRef("must not be consulted")
            }
        )
        #expect(validations == 0)
        #expect(result["ok"] as? Bool == false)
        #expect(result["snapshot"] == nil)
        #expect(result["input_events_emitted"] as? Bool == true)
        #expect((result["providerResetRequired"] as? Bool ?? false) == reset)
    }
}
