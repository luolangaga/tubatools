import AppKit
import ApplicationServices
import CoreGraphics
import CryptoKit
import Foundation

func validateGUIAction(
    _ action: [String: Any],
    desktopBounds: CGRect
) throws {
    let type = string(action["type"]).lowercased()
    let supported = Set([
        "screenshot", "click", "double_click", "move", "drag", "scroll",
        "type", "keypress", "wait",
    ])
    guard supported.contains(type) else {
        throw ComputerUseError.invalidArguments(
            "unsupported gui action type '\(type)'"
        )
    }

    switch type {
    case "click", "double_click", "move":
        try validateGUIPoint(action, desktopBounds: desktopBounds)
        _ = try guiMouseModifierFlags(action)
    case "drag":
        guard let path = action["path"] as? [[String: Any]],
              path.count >= 2 else {
            throw ComputerUseError.invalidArguments(
                "gui drag requires path with at least two points"
            )
        }
        try path.forEach {
            try validateGUIPoint($0, desktopBounds: desktopBounds)
        }
        _ = try guiMouseModifierFlags(action)
    case "scroll":
        if action["x"] != nil || action["y"] != nil {
            guard action["x"] != nil, action["y"] != nil else {
                throw ComputerUseError.invalidArguments(
                    "gui scroll coordinates require both x and y"
                )
            }
            try validateGUIPoint(action, desktopBounds: desktopBounds)
        }
        _ = try guiMouseModifierFlags(action)
    case "type":
        guard action["text"] is String else {
            throw ComputerUseError.invalidArguments(
                "gui type requires text"
            )
        }
    case "keypress":
        guard let keys = action["keys"] as? [String],
              !keys.isEmpty else {
            throw ComputerUseError.invalidArguments(
                "gui keypress requires at least one key"
            )
        }
        _ = try KeyPressParser.parse(keys.joined(separator: "+"))
    default:
        break
    }
}

func guiExecutionFailure(
    error: String,
    text: String,
    code: String,
    phase: String,
    failedActionIndex: Int,
    inputActionsDispatched: Int,
    inputEventsEmitted: Bool,
    partialInputPossible: Bool,
    mutationUnknown: Bool,
    snapshot: [String: Any]?
) -> [String: Any] {
    var result: [String: Any] = [
        "ok": false,
        "error": error,
        "text": text,
        "failureCode": code,
        "failurePhase": phase,
        "dispatched": failedActionIndex,
        "input_actions_dispatched": inputActionsDispatched,
        "actions_completed_without_call_error": failedActionIndex,
        "failed_action_index": failedActionIndex,
        "input_events_emitted": inputEventsEmitted,
        "partial_input_possible": partialInputPossible,
        "desktop_state_mutated": inputEventsEmitted,
        "effect_unknown": mutationUnknown,
        "mutation_unknown": mutationUnknown,
        "failureFactsComplete": true,
        "guiResult": [
            "dispatched": failedActionIndex,
            "input_events_emitted": inputEventsEmitted,
            "input_actions_dispatched": inputActionsDispatched,
            "desktop_state_mutated": inputEventsEmitted,
            "partial_input_possible": partialInputPossible,
            "effect_unknown": mutationUnknown,
            "mutation_unknown": mutationUnknown,
        ],
    ]
    if let snapshot {
        result["snapshot"] = snapshot
        DesktopPublishedLayouts.shared.remember(snapshot)
    }
    return result
}

func guiActionUsesCoordinates(_ type: String) -> Bool {
    ["click", "double_click", "move", "drag", "scroll"].contains(type)
}

func guiClickEventTypes(
    button: (
        button: CGMouseButton,
        down: CGEventType,
        up: CGEventType
    ),
    clickCount: Int
) -> [CGEventType] {
    var events: [CGEventType] = [.mouseMoved]
    for _ in 0..<max(clickCount, 1) {
        events.append(button.down)
        events.append(button.up)
    }
    return events
}

