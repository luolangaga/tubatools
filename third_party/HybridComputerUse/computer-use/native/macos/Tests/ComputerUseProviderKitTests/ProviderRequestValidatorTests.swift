@testable import ComputerUseProviderKit
import Foundation
import Testing

private func envelope(
    operation: String,
    payloadName: String,
    payload: [String: Any]
) -> [String: Any] {
    [
        "protocol": "computer-use-provider",
        "schema_version": 2,
        "request_id": "validation-test",
        "operation": operation,
        payloadName: payload,
    ]
}

@Test func semanticWriteConstraintIsWindowsOnly() throws {
    let values: [Any] = [false, true, "true", 1]
    for value in values {
        let request = envelope(operation: "resolve_and_act", payloadName: "action", payload: [
            "protocol_version": 2, "intent": "set_value", "app": "TextEdit",
            "target": ["providerObjectPath": "document"],
            "input": ["value": "replacement", "require_semantic_write": value],
        ])
        if let flag = value as? Bool, type(of: value) == Bool.self, !flag {
            try ProviderRequestValidator.validate(request)
        } else {
            #expect(throws: (any Error).self) { try ProviderRequestValidator.validate(request) }
        }
    }
}

@Test func selectWirePreservesTextAndRangeEnd() async throws {
    let inputs: [[String: Any]] = [[:], ["text": "😀中文"], ["to_target": ["providerObjectPath": "end"]]]
    for input in inputs {
        let request = envelope(operation: "resolve_and_act", payloadName: "action", payload: [
            "protocol_version": 2, "intent": "select", "app": "TextEdit",
            "target": ["providerObjectPath": "start"], "input": input,
        ])
        try ProviderRequestValidator.validate(request)
        var dispatched: [[String: Any]] = []
        let server = ProviderServer(
            transport: ProviderLineTransport(inputDescriptor: -1, outputDescriptor: -1),
            executeRuntime: { operation in
                dispatched.append(operation)
                return ["ok": true, "observationSkipped": true]
            }
        )
        _ = await server.handle(line: String(decoding: try JSONSerialization.data(withJSONObject: request), as: UTF8.self))
        let operation = try #require(dispatched.first)
        #expect(operation["tool"] as? String == "select")
        for (key, value) in input {
            #expect(NSDictionary(dictionary: [key: operation[key]!]).isEqual(to: [key: value]))
        }
    }
}

@Test
func allSevenProviderOperationsAreAccepted() throws {
    let identity: [String: Any] = [
        "providerBusName": "mac:42",
        "processGeneration": "123456",
    ]
    let requests: [[String: Any]] = [
        envelope(
            operation: "observe_apps",
            payloadName: "observe_apps",
            payload: ["protocol_version": 2]
        ),
        envelope(
            operation: "observe",
            payloadName: "observe",
            payload: [
                "protocol_version": 2,
                "app": "TextEdit",
                "app_identity": identity,
            ]
        ),
        envelope(
            operation: "resolve_and_act",
            payloadName: "action",
            payload: [
                "protocol_version": 2,
                "intent": "resolve_target",
                "app": "TextEdit",
                "target": [
                    "selector": "role=button",
                    "locatorKind": "exact",
                    "providerRoleAliases": ["AXButton"],
                    "providerIdentityScope": "process",
                    "runtimeId": [4],
                    "runtimeAliases": [[3]],
                    "hasName": false,
                ] as [String: Any],
            ]
        ),
        envelope(
            operation: "ordered_input",
            payloadName: "ordered_input",
            payload: orderedInputValidationPayload()
        ),
        envelope(
            operation: "desktop_execute_batch",
            payloadName: "desktop",
            payload: [
                "protocol_version": 2,
                "actions": [["type": "screenshot"]],
                "settle_ms": 0,
            ]
        ),
        envelope(
            operation: "window_list",
            payloadName: "window_list",
            payload: [
                "protocol_version": 2,
                "app": "TextEdit",
                "app_identity": identity,
            ]
        ),
        envelope(
            operation: "window_action",
            payloadName: "window_action",
            payload: [
                "protocol_version": 2,
                "action": "activate",
                "app": "TextEdit",
                "target": [
                    "selector": "role=window",
                    "windowHandle": "w2:mac:42:123456:7",
                    "hasName": false,
                ] as [String: Any],
            ]
        ),
    ]
    for request in requests {
        try ProviderRequestValidator.validate(request)
    }
}

