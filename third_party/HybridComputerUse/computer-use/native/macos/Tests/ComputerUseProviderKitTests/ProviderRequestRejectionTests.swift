@testable import ComputerUseProviderKit
import Foundation
import Testing

private func rejectionTestRequest(_ operation: String) -> [String: Any] {
    let handle = "w2:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    let identity = [
        "providerBusName": "mac:42",
        "processGeneration": "123456",
    ]
    var payload: [String: Any] = ["protocol_version": 2]
    switch operation {
    case "observe", "window_list":
        payload["app"] = "Test App"
    case "resolve_and_act":
        payload["app"] = "Test App"
        payload["intent"] = "keypress"
        payload["window_handle"] = handle
        payload["input"] = ["key": "ENTER"]
    case "ordered_input":
        payload["contract_version"] = 1
        payload["app"] = "Test App"
        payload["app_identity"] = identity
        payload["window_handle"] = handle
        payload["expected_window"] = [
            "windowHandle": handle,
            "providerBusName": "mac:42",
            "processGeneration": "123456",
            "providerObjectPath": "window:7",
            "role": "window",
            "name": "Document",
        ]
        payload["steps"] = [
            ["action_index": 3, "key": "DOWN"],
            ["action_index": 4, "key": "ENTER"],
        ]
        payload["final_observation"] = ["capture_snapshot": true]
    case "desktop_execute_batch":
        payload["actions"] = [["type": "screenshot"]]
    case "window_action":
        payload["app"] = "Test App"
        payload["action"] = "activate"
        payload["target"] = [
            "selector": "role=window",
            "windowHandle": handle,
            "hasName": false,
        ] as [String: Any]
    default:
        break
    }
    return [
        "protocol": "computer-use-provider",
        "schema_version": 2,
        "request_id": " \tvalidation-\(operation)\n",
        "operation": operation,
        ProviderOperationAdapter.payloadName(for: operation): payload,
    ]
}

private func requestLine(_ request: [String: Any]) throws -> String {
    String(
        decoding: try JSONSerialization.data(withJSONObject: request),
        as: UTF8.self
    )
}

private func rejectionTestServer(
    executeRuntime: @escaping ([String: Any]) async -> [String: Any]
) -> ProviderServer {
    ProviderServer(
        transport: ProviderLineTransport(
            inputDescriptor: -1,
            outputDescriptor: -1
        ),
        executeRuntime: executeRuntime
    )
}

private func expectRequestRejection(
    _ request: [String: Any],
    messageContains: String
) async throws {
    var dispatchCount = 0
    let server = rejectionTestServer { _ in
        dispatchCount += 1
        return ["ok": true]
    }
    let response = await server.handle(line: try requestLine(request))
    #expect(dispatchCount == 0)
    #expect(Set(response.keys) == [
        "protocol", "schema_version", "request_id", "operation",
        "request_rejection",
    ])
    #expect(response["protocol"] as? String == "computer-use-provider")
    #expect(response["schema_version"] as? Int == 2)
    #expect(response["request_id"] as? String == request["request_id"] as? String)
    #expect(response["operation"] as? String == request["operation"] as? String)
    let rejection = try #require(response["request_rejection"] as? [String: Any])
    #expect(Set(rejection.keys) == [
        "code", "phase", "dispatch_attempted", "message",
    ])
    #expect(rejection["code"] as? String == "invalid_arguments")
    #expect(rejection["phase"] as? String == "request_validation")
    #expect(rejection["dispatch_attempted"] as? Bool == false)
    let message = try #require(rejection["message"] as? String)
    #expect(message.contains(messageContains))
    do {
        try ProviderRequestValidator.validate(request)
        Issue.record("Expected the request to fail before runtime dispatch")
    } catch {
        let expected = (error as? LocalizedError)?.errorDescription
            ?? String(describing: error)
        #expect(message == expected)
    }
}

@Test(arguments: ProviderRequestValidator.operations)
func knownOperationValidationFailuresRemainCorrelated(_ operation: String) async throws {
    var request = rejectionTestRequest(operation)
    try ProviderRequestValidator.validate(request)
    let payloadName = ProviderOperationAdapter.payloadName(for: operation)
    var payload = try #require(request[payloadName] as? [String: Any])
    let message: String
    switch operation {
    case "observe_apps":
        payload["protocol_version"] = 3
        message = ".protocol_version"
    case "observe", "window_list":
        payload["app"] = " \n"
        message = ".app"
    case "resolve_and_act":
        payload["intent"] = "unsupported_intent"
        message = "unsupported provider accessibility intent"
    case "ordered_input":
        payload["steps"] = "DOWN"
        message = ".steps"
    case "desktop_execute_batch":
        payload["actions"] = []
        message = ".actions"
    default:
        payload["action"] = "unsupported_action"
        message = "unsupported provider window action"
    }
    request[payloadName] = payload
    try await expectRequestRejection(request, messageContains: message)
}