func executeGUIClick(
    button: (button: CGMouseButton, down: CGEventType, up: CGEventType),
    clickCount: Int,
    point: CGPoint,
    flags: CGEventFlags,
    post: (CGEventType, CGPoint, CGMouseButton, Int, CGEventFlags) throws -> Void = {
        try postMouse(type: $0, point: $1, button: $2, clickState: $3, flags: $4)
    },
    sleep: (TimeInterval) -> Void = { Thread.sleep(forTimeInterval: $0) }
) throws {
    var pairCount = 0
    for eventType in guiClickEventTypes(button: button, clickCount: clickCount) {
        if eventType == button.down {
            pairCount += 1
        }
        let state = eventType == .mouseMoved ? clickCount : pairCount
        try post(eventType, point, button.button, state, flags)
        if eventType == .mouseMoved {
            sleep(0.03)
        }
    }
}

func executeGUIAction(
    _ action: [String: Any],
    deliveredBounds: CGRect,
    nativeBounds: CGRect
) throws {
    let type = string(action["type"]).lowercased()
    let mouseFlags = guiActionUsesCoordinates(type)
        ? try guiMouseModifierFlags(action)
        : []
    switch type {
    case "move":
        try postMouse(
            type: .mouseMoved,
            point: guiPoint(
                action,
                deliveredBounds: deliveredBounds,
                nativeBounds: nativeBounds
            ),
            button: .left,
            flags: mouseFlags
        )
    case "click", "double_click":
        let button = mouseButton(string(action["button"], fallback: "left"))
        let count = type == "double_click" ? 2 : 1
        let point = guiPoint(
            action,
            deliveredBounds: deliveredBounds,
            nativeBounds: nativeBounds
        )
        try executeGUIClick(button: button, clickCount: count, point: point, flags: mouseFlags)
    case "drag":
        guard let path = action["path"] as? [[String: Any]],
              path.count >= 2 else {
            throw ComputerUseError.invalidArguments(
                "gui drag requires path with at least two points"
            )
        }
        try InputSimulation.dragGlobally(
            path: path.map {
                guiPoint($0, deliveredBounds: deliveredBounds, nativeBounds: nativeBounds)
            },
            flags: mouseFlags
        )
    case "scroll":
        let x = double(action["scroll_x"]) ?? 0
        let y = double(action["scroll_y"]) ?? 0
        let point = action["x"] == nil ? CGEvent(source: nil)?.location ?? .zero
            : guiPoint(
                action,
                deliveredBounds: deliveredBounds,
                nativeBounds: nativeBounds
            )
        if y != 0 {
            try InputSimulation.scrollGlobally(
                at: point,
                direction: y > 0 ? "down" : "up",
                pages: abs(y),
                flags: mouseFlags
            )
        }
        if x != 0 {
            try InputSimulation.scrollGlobally(
                at: point,
                direction: x > 0 ? "right" : "left",
                pages: abs(x),
                flags: mouseFlags
            )
        }
    case "type":
        try InputSimulation.typeTextGlobally(string(action["text"]))
    case "keypress":
        let specification = (action["keys"] as? [String] ?? []).joined(separator: "+")
        try InputSimulation.pressKeyGlobally(specification)
    default:
        break
    }
}

func guiNeedsProcessingBarrier(
    previousActionType: String,
    currentActionType: String
) -> Bool {
    ["click", "double_click"].contains(previousActionType)
        && ["type", "keypress"].contains(currentActionType)
}

func postMouse(
    type: CGEventType,
    point: CGPoint,
    button: CGMouseButton,
    clickState: Int = 0,
    flags: CGEventFlags = []
) throws {
    guard let event = CGEvent(
        mouseEventSource: nil,
        mouseType: type,
        mouseCursorPosition: point,
        mouseButton: button
    ) else {
        throw ComputerUseError.message("Failed to create CGEvent")
    }
    if clickState > 0 {
        event.setIntegerValueField(
            .mouseEventClickState,
            value: Int64(clickState)
        )
    }
    event.flags = flags
    event.post(tap: .cghidEventTap)
}

func guiMouseModifierFlags(
    _ action: [String: Any]
) throws -> CGEventFlags {
    guard let value = action["keys"] else {
        return []
    }
    guard let keys = value as? [String] else {
        throw ComputerUseError.invalidArguments(
            "gui mouse action keys must be an array of modifier names"
        )
    }
    return try KeyPressParser.modifierFlags(keys)
}

func mouseButton(
    _ value: String
) -> (button: CGMouseButton, down: CGEventType, up: CGEventType) {
    switch value.lowercased() {
    case "right":
        return (.right, .rightMouseDown, .rightMouseUp)
    case "middle":
        return (.center, .otherMouseDown, .otherMouseUp)
    default:
        return (.left, .leftMouseDown, .leftMouseUp)
    }
}

