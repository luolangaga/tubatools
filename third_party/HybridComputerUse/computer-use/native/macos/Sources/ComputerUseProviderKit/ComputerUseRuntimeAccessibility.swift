import ApplicationServices
import CoreGraphics
import Foundation

func diagnosticPhaseTimingEnabled(
    operation: [String: Any]
) -> Bool {
    guard let diagnostics = operation["diagnostics"] as? [String: Any] else {
        return false
    }
    return diagnostics["phase_timing"] as? Bool == true
}

extension ComputerUseRuntimeBridge {
    func executeAccessibility(
        tool: String,
        operation: [String: Any]
    ) throws -> [String: Any] {
        try validateMacAccessibilityCapability(tool: tool, operation: operation)
        let relativeReadback = RelativeReadbackDiagnostic.requested(tool: tool, operation: operation)
        defer { relativeReadback?.emit() }
        let timingEnabled = diagnosticPhaseTimingEnabled(operation: operation)
        var timingStarted = ProcessInfo.processInfo.systemUptime
        var diagnosticTimingMS: [String: Double] = [:]
        func recordTiming(_ phase: String) {
            guard timingEnabled else {
                return
            }
            let now = ProcessInfo.processInfo.systemUptime
            diagnosticTimingMS[phase] = (now - timingStarted) * 1_000
            timingStarted = now
        }

        let app = try requiredString(operation, "app")
        let appDescriptor = try resolveRuntimeApplication(query: app, identity: try runtimeAppIdentity(operation))
        try validateRequestedIdentity(operation, app: appDescriptor)
        recordTiming("app_resolution")
        let expectedWindowHandle = string(operation["targetWindowHandle"])
        let initialSnapshot: AppSnapshot
        let elementIndex: String?
        if let lease = leasedActionTarget(
            tool: tool,
            operation: operation,
            app: app,
            appDescriptor: appDescriptor,
            expectedWindowHandle: expectedWindowHandle
        ) {
            initialSnapshot = lease.snapshot
            elementIndex = lease.elementIndex
            recordTiming("initial_snapshot")
        } else {
            let cachedSnapshot = service.cachedSnapshot(for: app)
            initialSnapshot = try service.refreshSnapshot(
                for: app,
                recoveryPolicy: .readOnly,
                fallbackWindow: cachedSnapshot?.rootWindowElement,
                expectedWindowHandle: expectedWindowHandle
            )
            try validateRequestedWindow(operation, snapshot: initialSnapshot)
            recordTiming("initial_snapshot")
            elementIndex = try resolvedRuntimeElementIndex(
                operation,
                app: app,
                semanticTargetKey: "semanticTarget",
                elementKey: "element",
                targetLabel: "target"
            )
        }
        let changeWake = AXChangeWake(
            pid: appDescriptor.pid,
            targetElement: initialSnapshot.rootWindowElement
        )
        recordTiming("target_resolution")
        if tool == "resolve_target" {
            guard let elementIndex, let index = Int(elementIndex),
                  let element = try service.currentSnapshot(for: app)
                    .providerElementDictionaries.first(where: {
                        int($0["index"]) == index
                    }) else {
                throw ComputerUseError.stateUnavailable(
                    "semantic target could not be resolved"
                )
            }
            return [
                "ok": true,
                "resolved_element": element,
                "candidate_count": 1,
                "candidates": [],
            ]
        }

        var executedRoute = "macos.native"
        var geometrySampled = false
        var readbackSampled = false
        var readbackVerified: Bool?
        var mutationUnknown = false

        switch tool {
        case "click":
            try service.performClick(
                app: app,
                elementIndex: elementIndex,
                x: double(operation["x"]),
                y: double(operation["y"]),
                clickCount: int(operation["click_count"]) ?? 1,
                mouseButton: string(operation["mouse_button"], fallback: "left"),
                clickMethod: try parseClickMethod(string(operation["click_method"]))
            )
            executedRoute = "macos.click.dispatch"
            geometrySampled = true
        case "scroll":
            let direction = string(operation["direction"], fallback: "down")
            let pages = double(operation["pages"]) ?? 1
            if let elementIndex {
                _ = try service.scroll(
                    app: app,
                    direction: direction,
                    elementIndex: elementIndex,
                    pages: pages
                )
            } else {
                let snapshot = try service.currentSnapshot(for: app)
                guard let bounds = snapshot.windowBounds else {
                    throw ComputerUseError.staleElementRef(
                        "The selected window has no scrollable bounds"
                    )
                }
                try InputSimulation.scrollTargeted(
                    at: CGPoint(x: bounds.midX, y: bounds.midY),
                    direction: direction,
                    pages: pages,
                    pid: snapshot.app.pid
                )
            }
            executedRoute = "input.cg_event.scroll"
            geometrySampled = true
        case "type":
            try focusRuntimeElement(elementIndex, app: app)
            let dispatch = try service.typeTextWithRoute(
                app: app,
                text: string(operation["text"]),
                elementIndex: elementIndex
            )
            executedRoute = dispatch.route
            readbackSampled = dispatch.readbackVerified != nil
            readbackVerified = dispatch.readbackVerified
        case "keypress":
            try focusRuntimeElement(elementIndex, app: app)
            try service.pressKey(
                app: app,
                key: string(operation["key"])
            )
            executedRoute = "input.cg_event.keyboard"
        case "set_value":
            let dispatch = try service.setValueWithRoute(
                app: app,
                elementIndex: try requiredElementIndex(elementIndex),
                value: string(operation["value"])
            )
            executedRoute = dispatch.route
            readbackSampled = true
            readbackVerified = true
        case "select":
            try service.selectText(
                app: app,
                elementIndex: try requiredElementIndex(elementIndex),
                text: string(operation["text"])
            )
            executedRoute = "ax.attribute.selected_text_range.set"
            readbackSampled = true
            readbackVerified = true
        case "increment", "decrement":
            let dispatch = try service.performRelativeAdjustment(
                app: app,
                elementIndex: try requiredElementIndex(elementIndex),
                direction: tool,
                diagnostic: relativeReadback
            )
            executedRoute = "ax.action.\(tool)"
            readbackSampled = dispatch.readbackSampled
            readbackVerified = dispatch.readbackVerified
            mutationUnknown = dispatch.readbackVerified != true
        default:
            throw ComputerUseError.unsupportedTool(tool)
        }
        recordTiming("action_dispatch")

        _ = changeWake?.wait(timeout: 0.12)
        recordTiming("processing_barrier")
        var evidence: [String: Any] = [
            "route": executedRoute,
            "geometry_sampled": geometrySampled,
            "readback_sampled": readbackSampled,
            "input_emitted": true,
        ]
        if let readbackVerified {
            evidence["readback_verified"] = readbackVerified
        }
        if timingEnabled {
            evidence["diagnostic_timing_ms"] = diagnosticTimingMS
        }
        if operation["captureSnapshot"] as? Bool == false {
            relativeReadback?.finalSnapshotStatus = "skipped"
            var result: [String: Any] = [
                "ok": true,
                "observationSkipped": true,
                "input_events_emitted": true,
                "desktop_state_mutated": true,
                "actionEvidence": evidence,
            ]
            if mutationUnknown {
                result["mutation_unknown"] = true
            }
            return result
        }
        let snapshot: AppSnapshot
        let projectedSnapshot: [String: Any]
        do {
            relativeReadback?.finalSnapshotStatus = "attempted"
            snapshot = try service.refreshSnapshot(
                for: app,
                recoveryPolicy: .readOnly
            )
            relativeReadback?.finalSnapshotStatus = "captured"
            projectedSnapshot = try snapshot.providerDictionary()
            relativeReadback?.bindFinal(snapshot: snapshot)
            recordTiming("final_snapshot")
            if timingEnabled {
                evidence["diagnostic_timing_ms"] = diagnosticTimingMS
            }
        } catch {
            relativeReadback?.finalSnapshotStatus = "failed"
            recordTiming("failed_final_snapshot")
            if timingEnabled {
                evidence["diagnostic_timing_ms"] = diagnosticTimingMS
            }
            if requiresProviderReset(error) {
                return postInputObservationFailureResult(error, evidence: evidence)
            }
            let targetDestroyed = changeWake?
                .waitForTargetDestruction(timeout: 0.12) == true
            let currentLiveness = SnapshotBuilder.targetWindowLiveness(
                initialSnapshot,
                expectedWindowHandle: expectedWindowHandle
            )
            if postActionTargetWindowTransitionProven(
                targetDestroyed: targetDestroyed,
                currentLiveness: currentLiveness
            ) {
                return postActionTargetWindowAbsentResult(
                    evidence: evidence
                )
            }
            return postInputObservationFailureResult(
                error,
                evidence: evidence
            )
        }
        var result: [String: Any] = [
            "ok": true,
            "snapshot": projectedSnapshot,
            "input_events_emitted": true,
            "desktop_state_mutated": true,
            "actionEvidence": evidence,
        ]
        if mutationUnknown {
            result["mutation_unknown"] = true
        }
        return result
    }

