import AppKit
import ApplicationServices
import Foundation

enum OrderedInputWindowContinuity: String {
    case same
    case changed
    case unknown
}

struct OrderedInputRuntimeDependencies {
    let inspectWindow: () throws -> OrderedInputWindowContinuity
    let dispatchKey: (String) throws -> Void
    let prepareFinalObservationReadiness: () -> Void
    let awaitFinalObservationReadiness: () -> Void
    let finalSnapshot: () throws -> [String: Any]
}

extension ComputerUseRuntimeBridge {
    func executeOrderedInput(
        _ operation: [String: Any]
    ) -> [String: Any] {
        guard let request = operation["orderedInput"] as? [String: Any] else {
            return orderedInputInvalidRequestResult()
        }
        let app = string(request["app"])
        let appIdentity = request["app_identity"] as? [String: Any] ?? [:]
        let expectedWindow =
            request["expected_window"] as? [String: Any] ?? [:]
        let windowHandle = string(request["window_handle"])
        let finalObservation =
            request["final_observation"] as? [String: Any] ?? [:]
        let descriptor = try? resolveOrderedInputApp(appIdentity)
        var focusedElementBeforeFinalKey: AXUIElement?
        let inspectLiveness = boundWindowInspector(
            windowHandle: windowHandle
        )
        var inspectionIndex = 0

        let dependencies = OrderedInputRuntimeDependencies(
            inspectWindow: {
                inspectionIndex += 1
                Thread.current.threadDictionary["computer-use.liveness.phase"] =
                    "ordered_precondition_\(inspectionIndex)"
                return try orderedInputWindowContinuity(
                    appIdentity: appIdentity,
                    expectedWindow: expectedWindow,
                    windowHandle: windowHandle,
                    inspectLiveness: inspectLiveness
                )
            },
            dispatchKey: { key in
                _ = try resolveOrderedInputApp(appIdentity)
                try dispatchFocusedOrderedKeypress(key)
            },
            prepareFinalObservationReadiness: {
                focusedElementBeforeFinalKey = descriptor.flatMap {
                    orderedInputFocusedElement(targetPID: $0.pid)
                }
            },
            awaitFinalObservationReadiness: {
                guard let descriptor else {
                    return
                }
                _ = awaitOrderedInputFocusConvergence(
                    baseline: focusedElementBeforeFinalKey,
                    timeout: 0.75,
                    stableFor: 0.08,
                    now: {
                        ProcessInfo.processInfo.systemUptime
                    },
                    pause: { duration in
                        RunLoop.current.run(
                            mode: .default,
                            before: Date(timeIntervalSinceNow: duration)
                        )
                    },
                    current: {
                        orderedInputFocusedElement(
                            targetPID: descriptor.pid
                        )
                    },
                    isSame: CFEqual
                )
            },
            finalSnapshot: {
                Thread.current.threadDictionary["computer-use.liveness.phase"] =
                    "ordered_final"
                let snapshot = try self.refreshIdentityBoundSnapshot(
                    for: app,
                    appIdentity: appIdentity,
                    windowHandle: windowHandle,
                    resolveApp: { try resolveOrderedInputApp(appIdentity) },
                    capture: { descriptor in
                        try SnapshotBuilder.build(
                            for: descriptor,
                            textLimit: try snapshotTextLimit(
                                finalObservation["text_limit"]
                            ),
                            treeLimits: AccessibilityTreeLimits(
                                maxNodeCount: positiveInt(
                                    finalObservation["max_tree_nodes"]
                                ) ?? AccessibilityTreeLimits.defaultMaxNodeCount,
                                maxDepth: positiveInt(
                                    finalObservation["max_tree_depth"]
                                ) ?? AccessibilityTreeLimits.defaultMaxDepth
                            ),
                            recoveryPolicy: .readOnly,
                            expectedWindowHandle: windowHandle
                        )
                    }
                )
                return try snapshot.providerDictionary()
            }
        )
        return executeOrderedInputTransaction(
            request: request,
            dependencies: dependencies
        )
    }
}

func dispatchFocusedOrderedKeypress(
    _ key: String,
    postGlobally: (String) throws -> Void = InputSimulation.pressKeyGlobally
) throws {
    // Window continuity already proved the requested owner is desktop focus.
    // The focused responder may live in a service process rather than app PID.
    try postGlobally(key)
}

