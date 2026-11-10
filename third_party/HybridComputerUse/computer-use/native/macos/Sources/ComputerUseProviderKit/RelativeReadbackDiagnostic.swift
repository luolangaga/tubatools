import ApplicationServices
import Foundation
import os

func relativeReadbackDiagnosticEnabled(tool: String, operation: [String: Any]) -> Bool {
    guard tool == "increment" || tool == "decrement",
          let diagnostics = operation["diagnostics"] as? [String: Any],
          let enabled = diagnostics["relative_readback"] as? NSNumber,
          CFGetTypeID(enabled) == CFBooleanGetTypeID()
    else { return false }
    return enabled.boolValue
}

// Raw content is opt-in and never travels in actionEvidence or timing evidence.
final class RelativeReadbackDiagnostic {
    let requestID: String
    let phase: String
    private let clock: () -> TimeInterval
    private let registry: ProviderObjectIdentityRegistry<AXUIElement>?
    private var emitted = false
    private var snapshot: AppSnapshot?
    private var target: ElementRecord?
    private var initialTarget: [String: Any] = [
        "status": "unavailable", "reason": "target_not_bound", "complete": true,
    ]
    private var finalTarget: [String: Any] = [
        "status": "unavailable", "reason": "not_collected", "complete": true,
    ]
    private(set) var before: RelativeAdjustmentScalarReadback?
    private(set) var decisiveAfter: RelativeAdjustmentScalarReadback?
    private(set) var lastAfter: RelativeAdjustmentScalarReadback?
    private(set) var pollCount = 0
    private(set) var times: [String: Double] = [:]
    private var nativeReturnCode: Int?
    private var outcome = "not_observed"
    private var sampled = false
    private var verified: Bool?
    var finalSnapshotStatus = "not_attempted"

