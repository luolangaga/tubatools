import AppKit
import ApplicationServices
import CoreGraphics
import Foundation
import ImageIO
import ScreenCaptureKit
import os

@_silgen_name("_AXUIElementGetWindow")
func AXUIElementGetWindow(
    _ element: AXUIElement,
    _ identifier: UnsafeMutablePointer<CGWindowID>
) -> AXError

public final class ComputerUseRuntimeBridge: @unchecked Sendable {
    let service = ComputerUseService()

    public init() {}

    public func execute(_ operation: [String: Any]) async -> [String: Any] {
        let tool = string(operation["tool"])
        do {
            return try await AccessibilityThread.shared.run(
                timeout: providerReadOnlyToolNeedsRuntimeBound(tool)
                    ? accessibilityReadTimeout : nil
            ) {
                tool == "gui"
                    ? try self.executeGUI(operation)
                    : try self.executeSynchronousThrowing(operation)
            }
        } catch {
            return runtimeFailureResult(error)
        }
    }

    private func executeSynchronousThrowing(
        _ operation: [String: Any]
    ) throws -> [String: Any] {
        try service.withAppIdentity(
            runtimeAppIdentity(operation),
            windowHandle: runtimeWindowHandle(operation)
        ) {
            try executeSynchronousBound(operation)
        }
    }

    private func executeSynchronousBound(_ operation: [String: Any]) throws -> [String: Any] {
        let context = Thread.current.threadDictionary
        context["computer-use.liveness.request"] =
            string(operation["_diagnostic_request_id"])
        context["computer-use.liveness.phase"] = string(operation["tool"])
        context["computer-use.liveness.count"] = 0
        context["computer-use.failure.count"] = 0
        context["computer-use.failure.snapshot"] = 0
        windowLivenessDiagnostic("request_begin", [:])
        defer {
            windowLivenessDiagnostic("request_end", [:])
            context.removeObject(forKey: "computer-use.liveness.request")
            context.removeObject(forKey: "computer-use.liveness.phase")
            context.removeObject(forKey: "computer-use.liveness.count")
            context.removeObject(forKey: "computer-use.failure.count")
            context.removeObject(forKey: "computer-use.failure.snapshot")
        }
        do {
            return try executeSynchronousOperation(operation)
        } catch {
            windowLivenessDiagnostic("runtime_failure", [
                "error_kind": windowLivenessErrorKind(error),
            ])
            throw error
        }
    }

    private func executeSynchronousOperation(
        _ operation: [String: Any]
    ) throws -> [String: Any] {
        let tool = string(operation["tool"])
        switch tool {
        case "list_apps":
            let inventory = try macApplicationListing(AppDiscovery.runningApps())
            return [
                "ok": true,
                "text": inventory.apps.compactMap { $0["displayText"] as? String }
                    .joined(separator: "\n"),
                "apps": inventory.apps,
                "inventory_issues": inventory.issues,
            ]
        case "get_app_state":
            let snapshot = try getAppStateSnapshot(operation)
            return [
                "ok": true,
                "snapshot": try snapshot.providerDictionary(),
            ]
        case "gui":
            throw ComputerUseError.unsupportedTool(tool)
        case "list_app_windows":
            return try listWindows(operation)
        case "activate", "close":
            return try executeWindow(tool: tool, operation: operation)
        case "ordered_keypress":
            return executeOrderedInput(operation)
        default:
            return try executeAccessibility(tool: tool, operation: operation)
        }
    }

