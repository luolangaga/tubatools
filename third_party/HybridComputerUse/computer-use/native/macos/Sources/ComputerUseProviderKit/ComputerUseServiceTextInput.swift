import ApplicationServices
import CoreGraphics
import Foundation

struct AXTextMutationFingerprint: Equatable {
    let value: String?
    let selectedText: String?
    let selectedRangeLocation: Int?
    let selectedRangeLength: Int?
}

func pasteboardRetentionBarrierSatisfied(
    before: AXTextMutationFingerprint,
    after: AXTextMutationFingerprint,
    notificationObserved: Bool,
    deadlineReached: Bool
) -> Bool {
    notificationObserved || after != before || deadlineReached
}

private func textMutationFingerprint(
    of element: AXUIElement
) -> AXTextMutationFingerprint {
    var selectedRangeValue: CFTypeRef?
    let selectedRangeResult = AXUIElementCopyAttributeValue(
        element,
        kAXSelectedTextRangeAttribute as CFString,
        &selectedRangeValue
    )
    var selectedRange = CFRange()
    let selectedRangeAXValue = checkedAXValue(selectedRangeValue)
    var hasSelectedRange = false
    if selectedRangeResult == .success,
       let selectedRangeAXValue,
       AXValueGetType(selectedRangeAXValue) == .cfRange {
        hasSelectedRange = AXValueGetValue(
            selectedRangeAXValue,
            .cfRange,
            &selectedRange
        )
    }

    return AXTextMutationFingerprint(
        value: stringValue(of: element, attribute: kAXValueAttribute),
        selectedText: stringValue(
            of: element,
            attribute: kAXSelectedTextAttribute
        ),
        selectedRangeLocation: hasSelectedRange
            ? Int(selectedRange.location)
            : nil,
        selectedRangeLength: hasSelectedRange
            ? Int(selectedRange.length)
            : nil
    )
}

final class AXPasteboardRetentionBarrier {
    private let target: AXUIElement
    private let before: AXTextMutationFingerprint
    private let changeWake: AXChangeWake?

    init(pid: pid_t, target: AXUIElement) {
        self.target = target
        before = textMutationFingerprint(of: target)
        changeWake = AXChangeWake(pid: pid, targetElement: target)
    }

    func waitForSafeRestore(maximumDelay: TimeInterval = 1.0) {
        let deadline = Date(timeIntervalSinceNow: maximumDelay)
        while true {
            if pasteboardRetentionBarrierSatisfied(
                before: before,
                after: textMutationFingerprint(of: target),
                notificationObserved: changeWake?.targetDidMutate == true,
                deadlineReached: Date() >= deadline
            ) {
                return
            }
            let remaining = max(0, deadline.timeIntervalSinceNow)
            _ = changeWake?.wait(
                timeout: min(0.05, remaining)
            )
            RunLoop.current.run(
                mode: .default,
                before: min(
                    deadline,
                    Date(timeIntervalSinceNow: 0.01)
                )
            )
        }
    }
}

extension ComputerUseService {
    func focusedEditableTextTarget(
        from start: AXUIElement?,
        in snapshot: AppSnapshot
    ) throws -> AXUIElement? {
        try nearestFocusedEditableTarget(
            from: start,
            root: snapshot.rootWindowElement,
            parentOf: copyParent,
            isSame: CFEqual
        ) { element in
            let role = stringValue(
                of: element,
                attribute: kAXRoleAttribute
            )
            let roleDescription = role.flatMap {
                stringValue(
                    of: element,
                    attribute: kAXRoleDescriptionAttribute
                ) ?? humanizedRoleDescription(for: $0)
            }
            return canUseKeyboardTextFallback(
                role: role,
                roleDescription: roleDescription,
                isValueSettable: try isSettableForSetValue(
                    element: element,
                    attribute: kAXValueAttribute
                )
            )
        }
    }

    private func humanizedRoleDescription(for role: String) -> String {
        if role == kAXTextFieldRole as String {
            return "text field"
        }

        switch role {
        case "AXTextArea", "AXTextView":
            return "text entry area"
        default:
            return ""
        }
    }

    func clickFrame(for record: ElementRecord, snapshot: AppSnapshot) -> CGRect? {
        guard let frame = record.localFrame else {
            return nil
        }

        guard
            !record.isSyntheticText,
            let element = record.element,
            stringValue(of: element, attribute: kAXRoleAttribute) == kAXStaticTextRole as String,
            let rowFrame = containingRowFrame(for: element, textFrame: frame, windowBounds: snapshot.windowBounds)
        else {
            return frame
        }

        return rowFrame
    }

    private func containingRowFrame(for element: AXUIElement, textFrame: CGRect, windowBounds: CGRect?) -> CGRect? {
        let textCenter = CGPoint(x: textFrame.midX, y: textFrame.midY)
        var current = element

        for _ in 0..<4 {
            guard let parent = copyParent(of: current) else {
                return nil
            }

            if let frame = localFrame(of: parent, windowBounds: windowBounds),
               frame.insetBy(dx: -2, dy: -2).contains(textCenter),
               frame.width >= textFrame.width + 40,
               frame.height >= textFrame.height,
               frame.height <= max(textFrame.height * 4, 96)
            {
                return frame
            }

            current = parent
        }

        return nil
    }

    func copyActions(for element: AXUIElement) -> [String]? {
        var actions: CFArray?
        let result = AXUIElementCopyActionNames(element, &actions)
        guard result == .success else {
            return nil
        }

        return actions as? [String]
    }

    func copyChildren(of element: AXUIElement) -> [AXUIElement] {
        var value: CFTypeRef?
        let result = AXUIElementCopyAttributeValue(element, kAXChildrenAttribute as CFString, &value)
        guard result == .success, let value else {
            return []
        }

        return value as? [AXUIElement] ?? []
    }