func executeOrderedInputTransaction(
    request: [String: Any],
    dependencies: OrderedInputRuntimeDependencies
) -> [String: Any] {
    guard let requestedSteps = request["steps"] as? [[String: Any]],
          !requestedSteps.isEmpty else {
        return orderedInputInvalidRequestResult()
    }

    var results: [[String: Any]] = []
    var anyEmission = false
    var anyPartial = false

    for (position, requestedStep) in requestedSteps.enumerated() {
        let actionIndex = int(requestedStep["action_index"]) ?? -1
        let key = string(requestedStep["key"])
        let continuity: OrderedInputWindowContinuity
        var inspectionError: Error?
        do {
            continuity = try dependencies.inspectWindow()
        } catch {
            inspectionError = error
            continuity = (error as? ComputerUseError)?.staleIdentityFailure
                == nil ? .unknown : .changed
        }
        guard continuity == .same else {
            let stale = (inspectionError as? ComputerUseError)?
                .staleIdentityFailure
            results.append([
                "action_index": actionIndex,
                "status": "failed",
                "dispatch_attempted": false,
                "emission": "none",
                "window_continuity": continuity.rawValue,
            ])
            appendOrderedInputNotAttempted(
                requestedSteps.dropFirst(position + 1),
                to: &results
            )
            var result = orderedInputFailureResult(
                steps: results,
                stoppedActionIndex: actionIndex,
                firstUnattemptedActionIndex: actionIndex,
                failureCode: stale?.code ?? (continuity == .changed
                    ? "stale_window_ref"
                    : "state_unavailable"),
                failurePhase: "precondition",
                failureSubject: stale?.subject
                    ?? (continuity == .changed ? "window" : nil),
                underlyingFailureCode: stale?.reason,
                anyEmission: anyEmission,
                anyPartial: anyPartial
            )
            if let inspectionError, requiresProviderReset(inspectionError) {
                result["providerResetRequired"] = true
            }
            return result
        }

        do {
            if position == requestedSteps.count - 1 {
                dependencies.prepareFinalObservationReadiness()
            }
            try dependencies.dispatchKey(key)
            anyEmission = true
            results.append([
                "action_index": actionIndex,
                "status": "completed",
                "dispatch_attempted": true,
                "emission": "complete",
                "window_continuity": continuity.rawValue,
            ])
        } catch {
            let stale = (error as? ComputerUseError)?.staleIdentityFailure
            let partial = orderedInputFailureMayHaveEmitted(error)
            anyEmission = anyEmission || partial
            anyPartial = anyPartial || partial
            results.append([
                "action_index": actionIndex,
                "status": "failed",
                "dispatch_attempted": stale == nil,
                "emission": partial ? "partial" : "none",
                "window_continuity": stale == nil
                    ? continuity.rawValue
                    : OrderedInputWindowContinuity.changed.rawValue,
            ])
            appendOrderedInputNotAttempted(
                requestedSteps.dropFirst(position + 1),
                to: &results
            )
            let firstUnattempted = stale != nil ? actionIndex
                : (position + 1 < requestedSteps.count
                    ? int(requestedSteps[position + 1]["action_index"])
                    : nil)
            return orderedInputFailureResult(
                steps: results,
                stoppedActionIndex: actionIndex,
                firstUnattemptedActionIndex: firstUnattempted,
                failureCode: stale?.code ?? "input_emission_failed",
                failurePhase: partial ? "emission" : "precondition",
                failureSubject: stale?.subject,
                underlyingFailureCode: stale?.reason,
                anyEmission: anyEmission,
                anyPartial: anyPartial
            )
        }
    }

    dependencies.awaitFinalObservationReadiness()
    do {
        return [
            "contract_version": 1,
            "ok": true,
            "steps": results,
            "snapshot": try dependencies.finalSnapshot(),
            "mutation": orderedInputMutation(
                anyEmission: anyEmission,
                anyPartial: anyPartial,
                mutationUnknown: false
            ),
            "retry_same_request": false,
        ]
    } catch {
        let stale = (error as? ComputerUseError)?.staleIdentityFailure
        var failure: [String: Any] = [
            "code": stale?.code ?? "state_unavailable",
            "underlying_code": stale?.reason ?? "final_snapshot_failed",
            "phase": "observation",
            "facts_complete": true,
        ]
        if let stale {
            failure["subject"] = stale.subject
        }
        var result: [String: Any] = [
            "contract_version": 1,
            "ok": false,
            "steps": results,
            "failure": failure,
            "mutation": orderedInputMutation(
                anyEmission: anyEmission,
                anyPartial: anyPartial,
                mutationUnknown: anyEmission
            ),
            "retry_same_request": false,
        ]
        if requiresProviderReset(error) {
            result["providerResetRequired"] = true
        }
        return result
    }
}