    func getAppStateSnapshot(
        _ operation: [String: Any],
        resolveByName: (String) throws -> RunningAppDescriptor = AppDiscovery.resolve,
        refreshByName: ((String) throws -> AppSnapshot)? = nil,
        resolveBoundApp: ([String: Any]) throws -> RunningAppDescriptor = {
            try resolveOrderedInputApp($0)
        },
        captureBound: ((RunningAppDescriptor) throws -> AppSnapshot)? = nil,
        inspectBoundWindow: ((pid_t, String) -> BoundWindowLiveness)? = nil
    ) throws -> AppSnapshot {
        let app = try requiredString(operation, "app")
        let limit = try snapshotTextLimit(operation["text_limit"])
        let limits = AccessibilityTreeLimits(
            maxNodeCount: positiveInt(operation["max_tree_nodes"])
                ?? AccessibilityTreeLimits.defaultMaxNodeCount,
            maxDepth: positiveInt(operation["max_tree_depth"])
                ?? AccessibilityTreeLimits.defaultMaxDepth
        )
        if let rawIdentity = operation["appIdentity"] {
            guard let identity = rawIdentity as? [String: Any] else {
                throw ComputerUseError.invalidArguments(
                    "appIdentity must be an identity object."
                )
            }
            let windowHandle = string(operation["targetWindowHandle"])
            return try refreshIdentityBoundSnapshot(
                for: app,
                appIdentity: identity,
                windowHandle: windowHandle,
                resolveApp: { try resolveBoundApp(identity) },
                capture: { descriptor in
                    if let captureBound {
                        return try captureBound(descriptor)
                    }
                    return try SnapshotBuilder.build(
                        for: descriptor,
                        textLimit: limit,
                        treeLimits: limits,
                        recoveryPolicy: .readOnly,
                        expectedWindowHandle: windowHandle
                    )
                },
                inspectWindow: inspectBoundWindow
            )
        }

        _ = try resolveByName(app)
        let snapshot: AppSnapshot
        if let refreshByName {
            snapshot = try refreshByName(app)
        } else {
            // Observation never unhides, activates or opens the application.
            snapshot = try service.refreshSnapshot(
                for: app,
                textLimit: limit,
                treeLimits: limits,
                recoveryPolicy: .readOnly
            )
        }
        try validateRequestedWindow(operation, snapshot: snapshot)
        return snapshot
    }

    func refreshIdentityBoundSnapshot(
        for query: String,
        appIdentity: [String: Any],
        windowHandle: String,
        resolveApp: () throws -> RunningAppDescriptor,
        capture: (RunningAppDescriptor) throws -> AppSnapshot,
        inspectWindow: ((pid_t, String) -> BoundWindowLiveness)? = nil
    ) throws -> AppSnapshot {
        // Name-based refresh can launch a replacement. Cache only a validated
        // snapshot of the bound process, with the same keys as service refresh.
        let snapshot = try orderedInputFinalSnapshot(
            appIdentity: appIdentity,
            windowHandle: windowHandle,
            resolveApp: resolveApp,
            capture: capture,
            inspectWindow: inspectWindow ?? boundWindowInspector(
                windowHandle: windowHandle
            )
        )
        for key in Set([
            query.lowercased(),
            snapshot.app.name.lowercased(),
            (snapshot.app.bundleIdentifier ?? "").lowercased(),
        ].filter { !$0.isEmpty }) {
            service.snapshotsByApp[key] = snapshot
        }
        service.rememberNativeWindowIdentity(snapshot)
        return snapshot
    }
}

func macApplicationListing(
    _ apps: [RunningAppDescriptor],
    accessibilityTrusted: () -> Bool = AXIsProcessTrusted,
    inputCapable: (RunningAppDescriptor) -> Bool = { $0.runningApplication.activationPolicy != .prohibited },
    windows: (RunningAppDescriptor) throws -> [[String: Any]] = { app in
        let inventory = axWindowInventory(pid: app.pid)
        if inventory.status == .apiDisabled {
            throw ComputerUseError.permissionDenied("Accessibility permission is unavailable. Enable Accessibility for Computer Use Provider.")
        }
        guard inventory.complete else {
            throw ComputerUseError.stateUnavailable("AXWindows could not be read completely (AX \(inventory.status.rawValue)). Missing windows remain unknown.")
        }
        guard let records = try windowInventorySnapshot(app: app, windows: inventory.windows)["windows"] as? [[String: Any]] else {
            throw ComputerUseError.stateUnavailable("The window inventory could not be encoded completely.")
        }
        return records
    }
) throws -> (apps: [[String: Any]], issues: [[String: String]]) {
    guard accessibilityTrusted() else {
        throw ComputerUseError.permissionDenied("Accessibility permission is unavailable. Enable Accessibility for Computer Use Provider.")
    }
    var issues: [[String: String]] = []
    let listed = try apps.map { app -> [String: Any] in
        let capable = inputCapable(app)
        let records: [[String: Any]]
        do {
            records = capable ? try windows(app) : []
        } catch ComputerUseError.stateUnavailable(let reason) {
            // Keep other applications discoverable. This is not an empty,
            // complete inventory and cannot prove that a window disappeared.
            issues.append(["app": app.name, "reason": String(reason.prefix(512))])
            records = []
        }
        return [
            "name": app.name,
            "bundleIdentifier": app.bundleIdentifier ?? "",
            "pid": Int(app.pid),
            "providerBusName": "mac:\(app.pid)",
            "processGeneration": macProcessGeneration(pid: app.pid),
            "displayText": app.bundleIdentifier.map { "\(app.name) — \($0)" } ?? app.name,
            "inputCapable": capable,
            "windows": appListingWindows(records),
        ]
    }
    guard accessibilityTrusted() else {
        throw ComputerUseError.permissionDenied("Accessibility permission changed during enumeration. Enable Accessibility for Computer Use Provider.")
    }
    return (listed, issues)
}