@Test(arguments: ProviderRequestValidator.operations)
func trustedHeadersSurviveEnvelopePayloadErrors(_ operation: String) async throws {
    let original = rejectionTestRequest(operation)
    let payloadName = ProviderOperationAdapter.payloadName(for: operation)
    var request = original
    request.removeValue(forKey: payloadName)
    try await expectRequestRejection(request, messageContains: "exactly its operation payload")
    request = original
    request[payloadName] = "not an object"
    try await expectRequestRejection(request, messageContains: "must be an object")
    request = original
    request[payloadName] = ["protocol_version": 1]
    try await expectRequestRejection(request, messageContains: "protocol_version")
    request = original
    request[operation == "observe_apps" ? "observe" : "observe_apps"] = [
        "protocol_version": 2,
    ]
    try await expectRequestRejection(request, messageContains: "exactly its operation payload")
}

@Test(arguments: ["SUPER+NOT_A_REAL_KEY", "NOT_A_MODIFIER+ENTER", "ENTER+TAB"])
func unsupportedKeysRejectBeforeAnyRuntimeDispatch(_ key: String) async throws {
    for operation in ["resolve_and_act", "ordered_input"] {
        var request = rejectionTestRequest(operation)
        try ProviderRequestValidator.validate(request)
        let payloadName = ProviderOperationAdapter.payloadName(for: operation)
        var payload = try #require(request[payloadName] as? [String: Any])
        if operation == "resolve_and_act" {
            payload["input"] = ["key": key]
        } else {
            // A valid earlier step must not execute when the final key is invalid.
            payload["steps"] = [
                ["action_index": 3, "key": "DOWN"],
                ["action_index": 4, "key": key],
            ]
        }
        request[payloadName] = payload
        try await expectRequestRejection(request, messageContains: "unsupported")
    }
}

@Test
func untrustedHeadersKeepTheHistoricalInvalidEnvelope() async throws {
    let invalidHeaders: [(String, Any)] = [
        ("protocol", NSNull()), ("protocol", 2), ("protocol", "computer-use-provider-v1"),
        ("protocol", " computer-use-provider"), ("protocol", ["computer-use-provider"]),
        ("schema_version", NSNull()), ("schema_version", true),
        ("schema_version", "2"), ("schema_version", 1),
        ("schema_version", 2.5), ("schema_version", ["version": 2]),
        ("request_id", NSNull()), ("request_id", 42), ("request_id", ""),
        ("request_id", " \t\n"), ("request_id", ["id"]),
        ("operation", NSNull()), ("operation", 42), ("operation", ""),
        ("operation", " \n"), ("operation", "unknown"),
        ("operation", "observe_apps "), ("operation", "OBSERVE_APPS"),
        ("operation", ["observe_apps"]),
    ]
    var requests: [[String: Any]] = []
    for (field, value) in invalidHeaders {
        var request = rejectionTestRequest("observe_apps")
        request[field] = value
        requests.append(request)
    }
    for field in ["protocol", "schema_version", "request_id", "operation"] {
        var request = rejectionTestRequest("observe_apps")
        request.removeValue(forKey: field)
        requests.append(request)
    }
    var dispatchCount = 0
    let server = rejectionTestServer { _ in
        dispatchCount += 1
        return ["ok": true]
    }
    for var request in requests {
        // The payload error can precede header checks; it cannot make them trusted.
        request["unexpected"] = true
        let response = await server.handle(line: try requestLine(request))
        do {
            try ProviderRequestValidator.validate(request)
            Issue.record("Expected invalid headers or payload to fail validation")
        } catch {
            let expected = ProviderResponseBuilder.invalidEnvelope(error)
            #expect(NSDictionary(dictionary: response).isEqual(to: expected))
        }
        #expect(response["request_id"] as? String == "invalid")
        #expect(response["operation"] as? String == "observe_apps")
        #expect(response["request_rejection"] == nil)
        #expect(response["provider_reset_required"] == nil)
    }
    #expect(dispatchCount == 0)
}

@Test
func desktopRuntimeKeyParsingFailureIsNotARequestRejection() async throws {
    var request = rejectionTestRequest("desktop_execute_batch")
    request["desktop"] = [
        "protocol_version": 2,
        "actions": [["type": "keypress", "keys": ["SUPER", "NOT_A_REAL_KEY"]]],
    ] as [String: Any]
    // Desktop validation checks array shape; native key parsing occurs in runtime.
    try ProviderRequestValidator.validate(request)
    var dispatchCount = 0
    let server = rejectionTestServer { operation in
        dispatchCount += 1
        let actions = operation["actions"] as? [[String: Any]]
        let keys = actions?.first?["keys"] as? [String] ?? []
        do {
            _ = try KeyPressParser.parse(keys.joined(separator: "+"))
            Issue.record("Expected unsupported native key")
            return ["ok": true]
        } catch {
            return runtimeFailureResult(error)
        }
    }
    let response = await server.handle(line: try requestLine(request))
    #expect(dispatchCount == 1)
    #expect(response["request_rejection"] == nil)
    #expect(response["request_id"] as? String == request["request_id"] as? String)
    #expect(response["operation"] as? String == "desktop_execute_batch")
    let payload = try #require(response["desktop"] as? [String: Any])
    #expect(payload["ok"] as? Bool == false)
    #expect((payload["error"] as? String)?.contains("unsupported key") == true)
}

