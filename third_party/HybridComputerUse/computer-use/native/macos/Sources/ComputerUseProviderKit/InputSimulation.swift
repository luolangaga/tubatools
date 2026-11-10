import AppKit
import ApplicationServices
import CoreGraphics
import Foundation

enum MouseButtonKind: String {
    case left
    case right
    case middle

    var cgButton: CGMouseButton {
        switch self {
        case .left:
            return .left
        case .right:
            return .right
        case .middle:
            return .center
        }
    }

    var downEvent: CGEventType {
        switch self {
        case .left:
            return .leftMouseDown
        case .right:
            return .rightMouseDown
        case .middle:
            return .otherMouseDown
        }
    }

    var upEvent: CGEventType {
        switch self {
        case .left:
            return .leftMouseUp
        case .right:
            return .rightMouseUp
        case .middle:
            return .otherMouseUp
        }
    }
}

enum InputSimulation {
    static let maxKeyboardUnicodeChunkLength = 64
    static let pasteboardCaptureAttempts = 3
    static let pasteConsumptionDelay: TimeInterval = 0.5
    private static let pasteboardLock = NSLock()

    static func prepareAppForGlobalPointerInput(
        _ app: RunningAppDescriptor,
        windowID: CGWindowID?
    ) throws {
        let prepared = prepareApplicationForGlobalPointerInput(
            timeout: 1,
            pollInterval: 0.05,
            raiseWindow: {
                _ = raiseAppWindowViaAccessibility(pid: app.pid)
            },
            activateBoundWindow: {
                guard let windowID else {
                    return false
                }
                return SkyLightSPI.shared.bringWindowToFront(
                    pid: app.pid,
                    windowID: windowID
                )
            },
            activateApplication: {
                app.runningApplication.activate(options: [.activateAllWindows])
            },
            windowServerForeground: {
                windowServerForegroundObservation(targetPID: app.pid)
            },
            wait: Thread.sleep(forTimeInterval:)
        )
        guard prepared else {
            throw ComputerUseError.stateUnavailable(
                "could not verify target application foreground ownership before global pointer input"
            )
        }
    }

    static func prepareBoundWindowForSemanticPointerInput(
        _ snapshot: AppSnapshot
    ) throws {
        guard let windowID = snapshot.targetWindowID,
              let snapshotBounds = snapshot.windowBounds
        else {
            throw ComputerUseError.stateUnavailable(
                "semantic pointer input requires current bound native window geometry"
            )
        }

        let prepared = prepareBoundWindowForPointerInput(
            activationTimeout: 0.75,
            fallbackTimeout: 2.25,
            pollInterval: 0.05,
            activateAndRaise: {
                _ = snapshot.app.runningApplication.activate(
                    options: [.activateAllWindows]
                )
                _ = AXUIElementPerformAction(
                    snapshot.rootWindowElement,
                    kAXRaiseAction as CFString
                )
            },
            activateBoundWindow: {
                SkyLightSPI.shared.bringWindowToFront(
                    pid: snapshot.app.pid,
                    windowID: windowID
                )
            },
            ready: {
                boundWindowMatchesSnapshot(
                    foreground: windowServerForegroundObservation(
                        targetPID: snapshot.app.pid
                    ),
                    window: windowServerBoundWindowObservation(
                        windowID: windowID,
                        targetPID: snapshot.app.pid
                    ),
                    snapshotBounds: snapshotBounds
                )
            },
            wait: { interval in
                RunLoop.current.run(
                    until: Date(timeIntervalSinceNow: interval)
                )
            }
        )
        guard prepared else {
            throw ComputerUseError.stateUnavailable(
                "could not verify the bound target window at its snapshot geometry before semantic pointer input"
            )
        }
    }

    static func clickGlobally(at point: CGPoint, button: MouseButtonKind, clickCount: Int) throws {
        guard let source = CGEventSource(stateID: .hidSystemState) else {
            throw ComputerUseError.message("Failed to create HID event source.")
        }

        for _ in 0..<max(clickCount, 1) {
            try postMouseEvent(type: .mouseMoved, source: source, point: point, button: button.cgButton, clickState: clickCount)
            try postMouseEvent(type: button.downEvent, source: source, point: point, button: button.cgButton, clickState: clickCount)
            try postMouseEvent(type: button.upEvent, source: source, point: point, button: button.cgButton, clickState: clickCount)
        }
    }