func runtimeAppIdentity(_ operation: [String: Any]) throws -> [String: Any]? {
    guard let raw = operation["appIdentity"] else { return nil }
    guard let identity = raw as? [String: Any] else {
        throw ComputerUseError.invalidArguments("appIdentity must be an identity object.")
    }
    return identity
}

func resolveRuntimeApplication(
    query: String,
    identity: [String: Any]?,
    resolveByName: (String) throws -> RunningAppDescriptor = AppDiscovery.resolve,
    resolveBoundApp: ([String: Any]) throws -> RunningAppDescriptor = { try resolveOrderedInputApp($0) }
) throws -> RunningAppDescriptor {
    guard let identity else { return try resolveByName(query) }
    let app = try resolveBoundApp(identity)
    try validateRequestedIdentity(["appIdentity": identity], app: app)
    return app
}

let accessibilityReadTimeout: TimeInterval = 8

func providerReadOnlyToolNeedsRuntimeBound(_ tool: String) -> Bool {
    ["list_apps", "get_app_state", "list_app_windows", "resolve_target"]
        .contains(tool)
}

func runtimeFailureResult(_ error: Error) -> [String: Any] {
    let error = normalizedScreenCaptureError(error)
    let message = (error as? LocalizedError)?.errorDescription
        ?? String(describing: error)
    let inputEventsEmitted =
        (error as? InputEmissionReportingError)?.inputEventsEmitted
        ?? false
    let actionFailureEvidence =
        error as? ActionFailureEvidenceReportingError
    let partialInputPossible =
        actionFailureEvidence?.partialInputPossible
        ?? inputEventsEmitted
    var failureCode =
        actionFailureEvidence?.failureCode
        ?? "provider_error"
    if let computerError = error as? ComputerUseError {
        switch computerError {
        case .capabilityUnsupported:
            failureCode = "capability_unsupported"
        case .invalidArguments, .unsupportedTool:
            failureCode = "invalid_request"
        case .appNotFound:
            failureCode = "ax_app_unavailable"
        case .permissionDenied:
            failureCode = "permission_denied"
        case .staleAppRef:
            failureCode = "stale_app_ref"
        case .staleWindowRef:
            failureCode = "stale_window_ref"
        case .staleElementRef:
            failureCode = "stale_element_ref"
        case .stateUnavailable:
            failureCode = "state_unavailable"
        case .providerResetRequired:
            failureCode = "state_unavailable"
        case .message:
            break
        }
    }
    var result: [String: Any] = [
        "ok": false,
        "error": message,
        "failureCode": failureCode,
        "failurePhase": inputEventsEmitted ? "emission" : "precondition",
        "input_events_emitted": inputEventsEmitted,
        "partial_input_possible": partialInputPossible,
        "mutation_unknown": inputEventsEmitted,
        "failureFactsComplete": true,
    ]
    if let stale = (error as? ComputerUseError)?.staleIdentityFailure {
        result["failureSubject"] = stale.subject
        result["underlyingFailureCode"] = stale.reason
    }
    if let evidence = actionFailureEvidence,
       evidence.readbackSampled || evidence.actionRoute != nil {
        var actionEvidence: [String: Any] = [
            "input_emitted": inputEventsEmitted,
            "readback_sampled": evidence.readbackSampled,
        ]
        if let verified = evidence.readbackVerified {
            actionEvidence["readback_verified"] = verified
        }
        if let route = evidence.actionRoute {
            actionEvidence["route"] = route
        }
        result["actionEvidence"] = actionEvidence
    }
    if inputEventsEmitted {
        result["desktop_state_mutated"] = true
    }
    if requiresProviderReset(error) {
        result["providerResetRequired"] = true
    }
    return result
}

