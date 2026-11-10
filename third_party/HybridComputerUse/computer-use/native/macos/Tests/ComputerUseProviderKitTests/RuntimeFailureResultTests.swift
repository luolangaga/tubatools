@testable import ComputerUseProviderKit
import Testing

@Test func unsupportedSelectionStopsBeforeAppDiscovery() async {
    let bridge = ComputerUseRuntimeBridge()
    let operations: [[String: Any]] = [
        ["tool": "select", "to_target": ["providerObjectPath": "end"]],
        ["tool": "set_value", "semanticTarget": ["editTarget": ["providerObjectPath": "editor"]]],
    ]
    for operation in operations {
        // No app is supplied: entering app discovery would produce a different failure.
        let result = await bridge.execute(operation)
        #expect(result["failureCode"] as? String == "capability_unsupported")
        #expect(result["failurePhase"] as? String == "precondition")
        #expect(result["input_events_emitted"] as? Bool == false)
        #expect(result["mutation_unknown"] as? Bool == false)
    }
}

@Test
func providerResetFailureRequestsSidecarRestart() {
    let result = runtimeFailureResult(
        ComputerUseError.providerResetRequired("capture timed out")
    )

    #expect(result["failureCode"] as? String == "state_unavailable")
    #expect(result["providerResetRequired"] as? Bool == true)
}

@Test
func capabilityUnsupportedSurvivesRuntimeBridge() {
    let result = runtimeFailureResult(
        ComputerUseError.capabilityUnsupported("unsupported operation")
    )

    #expect(result["ok"] as? Bool == false)
    #expect(result["failureCode"] as? String == "capability_unsupported")
    #expect(result["failurePhase"] as? String == "precondition")
    #expect(result["input_events_emitted"] as? Bool == false)
    #expect(result["mutation_unknown"] as? Bool == false)
}

@Test
func invalidArgumentsRemainPreflightFailures() {
    let result = runtimeFailureResult(
        ComputerUseError.invalidArguments("bad direction")
    )

    #expect(result["failureCode"] as? String == "invalid_request")
    #expect(result["failureFactsComplete"] as? Bool == true)
}

@Test
func staleElementRefPreservesCompleteNoInputFacts() {
    let result = runtimeFailureResult(
        ComputerUseError.staleElementRef("stale element")
    )

    #expect(result["failureCode"] as? String == "stale_element_ref")
    #expect(result["failurePhase"] as? String == "precondition")
    #expect(result["failureFactsComplete"] as? Bool == true)
    #expect(result["input_events_emitted"] as? Bool == false)
    #expect(result["partial_input_possible"] as? Bool == false)
    #expect(result["mutation_unknown"] as? Bool == false)
}

@Test
func staleAppAndWindowRefsSurviveRuntimeBridge() {
    for (error, code, subject, reason) in [
        (ComputerUseError.staleAppRef("owner replaced"), "stale_app_ref", "app", "provider_process_changed"),
        (ComputerUseError.staleWindowRef("owner replaced"), "stale_window_ref", "window", "provider_window_changed"),
    ] {
        let result = runtimeFailureResult(error)

        #expect(result["ok"] as? Bool == false)
        #expect(result["failureCode"] as? String == code)
        #expect(result["failureSubject"] as? String == subject)
        #expect(result["underlyingFailureCode"] as? String == reason)
        #expect(result["failurePhase"] as? String == "precondition")
        #expect(result["failureFactsComplete"] as? Bool == true)
        #expect(result["input_events_emitted"] as? Bool == false)
        #expect(result["partial_input_possible"] as? Bool == false)
        #expect(result["mutation_unknown"] as? Bool == false)
    }
}

@Test
func staleIdentityWordsDoNotClassifyRuntimeFailure() {
    let result = runtimeFailureResult(ComputerUseError.stateUnavailable(
        "stale_app_ref stale_window_ref provider_process_changed"
    ))

    #expect(result["failureCode"] as? String == "state_unavailable")
    #expect(result["failureSubject"] == nil)
    #expect(result["underlyingFailureCode"] == nil)
}

@Test
func genericStateUnavailableDoesNotBecomeStaleElementRef() {
    let result = runtimeFailureResult(
        ComputerUseError.stateUnavailable("selector did not match")
    )

    #expect(result["failureCode"] as? String == "state_unavailable")
}

@Test
func emittedInputFailureIsNotReportedAsPreconditionSafe() {
    let result = runtimeFailureResult(
        InputDeliveryError(
            message: "clipboard restoration failed",
            inputEventsEmitted: true
        )
    )

    #expect(result["failurePhase"] as? String == "emission")
    #expect(result["input_events_emitted"] as? Bool == true)
    #expect(result["partial_input_possible"] as? Bool == true)
    #expect(result["mutation_unknown"] as? Bool == true)
    #expect(result["desktop_state_mutated"] as? Bool == true)
}

@Test
func setValueReadbackMismatchIsAnEmissionFailure() {
    let result = runtimeFailureResult(
        InputDeliveryError(
            message: "AX value write returned success, but exact readback did not match",
            inputEventsEmitted: true,
            failureCode: "readback_mismatch",
            partialInputPossible: false,
            readbackSampled: true,
            readbackVerified: false,
            actionRoute: "ax.attribute.value.set"
        )
    )

    #expect(result["ok"] as? Bool == false)
    #expect(result["failureCode"] as? String == "readback_mismatch")
    #expect(result["failurePhase"] as? String == "emission")
    #expect(result["input_events_emitted"] as? Bool == true)
    #expect(result["partial_input_possible"] as? Bool == false)
    #expect(result["mutation_unknown"] as? Bool == true)
    let evidence = result["actionEvidence"] as? [String: Any]
    #expect(evidence?["input_emitted"] as? Bool == true)
    #expect(evidence?["readback_sampled"] as? Bool == true)
    #expect(evidence?["readback_verified"] as? Bool == false)
    #expect(evidence?["route"] as? String == "ax.attribute.value.set")
}

@Test
func unchangedRelativeAdjustmentReadbackIsAnEmissionFailure() {
    let result = runtimeFailureResult(
        InputDeliveryError(
            message: "AX decrement returned success, but scalar AX value readback remained unchanged",
            inputEventsEmitted: true,
            failureCode: "readback_unchanged",
            partialInputPossible: false,
            readbackSampled: true,
            readbackVerified: false,
            actionRoute: "ax.action.decrement"
        )
    )

    #expect(result["ok"] as? Bool == false)
    #expect(result["failureCode"] as? String == "readback_unchanged")
    #expect(result["failurePhase"] as? String == "emission")
    #expect(result["input_events_emitted"] as? Bool == true)
    #expect(result["partial_input_possible"] as? Bool == false)
    #expect(result["mutation_unknown"] as? Bool == true)
    let evidence = result["actionEvidence"] as? [String: Any]
    #expect(evidence?["input_emitted"] as? Bool == true)
    #expect(evidence?["readback_sampled"] as? Bool == true)
    #expect(evidence?["readback_verified"] as? Bool == false)
    #expect(evidence?["route"] as? String == "ax.action.decrement")
}
