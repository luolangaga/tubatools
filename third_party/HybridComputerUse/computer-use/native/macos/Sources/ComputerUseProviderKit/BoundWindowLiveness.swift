import AppKit
import ApplicationServices
import Foundation
import os

private let windowLivenessLogger = Logger(
    subsystem: "org.computer-use.provider",
    category: "window-liveness"
)

func windowLivenessDiagnosticJSON(
    event: String, requestID: String, phase: String, facts: [String: Any]
) -> String? {
    // Scalars only: no tree, title, input text, handle or native window ID.
    let scalarKeys: Set<String> = [
        "status", "matches", "readable", "positive", "registered",
        "positive_native_id", "has_handle", "validated", "complete",
        "ax_complete", "ax_count", "ax_unknown", "ax_matches",
        "cg_nil", "cg_cast", "cg_count", "cg_unknown", "cg_matches",
        "native_comparable", "native_empty", "value_nil", "type_id",
        "array_type_id", "ax_element_type_id", "array_count", "cast_success",
    ]
    var record = facts.filter {
        scalarKeys.contains($0.key) && ($0.value is Bool || $0.value is Int)
    }
    for key in ["ax_state", "cg_state", "result", "error_kind", "focus"] {
        if let value = facts[key] as? String {
            record[key] = String(value.prefix(64))
        }
    }
    record["event"] = String(event.prefix(64))
    record["request_id"] = String(requestID.prefix(128))
    record["phase"] = String(phase.prefix(64))
    record["provider_pid"] = Int(getpid())
    guard let data = try? JSONSerialization.data(
        withJSONObject: record, options: [.sortedKeys]
    ), data.count <= 2048 else {
        return nil
    }
    return String(data: data, encoding: .utf8)
}

func windowLivenessDiagnostic(_ event: String, _ facts: [String: Any]) {
    let context = Thread.current.threadDictionary
    guard let request = context["computer-use.liveness.request"] as? String,
          !request.isEmpty,
          let json = windowLivenessDiagnosticJSON(
            event: event, requestID: request,
            phase: context["computer-use.liveness.phase"] as? String ?? "",
            facts: facts
          ) else {
        return
    }
    let count = context["computer-use.liveness.count"] as? Int ?? 0
    guard count < 128 else { return }
    context["computer-use.liveness.count"] = count + 1
    windowLivenessLogger.notice("\(json, privacy: .public)")
    if count == 127 {
        windowLivenessLogger.notice("window-liveness request event limit reached")
    }
}

func windowLivenessErrorKind(_ error: Error) -> String {
    guard let error = error as? ComputerUseError else {
        return "other"
    }
    switch error {
    case .stateUnavailable: return "state_unavailable"
    case .providerResetRequired: return "provider_reset_required"
    case .staleWindowRef: return "stale_window_ref"
    case .staleAppRef: return "stale_app_ref"
    case .staleElementRef: return "stale_element_ref"
    default: return "other_computer_use_error"
    }
}

enum BoundWindowLiveness: Equatable {
    case present
    case absent
    case unknown
}

func windowHandleInventoryLiveness(
    _ handles: [String?]?,
    requestedHandle: String
) -> BoundWindowLiveness {
    guard !requestedHandle.isEmpty, let handles else {
        return .unknown
    }
    if handles.contains(where: { $0 == requestedHandle }) {
        return .present
    }
    return handles.contains(where: { $0 == nil || $0 == "" })
        ? .unknown : .absent
}

func corroboratedWindowLiveness(
    accessibility: BoundWindowLiveness,
    windowServer: BoundWindowLiveness,
    nativeIdentityComparable: Bool = true,
    nativeInventoryEmpty: Bool = false
) -> BoundWindowLiveness {
    if windowServer == .present {
        return accessibility == .absent ? .unknown : .present
    }
    if !nativeIdentityComparable && !nativeInventoryEmpty {
        return accessibility == .present ? .present : .unknown
    }
    switch (accessibility, windowServer) {
    case (.absent, .absent):
        return .absent
    case (.present, .absent), (.absent, .present):
        return .unknown
    case (.present, _), (_, .present):
        return .present
    default:
        return .unknown
    }
}