    private func postActionTargetWindowAbsentResult(
        evidence: [String: Any]
    ) -> [String: Any] {
        [
            "ok": true,
            "notes": [
                "The exact AX target window disappeared after input. Continue from a fresh GUI screenshot."
            ],
            "observationSkipped": true,
            "external_desktop_transition": true,
            "input_events_emitted": true,
            "desktop_state_mutated": true,
            "actionEvidence": evidence,
        ]
    }

    private func focusRuntimeElement(
        _ elementIndex: String?,
        app: String
    ) throws {
        guard let elementIndex, let index = Int(elementIndex) else {
            return
        }
        let snapshot = try service.currentSnapshot(for: app)
        guard let element = snapshot.elements[index]?.element else {
            throw ComputerUseError.stateUnavailable(
                "element \(elementIndex) has no backing accessibility object"
            )
        }
        var settable: DarwinBoolean = false
        guard AXUIElementIsAttributeSettable(
            element,
            kAXFocusedAttribute as CFString,
            &settable
        ) == .success, settable.boolValue else {
            throw ComputerUseError.stateUnavailable(
                "element \(elementIndex) cannot receive accessibility focus"
            )
        }
        guard AXUIElementSetAttributeValue(
            element,
            kAXFocusedAttribute as CFString,
            kCFBooleanTrue
        ) == .success else {
            throw ComputerUseError.stateUnavailable(
                "failed to focus element \(elementIndex)"
            )
        }
    }