    static func clickTargeted(at point: CGPoint, button: MouseButtonKind, clickCount: Int, pid: pid_t) throws {
        guard let source = CGEventSource(stateID: .combinedSessionState) else {
            throw ComputerUseError.message("Failed to create app-post event source.")
        }

        for _ in 0..<max(clickCount, 1) {
            try postMouseEventToPid(type: .mouseMoved, source: source, point: point, button: button.cgButton, clickState: clickCount, pid: pid)
            try postMouseEventToPid(type: button.downEvent, source: source, point: point, button: button.cgButton, clickState: clickCount, pid: pid)
            try postMouseEventToPid(type: button.upEvent, source: source, point: point, button: button.cgButton, clickState: clickCount, pid: pid)
        }
    }

    static func clickWithSkyLight(
        at screenPoint: CGPoint,
        windowPoint: CGPoint,
        windowBounds: CGRect,
        windowID: CGWindowID,
        clickCount: Int,
        pid: pid_t
    ) throws {
        try SkyClickDispatcher.click(
            target: SkyClickTarget(
                screenPoint: screenPoint,
                windowPoint: windowPoint,
                windowBounds: windowBounds,
                windowID: windowID,
                pid: pid
            ),
            clickCount: clickCount
        )
    }

    static func scrollTargeted(at point: CGPoint, direction: String, pages: Double, pid: pid_t) throws {
        guard let event = CGEvent(scrollWheelEvent2Source: nil, units: .line, wheelCount: 2, wheel1: wheel1(direction: direction, pages: pages), wheel2: wheel2(direction: direction, pages: pages), wheel3: 0) else {
            throw ComputerUseError.message("Failed to create scroll event.")
        }

        event.location = point
        event.postToPid(pid)
        Thread.sleep(forTimeInterval: 0.1)
    }

    static func scrollGlobally(
        at point: CGPoint,
        direction: String,
        pages: Double,
        flags: CGEventFlags = []
    ) throws {
        guard let event = CGEvent(scrollWheelEvent2Source: nil, units: .line, wheelCount: 2, wheel1: wheel1(direction: direction, pages: pages), wheel2: wheel2(direction: direction, pages: pages), wheel3: 0) else {
            throw ComputerUseError.message("Failed to create scroll event.")
        }

        event.location = point
        event.flags = flags
        event.post(tap: .cghidEventTap)
        Thread.sleep(forTimeInterval: 0.1)
    }

    static func dragGlobally(path: [CGPoint], flags: CGEventFlags = []) throws {
        guard let source = CGEventSource(stateID: .hidSystemState) else {
            throw ComputerUseError.message("Failed to create HID event source.")
        }
        try executeGUIDrag(path: path, flags: flags, post: { type, point, flags in
            try postMouseEvent(
                type: type, source: source, point: point, button: .left,
                clickState: 1, flags: flags, delay: 0
            )
        })
    }

    static func interpolatedDragPoints(
        from start: CGPoint,
        to end: CGPoint,
        steps: Int = 24
    ) -> [CGPoint] {
        precondition(steps > 0, "drag interpolation steps must be positive")
        return (1...steps).map { step in
            if step == steps { return end }
            let progress = CGFloat(step) / CGFloat(steps)
            return CGPoint(
                x: start.x + ((end.x - start.x) * progress),
                y: start.y + ((end.y - start.y) * progress)
            )
        }
    }

    static func typeTextGlobally(
        _ text: String,
        awaitConsumption: (() throws -> Void)? = nil
    ) throws {
        guard !text.isEmpty else {
            return
        }

        try withTemporaryPasteboardText(
            text,
            pasteboard: .general,
            consumptionDelay: pasteConsumptionDelay,
            awaitConsumption: awaitConsumption
        ) {
            try pressKeyGlobally("CMD+V")
        }
    }

