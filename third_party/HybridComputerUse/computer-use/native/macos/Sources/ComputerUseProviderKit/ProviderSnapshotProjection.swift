import AppKit
import ApplicationServices
import CoreGraphics
import Foundation

extension AppSnapshot {
    func providerDictionary() throws -> [String: Any] {
        let generation = providerProcessGeneration ?? macProcessGeneration(pid: app.pid)
        var result: [String: Any] = [
            "app": [
                "name": app.name,
                "bundleIdentifier": app.bundleIdentifier ?? app.name,
                "pid": Int(app.pid),
                "providerBusName": "mac:\(app.pid)",
                "processGeneration": generation,
                "displayText": app.bundleIdentifier.map {
                    "\(app.name) — \($0)"
                } ?? app.name,
                "inputCapable": true,
            ],
            "sampledAt": ISO8601DateFormatter().string(from: Date()),
            "treeLines": treeLines,
            "elements": try providerElementDictionaries,
        ]
        if let windowTitle {
            result["windowTitle"] = windowTitle
        }
        if let windowBounds {
            result["windowBounds"] = frameDictionary(windowBounds)
        }
        if let handle = targetWindowHandle {
            result["windowHandle"] = handle
            result["windows"] = [[
                "title": windowTitle ?? "",
                "role": "window",
                "handle": handle,
                "selected": true,
                "active": NSRunningApplication(
                    processIdentifier: app.pid
                )?.isActive == true && focusedAXWindowHandle(pid: app.pid) == handle,
                "modal": false,
                "showing": true,
            ]]
        }
        if !omissions.isEmpty {
            result["annotations"] = omissions.map { omission in
                [
                    "parentRuntimeId": [0],
                    "kind": "provider_omission",
                    "count": omission.count,
                    "reason": omission.reason,
                    "detail": omission.detail,
                ] as [String: Any]
            }
        }
        if let focusedSummary {
            result["focusedSummary"] = focusedSummary
        }
        if let selectedText {
            result["selectedText"] = selectedText
        }
        return result
    }

    var providerElementDictionaries: [[String: Any]] {
        get throws {
        guard elements.values.allSatisfy({
            $0.isSyntheticText || $0.element == nil || $0.boundProviderObjectPath != nil
        }) else {
            throw ComputerUseError.stateUnavailable(
                "Unbound native object identity; snapshot rejected."
            )
        }
        return elements.keys.sorted().map { index in
            let record = elements[index]!
            let processGeneration = providerProcessGeneration ?? macProcessGeneration(pid: app.pid)
            let windowObjectPath = targetWindowHandle.map {
                "window:\($0)"
            } ?? ""
            var item: [String: Any] = [
                "index": index,
                "runtimeId": record.runtimeID,
                "runtimeAliases": [],
                "automationId": record.identifier ?? "",
                "name": record.name ?? "",
                "controlType": record.providerRole ?? "unknown",
                "localizedControlType": record.localizedRole
                    ?? "unknown",
                "actions": record.prettyActions,
                "setValue": record.setValueSupported ?? false,
                "providerBusName": "mac:\(app.pid)",
                "providerObjectPath": macProviderObjectPath(record),
                "processGeneration": processGeneration,
                "providerIdentityScope": record.boundProviderObjectPath == nil
                    ? "process" : "process_instance",
                "providerWindowBusName": "mac:\(app.pid)",
                "providerWindowObjectPath": windowObjectPath,
                "providerWindowProcessGeneration": processGeneration,
                "providerWindowRole": "window",
                "providerWindowName": windowTitle ?? "",
                "showing": true,
                "visible": true,
            ]
            if let value = record.value {
                item["value"] = value
            }
            if let description = record.description {
                item["description"] = description
            }
            if let placeholder = record.placeholder {
                item["placeholder"] = placeholder
            }
            if let states = record.states {
                item["states"] = states
            }
            if let frame = record.localFrame {
                item["frame"] = frameDictionary(frame)
            }
            return item
        }
        }
    }
}

func validateRequestedIdentity(
    _ operation: [String: Any],
    app: RunningAppDescriptor
) throws {
    guard let identity = operation["appIdentity"] as? [String: Any] else {
        return
    }
    let expectedBus = "mac:\(app.pid)"
    let expectedGeneration = macProcessGeneration(pid: app.pid)
    guard string(identity["providerBusName"]) == expectedBus,
          string(identity["processGeneration"]) == expectedGeneration else {
        throw ComputerUseError.staleAppRef(
            "stale_app_ref provider_process_changed."
        )
    }
}

