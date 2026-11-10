import Foundation

public final class ProviderServer {
    private let executeRuntime: ([String: Any]) async -> [String: Any]
    private let transport: ProviderLineTransport

    public init(
        transport: ProviderLineTransport,
        bridge: ComputerUseRuntimeBridge = ComputerUseRuntimeBridge()
    ) {
        self.transport = transport
        self.executeRuntime = { await bridge.execute($0) }
    }

    init(
        transport: ProviderLineTransport,
        executeRuntime: @escaping ([String: Any]) async -> [String: Any]
    ) {
        self.transport = transport
        self.executeRuntime = executeRuntime
    }

    public func run() async {
        do {
            try await Self.runLoop(
                readLine: { try self.transport.readLine() },
                writeLine: { try self.transport.writeLine($0) },
                handle: { await self.handle(line: $0) }
            )
        } catch {
            fputs("computer-use provider transport failed: \(error)\n", stderr)
        }
    }

    static func runLoop(
        readLine: () throws -> String?,
        writeLine: (Data) throws -> Void,
        handle: (String) async -> [String: Any]
    ) async throws {
        while let line = try readLine() {
            let response = await handle(line)
            let data = try JSONSerialization.data(
                withJSONObject: response,
                options: [.sortedKeys]
            )
            try writeLine(data)
            if response["provider_reset_required"] as? Bool == true {
                return
            }
        }
    }

    public func handle(line: String) async -> [String: Any] {
        let request: [String: Any]
        do {
            let data = Data(line.utf8)
            guard let object = try JSONSerialization.jsonObject(with: data)
                as? [String: Any] else {
                throw ComputerUseError.invalidArguments(
                    "request must be a JSON object"
                )
            }
            request = object
        } catch {
            return ProviderResponseBuilder.invalidEnvelope(error)
        }
        do {
            try ProviderRequestValidator.validate(request)
        } catch {
            return requestRejection(request, error: error)
        }
        return await dispatch(request)
    }

    private func requestRejection(
        _ request: [String: Any],
        error: Error
    ) -> [String: Any] {
        // Correlate only trusted headers, independently of payload validation.
        guard request["protocol"] as? String == "computer-use-provider",
              request["schema_version"] as? Int == 2,
              let requestID = request["request_id"] as? String,
              !requestID.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              let operation = request["operation"] as? String,
              ProviderRequestValidator.operations.contains(operation)
        else {
            return ProviderResponseBuilder.invalidEnvelope(error)
        }
        return [
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": requestID,
            "operation": operation,
            "request_rejection": [
                "code": "invalid_arguments",
                "phase": "request_validation",
                "dispatch_attempted": false,
                "message": (error as? LocalizedError)?.errorDescription
                    ?? String(describing: error),
            ] as [String: Any],
        ]
    }

    private func dispatch(_ request: [String: Any]) async -> [String: Any] {
        let requestID = request["request_id"] as! String
        let operation = request["operation"] as! String
        let payloadName = ProviderOperationAdapter.payloadName(
            for: operation
        )
        let payload = request[payloadName] as! [String: Any]
        var runtimeOperation: [String: Any]
        do {
            runtimeOperation = try ProviderOperationAdapter.operation(
                operation: operation,
                payload: payload
            )
        } catch {
            return requestRejection(request, error: error)
        }
        runtimeOperation["_diagnostic_request_id"] = requestID
        let runtimeResult = await executeRuntime(runtimeOperation)
        return ProviderResponseBuilder.envelope(
            requestID: requestID,
            operation: operation,
            runtimeResult: runtimeResult
        )
    }
}

public enum ProviderOperationAdapter {
    public static func payloadName(for operation: String) -> String {
        switch operation {
        case "observe_apps":
            return "observe_apps"
        case "observe":
            return "observe"
        case "resolve_and_act":
            return "action"
        case "ordered_input":
            return "ordered_input"
        case "desktop_execute_batch":
            return "desktop"
        case "window_list":
            return "window_list"
        case "window_action":
            return "window_action"
        default:
            return ""
        }
    }

    public static func operation(
        operation: String,
        payload: [String: Any]
    ) throws -> [String: Any] {
        switch operation {
        case "observe_apps":
            return ["tool": "list_apps"]
        case "observe":
            var result: [String: Any] = [
                "tool": "get_app_state",
                "app": payload["app"]!,
            ]
            copyIdentityAndObservation(payload, to: &result)
            return result
        case "resolve_and_act":
            let intent = payload["intent"] as! String
            var result: [String: Any] = [
                "tool": intent,
                "app": payload["app"]!,
            ]
            copyIdentityAndObservation(payload, to: &result)
            if let element = payload["element"] {
                result["element"] = element
            }
            if let expectedWindow = payload["expected_window"] {
                result["expectedWindow"] = expectedWindow
            }
            if let target = payload["target"] {
                result["semanticTarget"] = target
            }
            if let bounds = payload["window_bounds"] {
                result["windowBounds"] = bounds
            }
            if let diagnostics = payload["diagnostics"] {
                result["diagnostics"] = diagnostics
            }
            if let input = payload["input"] as? [String: Any] {
                for (key, value) in input {
                    result[key] = value
                }
            }
            return result
        case "ordered_input":
            return [
                "tool": "ordered_keypress",
                "orderedInput": payload,
            ]
        case "desktop_execute_batch":
            return [
                "tool": "gui",
                "actions": payload["actions"]!,
                "settle_ms": payload["settle_ms"] ?? 0,
                "frame_id": payload["frame_id"] ?? "",
            ]
        case "window_list":
            var result: [String: Any] = [
                "tool": "list_app_windows",
                "app": payload["app"]!,
            ]
            copyIdentity(payload, to: &result)
            return result
        case "window_action":
            var result: [String: Any] = [
                "tool": payload["action"]!,
                "app": payload["app"]!,
                "semanticTarget": payload["target"]!,
            ]
            copyIdentityAndObservation(payload, to: &result)
            return result
        default:
            throw ComputerUseError.invalidArguments(
                "unsupported provider operation \(operation)"
            )
        }
    }