private let providerFailureLogger = Logger(
    subsystem: "org.computer-use.provider",
    category: "provider-failure"
)

func providerFailureDiagnostic(_ event: String, facts: () -> [String: Any]) {
    let context = Thread.current.threadDictionary
    guard let request = context["computer-use.liveness.request"] as? String,
          !request.isEmpty else { return }
    let count = context["computer-use.failure.count"] as? Int ?? 0
    guard count < 8 else { return }
    context["computer-use.failure.count"] = count + 1
    var payload = facts()
    payload["event"] = event
    payload["request_id"] = String(request.prefix(128))
    payload["phase"] = String((context["computer-use.liveness.phase"] as? String ?? "").prefix(64))
    payload["provider_pid"] = Int(getpid())
    payload["snapshot_serial"] = context["computer-use.failure.snapshot"] as? Int ?? 0
    payload["event_ordinal"] = count + 1
    payload["event_limit"] = 8
    payload["event_limit_reached"] = count == 7
    guard let data = try? JSONSerialization.data(withJSONObject: payload, options: [.sortedKeys]),
          data.count <= 16_384 else {
        let fallback: [String: Any] = [
            "event": "diagnostic_payload_rejected", "original_event": event,
            "request_id": String(request.prefix(128)), "provider_pid": Int(getpid()),
            "snapshot_serial": payload["snapshot_serial"] ?? 0,
            "event_ordinal": count + 1, "event_limit": 8,
            "event_limit_reached": count == 7,
        ]
        if let data = try? JSONSerialization.data(withJSONObject: fallback, options: [.sortedKeys]) {
            emitProviderFailureDiagnostic(data)
        }
        return
    }
    emitProviderFailureDiagnostic(data)
}

func providerFailureDiagnosticFragments(_ data: Data) -> [String] {
    guard !data.isEmpty, data.count <= 16_384 else { return [] }
    // Unified logging can truncate one dynamic string below the payload bound.
    // ASCII envelopes avoid UTF-8 split/escape expansion and permit exact host reassembly.
    let identifier = UUID().uuidString
    let size = 480
    let count = (data.count + size - 1) / size
    return stride(from: 0, to: data.count, by: size).compactMap { offset in
        let chunk = data.subdata(in: offset..<min(offset + size, data.count))
        let envelope: [String: Any] = [
            "event": "diagnostic_chunk", "version": 1, "id": identifier,
            "part": offset / size, "parts": count,
            "bytes": data.count, "data": chunk.base64EncodedString(),
        ]
        guard let encoded = try? JSONSerialization.data(
            withJSONObject: envelope, options: [.sortedKeys, .withoutEscapingSlashes]
        ) else { return nil }
        return String(data: encoded, encoding: .utf8)
    }
}

private func emitProviderFailureDiagnostic(_ data: Data) {
    for fragment in providerFailureDiagnosticFragments(data) {
        providerFailureLogger.notice("\(fragment, privacy: .public)")
    }
}

