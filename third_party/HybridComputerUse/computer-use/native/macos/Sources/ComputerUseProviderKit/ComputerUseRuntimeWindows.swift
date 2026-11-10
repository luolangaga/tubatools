import AppKit
import ApplicationServices
import CoreGraphics
import Foundation

extension ComputerUseRuntimeBridge {
    func listWindows(_ operation: [String: Any]) throws -> [String: Any] {
        let app = try resolveRuntimeApplication(query: requiredString(operation, "app"), identity: runtimeAppIdentity(operation))
        try validateRequestedIdentity(operation, app: app)
        let inventory = axWindowInventory(pid: app.pid)
        guard inventory.complete else {
            throw ComputerUseError.stateUnavailable("The application window inventory is incomplete; no window absence can be verified.")
        }
        let windows = inventory.windows
        let titles = try windows.map(inventoryWindowTitle)
        return [
            "ok": true,
            "appName": app.name,
            "appPid": Int(app.pid),
            "windowTitles": titles,
            "snapshot": try windowInventorySnapshot(app: app, windows: windows),
        ]
    }

    func executeWindow(
        tool: String,
        operation: [String: Any]
    ) throws -> [String: Any] {
        let query = try requiredString(operation, "app")
        let app = try resolveRuntimeApplication(query: query, identity: runtimeAppIdentity(operation))
        try validateRequestedIdentity(operation, app: app)
        let changeWake = AXChangeWake(pid: app.pid)
        let requestedTitle = runtimeWindowTitle(operation)
        let requestedHandle = runtimeWindowHandle(operation)
        guard let window = preferredAXWindow(
            pid: app.pid,
            title: requestedTitle,
            handle: requestedHandle
        ) else {
            throw ComputerUseError.stateUnavailable(
                "No accessibility window is available for \(app.name)"
            )
        }
        let selectedHandle = canonicalMacAXWindowHandle(pid: app.pid, window: window)
        let appIdentity: [String: Any] = [
            "providerBusName": "mac:\(app.pid)",
            "processGeneration": macProcessGeneration(pid: app.pid),
        ]
        // An apps-only window ref may not have had a full snapshot yet.
        // Preserve the native identity while the selected window still exists.
        service.rememberNativeWindowIdentity(
            pid: app.pid,
            windowID: windowNumber(window),
            windowHandle: selectedHandle
        )
        let inspectWindow = boundWindowInspector(windowHandle: selectedHandle)

        if tool == "activate" {
            _ = app.runningApplication.activate(
                options: [.activateAllWindows]
            )
            let result = AXUIElementPerformAction(window, kAXRaiseAction as CFString)
            guard result == .success else {
                return windowActivationFailureResult(
                    appName: app.name,
                    appPID: app.pid,
                    underlyingCode: "ax_raise_failed_\(result.rawValue)"
                )
            }
            let activated = waitForBoundWindowReadiness(
                timeout: 0.6,
                pollInterval: 0.05,
                ready: {
                    activatedWindowMatchesRequest(
                        foreground: windowServerForegroundObservation(
                            targetPID: app.pid
                        ),
                        focusedWindowHandle: focusedAXWindowHandle(
                            pid: app.pid
                        ),
                        requestedWindowHandle: requestedHandle ?? ""
                    )
                },
                wait: { interval in
                    RunLoop.current.run(
                        mode: .default,
                        before: Date(timeIntervalSinceNow: interval)
                    )
                }
            )
            guard activated else {
                return windowActivationFailureResult(
                    appName: app.name,
                    appPID: app.pid,
                    underlyingCode: "activation_not_frontmost"
                )
            }
        } else {
            guard let closeButton = axElement(
                window,
                attribute: kAXCloseButtonAttribute as String
            ) else {
                throw ComputerUseError.stateUnavailable(
                    "The selected window does not expose an AX close button"
                )
            }
            try performNativeAXMutation(
                operation: "AXUIElementPerformAction(AXPress)",
                route: "ax.window.close"
            ) {
                AXUIElementPerformAction(
                    closeButton,
                    kAXPressAction as CFString
                )
            }
        }

        if tool == "close" {
            _ = changeWake?.wait(timeout: 0.12)
            Thread.sleep(forTimeInterval: 0.15)
        }
        return windowActionFinalObservation(
            tool: tool,
            app: app,
            appIdentity: appIdentity,
            windowHandle: selectedHandle,
            capture: {
                let snapshot = try self.service.refreshSnapshot(
                    for: query,
                    recoveryPolicy: .readOnly
                )
                return try snapshot.providerDictionary()
            },
            validateWindow: {
                try validateClosingWindow(
                    timeout: 0.6,
                    wait: { interval in
                        if let changeWake {
                            _ = changeWake.wait(timeout: interval)
                        } else {
                            Thread.sleep(forTimeInterval: interval)
                        }
                    },
                    validateWindow: {
                        try validateBoundWindowLiveness(
                            appIdentity: appIdentity,
                            windowHandle: selectedHandle,
                            resolveApp: { try resolveOrderedInputApp(appIdentity) },
                            inspectWindow: inspectWindow
                        )
                    }
                )
            }
        )
    }