    init(requestID: String, phase: String,
         registry: ProviderObjectIdentityRegistry<AXUIElement>? = nil,
         clock: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime }) {
        self.requestID = requestID
        self.phase = phase
        self.registry = registry
        self.clock = clock
    }

    static func requested(tool: String, operation: [String: Any]) -> RelativeReadbackDiagnostic? {
        guard relativeReadbackDiagnosticEnabled(tool: tool, operation: operation),
              let request = Thread.current.threadDictionary[
                "computer-use.liveness.request"
              ] as? String, !request.isEmpty, request.utf8.count <= 128
        else { return nil }
        return RelativeReadbackDiagnostic(requestID: request, phase: tool)
    }

    func bind(snapshot: AppSnapshot, target: ElementRecord) {
        self.snapshot = snapshot
        self.target = target
        initialTarget = snapshotTargetFacts(snapshot, selected: target)
    }

    func bindFinal(snapshot: AppSnapshot) {
        finalTarget = snapshotTargetFacts(snapshot, selected: nil)
    }

    func willReadBefore() { times["before_read_start_uptime_s"] = clock() }

    func didReadBefore(_ value: RelativeAdjustmentScalarReadback?) {
        times["before_read_end_uptime_s"] = clock()
        before = value
    }

    func willPerformAction() { times["native_action_start_uptime_s"] = clock() }

    func didPerformAction(_ result: AXError) {
        times["native_action_end_uptime_s"] = clock()
        nativeReturnCode = Int(result.rawValue)
        if result != .success { outcome = "native_error" }
    }

    func willReadAfter() { times["last_after_read_start_uptime_s"] = clock() }

    func didReadAfter(_ value: RelativeAdjustmentScalarReadback?) {
        times["last_after_read_end_uptime_s"] = clock()
        lastAfter = value
        pollCount += 1
    }

    func finish(_ outcome: String, sampled: Bool, verified: Bool?) {
        self.outcome = outcome
        self.sampled = sampled
        self.verified = verified
        if outcome == "changed" {
            decisiveAfter = lastAfter
            times["decisive_after_read_start_uptime_s"] = times["last_after_read_start_uptime_s"]
            times["decisive_after_read_end_uptime_s"] = times["last_after_read_end_uptime_s"]
        }
    }

    // Runs only from the outer action defer, after its existing observation attempt.
    func emit(
        write: (String) -> Void = { providerReadbackLogger.notice("\($0, privacy: .public)") }
    ) {
        guard !emitted else { return }
        emitted = true
        let before = relativeReadbackScalarFacts(self.before)
        let decisive = relativeReadbackScalarFacts(self.decisiveAfter)
        let last = relativeReadbackScalarFacts(self.lastAfter)
        let complete = [before, decisive, last, initialTarget, finalTarget].allSatisfy {
            $0["complete"] as? Bool != false
        }
        var final = finalTarget
        final["snapshot_status"] = finalSnapshotStatus
        var payload: [String: Any] = [
            "schema_version": 2, "event": "relative_readback",
            "request_id": requestID, "phase": phase, "provider_pid": Int(getpid()),
            "complete": complete, "outcome": outcome,
            "before": before, "decisive_after": decisive, "last_after": last,
            "poll_count": pollCount, "times": times,
            "readback_sampled": sampled,
            "readback_verified": verified.map { $0 as Any } ?? NSNull(),
            "native_return_code": nativeReturnCode.map { $0 as Any } ?? NSNull(),
            "initial_target": initialTarget,
            "final_target": final,
        ]
        var data = try? JSONSerialization.data(withJSONObject: payload, options: [.sortedKeys])
        if let encoded = data, encoded.count > 16_384 {
            // Snapshot diagnostics must not displace the existing action scalars.
            for key in ["initial_target", "final_target"] {
                guard let detail = payload[key] as? [String: Any] else { continue }
                var bounded: [String: Any] = [
                    "status": "unavailable", "reason": "snapshot_detail_oversize", "complete": false,
                ]
                for field in ["snapshot_serial", "binding_uptime_s", "time_basis", "snapshot_status"] {
                    bounded[field] = detail[field]
                }
                payload[key] = bounded
            }
            payload["complete"] = false
            data = try? JSONSerialization.data(withJSONObject: payload, options: [.sortedKeys])
        }
        if data == nil || data!.count > 16_384 {
            payload = [
                "schema_version": 2, "event": "relative_readback",
                "request_id": requestID, "phase": phase, "provider_pid": Int(getpid()),
                "complete": false, "reason": "payload_oversize_or_invalid",
            ]
            data = try? JSONSerialization.data(withJSONObject: payload, options: [.sortedKeys])
        }
        guard let data else { return }
        for fragment in providerFailureDiagnosticFragments(data) { write(fragment) }
    }

    // Only cached record values and local object equality are inspected here.
    // Binding time is not the time at which AXValue was read while building the tree.
    private func snapshotTargetFacts(
        _ current: AppSnapshot, selected: ElementRecord?
    ) -> [String: Any] {
        var facts: [String: Any] = [
            "status": "unavailable", "complete": true,
            "snapshot_serial": current.diagnosticSnapshotSerial,
            "binding_uptime_s": clock(), "time_basis": "snapshot_binding_not_attribute_sample",
            "value_kind": "normalized_snapshot_text",
            "snapshot_pid": Int(current.app.pid),
        ]
        guard let origin = snapshot, let target else {
            facts["reason"] = "target_not_bound"
            return facts
        }
        let token = target.boundProviderObjectPath ?? ""
        let generation = origin.providerProcessGeneration ?? ""
        let window = origin.targetWindowHandle ?? ""
        let currentGeneration = current.providerProcessGeneration ?? ""
        let currentWindow = current.targetWindowHandle ?? ""
        let processMatches = !generation.isEmpty && !currentGeneration.isEmpty &&
            origin.app.pid == current.app.pid && generation == currentGeneration
        let windowMatches = !window.isEmpty && !currentWindow.isEmpty && window == currentWindow
        let scopeMatches = processMatches && windowMatches
        let scope = "mac:\(origin.app.pid):\(generation)"
        let candidates = token.isEmpty ? [] : current.elements.values.filter {
            $0.boundProviderObjectPath == token
        }.sorted { $0.index < $1.index }
        let limit = 4
        var checks: [String: String] = [:]
        func check(_ name: String, _ result: Bool?) {
            checks[name] = relativeReadbackCheck(result)
        }
        check("target_non_synthetic", !target.isSyntheticText)
        check("target_native_object_present", target.element != nil)
        check("target_token_present", !token.isEmpty)
        check("initial_process_generation_present", !generation.isEmpty)
        check("initial_window_present", !window.isEmpty)
        check("selected_record_in_initial_snapshot", origin.elements[target.index] === target)
        check("snapshot_process_generation_present", !currentGeneration.isEmpty)
        check("snapshot_window_present", !currentWindow.isEmpty)
        check("process_scope_matches", processMatches)
        check("window_scope_matches", windowMatches)
        check("target_present", token.isEmpty ? nil : !candidates.isEmpty)
        check("token_candidate_unique", candidates.isEmpty ? nil : candidates.count == 1)
        let canMatchRetained = scopeMatches && !token.isEmpty
        let actionRetained = target.element.flatMap { object -> Bool? in
            guard canMatchRetained else { return nil }
            return matchesRetainedMacProviderObject(object, token: token, scope: scope, registry: registry)
        }
        check("action_retained_match", actionRetained)
        let candidateFacts: [[String: Any]] = candidates.prefix(limit).map { record in
            let forward = target.element.flatMap { action in
                record.element.map { CFEqual(action, $0) }
            }
            let reverse = target.element.flatMap { action in
                record.element.map { CFEqual($0, action) }
            }
            let retained = record.element.flatMap { object -> Bool? in
                guard canMatchRetained else { return nil }
                return matchesRetainedMacProviderObject(object, token: token, scope: scope, registry: registry)
            }
            return [
                "index": record.index, "non_synthetic": !record.isSyntheticText,
                "native_object_present": record.element != nil,
                "association": "not_claimed",
                "value": scopeMatches
                    ? relativeReadbackScalarFacts(record.value.map { .string($0) })
                    : ["kind": "unavailable", "complete": true, "reason": "scope_not_matched"],
                "equal_forward": relativeReadbackCheck(forward),
                "equal_reverse": relativeReadbackCheck(reverse),
                "retained_match": relativeReadbackCheck(retained),
            ]
        }
        if candidates.count == 1, let candidate = candidateFacts.first {
            check("candidate_non_synthetic", candidate["non_synthetic"] as? Bool)
            check("candidate_native_object_present", candidate["native_object_present"] as? Bool)
            for key in ["equal_forward", "equal_reverse", "retained_match"] {
                checks["candidate_" + key] = candidate[key] as? String
            }
        } else {
            for key in ["non_synthetic", "native_object_present", "equal_forward", "equal_reverse", "retained_match"] {
                check("candidate_" + key, nil)
            }
        }
        var complete = candidates.count <= limit && candidateFacts.allSatisfy {
            ($0["value"] as? [String: Any])?["complete"] as? Bool != false
        }
        if let selected {
            let value = relativeReadbackScalarFacts(selected.value.map { .string($0) })
            facts["selected_record"] = [
                "index": selected.index, "value": value,
                "evidence_scope": "selected_record", "association": "not_claimed",
            ]
            complete = complete && (value["complete"] as? Bool != false)
        }
        facts["action_snapshot_serial"] = origin.diagnosticSnapshotSerial
        facts["action_pid"] = Int(origin.app.pid)
        facts["checks"] = checks
        facts["failed_checks"] = checks.filter { $0.value == "fail" }.map(\.key).sorted()
        facts["token_candidate_count"] = candidates.count
        facts["candidate_limit"] = limit
        facts["candidates"] = candidateFacts
        facts["candidates_complete"] = candidates.count <= limit
        facts["omitted_candidate_count"] = max(0, candidates.count - limit)
        facts["complete"] = complete
        if checks.values.allSatisfy({ $0 == "pass" }) {
            facts["status"] = "associated"
            facts["value"] = candidateFacts.first?["value"]
        } else {
            facts["reason"] = "association_checks_failed"
        }
        return facts
    }
}

private func relativeReadbackCheck(_ result: Bool?) -> String {
    result.map { $0 ? "pass" : "fail" } ?? "not_evaluated"
}

func relativeReadbackScalarFacts(_ scalar: RelativeAdjustmentScalarReadback?) -> [String: Any] {
    let kind: String
    let text: String
    switch scalar {
    case .string(let value): kind = "string"; text = value
    case .number(let value): kind = "number"; text = value
    case nil: return ["kind": "unavailable", "complete": true]
    }
    let bytes = text.utf8.count
    guard bytes <= 1024 else {
        return ["kind": kind, "utf8_bytes": bytes, "complete": false, "reason": "value_oversize"]
    }
    return ["kind": kind, "text": text, "utf8_bytes": bytes, "complete": true]
}

private let providerReadbackLogger = Logger(
    subsystem: "org.computer-use.provider",
    category: "provider-readback"
)