func providerAmbiguityFacts(
    target: [String: Any], candidates: [[String: Any]], providerMatchCount: Int,
    snapshot: AppSnapshot
) -> [String: Any] {
    let selected = Array(candidates.prefix(4))
    func path(_ value: Any?) -> [Int] {
        Array((value as? [Int] ?? []).prefix(64))
    }
    let records: [[String: Any]] = selected.map { candidate in
        let index = int(candidate["index"]) ?? -1
        var result: [String: Any] = [
            "index": index, "runtime_path": path(candidate["runtimeId"]),
            "runtime_path_length": (candidate["runtimeId"] as? [Int] ?? []).count,
            "provider_token": String(string(candidate["providerObjectPath"]).prefix(128)),
            "role": String(string(candidate["controlType"]).prefix(96)),
            "window_token": String(string(candidate["providerWindowObjectPath"]).prefix(128)),
            "synthetic": snapshot.elements[index]?.isSyntheticText ?? false,
            "scope": String(string(candidate["providerIdentityScope"]).prefix(64)),
            "record_path_matches": snapshot.elements[index]?.runtimeID ==
                (candidate["runtimeId"] as? [Int]),
            "record_token_matches": snapshot.elements[index]?.boundProviderObjectPath ==
                (candidate["providerObjectPath"] as? String),
        ]
        for key in ["providerWindowBusName", "providerWindowObjectPath", "providerWindowProcessGeneration"] {
            result[key] = String(string(candidate[key]).prefix(128))
            result[key + "_matches_target"] = string(target[key]).isEmpty ||
                string(candidate[key]) == string(target[key])
        }
        if let element = snapshot.elements[index]?.element {
            var pid: pid_t = 0
            result["pid_status"] = Int(AXUIElementGetPid(element, &pid).rawValue)
            result["pid"] = Int(pid)
            result["hash"] = String(CFHash(element))
        } else {
            result["missing_object"] = true
        }
        return result
    }
    var pairs: [[String: Any]] = []
    for left in selected.indices {
        for right in selected.indices where right > left {
            let lhsIndex = int(selected[left]["index"]) ?? -1
            let rhsIndex = int(selected[right]["index"]) ?? -1
            var pair: [String: Any] = ["left": lhsIndex, "right": rhsIndex]
            if let lhs = snapshot.elements[lhsIndex]?.element,
               let rhs = snapshot.elements[rhsIndex]?.element {
                pair["equal_forward"] = CFEqual(lhs, rhs)
                pair["equal_reverse"] = CFEqual(rhs, lhs)
                pair["same_wrapper"] = lhs === rhs
            } else {
                pair["missing_object"] = true
            }
            pairs.append(pair)
        }
    }
    let aliases = target["runtimeAliases"] as? [[Int]] ?? []
    let primary = target["runtimeId"] as? [Int] ?? []
    let pathTruncated = primary.count > 64 || aliases.contains { $0.count > 64 } ||
        selected.contains { ($0["runtimeId"] as? [Int] ?? []).count > 64 }
    return [
        "app_pid": Int(snapshot.app.pid),
        "resolved_snapshot_serial": snapshot.diagnosticSnapshotSerial,
        "target_token": String(string(target["providerObjectPath"]).prefix(128)),
        "target_scope": String(string(target["providerIdentityScope"]).prefix(64)),
        "target_runtime": path(target["runtimeId"]),
        "target_runtime_length": primary.count,
        "target_aliases": aliases.prefix(8).map { Array($0.prefix(64)) },
        "target_alias_count": aliases.count,
        "target_alias_lengths": aliases.prefix(8).map(\.count),
        "provider_match_count": providerMatchCount,
        "exact_match_count": candidates.count,
        "sampled_count": selected.count,
        "truncated": candidates.count > selected.count || aliases.count > 8 || pathTruncated,
        "candidates": records, "pairs": pairs,
    ]
}

func macProviderObjectPath(_ record: ElementRecord) -> String {
    guard let element = record.element else {
        return "missing:\(record.index)"
    }
    if record.isSyntheticText {
        return "synthetic:\(record.index):\(CFHash(element))"
    }
    return record.boundProviderObjectPath ?? ""
}

func imageDimensions(_ data: Data) -> (width: Int, height: Int)? {
    guard let source = CGImageSourceCreateWithData(data as CFData, nil),
          let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil)
            as? [CFString: Any],
          let width = properties[kCGImagePropertyPixelWidth] as? Int,
          let height = properties[kCGImagePropertyPixelHeight] as? Int
    else {
        return nil
    }
    return (width, height)
}

func frameDictionary(_ frame: CGRect) -> [String: Double] {
    [
        "x": frame.origin.x,
        "y": frame.origin.y,
        "width": frame.size.width,
        "height": frame.size.height,
    ]
}
