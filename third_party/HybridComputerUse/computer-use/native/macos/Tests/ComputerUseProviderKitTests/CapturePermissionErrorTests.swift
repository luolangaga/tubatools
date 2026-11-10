import Foundation
import ScreenCaptureKit
import Testing
@testable import ComputerUseProviderKit

@Test
func screenCaptureDenialPreservesGUIInputEvidence() {
    for emitted in [false, true] {
        let result = guiFinalObservationResult(
            actionCount: 2,
            inputActionsDispatched: emitted ? 1 : 0,
            emitted: emitted
        ) {
            throw NSError(
                domain: SCStreamErrorDomain,
                code: SCStreamError.Code.userDeclined.rawValue,
                userInfo: [NSLocalizedDescriptionKey: "用户拒绝了录屏权限"]
            )
        }
        #expect(result["ok"] as? Bool == false)
        #expect(result["failureCode"] as? String == "permission_denied")
        #expect(result["failurePhase"] as? String == (emitted ? "observation" : "precondition"))
        #expect(result["input_events_emitted"] as? Bool == emitted)
        #expect(result["partial_input_possible"] as? Bool == emitted)
        #expect(result["mutation_unknown"] as? Bool == emitted)
        #expect(result["input_actions_dispatched"] as? Int == (emitted ? 1 : 0))
        #expect(result["snapshot"] == nil)
        #expect(result["providerResetRequired"] == nil)
        let message = result["error"] as? String ?? ""
        #expect(message.contains("Screen Recording"))
        #expect(message.contains("Computer Use Provider"))
        #expect(message.contains("restart"))
    }
}

@Test
func screenCaptureDenialIsPermissionDeniedAtRuntimeBoundary() {
    let result = runtimeFailureResult(NSError(
        domain: SCStreamErrorDomain,
        code: SCStreamError.Code.userDeclined.rawValue
    ))
    #expect(result["failureCode"] as? String == "permission_denied")
    #expect(result["input_events_emitted"] as? Bool == false)
    #expect(result["partial_input_possible"] as? Bool == false)
    #expect(result["mutation_unknown"] as? Bool == false)
}

@Test
func capturePermissionClassificationRequiresDomainAndCode() {
    for error in [
        NSError(domain: "other.capture", code: SCStreamError.Code.userDeclined.rawValue),
        NSError(domain: SCStreamErrorDomain, code: SCStreamError.Code.failedToStart.rawValue),
        NSError(domain: SCStreamErrorDomain, code: SCStreamError.Code.missingEntitlements.rawValue),
        NSError(domain: "other.capture", code: 1, userInfo: [
            NSLocalizedDescriptionKey: "The user declined TCCs for application, window, display capture",
        ]),
    ] {
        let result = guiFinalObservationResult(
            actionCount: 1, inputActionsDispatched: 0, emitted: false
        ) { throw error }
        #expect(result["failureCode"] as? String == "gui_snapshot_failed")
        #expect((result["error"] as? String ?? "").contains(error.domain))
        #expect(runtimeFailureResult(error)["failureCode"] as? String == "provider_error")
    }
}
