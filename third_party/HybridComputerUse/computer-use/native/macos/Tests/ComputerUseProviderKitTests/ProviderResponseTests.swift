@testable import ComputerUseProviderKit
import Foundation
import Testing

@Test
func providerResetRequirementIsPromotedToEnvelope() {
    let response = ProviderResponseBuilder.envelope(
        requestID: "reset-1",
        operation: "desktop_execute_batch",
        runtimeResult: [
            "ok": false,
            "error": "capture timed out",
            "failureCode": "state_unavailable",
            "providerResetRequired": true,
        ]
    )

    #expect(response["provider_reset_required"] as? Bool == true)
}

@Test
func providerServerStopsAfterWritingResetRequiredResponse() async throws {
    var requests = ["first", "second"]
    var responses: [[String: Any]] = []

    try await ProviderServer.runLoop(
        readLine: {
            requests.isEmpty ? nil : requests.removeFirst()
        },
        writeLine: { data in
            let object = try JSONSerialization.jsonObject(with: data)
            responses.append(object as! [String: Any])
        },
        handle: { line in
            [
                "request": line,
                "provider_reset_required": true,
            ]
        }
    )

    #expect(requests == ["second"])
    #expect(responses.count == 1)
    #expect(responses[0]["request"] as? String == "first")
}

@Test
func requestAndResponsePayloadNamesRemainDistinct() {
    let expected = [
        "observe_apps": ("observe_apps", "accessibility"),
        "observe": ("observe", "accessibility"),
        "resolve_and_act": ("action", "accessibility"),
        "ordered_input": ("ordered_input", "ordered_input"),
        "desktop_execute_batch": ("desktop", "desktop"),
        "window_list": ("window_list", "window"),
        "window_action": ("window_action", "window"),
    ]
    for (operation, names) in expected {
        #expect(ProviderOperationAdapter.payloadName(for: operation) == names.0)
        #expect(ProviderResponseBuilder.payloadName(for: operation) == names.1)
    }
}

@Test
func orderedInputResponsePreservesTransactionEvidence() {
    let runtime: [String: Any] = [
        "contract_version": 1,
        "ok": false,
        "steps": [[
            "action_index": 2,
            "status": "failed",
            "dispatch_attempted": false,
            "emission": "none",
            "window_continuity": "changed",
        ]],
        "stopped_action_index": 2,
        "first_unattempted_action_index": 2,
        "failure": [
            "code": "stale_window_ref",
            "phase": "precondition",
            "facts_complete": true,
        ],
        "mutation": [
            "input_events_emitted": false,
            "partial_input_possible": false,
            "desktop_state_mutated": false,
            "mutation_unknown": false,
        ],
        "retry_same_request": false,
    ]
    let response = ProviderResponseBuilder.envelope(
        requestID: "ordered-stop",
        operation: "ordered_input",
        runtimeResult: runtime
    )
    let ordered = response["ordered_input"] as? [String: Any]
    #expect(ordered?["stopped_action_index"] as? Int == 2)
    #expect((ordered?["steps"] as? [[String: Any]])?.count == 1)
    #expect(response["accessibility"] == nil)
}

@Test
func orderedInputResetFlagStaysInEnvelopeOnly() {
    let response = ProviderResponseBuilder.envelope(
        requestID: "ordered-reset",
        operation: "ordered_input",
        runtimeResult: [
            "contract_version": 1,
            "ok": false,
            "steps": [],
            "failure": [
                "code": "state_unavailable",
                "phase": "observation",
                "facts_complete": true,
            ],
            "mutation": [:],
            "retry_same_request": false,
            "providerResetRequired": true,
        ]
    )
    let ordered = response["ordered_input"] as? [String: Any]
    #expect(response["provider_reset_required"] as? Bool == true)
    #expect(ordered?["providerResetRequired"] == nil)
}