func orderedInputFailureMayHaveEmitted(_ error: Error) -> Bool {
    if let reported = error as? InputEmissionReportingError {
        return reported.inputEventsEmitted
    }
    if let computerError = error as? ComputerUseError {
        switch computerError {
        case .invalidArguments, .unsupportedTool, .capabilityUnsupported,
             .staleAppRef, .staleWindowRef:
            return false
        default:
            break
        }
    }
    return true
}

func orderedInputFocusedElement(targetPID: pid_t) -> AXUIElement? {
    let systemWide = AXUIElementCreateSystemWide()
    if let focused = copyElement(
        systemWide,
        attribute: kAXFocusedUIElementAttribute
    ),
    pid(of: focused) == targetPID {
        return focused
    }
    return copyElement(
        AXUIElementCreateApplication(targetPID),
        attribute: kAXFocusedUIElementAttribute
    )
}

func awaitOrderedInputFocusConvergence<Node>(
    baseline: Node?,
    timeout: TimeInterval,
    stableFor: TimeInterval,
    now: () -> TimeInterval,
    pause: (TimeInterval) -> Void,
    current: () -> Node?,
    isSame: (Node, Node) -> Bool
) -> Bool {
    let deadline = now() + timeout
    var stableCandidate: Node?
    var stableSince: TimeInterval?

    while true {
        let sampledAt = now()
        let observed = current()
        let changedFromBaseline: Bool
        if let observed {
            changedFromBaseline = baseline.map {
                !isSame($0, observed)
            } ?? true
        } else {
            changedFromBaseline = false
        }

        if changedFromBaseline, let observed {
            if let stableCandidate, isSame(stableCandidate, observed) {
                if sampledAt - (stableSince ?? sampledAt) >= stableFor {
                    return true
                }
            } else {
                stableCandidate = observed
                stableSince = sampledAt
            }
        } else {
            stableCandidate = nil
            stableSince = nil
        }

        let remaining = deadline - now()
        if remaining <= 0 {
            return false
        }
        pause(min(0.01, remaining))
    }
}

private func appendOrderedInputNotAttempted(
    _ steps: ArraySlice<[String: Any]>,
    to results: inout [[String: Any]]
) {
    for step in steps {
        results.append([
            "action_index": int(step["action_index"]) ?? -1,
            "status": "not_attempted",
            "dispatch_attempted": false,
            "emission": "none",
        ])
    }
}

private func orderedInputFailureResult(
    steps: [[String: Any]],
    stoppedActionIndex: Int,
    firstUnattemptedActionIndex: Int?,
    failureCode: String,
    failurePhase: String,
    failureSubject: String?,
    underlyingFailureCode: String? = nil,
    anyEmission: Bool,
    anyPartial: Bool
) -> [String: Any] {
    var failure: [String: Any] = [
        "code": failureCode,
        "phase": failurePhase,
        "facts_complete": true,
    ]
    if let failureSubject, !failureSubject.isEmpty {
        failure["subject"] = failureSubject
    }
    if let underlyingFailureCode {
        failure["underlying_code"] = underlyingFailureCode
    }
    var result: [String: Any] = [
        "contract_version": 1,
        "ok": false,
        "steps": steps,
        "stopped_action_index": stoppedActionIndex,
        "failure": failure,
        "mutation": orderedInputMutation(
            anyEmission: anyEmission,
            anyPartial: anyPartial,
            mutationUnknown: anyEmission
        ),
        "retry_same_request": false,
    ]
    if let firstUnattemptedActionIndex {
        result["first_unattempted_action_index"] =
            firstUnattemptedActionIndex
    }
    return result
}