    private static func copyIdentity(
        _ payload: [String: Any],
        to result: inout [String: Any]
    ) {
        if let identity = payload["app_identity"] {
            result["appIdentity"] = identity
        }
        if let handle = payload["window_handle"] {
            result["targetWindowHandle"] = handle
        }
    }

    private static func copyIdentityAndObservation(
        _ payload: [String: Any],
        to result: inout [String: Any]
    ) {
        copyIdentity(payload, to: &result)
        guard let observation = payload["observation"] as? [String: Any] else {
            return
        }
        let mapping = [
            "text_limit": "text_limit",
            "max_tree_nodes": "max_tree_nodes",
            "max_tree_depth": "max_tree_depth",
            "prune": "prune",
            "boxes": "boxes",
            "capture_snapshot": "captureSnapshot",
        ]
        for (source, destination) in mapping {
            if let value = observation[source] {
                result[destination] = value
            }
        }
    }
}

public enum ProviderResponseBuilder {
    private static let protocolName = "computer-use-provider"
    private static let schemaVersion = 2

    public static func payloadName(for operation: String) -> String {
        switch operation {
        case "observe_apps", "observe", "resolve_and_act":
            return "accessibility"
        case "ordered_input":
            return "ordered_input"
        case "desktop_execute_batch":
            return "desktop"
        case "window_list", "window_action":
            return "window"
        default:
            return "accessibility"
        }
    }

    public static func envelope(
        requestID: String,
        operation: String,
        runtimeResult: [String: Any]
    ) -> [String: Any] {
        var result: [String: Any] = [
            "protocol": protocolName,
            "schema_version": schemaVersion,
            "request_id": requestID,
            "operation": operation,
        ]
        if wireBool(runtimeResult["providerResetRequired"]) {
            result["provider_reset_required"] = true
        }
        if operation == "ordered_input" {
            var orderedResult = runtimeResult
            orderedResult.removeValue(forKey: "providerResetRequired")
            result["ordered_input"] = orderedResult
        } else {
            result[payloadName(for: operation)] = domainResult(
                runtimeResult,
                operation: operation
            )
        }
        return result
    }

    public static func invalidEnvelope(_ error: Error) -> [String: Any] {
        envelope(
            requestID: "invalid",
            operation: "observe_apps",
            runtimeResult: runtimeFailureResult(error)
        )
    }

    public static func domainResult(
        _ runtime: [String: Any],
        operation: String
    ) -> [String: Any] {
        var result: [String: Any] = ["ok": wireBool(runtime["ok"])]
        for key in [
            "text", "error", "notes", "snapshot", "apps", "inventory_issues",
            "resolved_element", "candidates", "candidate_count",
            "candidates_truncated",
            "external_desktop_transition",
        ] {
            if let value = runtime[key] {
                result[key] = value
            }
        }
        if let gui = runtime["guiResult"] {
            result["gui_result"] = gui
        }
        for (source, destination) in [
            ("appName", "app_name"),
            ("appPid", "app_pid"),
            ("windowTitles", "window_titles"),
            ("actionEvidence", "action_evidence"),
            ("observationSkipped", "observation_skipped"),
        ] {
            if let value = runtime[source] {
                result[destination] = value
            }
        }

        let mutation = mutationFacts(runtime)
        if !mutation.isEmpty {
            result["mutation"] = mutation
        }
        if !wireBool(runtime["ok"]) {
            result["failure"] = [
                "code": wireString(
                    runtime["failureCode"],
                    fallback: "provider_error"
                ),
                "underlying_code": wireString(runtime["underlyingFailureCode"]),
                "phase": wireString(
                    runtime["failurePhase"],
                    fallback: "precondition"
                ),
                "facts_complete": runtime["failureFactsComplete"] as? Bool
                    ?? true,
                "subject": wireString(runtime["failureSubject"]),
            ].filteringEmptyStrings()
        }
        return result
    }

    public static func mutationFacts(
        _ runtime: [String: Any]
    ) -> [String: Any] {
        let facts: [String: Bool] = [
            "input_events_emitted": wireBool(runtime["input_events_emitted"]),
            "partial_input_possible": wireBool(runtime["partial_input_possible"]),
            "desktop_state_mutated": wireBool(runtime["desktop_state_mutated"]),
            "mutation_unknown": wireBool(runtime["mutation_unknown"]),
        ]
        if facts.values.allSatisfy({ !$0 }) {
            return [:]
        }
        return facts
    }
}

private extension Dictionary where Key == String, Value == Any {
    func filteringEmptyStrings() -> [String: Any] {
        filter { !($0.value as? String ?? "").isEmpty || !($0.value is String) }
    }
}

private func wireBool(_ value: Any?) -> Bool {
    value as? Bool ?? false
}

private func wireString(_ value: Any?, fallback: String = "") -> String {
    value as? String ?? fallback
}