    /// Posts `text` as Unicode key events to one process. A background
    /// application receives them without a focus change or a clipboard write.
    static func typeTextToProcess(_ text: String, pid: pid_t) throws {
        for chunk in keyboardUnicodeChunks(for: text) {
            var units = chunk
            guard let down = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: true),
                  let up = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: false) else {
                throw ComputerUseError.message("Failed to create keyboard event.")
            }
            units.withUnsafeMutableBufferPointer { buffer in
                guard let baseAddress = buffer.baseAddress else {
                    return
                }
                down.keyboardSetUnicodeString(stringLength: buffer.count, unicodeString: baseAddress)
                up.keyboardSetUnicodeString(stringLength: buffer.count, unicodeString: baseAddress)
            }
            down.postToPid(pid)
            up.postToPid(pid)
            Thread.sleep(forTimeInterval: 0.02)
        }
    }

    /// Pastes `text` into one process with Command-V and restores the
    /// clipboard afterwards.
    static func pasteTextToProcess(
        _ text: String,
        pid: pid_t,
        awaitConsumption: (() throws -> Void)? = nil
    ) throws {
        guard !text.isEmpty else {
            return
        }
        try withTemporaryPasteboardText(
            text,
            pasteboard: .general,
            consumptionDelay: pasteConsumptionDelay,
            awaitConsumption: awaitConsumption
        ) {
            try pressKey("CMD+V", pid: pid)
        }
    }

    static func withTemporaryPasteboardText(
        _ text: String,
        pasteboard: NSPasteboard,
        consumptionDelay: TimeInterval = 0,
        awaitConsumption: (() throws -> Void)? = nil,
        performPaste: () throws -> Void
    ) throws {
        guard !text.isEmpty else {
            return
        }

        pasteboardLock.lock()
        defer {
            pasteboardLock.unlock()
        }

        let snapshot = try PasteboardSnapshot.capture(
            from: pasteboard,
            attempts: pasteboardCaptureAttempts
        )
        pasteboard.clearContents()
        let clearedChangeCount = pasteboard.changeCount
        guard pasteboard.setString(text, forType: .string) else {
            if pasteboard.changeCount == clearedChangeCount {
                try snapshot.restore(to: pasteboard)
            }
            throw ComputerUseError.message(
                "Failed to stage text on the pasteboard."
            )
        }
        let injectedChangeCount = pasteboard.changeCount

        do {
            try performPaste()
        } catch {
            let pasteError = localizedMessage(error)
            do {
                try snapshot.restore(
                    to: pasteboard,
                    onlyIfChangeCountIs: injectedChangeCount
                )
            } catch {
                throw InputDeliveryError(
                    message: "Paste command failed (\(pasteError)); clipboard restoration also failed (\(localizedMessage(error))).",
                    inputEventsEmitted: true
                )
            }
            throw InputDeliveryError(
                message: "Paste command failed: \(pasteError)",
                inputEventsEmitted: true
            )
        }

        var consumptionError: Error?
        if let awaitConsumption {
            do {
                try awaitConsumption()
            } catch {
                consumptionError = error
            }
        } else if consumptionDelay > 0 {
            Thread.sleep(forTimeInterval: consumptionDelay)
        }

        do {
            try snapshot.restore(
                to: pasteboard,
                onlyIfChangeCountIs: injectedChangeCount
            )
        } catch {
            let consumptionContext = consumptionError.map {
                " Paste consumption acknowledgement also failed: \(localizedMessage($0))."
            } ?? ""
            throw InputDeliveryError(
                message: "Paste command was emitted, but clipboard restoration failed: \(localizedMessage(error)).\(consumptionContext)",
                inputEventsEmitted: true
            )
        }
        if let consumptionError {
            throw InputDeliveryError(
                message: "Paste command was emitted, but target consumption was not acknowledged: \(localizedMessage(consumptionError))",
                inputEventsEmitted: true
            )
        }
    }

    static func keyboardUnicodeChunks(for text: String, maxUTF16Units: Int = maxKeyboardUnicodeChunkLength) -> [[UniChar]] {
        precondition(maxUTF16Units > 0, "maxUTF16Units must be positive")

        var chunks: [[UniChar]] = []
        var current: [UniChar] = []

        for character in text {
            let units = Array(String(character).utf16)
            if !current.isEmpty, current.count + units.count > maxUTF16Units {
                chunks.append(current)
                current.removeAll(keepingCapacity: true)
            }

            current.append(contentsOf: units)
        }

        if !current.isEmpty {
            chunks.append(current)
        }

        return chunks
    }

    static func pressKey(_ specification: String, pid: pid_t) throws {
        try pressKey(specification) { event in
            event.postToPid(pid)
        }
    }

    static func pressKeyGlobally(_ specification: String) throws {
        try pressKey(specification) { event in
            event.post(tap: .cghidEventTap)
        }
    }

    private static func pressKey(
        _ specification: String,
        post: (CGEvent) -> Void
    ) throws {
        let parsed = try KeyPressParser.parse(specification)
        var activeFlags: CGEventFlags = []

        for modifier in parsed.modifiers {
            guard let event = CGEvent(keyboardEventSource: nil, virtualKey: modifier.keyCode, keyDown: true) else {
                throw ComputerUseError.message("Failed to create modifier key down event.")
            }

            activeFlags.insert(modifier.flag)
            event.flags = activeFlags
            post(event)
        }

        guard let keyDown = CGEvent(keyboardEventSource: nil, virtualKey: parsed.keyCode, keyDown: true),
              let keyUp = CGEvent(keyboardEventSource: nil, virtualKey: parsed.keyCode, keyDown: false) else {
            throw ComputerUseError.message("Failed to create key event.")
        }

        keyDown.flags = activeFlags
        keyUp.flags = activeFlags
        post(keyDown)
        post(keyUp)

        for modifier in parsed.modifiers.reversed() {
            guard let event = CGEvent(keyboardEventSource: nil, virtualKey: modifier.keyCode, keyDown: false) else {
                throw ComputerUseError.message("Failed to create modifier key up event.")
            }

            event.flags = activeFlags
            post(event)
            activeFlags.remove(modifier.flag)
        }

        Thread.sleep(forTimeInterval: 0.1)
    }

    private static func postMouseEvent(
        type: CGEventType,
        source: CGEventSource,
        point: CGPoint,
        button: CGMouseButton,
        clickState: Int,
        flags: CGEventFlags = [],
        delay: TimeInterval = 0.03
    ) throws {
        guard let event = CGEvent(mouseEventSource: source, mouseType: type, mouseCursorPosition: point, mouseButton: button) else {
            throw ComputerUseError.message("Failed to create mouse event \(type.rawValue).")
        }

        event.setIntegerValueField(.mouseEventClickState, value: Int64(clickState))
        event.flags = flags
        event.post(tap: .cghidEventTap)
        Thread.sleep(forTimeInterval: delay)
    }

    private static func postMouseEventToPid(type: CGEventType, source: CGEventSource, point: CGPoint, button: CGMouseButton, clickState: Int, pid: pid_t) throws {
        guard let event = CGEvent(mouseEventSource: source, mouseType: type, mouseCursorPosition: point, mouseButton: button) else {
            throw ComputerUseError.message("Failed to create mouse event \(type.rawValue).")
        }

        event.setIntegerValueField(.mouseEventClickState, value: Int64(clickState))
        event.postToPid(pid)
        Thread.sleep(forTimeInterval: 0.03)
    }

    private static func raiseAppWindowViaAccessibility(pid: pid_t) -> Bool {
        let appElement = AXUIElementCreateApplication(pid)
        guard let window = preferredWindow(for: appElement) else {
            return false
        }

        if performAction(named: kAXRaiseAction as String, on: window) {
            return true
        }

        if setBoolAttribute(named: kAXMainAttribute as String, on: window) {
            return true
        }

        if setBoolAttribute(named: kAXFocusedAttribute as String, on: window) {
            return true
        }

        return false
    }

    private static func preferredWindow(for appElement: AXUIElement) -> AXUIElement? {
        copyElement(appElement, attribute: kAXFocusedWindowAttribute)
            ?? copyArray(appElement, attribute: kAXWindowsAttribute)?.first
    }

    private static func performAction(named action: String, on element: AXUIElement) -> Bool {
        guard availableActions(for: element).contains(where: { $0.caseInsensitiveCompare(action) == .orderedSame }) else {
            return false
        }

        return AXUIElementPerformAction(element, action as CFString) == .success
    }

    private static func setBoolAttribute(named attribute: String, on element: AXUIElement) -> Bool {
        guard isSettable(element: element, attribute: attribute) else {
            return false
        }

        return AXUIElementSetAttributeValue(element, attribute as CFString, kCFBooleanTrue) == .success
    }

    private static func isSettable(element: AXUIElement, attribute: String) -> Bool {
        var settable: DarwinBoolean = false
        let result = AXUIElementIsAttributeSettable(element, attribute as CFString, &settable)
        return result == .success && settable.boolValue
    }

    private static func availableActions(for element: AXUIElement) -> [String] {
        var actions: CFArray?
        let result = AXUIElementCopyActionNames(element, &actions)
        guard result == .success, let actions else {
            return []
        }

        return actions as? [String] ?? []
    }

    private static func copyElement(_ element: AXUIElement, attribute: String) -> AXUIElement? {
        var value: CFTypeRef?
        let result = AXUIElementCopyAttributeValue(element, attribute as CFString, &value)
        guard result == .success, let value else {
            return nil
        }

        return checkedAXElement(value)
    }

    private static func copyArray(_ element: AXUIElement, attribute: String) -> [AXUIElement]? {
        var value: CFTypeRef?
        let result = AXUIElementCopyAttributeValue(element, attribute as CFString, &value)
        guard result == .success, let value else {
            return nil
        }

        return value as? [AXUIElement]
    }

    private static func wheel1(direction: String, pages: Double) -> Int32 {
        switch direction {
        case "up":
            return scrollWheelDelta(for: pages)
        case "down":
            return -scrollWheelDelta(for: pages)
        default:
            return 0
        }
    }

    private static func wheel2(direction: String, pages: Double) -> Int32 {
        switch direction {
        case "left":
            return scrollWheelDelta(for: pages)
        case "right":
            return -scrollWheelDelta(for: pages)
        default:
            return 0
        }
    }

    private static func scrollWheelDelta(for pages: Double) -> Int32 {
        let rawValue = (12.0 * pages).rounded(.toNearestOrAwayFromZero)
        let clamped = min(Double(Int32.max), max(1, rawValue))
        return Int32(clamped)
    }
}