    /// Reuses the last snapshot for an action instead of walking the tree
    /// again, when one batched live read shows that its window and target
    /// are unchanged. Returns nil whenever a fresh walk is needed.
    private func leasedActionTarget(
        tool: String,
        operation: [String: Any],
        app: String,
        appDescriptor: RunningAppDescriptor,
        expectedWindowHandle: String
    ) -> (snapshot: AppSnapshot, elementIndex: String?)? {
        // The resolver reads the snapshot stored under this exact query.
        guard let cached = service.snapshotsByApp[app.lowercased()],
              cached.app.pid == appDescriptor.pid else {
            return nil
        }
        let hasElementTarget = operation["semanticTarget"] != nil || operation["element"] != nil
        guard actionMaySkipInitialSnapshot(
                  tool: tool,
                  hasElementTarget: hasElementTarget,
                  hasCoordinates: operation["x"] != nil || operation["y"] != nil,
                  cachedGeneration: cached.providerProcessGeneration,
                  currentGeneration: macProcessGeneration(pid: appDescriptor.pid),
                  cachedWindowHandle: cached.targetWindowHandle,
                  requestedWindowHandle: expectedWindowHandle
              ),
              let recordedWindow = cached.rootWindowIdentity,
              AXLiveIdentity.readLive(cached.rootWindowElement) == recordedWindow,
              (try? validateRequestedWindow(operation, snapshot: cached)) != nil
        else {
            return nil
        }
        let resolved: String?
        do {
            resolved = try resolvedRuntimeElementIndex(
                operation,
                app: app,
                semanticTargetKey: "semanticTarget",
                elementKey: "element",
                targetLabel: "target"
            )
        } catch {
            return nil
        }
        guard let index = resolved else {
            return hasElementTarget ? nil : (cached, nil)
        }
        guard let position = Int(index),
              let record = cached.elements[position],
              let element = record.element,
              let recorded = record.liveIdentity,
              AXLiveIdentity.readLive(element) == recorded
        else {
            return nil
        }
        return (cached, index)
    }