private func orderedInputMutation(
    anyEmission: Bool,
    anyPartial: Bool,
    mutationUnknown: Bool
) -> [String: Any] {
    [
        "input_events_emitted": anyEmission,
        "partial_input_possible": anyPartial,
        "desktop_state_mutated": anyEmission,
        "mutation_unknown": mutationUnknown,
    ]
}

private func orderedInputInvalidRequestResult() -> [String: Any] {
    [
        "contract_version": 1,
        "ok": false,
        "steps": [],
        "failure": [
            "code": "invalid_request",
            "phase": "precondition",
            "facts_complete": true,
        ],
        "mutation": orderedInputMutation(
            anyEmission: false,
            anyPartial: false,
            mutationUnknown: false
        ),
        "retry_same_request": false,
    ]
}

private func orderedInputWindowContinuity(
    appIdentity: [String: Any],
    expectedWindow: [String: Any],
    windowHandle: String,
    inspectLiveness: (pid_t, String) -> BoundWindowLiveness
) throws -> OrderedInputWindowContinuity {
    let descriptor = try resolveOrderedInputApp(appIdentity)
    let applicationContinuity = orderedInputApplicationContinuity(
        targetPID: descriptor.pid,
        frontmostPID: focusedApplicationPID()
    )
    let validateLiveness = {
        try validateBoundWindowLiveness(
            appIdentity: appIdentity,
            windowHandle: windowHandle,
            resolveApp: { try resolveOrderedInputApp(appIdentity) },
            inspectWindow: inspectLiveness
        )
    }
    guard applicationContinuity == .same else {
        windowLivenessDiagnostic("focus", ["focus": "other_application"])
        return try checkedOrderedWindowContinuity(
            applicationContinuity, validateLiveness: validateLiveness
        )
    }
    guard string(expectedWindow["providerBusName"]) ==
            "mac:\(descriptor.pid)",
          string(expectedWindow["processGeneration"]) ==
            macProcessGeneration(pid: descriptor.pid)
    else {
        throw ComputerUseError.staleWindowRef(
            "stale_window_ref provider_window_changed."
        )
    }
    let focused = orderedInputAXWindow(pid: descriptor.pid)
    guard focused.complete, let window = focused.window else {
        windowLivenessDiagnostic("focus", [
            "focus": "window_unavailable", "complete": focused.complete,
        ])
        return try checkedOrderedWindowContinuity(
            .unknown, validateLiveness: validateLiveness
        )
    }
    let continuity: OrderedInputWindowContinuity = orderedInputWindowHandle(
        pid: descriptor.pid,
        window: window
    ) == windowHandle ? .same : .changed
    if continuity != .same {
        windowLivenessDiagnostic("focus", ["focus": "different_window"])
    }
    return try checkedOrderedWindowContinuity(
        continuity, validateLiveness: validateLiveness
    )
}

func orderedInputApplicationContinuity(
    targetPID: pid_t,
    frontmostPID: pid_t?
) -> OrderedInputWindowContinuity {
    guard let frontmostPID else {
        return .unknown
    }
    return frontmostPID == targetPID ? .same : .changed
}

private func orderedInputAXWindow(
    pid: pid_t
) -> (window: AXUIElement?, complete: Bool) {
    let appElement = AXUIElementCreateApplication(pid)
    let systemWide = AXUIElementCreateSystemWide()
    if let window = strictFocusedAXWindow(
        appElement: appElement,
        appPID: pid,
        systemWide: systemWide
    ) {
        return (window, true)
    }

    var value: CFTypeRef?
    guard AXUIElementCopyAttributeValue(
        appElement,
        kAXWindowsAttribute as CFString,
        &value
    ) == .success,
    let windows = value as? [AXUIElement]
    else {
        return (nil, false)
    }
    let uniqueWindow = uniqueUsableWindowCandidate(windows) {
        orderedInputWindowIsUsable(
            role: stringValue(of: $0, attribute: kAXRoleAttribute),
            minimized: boolValue(of: $0, attribute: kAXMinimizedAttribute)
        )
    }
    return uniqueWindow.map { ($0, true) } ?? (nil, false)
}