protocol InputEmissionReportingError {
    var inputEventsEmitted: Bool { get }
}

protocol ActionFailureEvidenceReportingError {
    var failureCode: String? { get }
    var partialInputPossible: Bool? { get }
    var readbackSampled: Bool { get }
    var readbackVerified: Bool? { get }
    var actionRoute: String? { get }
}

struct InputDeliveryError:
    Error,
    LocalizedError,
    InputEmissionReportingError,
    ActionFailureEvidenceReportingError
{
    let message: String
    let inputEventsEmitted: Bool
    let failureCode: String?
    let partialInputPossible: Bool?
    let readbackSampled: Bool
    let readbackVerified: Bool?
    let actionRoute: String?

    init(
        message: String,
        inputEventsEmitted: Bool,
        failureCode: String? = nil,
        partialInputPossible: Bool? = nil,
        readbackSampled: Bool = false,
        readbackVerified: Bool? = nil,
        actionRoute: String? = nil
    ) {
        self.message = message
        self.inputEventsEmitted = inputEventsEmitted
        self.failureCode = failureCode
        self.partialInputPossible = partialInputPossible
        self.readbackSampled = readbackSampled
        self.readbackVerified = readbackVerified
        self.actionRoute = actionRoute
    }

    var errorDescription: String? {
        message
    }
}