func validateRequestedWindow(
    _ operation: [String: Any],
    snapshot: AppSnapshot
) throws {
    let requested = string(operation["targetWindowHandle"])
    guard !requested.isEmpty else {
        return
    }
    let current = snapshot.targetWindowHandle ?? ""
    guard requested == current else {
        throw ComputerUseError.staleWindowRef(
            "stale_window_ref provider_window_changed."
        )
    }
}

func windowInventorySnapshot(
    app: RunningAppDescriptor,
    windows: [AXUIElement]
) throws -> [String: Any] {
    let generation = macProcessGeneration(pid: app.pid)
    let focusedHandle = app.runningApplication.isActive ? focusedAXWindowHandle(pid: app.pid) : nil
    let records = try windows.map { window -> [String: Any] in
        AXUIElementSetMessagingTimeout(window, 0.25)
        return macWindowInventoryRecord(
            pid: app.pid, generation: generation, windowID: windowNumber(window),
            title: try inventoryWindowTitle(window), focusedHandle: focusedHandle,
            modal: axBool(window, attribute: kAXModalAttribute as String)
        )
    }
    return [
        "app": [
            "name": app.name,
            "bundleIdentifier": app.bundleIdentifier ?? app.name,
            "pid": Int(app.pid),
            "providerBusName": "mac:\(app.pid)",
            "processGeneration": generation,
            "inputCapable": true,
        ],
        "windows": records,
        "treeLines": records.map {
            "window \"\(string($0["title"]))\""
        },
        "elements": [],
    ]
}

func inventoryWindowTitle(_ window: AXUIElement) throws -> String {
    var value: CFTypeRef?
    let status = AXUIElementCopyAttributeValue(window, kAXTitleAttribute as CFString, &value)
    return try completeWindowTitle(status: status, value: value)
}

func completeWindowTitle(status: AXError, value: CFTypeRef?) throws -> String {
    if status == .attributeUnsupported || status == .noValue { return "" }
    if status == .success {
        if value == nil { return "" }
        if let title = value as? String { return title }
    }
    throw ComputerUseError.stateUnavailable("A window title could not be read completely (AX \(status.rawValue)); missing titles cannot prove that a window closed.")
}

func macWindowInventoryRecord(
    pid: pid_t, generation: String, windowID: CGWindowID?, title: String,
    focusedHandle: String?, modal: Bool
) -> [String: Any] {
    // No native ID means no executable handle; keep the title for honest waits.
    let handle = windowID.flatMap { $0 == 0 ? nil : macWindowHandle(pid: pid, windowID: $0) } ?? ""
    let active = !handle.isEmpty && focusedHandle == handle
    return [
        "title": title, "role": "window", "handle": handle,
        "providerBusName": "mac:\(pid)",
        "providerObjectPath": handle.isEmpty ? "" : "window:\(handle)",
        "providerProcessGeneration": generation,
        "selected": active, "active": active, "modal": modal, "showing": true,
    ]
}

func appListingWindows(_ records: [[String: Any]]) -> [[String: Any]] {
    let handleCounts = records.reduce(into: [String: Int]()) { counts, record in
        counts[string(record["handle"]), default: 0] += 1
    }
    return records.map { record in
        let handle = string(record["handle"])
        return [
            "title": string(record["title"]), "role": string(record["role"]),
            "handle": handleCounts[handle] == 1 ? handle : "",
            "providerBusName": string(record["providerBusName"]),
            "providerObjectPath": string(record["providerObjectPath"]),
            "processGeneration": string(record["providerProcessGeneration"]),
            "active": bool(record["active"]), "inputTarget": true,
        ]
    }
}

func windowNumber(_ window: AXUIElement) -> CGWindowID? {
    var identifier: CGWindowID = 0
    let result = AXUIElementGetWindow(window, &identifier)
    return result == .success && identifier != 0 ? identifier : nil
}

private func axBool(_ element: AXUIElement, attribute: String) -> Bool {
    var value: CFTypeRef?
    guard AXUIElementCopyAttributeValue(
        element,
        attribute as CFString,
        &value
    ) == .success else {
        return false
    }
    return (value as? NSNumber)?.boolValue ?? false
}