    private func windowActivationFailureResult(
        appName: String,
        appPID: pid_t,
        underlyingCode: String
    ) -> [String: Any] {
        [
            "ok": false,
            "error": "The requested window activation could not be verified for \(appName)",
            "failureCode": "effect_unknown",
            "underlyingFailureCode": underlyingCode,
            "failurePhase": "verification",
            "failureFactsComplete": true,
            "input_events_emitted": true,
            "partial_input_possible": true,
            "desktop_state_mutated": true,
            "mutation_unknown": true,
            "actionEvidence": [
                "route": "ax.window.activate",
                "geometry_sampled": false,
                "readback_sampled": true,
                "readback_verified": false,
                "input_emitted": true,
            ],
        ]
    }
}

// Keep the validator's contract: a typed stale identity proves absence;
// returning at the deadline leaves absence unproven. AX and WindowServer can
// remove a closing window at different times, so resample the same identity.
func validateClosingWindow(
    timeout: TimeInterval,
    pollInterval: TimeInterval = 0.05,
    now: () -> TimeInterval = { ProcessInfo.processInfo.systemUptime },
    wait: (TimeInterval) -> Void,
    validateWindow: () throws -> Void
) throws {
    let deadline = now() + max(0, timeout)
    repeat {
        // Each sample validates the process generation before and after the
        // existing corroborated AX/WindowServer check. Do not retry errors.
        try validateWindow()
        let remaining = deadline - now()
        guard remaining > 0 else { return }
        wait(min(max(0.001, pollInterval), remaining))
    } while now() < deadline
}

func windowActionFinalObservation(
    tool: String,
    app: RunningAppDescriptor,
    appIdentity: [String: Any],
    windowHandle: String,
    capture: () throws -> [String: Any],
    validateWindow: () throws -> Void
) -> [String: Any] {
    let evidence: [String: Any] = [
        "route": "ax.window.\(tool)",
        "geometry_sampled": false,
        "readback_sampled": tool == "activate",
        "readback_verified": tool == "activate",
        "input_emitted": true,
    ]
    do {
        return [
            "ok": true,
            "snapshot": try capture(),
            "desktop_state_mutated": true,
            "input_events_emitted": true,
            "actionEvidence": evidence,
        ]
    } catch {
        var failure = postInputObservationFailureResult(error, evidence: evidence)
        guard tool == "close", !windowHandle.isEmpty,
              !requiresProviderReset(error) else {
            return failure
        }
        // A capture error alone never proves absence, including stale-looking text.
        do {
            try validateWindow()
        } catch {
            if (error as? ComputerUseError)?.staleIdentityFailure != nil {
                return [
                    "ok": true,
                    "snapshot": [
                        "app": [
                            "name": app.name,
                            "bundleIdentifier": app.bundleIdentifier ?? app.name,
                            "pid": Int(app.pid),
                            "providerBusName": string(appIdentity["providerBusName"]),
                            "processGeneration": string(appIdentity["processGeneration"]),
                            "inputCapable": true,
                        ],
                        "windowAbsent": true,
                        "closedWindowHandle": windowHandle,
                        "treeLines": [
                            "The bound AX window no longer exists after close."
                        ],
                        "elements": [],
                    ],
                    "desktop_state_mutated": true,
                    "input_events_emitted": true,
                    "actionEvidence": evidence,
                ]
            }
            if requiresProviderReset(error) {
                failure["providerResetRequired"] = true
            }
        }
        return failure
    }
}