    func copyParent(of element: AXUIElement) -> AXUIElement? {
        var value: CFTypeRef?
        let result = AXUIElementCopyAttributeValue(element, kAXParentAttribute as CFString, &value)
        guard result == .success, let value else {
            return nil
        }

        return checkedAXElement(value)
    }

    func stringValue(of element: AXUIElement, attribute: String) -> String? {
        var value: CFTypeRef?
        let result = AXUIElementCopyAttributeValue(element, attribute as CFString, &value)
        guard result == .success, let value else {
            return nil
        }

        return value as? String
    }

    func localFrame(of element: AXUIElement, windowBounds: CGRect?) -> CGRect? {
        var positionValue: CFTypeRef?
        var sizeValue: CFTypeRef?
        let positionResult = AXUIElementCopyAttributeValue(element, kAXPositionAttribute as CFString, &positionValue)
        let sizeResult = AXUIElementCopyAttributeValue(element, kAXSizeAttribute as CFString, &sizeValue)

        guard
            positionResult == .success,
            sizeResult == .success,
            let positionValue,
            let sizeValue
        else {
            return nil
        }

        guard let positionAXValue = checkedAXValue(positionValue),
              let sizeAXValue = checkedAXValue(sizeValue) else {
            return nil
        }
        var position = CGPoint.zero
        var size = CGSize.zero
        guard AXValueGetValue(positionAXValue, .cgPoint, &position), AXValueGetValue(sizeAXValue, .cgSize, &size) else {
            return nil
        }

        let frame = CGRect(origin: position, size: size)
        guard let windowBounds else {
            return frame
        }

        return windowRelativeFrame(elementFrame: frame, windowBounds: windowBounds)
    }

    func globalPoint(for record: ElementRecord, snapshot: AppSnapshot) throws -> CGPoint? {
        guard let frame = record.localFrame else {
            return nil
        }

        return try windowPointToGlobalPoint(
            snapshot: snapshot,
            point: CGPoint(x: frame.midX, y: frame.midY)
        )
    }

    func clickPoint(for record: ElementRecord, snapshot: AppSnapshot) -> CGPoint? {
        clickActionPoints(for: record, snapshot: snapshot).first ?? localCenter(for: record)
    }

    func debugClickDecision(_ message: String) {
        guard inputFallbackDebugEnabled(environment: ProcessInfo.processInfo.environment) else {
            return
        }

        fputs("[computer-use] click decision \(message)\n", stderr)
    }

    func clickDebugDescription(_ record: ElementRecord) -> String {
        let role = record.element.flatMap { stringValue(of: $0, attribute: kAXRoleAttribute) } ?? "nil"
        let actions = record.rawActions.joined(separator: ",")
        let frame = record.localFrame.map { "x=\(Int($0.minX)) y=\(Int($0.minY)) w=\(Int($0.width)) h=\(Int($0.height))" } ?? "nil"
        return "index=\(record.index) role=\(role) synthetic=\(record.isSyntheticText) actions=[\(actions)] frame=\(frame)"
    }
}

/// The value a text element should hold after `text` replaces its
/// selection, or nil when the selection lies outside the value. AX ranges
/// count UTF-16 code units.
func textAfterInsertion(_ value: String, selection: CFRange, text: String) -> String? {
    let current = value as NSString
    guard selection.location >= 0, selection.length >= 0,
          selection.location + selection.length <= current.length else {
        return nil
    }
    return current.replacingCharacters(
        in: NSRange(location: selection.location, length: selection.length),
        with: text
    )
}

struct TextInsertionBaseline {
    let value: String
    let expected: String
}

/// Nil when the element's value or selection cannot be read, for example
/// in a secure text field; typing then cannot be verified by readback.
func textInsertionBaseline(of element: AXUIElement, inserting text: String) -> TextInsertionBaseline? {
    var range = CFRange()
    guard let value = attributeValue(of: element, attribute: kAXValueAttribute) as? String,
          let rangeValue = attributeValue(
              of: element, attribute: kAXSelectedTextRangeAttribute
          ).flatMap(checkedAXValue),
          AXValueGetValue(rangeValue, .cfRange, &range),
          let expected = textAfterInsertion(value, selection: range, text: text)
    else {
        return nil
    }
    return TextInsertionBaseline(value: value, expected: expected)
}

enum TextReadback: Equatable {
    case expected
    case unchanged
    case other(String?)
}

func waitForTextReadback(
    _ element: AXUIElement,
    baseline: TextInsertionBaseline,
    timeout: TimeInterval
) -> TextReadback {
    let deadline = Date(timeIntervalSinceNow: timeout)
    while true {
        let observed = attributeValue(of: element, attribute: kAXValueAttribute) as? String
        if observed == baseline.expected {
            return .expected
        }
        if Date() >= deadline {
            return observed == baseline.value ? .unchanged : .other(observed)
        }
        Thread.sleep(forTimeInterval: 0.02)
    }
}

func axAttributeIsSettable(_ element: AXUIElement, _ attribute: String) -> Bool {
    var settable = DarwinBoolean(false)
    return AXUIElementIsAttributeSettable(element, attribute as CFString, &settable) == .success
        && settable.boolValue
}

func textReadbackMismatch(route: String, expected: String, observed: String?) -> InputDeliveryError {
    InputDeliveryError(
        message: "Text input changed the element, but readback did not match: expected \(String(reflecting: expected)), observed \(String(reflecting: observed ?? "<missing or non-string>"))",
        inputEventsEmitted: true,
        failureCode: "readback_mismatch",
        partialInputPossible: true,
        readbackSampled: true,
        readbackVerified: false,
        actionRoute: route
    )
}