    private func resolvedRuntimeElementIndex(
        _ operation: [String: Any],
        app: String,
        semanticTargetKey: String,
        elementKey: String,
        targetLabel: String
    ) throws -> String? {
        let snapshot = try service.currentSnapshot(for: app)
        let elements = try snapshot.providerElementDictionaries
        if let target = operation[semanticTargetKey] as? [String: Any] {
            let providerObjectPath = string(target["providerObjectPath"])
            if !providerObjectPath.isEmpty {
                let processGeneration = snapshot.providerProcessGeneration
                    ?? macProcessGeneration(pid: snapshot.app.pid)
                let providerBusName = "mac:\(snapshot.app.pid)"
                let requestedGeneration = string(target["processGeneration"])
                let requestedBusName = string(target["providerBusName"])
                guard processGeneration == macProcessGeneration(pid: snapshot.app.pid),
                      requestedGeneration == processGeneration,
                      requestedBusName == providerBusName
                else {
                    throw ComputerUseError.staleElementRef(
                        "stale_\(targetLabel)_ref provider_process_changed; no input was emitted."
                    )
                }
                let requestedWindowHandle = string(target["windowHandle"])
                let currentWindowHandle = snapshot.targetWindowHandle ?? ""
                guard requestedWindowHandle.isEmpty ||
                        requestedWindowHandle == currentWindowHandle
                else {
                    throw ComputerUseError.staleElementRef(
                        "stale_\(targetLabel)_ref provider_window_changed; no input was emitted."
                    )
                }
            }
            return String(try resolveProviderElementIndex(
                elements: elements,
                target: target,
                targetLabel: targetLabel,
                snapshot: snapshot,
                onProviderMismatch: { facts in
                    providerFailureDiagnostic("provider_identity_mismatch", facts: facts)
                },
                onProviderAmbiguity: { matches, count in
                    providerFailureDiagnostic("provider_identity_ambiguous") {
                        providerAmbiguityFacts(
                            target: target, candidates: matches,
                            providerMatchCount: count, snapshot: snapshot
                        )
                    }
                }
            ))
        }

        guard let requested = operation[elementKey] as? [String: Any],
              let index = int(requested["index"]) else {
            return nil
        }
        guard let current = elements.first(where: {
            int($0["index"]) == index
        }) else {
            throw ComputerUseError.staleElementRef(
                "stale_\(targetLabel)_ref element_index=\(index); no input was emitted."
            )
        }
        let requestedName = string(requested["name"])
        let requestedRole = string(requested["controlType"]).lowercased()
        if !requestedName.isEmpty,
           string(current["name"]) != requestedName
        {
            throw ComputerUseError.staleElementRef(
                "stale_\(targetLabel)_ref element_index=\(index) name_changed; no input was emitted."
            )
        }
        if !requestedRole.isEmpty,
           string(current["controlType"]).lowercased() != requestedRole
        {
            throw ComputerUseError.staleElementRef(
                "stale_\(targetLabel)_ref element_index=\(index) role_changed; no input was emitted."
            )
        }
        return String(index)
    }

    private func requiredElementIndex(_ value: String?) throws -> String {
        guard let value, !value.isEmpty else {
            throw ComputerUseError.invalidArguments(
                "This macOS operation requires a current element index"
            )
        }
        return value
    }
}