func validateGUIPoint(
    _ value: [String: Any],
    desktopBounds: CGRect
) throws {
    guard let x = double(value["x"]), let y = double(value["y"]) else {
        throw ComputerUseError.invalidArguments(
            "gui coordinate action requires numeric x and y"
        )
    }
    // The delivered image has pixels 0...D-1 on each axis, as on Linux and
    // Windows; a point past the last pixel center is outside the image.
    guard x >= 0, y >= 0,
          x <= desktopBounds.width - 1,
          y <= desktopBounds.height - 1 else {
        throw ComputerUseError.invalidArguments(
            "gui coordinate (\(x), \(y)) is outside the current desktop image bounds \(Int(desktopBounds.width))x\(Int(desktopBounds.height))"
        )
    }
}

func guiPoint(
    _ value: [String: Any],
    deliveredBounds: CGRect,
    nativeBounds: CGRect
) -> CGPoint {
    let deliveredX = double(value["x"]) ?? 0
    let deliveredY = double(value["y"]) ?? 0
    let scaleX = nativeBounds.width / deliveredBounds.width
    let scaleY = nativeBounds.height / deliveredBounds.height
    return CGPoint(
        x: nativeBounds.minX + min(
            nativeBounds.width - 1,
            max(0, (deliveredX + 0.5) * scaleX - 0.5)
        ),
        y: nativeBounds.minY + min(
            nativeBounds.height - 1,
            max(0, (deliveredY + 0.5) * scaleY - 0.5)
        )
    )
}

func macProcessGeneration(pid: pid_t) -> String {
    if let launchDate = NSRunningApplication(
        processIdentifier: pid
    )?.launchDate {
        return String(Int64(launchDate.timeIntervalSince1970 * 1000))
    }
    return "pid-\(pid)"
}

func macWindowHandle(pid: pid_t, windowID: CGWindowID) -> String {
    let identity = Data(
        "mac:\(pid):\(macProcessGeneration(pid: pid)):\(windowID)".utf8
    )
    return opaqueMacWindowHandle(identity)
}

func macAXWindowHandle(pid: pid_t, window: AXUIElement) -> String {
    let identity = Data(
        "mac-ax:\(pid):\(macProcessGeneration(pid: pid)):\(CFHash(window))".utf8
    )
    return opaqueMacWindowHandle(identity)
}

func canonicalMacAXWindowHandle(
    pid: pid_t,
    window: AXUIElement
) -> String {
    if let windowID = windowNumber(window) {
        return macWindowHandle(pid: pid, windowID: windowID)
    }
    return macAXWindowHandle(pid: pid, window: window)
}

func matchingWindowCandidate<Node>(
    _ windows: [Node],
    requestedHandle: String,
    handleOf: (Node) -> String
) -> Node? {
    windows.first {
        handleOf($0) == requestedHandle
    }
}

private func opaqueMacWindowHandle(_ identity: Data) -> String {
    let digest = SHA256.hash(data: identity).prefix(16).map {
        String(format: "%02x", $0)
    }.joined()
    return "w2:\(digest)"
}

func snapshotTextLimit(_ value: Any?) throws -> SnapshotTextLimit {
    if let text = value as? String, text.lowercased() == "max" {
        return .max
    }
    if let count = positiveInt(value) {
        return SnapshotTextLimit(maxCount: count)
    }
    return .defaults
}

func positiveInt(_ value: Any?) -> Int? {
    guard let value = int(value), value > 0 else {
        return nil
    }
    return value
}

func requiredString(
    _ object: [String: Any],
    _ key: String
) throws -> String {
    let value = string(object[key])
    guard !value.isEmpty else {
        throw ComputerUseError.missingArgument(key)
    }
    return value
}

func string(_ value: Any?, fallback: String = "") -> String {
    value as? String ?? fallback
}

func bool(_ value: Any?) -> Bool {
    value as? Bool ?? false
}

func int(_ value: Any?) -> Int? {
    if let value = value as? Int {
        return value
    }
    if let value = value as? NSNumber {
        return value.intValue
    }
    return nil
}

func double(_ value: Any?) -> Double? {
    if let value = value as? Double {
        return value
    }
    if let value = value as? NSNumber {
        return value.doubleValue
    }
    return nil
}
