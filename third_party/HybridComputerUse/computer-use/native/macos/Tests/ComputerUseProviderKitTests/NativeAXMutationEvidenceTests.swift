import ApplicationServices
import Foundation
import Testing
@testable import ComputerUseProviderKit

@Test
func nativeMutationCanActBeforeItsReplyFails() {
    for route in ["ax.attribute.value.set", "ax.window.close"] {
        var mutated = false
        var attempts = 0
        do {
            try performNativeAXMutation(operation: "injected mutation", route: route) {
                attempts += 1
                mutated = true
                return .cannotComplete
            }
            Issue.record("A failed reply must stop the operation")
        } catch {
            let result = runtimeFailureResult(error)
            #expect(result["failureCode"] as? String == "effect_unknown")
            #expect(result["failurePhase"] as? String == "emission")
            #expect(result["input_events_emitted"] as? Bool == true)
            #expect(result["partial_input_possible"] as? Bool == true)
            #expect(result["mutation_unknown"] as? Bool == true)
            let evidence = result["actionEvidence"] as? [String: Any]
            #expect(evidence?["route"] as? String == route)
            #expect(evidence?["readback_sampled"] as? Bool == false)
            #expect((result["error"] as? String ?? "").contains("Observe the current state before retrying"))
        }
        #expect(mutated)
        #expect(attempts == 1)
    }
}

@Test
func nativeRejectionsPreserveNoInputUnlessAnEarlierActionSucceeded() {
    for status in [AXError.illegalArgument, .invalidUIElement, .attributeUnsupported,
                   .actionUnsupported, .notImplemented, .apiDisabled] {
        for earlierInput in [false, true] {
            let error = nativeAXActionAttemptError(
                action: "AXPress", result: status,
                earlierInputEmitted: earlierInput
            )
            let result = runtimeFailureResult(error)
            #expect(result["input_events_emitted"] as? Bool == earlierInput)
            #expect(result["partial_input_possible"] as? Bool == earlierInput)
            #expect(result["mutation_unknown"] as? Bool == earlierInput)
            #expect(result["failurePhase"] as? String == (earlierInput ? "emission" : "precondition"))
        }
    }
}

@Test
func nativeUnknownFailuresCannotProveNoInput() {
    for status in [AXError.cannotComplete, .failure, .noValue, .notEnoughPrecision] {
        let result = runtimeFailureResult(nativeAXActionAttemptError(
            action: "AXPress", result: status
        ))
        #expect(result["failureCode"] as? String == "effect_unknown")
        #expect(result["input_events_emitted"] as? Bool == true)
        #expect(result["partial_input_possible"] as? Bool == true)
    }
}

@Test
func successfulNativeMutationRunsOnlyOnce() throws {
    var attempts = 0
    try performNativeAXMutation(operation: "injected write", route: "ax.attribute.value.set") {
        attempts += 1
        return .success
    }
    #expect(attempts == 1)
}