func resolveProviderElementIndex(
    elements: [[String: Any]],
    target: [String: Any],
    targetLabel: String,
    snapshot: AppSnapshot? = nil,
    identityRegistry: ProviderObjectIdentityRegistry<AXUIElement>? = nil,
    onProviderMismatch: (() -> [String: Any]) -> Void = { _ in },
    onProviderAmbiguity: ([[String: Any]], Int) -> Void = { _, _ in }
) throws -> Int {
    let requestedName = string(target["name"])
    let requestedRoles = Set(
        ([string(target["role"])] +
            (target["providerRoleAliases"] as? [String] ?? []))
            .filter { !$0.isEmpty }
            .map { $0.lowercased() }
    )
    let hasName = target["hasName"] as? Bool ?? false
    let requestedAutomationID = string(target["automationId"])
    let primaryRuntimeID: [Any] = target["runtimeId"].map { [$0] } ?? []
    let runtimeIDs = (primaryRuntimeID +
        (target["runtimeAliases"] as? [Any] ?? []))
        .compactMap { $0 as? [Int] }
    func roleMatches(_ element: [String: Any]) -> Bool {
        let role = string(element["controlType"]).lowercased()
        return requestedRoles.isEmpty ||
            requestedRoles.contains(where: {
                role == $0 || role.hasSuffix(" \($0)")
            })
    }
    func runtimeMatches(_ element: [String: Any]) -> Bool {
        let currentRuntimeID = element["runtimeId"] as? [Int] ?? []
        return runtimeIDs.isEmpty ||
            runtimeIDs.contains(currentRuntimeID)
    }
    func labelMatches(_ element: [String: Any]) -> Bool {
        if !requestedAutomationID.isEmpty {
            return string(element["automationId"]) == requestedAutomationID
        }
        return !hasName || string(element["name"]) == requestedName
    }
    func identityEvidenceMatches(_ element: [String: Any]) -> Bool {
        guard runtimeMatches(element) else {
            return false
        }
        return labelMatches(element)
    }
    func windowIdentityMatches(_ element: [String: Any]) -> Bool {
        for key in [
            "providerWindowBusName",
            "providerWindowObjectPath",
            "providerWindowProcessGeneration",
        ] {
            let requested = string(target[key])
            if !requested.isEmpty && requested != string(element[key]) {
                return false
            }
        }
        return true
    }
    let providerObjectPath = string(target["providerObjectPath"])
    if !providerObjectPath.isEmpty {
        let providerMatches = elements.filter {
            string($0["providerObjectPath"]) == providerObjectPath
        }
        let exactProviderMatches = providerMatches.filter {
            windowIdentityMatches($0) &&
                roleMatches($0) &&
                identityEvidenceMatches($0)
        }
        if exactProviderMatches.count == 1,
           let index = int(exactProviderMatches[0]["index"])
        {
            return index
        }
        if exactProviderMatches.count > 1, let snapshot,
           let index = equivalentProviderProjectionIndex(
               exactProviderMatches, target: target, snapshot: snapshot
           )
        {
            return index
        }
        // A provider-bound instance can move in the projection without being
        // replaced. The retained native object, not its old path or label,
        // must independently authorize this case on every action.
        if exactProviderMatches.isEmpty, !runtimeIDs.isEmpty,
           !providerMatches.isEmpty,
           !requestedRoles.isEmpty,
           string(target["locatorKind"]) == "provider_bound",
           providerMatches.allSatisfy({
               windowIdentityMatches($0) && roleMatches($0) && labelMatches($0)
           }), let snapshot,
           let index = equivalentProviderProjectionIndex(
               providerMatches, target: target, snapshot: snapshot,
               requireRetainedIdentity: true, identityRegistry: identityRegistry
           )
        {
            return index
        }
        let allowProviderRebind = target["allowProviderRebind"] as? Bool ?? false
        let locatorKind = string(target["locatorKind"])
        let stableProviderAnchor =
            locatorKind == "provider_bound" &&
            !requestedAutomationID.isEmpty &&
            !runtimeIDs.isEmpty
        let strictInstance = string(target["providerIdentityScope"]) == "process_instance"
        guard !strictInstance, allowProviderRebind,
              locatorKind == "semantic" || stableProviderAnchor else {
            let reason: String
            if providerMatches.isEmpty {
                reason = "identity_not_found"
            } else if exactProviderMatches.count > 1 {
                reason = "identity_ambiguous"
                onProviderAmbiguity(exactProviderMatches, providerMatches.count)
            } else {
                reason = "identity_reused_or_changed"
                onProviderMismatch {
                    let sampled = Array(providerMatches.prefix(4))
                    let primary = target["runtimeId"] as? [Int] ?? []
                    let aliases = target["runtimeAliases"] as? [[Int]] ?? []
                    return [
                        "resolved_snapshot_serial": snapshot?.diagnosticSnapshotSerial ?? 0,
                        "target_token": String(providerObjectPath.prefix(128)),
                        "target_scope": String(string(target["providerIdentityScope"]).prefix(64)),
                        "target_runtime": Array(primary.prefix(64)),
                        "target_runtime_length": primary.count,
                        "target_aliases": aliases.prefix(8).map { Array($0.prefix(64)) },
                        "target_alias_lengths": aliases.prefix(8).map(\.count),
                        "target_alias_count": aliases.count,
                        "runtime_enforced": !runtimeIDs.isEmpty,
                        "role_enforced": !requestedRoles.isEmpty,
                        "automation_id_enforced": !requestedAutomationID.isEmpty,
                        "name_enforced": requestedAutomationID.isEmpty && hasName,
                        "provider_match_count": providerMatches.count,
                        "exact_match_count": exactProviderMatches.count,
                        "truncated": providerMatches.count > sampled.count ||
                            primary.count > 64 || aliases.count > 8 ||
                            aliases.contains { $0.count > 64 } ||
                            sampled.contains { ($0["runtimeId"] as? [Int] ?? []).count > 64 },
                        "candidates": sampled.map { candidate -> [String: Any] in
                            let path = candidate["runtimeId"] as? [Int] ?? []
                            var facts: [String: Any] = [
                                "index": int(candidate["index"]) ?? -1,
                                "index_valid": int(candidate["index"]) != nil,
                                "runtime_path": Array(path.prefix(64)),
                                "runtime_path_length": path.count,
                                "runtime_matches": runtimeMatches(candidate),
                                "role_matches": roleMatches(candidate),
                                "window_matches": windowIdentityMatches(candidate),
                                "label_matches": labelMatches(candidate),
                                "automation_id_equal": string(candidate["automationId"]) == requestedAutomationID,
                                "name_equal": string(candidate["name"]) == requestedName,
                            ]
                            for key in [
                                "providerWindowBusName", "providerWindowObjectPath",
                                "providerWindowProcessGeneration",
                            ] {
                                facts[key + "_enforced"] = !string(target[key]).isEmpty
                                facts[key + "_matches"] = string(target[key]).isEmpty ||
                                    string(candidate[key]) == string(target[key])
                            }
                            return facts
                        },
                    ]
                }
            }
            throw ComputerUseError.staleElementRef(
                "stale_\(targetLabel)_ref provider_\(reason); no input was emitted."
            )
        }
    }

    let semanticMatches = elements.filter { element in
        windowIdentityMatches(element) &&
            roleMatches(element) &&
            identityEvidenceMatches(element)
    }
    guard semanticMatches.count == 1,
          let index = int(semanticMatches[0]["index"]) else {
        if semanticMatches.isEmpty {
            throw ComputerUseError.stateUnavailable(
                "semantic_target_not_found name='\(requestedName)'; no input was emitted."
            )
        }
        throw ComputerUseError.stateUnavailable(
            "semantic_target_ambiguous name='\(requestedName)' matches=\(semanticMatches.count); no input was emitted."
        )
    }
    return index
}