struct PasteboardDataSnapshot: Equatable {
    let type: String
    let data: Data
}

struct PasteboardItemSnapshot: Equatable {
    let values: [PasteboardDataSnapshot]
}

struct PasteboardSnapshot: Equatable {
    let items: [PasteboardItemSnapshot]

    static func capture(
        from pasteboard: NSPasteboard,
        attempts: Int
    ) throws -> PasteboardSnapshot {
        precondition(attempts > 0, "pasteboard capture attempts must be positive")

        for attempt in 1...attempts {
            let initialChangeCount = pasteboard.changeCount
            var itemSnapshots: [PasteboardItemSnapshot] = []
            var captureError: String?

            for item in pasteboard.pasteboardItems ?? [] {
                var values: [PasteboardDataSnapshot] = []
                for type in item.types {
                    guard let data = item.data(forType: type) else {
                        captureError =
                            "Pasteboard type '\(type.rawValue)' could not be materialized."
                        break
                    }
                    values.append(
                        PasteboardDataSnapshot(
                            type: type.rawValue,
                            data: data
                        )
                    )
                }
                if captureError != nil {
                    break
                }
                itemSnapshots.append(PasteboardItemSnapshot(values: values))
            }

            if captureError == nil,
               pasteboard.changeCount == initialChangeCount
            {
                return PasteboardSnapshot(items: itemSnapshots)
            }

            if attempt < attempts {
                Thread.sleep(forTimeInterval: 0.02)
                continue
            }

            throw ComputerUseError.message(
                captureError
                    ?? "Pasteboard changed while it was being captured."
            )
        }

        throw ComputerUseError.message("Pasteboard capture failed.")
    }

    func restore(
        to pasteboard: NSPasteboard,
        onlyIfChangeCountIs expectedChangeCount: Int? = nil
    ) throws {
        if let expectedChangeCount,
           pasteboard.changeCount != expectedChangeCount
        {
            return
        }

        var restoredItems: [NSPasteboardItem] = []
        for snapshot in items {
            let item = NSPasteboardItem()
            for value in snapshot.values {
                guard item.setData(
                    value.data,
                    forType: NSPasteboard.PasteboardType(value.type)
                ) else {
                    throw ComputerUseError.message(
                        "Failed to reconstruct pasteboard type '\(value.type)'."
                    )
                }
            }
            restoredItems.append(item)
        }

        pasteboard.clearContents()
        guard restoredItems.isEmpty || pasteboard.writeObjects(restoredItems)
        else {
            throw ComputerUseError.message(
                "Failed to restore the previous pasteboard contents."
            )
        }
    }
}

private func localizedMessage(_ error: Error) -> String {
    (error as? LocalizedError)?.errorDescription
        ?? String(describing: error)
}