func windowServerHandleInventory(
    _ records: [[String: Any]]?,
    targetPID: pid_t,
    handle: (CGWindowID) -> String
) -> [String?]? {
    guard let records else {
        return nil
    }
    var handles: [String?] = []
    for record in records {
        guard let owner = record[kCGWindowOwnerPID as String] as? NSNumber else {
            handles.append(nil)
            continue
        }
        guard owner.int32Value == targetPID else {
            continue
        }
        guard let number = record[kCGWindowNumber as String] as? NSNumber,
              number.int64Value > 0,
              number.uint64Value <= UInt64(UInt32.max) else {
            handles.append(nil)
            continue
        }
        handles.append(handle(number.uint32Value))
    }
    return handles
}

func accessibilityWindowHandleInventory<Node>(
    _ windows: [Node]?,
    requestedHandle: String,
    ownedReadableWindow: (Node) -> Bool,
    axHandle: (Node) -> String,
    nativeHandle: (Node) -> String?
) -> [String?]? {
    windows?.map { window in
        guard ownedReadableWindow(window) else {
            return nil
        }
        let fallback = axHandle(window)
        if fallback == requestedHandle {
            return fallback
        }
        // Failed native identity reads are not comparable non-matches.
        return nativeHandle(window)
    }
}

func boundWindowLiveness(
    pid: pid_t,
    windowHandle: String,
    nativeIdentityComparable: Bool = false
) -> BoundWindowLiveness {
    guard !windowHandle.isEmpty else {
        return .unknown
    }
    let inventory = axWindowInventory(pid: pid)
    let axHandles = accessibilityWindowHandleInventory(
        inventory.complete ? inventory.windows : nil,
        requestedHandle: windowHandle,
        ownedReadableWindow: { window in
            var owner: pid_t = 0
            let ownerStatus = AXUIElementGetPid(window, &owner)
            guard ownerStatus == .success, owner == pid else {
                windowLivenessDiagnostic("ax_window_owner", [
                    "status": Int(ownerStatus.rawValue), "matches": owner == pid,
                ])
                return false
            }
            var role: CFTypeRef?
            let status = AXUIElementCopyAttributeValue(
                window, kAXRoleAttribute as CFString, &role
            )
            let readable = status == .success && stringValue(role) != nil
            windowLivenessDiagnostic("ax_window_role", [
                "status": Int(status.rawValue), "readable": readable,
            ])
            return readable
        },
        axHandle: { macAXWindowHandle(pid: pid, window: $0) },
        nativeHandle: { window in
            var number: CGWindowID = 0
            let status = AXUIElementGetWindow(window, &number)
            let handle = status == .success && number > 0
                ? macWindowHandle(pid: pid, windowID: number) : nil
            windowLivenessDiagnostic("ax_window_native_identity", [
                "status": Int(status.rawValue), "positive": number > 0,
                "matches": handle == windowHandle,
            ])
            return handle
        }
    )
    // Include hidden/minimized/offscreen windows; capture visibility is not
    // window lifetime. A nil CG query must remain unavailable, never empty.
    let rawRecords = CGWindowListCopyWindowInfo(
        .optionAll, kCGNullWindowID
    )
    let records = rawRecords as? [[String: Any]]
    let nativeHandles = windowServerHandleInventory(
        records, targetPID: pid,
        handle: { macWindowHandle(pid: pid, windowID: $0) }
    )
    let axState = windowHandleInventoryLiveness(axHandles, requestedHandle: windowHandle)
    let cgState = windowHandleInventoryLiveness(nativeHandles, requestedHandle: windowHandle)
    let result = corroboratedWindowLiveness(
        accessibility: axState,
        windowServer: cgState,
        nativeIdentityComparable: nativeIdentityComparable,
        nativeInventoryEmpty: nativeHandles?.isEmpty == true
    )
    windowLivenessDiagnostic("inventory_result", [
        "ax_complete": inventory.complete, "ax_count": inventory.windows.count,
        "ax_unknown": axHandles?.filter { $0 == nil }.count ?? -1,
        "ax_matches": axHandles?.filter { $0 == windowHandle }.count ?? -1,
        "cg_nil": rawRecords == nil, "cg_cast": records != nil,
        "cg_count": nativeHandles?.count ?? -1,
        "cg_unknown": nativeHandles?.filter { $0 == nil }.count ?? -1,
        "cg_matches": nativeHandles?.filter { $0 == windowHandle }.count ?? -1,
        "native_comparable": nativeIdentityComparable,
        "native_empty": nativeHandles?.isEmpty == true,
        "ax_state": String(describing: axState),
        "cg_state": String(describing: cgState),
        "result": String(describing: result),
    ])
    return result
}