func equivalentProviderProjectionIndex(
    _ candidates: [[String: Any]], target: [String: Any], snapshot: AppSnapshot,
    requireRetainedIdentity: Bool = false,
    identityRegistry: ProviderObjectIdentityRegistry<AXUIElement>? = nil,
    objectsEqual: (AXUIElement, AXUIElement) -> Bool = { CFEqual($0, $1) }
) -> Int? {
    let token = string(target["providerObjectPath"])
    guard !candidates.isEmpty, candidates.count > 1 || requireRetainedIdentity,
          !token.isEmpty,
          string(target["providerIdentityScope"]) == "process_instance",
          let window = snapshot.targetWindowHandle, !window.isEmpty,
          let generation = snapshot.providerProcessGeneration, !generation.isEmpty
    else { return nil }
    let ownership = [
        "providerBusName": "mac:\(snapshot.app.pid)",
        "processGeneration": generation,
        "providerWindowBusName": "mac:\(snapshot.app.pid)",
        "providerWindowObjectPath": "window:\(window)",
        "providerWindowProcessGeneration": generation,
    ]
    guard ownership.allSatisfy({ string(target[$0.key]) == $0.value })
    else { return nil }

    var indices = Set<Int>()
    var records: [ElementRecord] = []
    for candidate in candidates {
        guard ownership.allSatisfy({ string(candidate[$0.key]) == $0.value }),
              string(candidate["providerIdentityScope"]) == "process_instance",
              string(candidate["providerObjectPath"]) == token,
              let index = int(candidate["index"]), indices.insert(index).inserted,
              let record = snapshot.elements[index], record.index == index,
              !record.isSyntheticText, record.boundProviderObjectPath == token,
              record.runtimeID == (candidate["runtimeId"] as? [Int]),
              let element = record.element
        else { return nil }
        var owner: pid_t = 0
        guard AXUIElementGetPid(element, &owner) == .success,
              owner == snapshot.app.pid else { return nil }
        if requireRetainedIdentity &&
            (string(candidate["controlType"]) != (record.providerRole ?? "unknown") ||
             string(candidate["name"]) != (record.name ?? "") ||
             string(candidate["automationId"]) != (record.identifier ?? "") ||
             !matchesRetainedMacProviderObject(
                element, token: token, scope: "mac:\(snapshot.app.pid):\(generation)",
                registry: identityRegistry
             ))
        {
            return nil
        }
        records.append(record)
    }
    guard let first = records.first, let object = first.element,
          records.dropFirst().allSatisfy({
              guard let other = $0.element else { return false }
              return objectsEqual(object, other) && objectsEqual(other, object) &&
                  Set($0.rawActions.map { $0.lowercased() }) ==
                      Set(first.rawActions.map { $0.lowercased() }) &&
                  $0.localFrame == first.localFrame
          })
    else { return nil }
    // Preserve the requested projection when present; equality authorizes an
    // alias only after every retained object and its ownership were checked.
    let primary = target["runtimeId"] as? [Int]
    return records.first(where: { $0.runtimeID == primary })?.index
        ?? records.map(\.index).min()
}

/// Whether an action may act on the last snapshot after a live check
/// instead of walking the tree first. Only window-bound actions whose
/// target is a recorded element, or that need no element, qualify; a
/// coordinate click hit-tests the snapshot and so needs a fresh one.
func actionMaySkipInitialSnapshot(
    tool: String,
    hasElementTarget: Bool,
    hasCoordinates: Bool,
    cachedGeneration: String?,
    currentGeneration: String,
    cachedWindowHandle: String?,
    requestedWindowHandle: String
) -> Bool {
    guard !requestedWindowHandle.isEmpty,
          cachedWindowHandle == requestedWindowHandle,
          cachedGeneration == currentGeneration,
          !hasCoordinates else {
        return false
    }
    switch tool {
    case "click", "set_value", "select", "increment", "decrement":
        return hasElementTarget
    case "type", "keypress", "scroll":
        return true
    default:
        return false
    }
}
