import Foundation
import CoreFoundation

/// Checks a provider request before dispatch.
///
/// The Go core validates each request against the provider contract before it
/// sends it: field names, value types, ranges, and the rules that relate one
/// field to another. This check does not repeat that contract. It keeps what
/// this provider needs on its own side:
///
/// - the envelope, so that a rejection names its request and dispatch can read
///   the payload;
/// - the payload members that dispatch reads without a fallback, and the
///   members it reads as objects, so that a malformed request fails here with
///   a message instead of trapping or being read as absent;
/// - what this provider cannot do: an unknown intent or window action, a
///   window screenshot, or a key it cannot press. All ordered keys are parsed
///   before the first key is sent.
///
/// The GUI runtime checks every desktop action before it sends any input.
public enum ProviderRequestValidator {
    public static let operations = [
        "observe_apps",
        "observe",
        "resolve_and_act",
        "ordered_input",
        "desktop_execute_batch",
        "window_list",
        "window_action",
    ]

    private static let payloadByOperation = [
        "observe_apps": "observe_apps",
        "observe": "observe",
        "resolve_and_act": "action",
        "ordered_input": "ordered_input",
        "desktop_execute_batch": "desktop",
        "window_list": "window_list",
        "window_action": "window_action",
    ]

    private static let intents: Set<String> = [
        "resolve_target",
        "click",
        "scroll",
        "type",
        "keypress",
        "set_value",
        "select",
        "increment",
        "decrement",
    ]

    private static let objectMembers = [
        "app_identity", "expected_window", "element", "target", "input",
        "window_bounds", "observation", "final_observation", "diagnostics",
    ]

    public static func validate(_ request: [String: Any]) throws {
        guard request["protocol"] as? String == "computer-use-provider" else {
            throw invalid("request.protocol must equal computer-use-provider")
        }
        guard request["schema_version"] as? Int == 2 else {
            throw invalid("request.schema_version must equal 2")
        }
        try requireNonblankString(request["request_id"], path: "request.request_id")
        let operation = try requiredString(request["operation"], path: "request.operation")
        guard let payloadName = payloadByOperation[operation] else {
            throw invalid("unsupported provider operation \(operation)")
        }
        let active = payloadByOperation.values.filter { request[$0] != nil }
        guard active == [payloadName] else {
            throw invalid("provider request must contain exactly its operation payload")
        }
        let path = "request.\(payloadName)"
        let payload = try object(request[payloadName], path: path)
        guard payload["protocol_version"] as? Int == 2 else {
            throw invalid("\(path).protocol_version must equal 2")
        }
        for member in objectMembers where payload[member] != nil {
            _ = try object(payload[member], path: "\(path).\(member)")
        }
        for member in ["observation", "final_observation"] {
            let observation = payload[member] as? [String: Any]
            if observation?["include_screenshot"] as? Bool == true {
                // The core captures desktop frames with a desktop batch; this
                // provider never captures a window image.
                throw invalid(
                    "\(path).\(member).include_screenshot is not supported; request a desktop batch screenshot"
                )
            }
        }

        switch operation {
        case "observe", "window_list":
            try requireNonblankString(payload["app"], path: "\(path).app")
        case "resolve_and_act":
            let intent = try requiredString(payload["intent"], path: "\(path).intent")
            guard intents.contains(intent) else {
                throw invalid("unsupported provider accessibility intent \(intent)")
            }
            try requireNonblankString(payload["app"], path: "\(path).app")
            if intent == "set_value",
               let input = payload["input"] as? [String: Any],
               let constraint = input["require_semantic_write"] {
                guard let flag = constraint as? NSNumber,
                      CFGetTypeID(flag) == CFBooleanGetTypeID() else {
                    throw invalid("require_semantic_write must be a boolean")
                }
                if flag.boolValue {
                    throw invalid("The semantic-only write constraint is Windows-only")
                }
            }
            if intent == "keypress" {
                let input = payload["input"] as? [String: Any]
                _ = try KeyPressParser.parse(
                    try requiredString(input?["key"], path: "\(path).input.key")
                )
            }
        case "ordered_input":
            guard let steps = payload["steps"] as? [Any] else {
                throw invalid("\(path).steps must be an array")
            }
            for (index, rawStep) in steps.enumerated() {
                let step = try object(rawStep, path: "\(path).steps[\(index)]")
                _ = try KeyPressParser.parse(
                    try requiredString(step["key"], path: "\(path).steps[\(index)].key")
                )
            }
        case "desktop_execute_batch":
            guard let actions = payload["actions"] as? [Any], !actions.isEmpty else {
                throw invalid("\(path).actions must be a non-empty array")
            }
            for (index, action) in actions.enumerated() {
                _ = try object(action, path: "\(path).actions[\(index)]")
            }
        case "window_action":
            let action = try requiredString(payload["action"], path: "\(path).action")
            guard ["activate", "close"].contains(action) else {
                throw invalid("unsupported provider window action \(action)")
            }
            try requireNonblankString(payload["app"], path: "\(path).app")
            guard payload["target"] != nil else {
                throw invalid("\(path).target is required")
            }
        default:
            break
        }
    }

    private static func object(_ raw: Any?, path: String) throws -> [String: Any] {
        guard let value = raw as? [String: Any] else {
            throw invalid("\(path) must be an object")
        }
        return value
    }

    private static func requiredString(_ raw: Any?, path: String) throws -> String {
        guard let value = raw as? String else {
            throw invalid("\(path) must be a string")
        }
        return value
    }

    private static func requireNonblankString(_ raw: Any?, path: String) throws {
        guard let value = raw as? String,
              !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        else {
            throw invalid("\(path) must be a non-empty string")
        }
    }

    private static func invalid(_ message: String) -> ComputerUseError {
        .invalidArguments(message)
    }
}