@Test(arguments: ["{", "[]", "null", "\"request\"", "42", "{}", "{\"protocol\":"])
func malformedRequestsDoNotInventCorrelation(_ line: String) async {
    var dispatchCount = 0
    let server = rejectionTestServer { _ in
        dispatchCount += 1
        return ["ok": true]
    }
    let response = await server.handle(line: line)
    #expect(dispatchCount == 0)
    #expect(Set(response.keys) == [
        "protocol", "schema_version", "request_id", "operation", "accessibility",
    ])
    #expect(response["request_id"] as? String == "invalid")
    #expect(response["operation"] as? String == "observe_apps")
    let payload = response["accessibility"] as? [String: Any]
    #expect(payload?["ok"] as? Bool == false)
}

@Test(arguments: ProviderRequestValidator.operations)
func dispatchedRuntimeFailuresAreNeverRequestRejections(_ operation: String) async throws {
    var runtime = runtimeFailureResult(ComputerUseError.invalidArguments("runtime-only error"))
    runtime["providerResetRequired"] = true
    runtime["input_events_emitted"] = true
    runtime["partial_input_possible"] = true
    var dispatched: [[String: Any]] = []
    let server = rejectionTestServer {
        dispatched.append($0)
        return runtime
    }
    let request = rejectionTestRequest(operation)
    let response = await server.handle(line: try requestLine(request))
    #expect(dispatched.count == 1)
    #expect(dispatched.first?["_diagnostic_request_id"] as? String
        == request["request_id"] as? String)
    #expect(response["request_rejection"] == nil)
    #expect(response["provider_reset_required"] as? Bool == true)
    let expected = ProviderResponseBuilder.envelope(
        requestID: try #require(request["request_id"] as? String),
        operation: operation,
        runtimeResult: runtime
    )
    #expect(NSDictionary(dictionary: response).isEqual(to: expected))
}

@Test
func requestRejectionDoesNotResetOrStopTheServerLoop() async throws {
    var invalid = rejectionTestRequest("ordered_input")
    var invalidPayload = try #require(invalid["ordered_input"] as? [String: Any])
    invalidPayload["steps"] = [["action_index": 3, "key": "NOT_A_REAL_KEY"]]
    invalid["ordered_input"] = invalidPayload
    let valid = rejectionTestRequest("observe_apps")
    var lines = [try requestLine(invalid), try requestLine(valid)]
    var responses: [[String: Any]] = []
    var dispatched: [[String: Any]] = []
    let server = rejectionTestServer {
        dispatched.append($0)
        return ["ok": true, "apps": []]
    }
    try await ProviderServer.runLoop(
        readLine: { lines.isEmpty ? nil : lines.removeFirst() },
        writeLine: { data in
            let object = try JSONSerialization.jsonObject(with: data)
            responses.append(try #require(object as? [String: Any]))
        },
        handle: { await server.handle(line: $0) }
    )
    #expect(lines.isEmpty)
    #expect(dispatched.count == 1)
    #expect(dispatched.first?["tool"] as? String == "list_apps")
    try #require(responses.count == 2)
    #expect(responses[0]["request_rejection"] != nil)
    #expect(responses[0]["provider_reset_required"] == nil)
    #expect(responses[1]["request_id"] as? String == valid["request_id"] as? String)
    let payload = responses[1]["accessibility"] as? [String: Any]
    #expect(payload?["ok"] as? Bool == true)
}

@Test(arguments: ["observe", "ordered_input"])
func windowScreenshotRequestsAreRejectedBeforeDispatch(_ operation: String) async throws {
    var request = rejectionTestRequest(operation)
    let payloadName = ProviderOperationAdapter.payloadName(for: operation)
    var payload = try #require(request[payloadName] as? [String: Any])
    let field = operation == "observe" ? "observation" : "final_observation"
    payload[field] = ["capture_snapshot": true, "include_screenshot": false]
    request[payloadName] = payload
    try ProviderRequestValidator.validate(request)
    payload[field] = ["capture_snapshot": true, "include_screenshot": true]
    request[payloadName] = payload
    try await expectRequestRejection(
        request,
        messageContains: "include_screenshot is not supported"
    )
}