extension ComputerUseService {
    func rememberNativeWindowIdentity(_ snapshot: AppSnapshot) {
        rememberNativeWindowIdentity(
            pid: snapshot.app.pid,
            windowID: snapshot.targetWindowID,
            windowHandle: snapshot.targetWindowHandle
        )
    }

    func rememberNativeWindowIdentity(
        pid: pid_t,
        windowID: CGWindowID?,
        windowHandle: String?
    ) {
        guard let windowID, windowID > 0,
              let windowHandle,
              windowHandle == macWindowHandle(pid: pid, windowID: windowID) else {
            windowLivenessDiagnostic("provenance", [
                "registered": false,
                "positive_native_id": (windowID ?? 0) > 0,
                "has_handle": windowHandle != nil,
            ])
            return
        }
        // Handles include PID and generation. Keep issuance provenance across
        // later observations of sibling windows, just like semantic refs.
        nativeWindowHandles.insert(windowHandle)
        windowLivenessDiagnostic("provenance", ["registered": true])
    }
}

extension ComputerUseRuntimeBridge {
    func boundWindowInspector(
        windowHandle: String,
        inspect: @escaping (pid_t, String, Bool) -> BoundWindowLiveness = {
            boundWindowLiveness(pid: $0, windowHandle: $1, nativeIdentityComparable: $2)
        }
    ) -> (pid_t, String) -> BoundWindowLiveness {
        let nativeComparable = service.nativeWindowHandles.contains(windowHandle)
        return { pid, handle in
            inspect(pid, handle, nativeComparable)
        }
    }
}

func checkedOrderedWindowContinuity(
    _ focusedContinuity: OrderedInputWindowContinuity,
    validateLiveness: () throws -> Void
) throws -> OrderedInputWindowContinuity {
    guard focusedContinuity != .same else {
        return .same
    }
    try validateLiveness()
    // A focus change prevents global input but does not retire a live window.
    return .unknown
}

func validateBoundWindowLiveness(
    appIdentity: [String: Any],
    windowHandle: String,
    resolveApp: () throws -> RunningAppDescriptor,
    inspectWindow: (pid_t, String) -> BoundWindowLiveness = {
        boundWindowLiveness(pid: $0, windowHandle: $1)
    }
) throws {
    guard !windowHandle.isEmpty else {
        return
    }
    let before = try resolveApp()
    try validateRequestedIdentity(["appIdentity": appIdentity], app: before)
    windowLivenessDiagnostic("process_before", ["validated": true])
    let liveness = inspectWindow(before.pid, windowHandle)
    let after = try resolveApp()
    try validateRequestedIdentity(["appIdentity": appIdentity], app: after)
    windowLivenessDiagnostic("process_after", [
        "validated": true, "result": String(describing: liveness),
    ])
    if liveness == .absent {
        throw ComputerUseError.staleWindowRef(
            "stale_window_ref provider_window_changed."
        )
    }
}