private func orderedInputValidationPayload() -> [String: Any] {
    let handle = "w2:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    return [
        "protocol_version": 2,
        "contract_version": 1,
        "app": "TextEdit",
        "app_identity": [
            "providerBusName": "mac:42",
            "processGeneration": "123456",
        ],
        "window_handle": handle,
        "expected_window": [
            "windowHandle": handle,
            "providerBusName": "mac:42",
            "providerObjectPath": "window:7",
            "processGeneration": "123456",
            "role": "window",
            "name": "Document",
        ],
        "steps": [
            ["action_index": 3, "key": "DOWN"],
            ["action_index": 4, "key": "ENTER"],
        ],
        "final_observation": ["capture_snapshot": true],
    ]
}

@Test
func orderedInputValidatesEveryKeyBeforeExecution() throws {
    var punctuation = orderedInputValidationPayload()
    punctuation["steps"] = [
        ["action_index": 3, "key": "SUPER+,"],
    ]
    try ProviderRequestValidator.validate(
        envelope(
            operation: "ordered_input",
            payloadName: "ordered_input",
            payload: punctuation
        )
    )

    var invalid = orderedInputValidationPayload()
    invalid["steps"] = [
        ["action_index": 3, "key": "SUPER+NOT_A_REAL_KEY"],
    ]
    #expect(throws: ComputerUseError.self) {
        try ProviderRequestValidator.validate(
            envelope(
                operation: "ordered_input",
                payloadName: "ordered_input",
                payload: invalid
            )
        )
    }
}

@Test(arguments: [
    "resolve_target", "click", "scroll", "type", "keypress", "set_value",
    "increment", "decrement",
])
func allAccessibilityIntentsAreAccepted(_ intent: String) throws {
    var payload: [String: Any] = [
        "protocol_version": 2,
        "intent": intent,
        "app": "TextEdit",
        "window_handle": "w2:mac:42:123456:7",
    ]
    if [
        "resolve_target", "click", "set_value", "increment", "decrement",
    ].contains(intent) {
        payload["target"] = [
            "selector": "role=text",
            "hasName": false,
        ]
    }
    switch intent {
    case "click":
        payload["input"] = ["click_count": 1]
    case "scroll":
        payload["input"] = ["direction": "down", "pages": 1]
    case "type":
        payload["input"] = ["text": "Ada"]
    case "keypress":
        payload["input"] = ["key": "CMD+A"]
    case "set_value":
        payload["input"] = ["value": ""]
    default:
        break
    }
    try ProviderRequestValidator.validate(
        envelope(
            operation: "resolve_and_act",
            payloadName: "action",
            payload: payload
        )
    )
}

@Test
func unsupportedOperationIsRejected() {
    #expect(throws: ComputerUseError.self) {
        try ProviderRequestValidator.validate(
            envelope(
                operation: "unsupported_operation",
                payloadName: "unsupported_operation",
                payload: ["protocol_version": 2]
            )
        )
    }
}

@Test
func unsupportedAccessibilityIntentIsRejected() {
    #expect(throws: ComputerUseError.self) {
        try ProviderRequestValidator.validate(
            envelope(
                operation: "resolve_and_act",
                payloadName: "action",
                payload: [
                    "protocol_version": 2,
                    "intent": "unsupported_intent",
                    "app": "TextEdit",
                    "target": [
                        "selector": "role=button",
                        "hasName": false,
                    ],
                ]
            )
        )
    }
}