func orderedInputWindowIsUsable(
    role: String?,
    minimized: Bool?
) -> Bool {
    role == kAXWindowRole as String && minimized == false
}

func resolveOrderedInputApp(
    _ identity: [String: Any],
    runningApplication: (pid_t) -> NSRunningApplication? = {
        NSRunningApplication(processIdentifier: $0)
    }
) throws -> RunningAppDescriptor {
    let bus = string(identity["providerBusName"])
    guard bus.hasPrefix("mac:"),
          let pid = pid_t(bus.dropFirst(4)), pid > 0,
          !string(identity["processGeneration"]).isEmpty else {
        throw ComputerUseError.stateUnavailable(
            "The bound application identity is unavailable."
        )
    }
    // Resolve the bound process only. Name discovery can launch a replacement.
    guard let application = runningApplication(pid),
          !application.isTerminated else {
        throw ComputerUseError.staleAppRef(
            "stale_app_ref provider_process_changed."
        )
    }
    if let bundleIdentifier = application.bundleIdentifier,
       AppSafetyPolicy.isBlocked(bundleIdentifier: bundleIdentifier) {
        throw AppSafetyPolicy.permissionDenied(bundleIdentifier: bundleIdentifier)
    }
    let descriptor = RunningAppDescriptor(
        name: AppDiscovery.appName(application),
        bundleIdentifier: application.bundleIdentifier,
        pid: application.processIdentifier,
        runningApplication: application
    )
    try validateRequestedIdentity(["appIdentity": identity], app: descriptor)
    return descriptor
}

func orderedInputFinalSnapshot(
    appIdentity: [String: Any],
    windowHandle: String,
    resolveApp: () throws -> RunningAppDescriptor,
    capture: (RunningAppDescriptor) throws -> AppSnapshot,
    inspectWindow: (pid_t, String) -> BoundWindowLiveness = {
        boundWindowLiveness(pid: $0, windowHandle: $1)
    }
) throws -> AppSnapshot {
    let descriptor = try resolveApp()
    try validateRequestedIdentity(["appIdentity": appIdentity], app: descriptor)
    let snapshot: AppSnapshot
    do {
        snapshot = try capture(descriptor)
    } catch {
        let captureError = error
        windowLivenessDiagnostic("capture_failure", [
            "error_kind": windowLivenessErrorKind(captureError),
        ])
        if (captureError as? ComputerUseError)?.staleIdentityFailure != nil
            || requiresProviderReset(captureError) {
            throw captureError
        }
        // Failed focus/capture alone is not stale. Recheck the process and
        // corroborate the exact window's absence without launching or input.
        do {
            if !windowHandle.isEmpty {
                try validateBoundWindowLiveness(
                    appIdentity: appIdentity,
                    windowHandle: windowHandle,
                    resolveApp: resolveApp,
                    inspectWindow: inspectWindow
                )
            } else {
                let current = try resolveApp()
                try validateRequestedIdentity(
                    ["appIdentity": appIdentity], app: current
                )
            }
        } catch {
            if (error as? ComputerUseError)?.staleIdentityFailure != nil
                || requiresProviderReset(error) {
                throw error
            }
        }
        throw captureError
    }
    let current = try resolveApp()
    try validateRequestedIdentity(["appIdentity": appIdentity], app: current)
    try validateRequestedIdentity(["appIdentity": appIdentity], app: snapshot.app)
    if !windowHandle.isEmpty && snapshot.targetWindowHandle != windowHandle {
        try validateBoundWindowLiveness(
            appIdentity: appIdentity, windowHandle: windowHandle,
            resolveApp: resolveApp, inspectWindow: inspectWindow
        )
        throw ComputerUseError.stateUnavailable(
            "The bound window could not be observed; its disappearance is unconfirmed."
        )
    }
    try validateRequestedWindow(
        ["targetWindowHandle": windowHandle], snapshot: snapshot
    )
    return snapshot
}

private func orderedInputWindowHandle(
    pid: pid_t,
    window: AXUIElement
) -> String {
    canonicalMacAXWindowHandle(pid: pid, window: window)
}