@Test
func actionAdapterAlonePropagatesPrivateDiagnostics() throws {
    let diagnostics: [String: Any] = ["phase_timing": true]
    let action = try ProviderOperationAdapter.operation(
        operation: "resolve_and_act",
        payload: [
            "intent": "keypress",
            "app": "TextEdit",
            "input": ["key": "ENTER"],
            "diagnostics": diagnostics,
        ]
    )
    let propagated = action["diagnostics"] as? [String: Any]
    #expect(propagated?["phase_timing"] as? Bool == true)

    let observe = try ProviderOperationAdapter.operation(
        operation: "observe",
        payload: [
            "app": "TextEdit",
            "diagnostics": diagnostics,
        ]
    )
    #expect(observe["diagnostics"] == nil)

    let desktop = try ProviderOperationAdapter.operation(
        operation: "desktop_execute_batch",
        payload: [
            "actions": [["type": "screenshot"]],
            "diagnostics": diagnostics,
        ]
    )
    #expect(desktop["diagnostics"] == nil)
}

@Test
func defaultAccessibilityResponseDoesNotAddScreenshot() {
    let response = ProviderResponseBuilder.envelope(
        requestID: "observe-default",
        operation: "observe",
        runtimeResult: [
            "ok": true,
            "snapshot": [
                "app": ["name": "TextEdit", "pid": 42],
                "treeLines": ["window"],
            ],
        ]
    )
    let accessibility = response["accessibility"] as? [String: Any]
    let snapshot = accessibility?["snapshot"] as? [String: Any]
    #expect(snapshot?["screenshotPngBase64"] == nil)
}

@Test
func explicitAccessibilityScreenshotIsPreservedInline() {
    let response = ProviderResponseBuilder.envelope(
        requestID: "observe-screenshot",
        operation: "observe",
        runtimeResult: [
            "ok": true,
            "snapshot": [
                "app": ["name": "TextEdit", "pid": 42],
                "screenshotPngBase64": "iVBORw0KGgo=",
                "screenshotMimeType": "image/png",
                "screenshotEncoding": "png",
            ],
        ]
    )
    let accessibility = response["accessibility"] as? [String: Any]
    let snapshot = accessibility?["snapshot"] as? [String: Any]
    #expect(snapshot?["screenshotPngBase64"] as? String == "iVBORw0KGgo=")
    #expect(snapshot?["screenshotBlob"] == nil)
}

@Test
func guiResponseKeepsFinalScreenshot() {
    let response = ProviderResponseBuilder.envelope(
        requestID: "gui-final",
        operation: "desktop_execute_batch",
        runtimeResult: [
            "ok": true,
            "snapshot": [
                "app": ["name": "", "pid": 0],
                "screenshotPngBase64": "iVBORw0KGgo=",
            ],
            "guiResult": ["dispatched": 1],
        ]
    )
    let desktop = response["desktop"] as? [String: Any]
    let snapshot = desktop?["snapshot"] as? [String: Any]
    #expect(snapshot?["screenshotPngBase64"] as? String == "iVBORw0KGgo=")
}

@Test
func failureAndMutationAreSeparateObjects() {
    let result = ProviderResponseBuilder.domainResult(
        [
            "ok": false,
            "error": "transport lost",
            "failureCode": "runtime_transport_lost",
            "failurePhase": "emission",
            "failureFactsComplete": true,
            "input_events_emitted": true,
            "partial_input_possible": true,
            "desktop_state_mutated": true,
            "mutation_unknown": true,
        ],
        operation: "resolve_and_act"
    )
    let failure = result["failure"] as? [String: Any]
    let mutation = result["mutation"] as? [String: Any]
    #expect(failure?["code"] as? String == "runtime_transport_lost")
    #expect(failure?["input_events_emitted"] == nil)
    #expect(mutation?["input_events_emitted"] as? Bool == true)
    #expect(mutation?["mutation_unknown"] as? Bool == true)
}

@Test
func readbackMismatchCodeSurvivesProviderResponse() {
    let response = ProviderResponseBuilder.domainResult(
        [
            "ok": false,
            "error": "exact AX value readback did not match",
            "failureCode": "readback_mismatch",
            "failurePhase": "emission",
            "failureFactsComplete": true,
            "input_events_emitted": true,
            "partial_input_possible": false,
            "desktop_state_mutated": true,
            "mutation_unknown": true,
        ],
        operation: "resolve_and_act"
    )
    let failure = response["failure"] as? [String: Any]
    let mutation = response["mutation"] as? [String: Any]
    #expect(failure?["code"] as? String == "readback_mismatch")
    #expect(failure?["phase"] as? String == "emission")
    #expect(mutation?["input_events_emitted"] as? Bool == true)
    #expect(mutation?["mutation_unknown"] as? Bool == true)
}