func focusedAXWindowHandle(pid: pid_t) -> String? {
    let appElement = AXUIElementCreateApplication(pid)
    let systemWide = AXUIElementCreateSystemWide()
    guard let window = strictFocusedAXWindow(
        appElement: appElement,
        appPID: pid,
        systemWide: systemWide
    ) else {
        return nil
    }
    return canonicalMacAXWindowHandle(pid: pid, window: window)
}

func axWindowInventory(
    pid: pid_t
) -> (windows: [AXUIElement], complete: Bool, status: AXError) {
    let app = AXUIElementCreateApplication(pid)
    AXUIElementSetMessagingTimeout(app, 0.25)
    var value: CFTypeRef?
    let status = AXUIElementCopyAttributeValue(
        app,
        kAXWindowsAttribute as CFString,
        &value
    )
    windowLivenessDiagnostic("ax_inventory_read", [
        "status": Int(status.rawValue), "value_nil": value == nil,
        "type_id": value.map { Int(CFGetTypeID($0)) } ?? -1,
        "array_type_id": Int(CFArrayGetTypeID()),
        "ax_element_type_id": Int(AXUIElementGetTypeID()),
        "array_count": (value as? [Any])?.count ?? -1,
        "cast_success": value as? [AXUIElement] != nil,
    ])
    guard status == .success else {
        return ([], false, status)
    }
    guard let windows = value as? [AXUIElement] else {
        return ([], false, status)
    }
    return (windows, true, status)
}

private func axWindows(pid: pid_t) -> [AXUIElement] {
    axWindowInventory(pid: pid).windows
}

func axTitle(_ element: AXUIElement) -> String? {
    var value: CFTypeRef?
    guard AXUIElementCopyAttributeValue(
        element,
        kAXTitleAttribute as CFString,
        &value
    ) == .success else {
        return nil
    }
    return value as? String
}

private func axElement(
    _ element: AXUIElement,
    attribute: String
) -> AXUIElement? {
    var value: CFTypeRef?
    guard AXUIElementCopyAttributeValue(
        element,
        attribute as CFString,
        &value
    ) == .success, let value else {
        return nil
    }
    return checkedAXElement(value)
}

private func preferredAXWindow(
    pid: pid_t,
    title: String?,
    handle: String?
) -> AXUIElement? {
    let windows = axWindows(pid: pid)
    if let handle, !handle.isEmpty {
        return matchingWindowCandidate(
            windows,
            requestedHandle: handle
        ) { window in
            canonicalMacAXWindowHandle(pid: pid, window: window)
        }
    }
    if let title, !title.isEmpty,
       let exact = windows.first(where: { axTitle($0) == title })
    {
        return exact
    }
    return windows.first
}

func runtimeWindowHandle(_ operation: [String: Any]) -> String? {
    let direct = string(operation["targetWindowHandle"])
    if !direct.isEmpty {
        return direct
    }
    if let target = operation["semanticTarget"] as? [String: Any] {
        let value = string(target["windowHandle"])
        return value.isEmpty ? nil : value
    }
    return nil
}

private func runtimeWindowTitle(_ operation: [String: Any]) -> String? {
    if let target = operation["semanticTarget"] as? [String: Any] {
        for key in ["providerWindowName", "name"] {
            let value = string(target[key])
            if !value.isEmpty {
                return value
            }
        }
    }
    if let element = operation["element"] as? [String: Any] {
        let value = string(element["name"])
        if !value.isEmpty {
            return value
        }
    }
    return nil
}

