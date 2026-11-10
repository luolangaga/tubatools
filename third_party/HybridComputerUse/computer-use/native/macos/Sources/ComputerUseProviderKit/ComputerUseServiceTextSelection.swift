import ApplicationServices
import Foundation

extension ComputerUseService {
    func selectText(app: String, elementIndex: String, text: String) throws {
        let snapshot = try currentSnapshot(for: app)
        let record = try lookupElement(snapshot: snapshot, index: elementIndex)
        guard let element = record.element else {
            throw ComputerUseError.stateUnavailable("The target has no backing accessibility object")
        }
        guard try isSettableForSetValue(element: element, attribute: kAXSelectedTextRangeAttribute),
              let value = stringValue(of: element, attribute: kAXValueAttribute) else {
            throw ComputerUseError.capabilityUnsupported(
                "This element does not expose readable text with a writable AX selection range; no input was emitted."
            )
        }
        try setVerifiedAXTextSelection(value: value, text: text, write: { range in
            var nativeRange = CFRange(location: range.location, length: range.length)
            guard let rangeValue = AXValueCreate(.cfRange, &nativeRange) else {
                throw ComputerUseError.stateUnavailable("Could not encode the selection range; no input was emitted.")
            }
            let result = AXUIElementSetAttributeValue(element, kAXSelectedTextRangeAttribute as CFString, rangeValue)
            guard result == .success else {
                // AX can fail after delivery. Do not report that input certainly never happened.
                throw InputDeliveryError(
                    message: "AX selection write failed (\(result.rawValue)); its effect is unknown. Observe before retrying.",
                    inputEventsEmitted: true,
                    failureCode: "effect_unknown",
                    partialInputPossible: true,
                    actionRoute: "ax.attribute.selected_text_range.set"
                )
            }
        }, read: {
            var raw: CFTypeRef?
            var range = CFRange()
            var observedRange: NSRange?
            if AXUIElementCopyAttributeValue(element, kAXSelectedTextRangeAttribute as CFString, &raw) == .success,
               let axValue = checkedAXValue(raw), AXValueGetType(axValue) == .cfRange,
               AXValueGetValue(axValue, .cfRange, &range), range.location >= 0, range.length >= 0 {
                observedRange = NSRange(location: range.location, length: range.length)
            }
            return (
                self.stringValue(of: element, attribute: kAXValueAttribute),
                observedRange,
                self.stringValue(of: element, attribute: kAXSelectedTextAttribute)
            )
        })
    }
}
