import Foundation

func validateMacAccessibilityCapability(tool: String, operation: [String: Any]) throws {
    if tool == "select", operation["to_target"] != nil {
        throw ComputerUseError.capabilityUnsupported(
            "macOS AX select currently supports text within one element. Cross-element selection is unavailable; no input was emitted."
        )
    }
    for key in ["semanticTarget", "element"] {
        if let target = operation[key] as? [String: Any], target["editTarget"] != nil {
            throw ComputerUseError.capabilityUnsupported(
                "This macOS provider does not expose composite editor targets. Refresh AX observe/find and target the native text field directly; no input was emitted."
            )
        }
    }
}

// AXSelectedTextRange counts UTF-16 code units, not Swift Characters or UTF-8 bytes.
func uniqueAXTextSelectionRange(value: String, text: String) throws -> NSRange {
    let source = value as NSString
    guard source.length > 0 else {
        throw ComputerUseError.capabilityUnsupported("The target has no selectable text; no input was emitted.")
    }
    if text.isEmpty { return NSRange(location: 0, length: source.length) }
    let range = source.range(of: text, options: .literal)
    guard range.location != NSNotFound else {
        throw ComputerUseError.invalidArguments("The requested text is not present in the target; no input was emitted.")
    }
    // Search one code unit after the first start to include overlapping matches.
    let remainder = NSRange(location: range.location + 1, length: source.length - range.location - 1)
    guard source.range(of: text, options: .literal, range: remainder).location == NSNotFound else {
        throw ComputerUseError.invalidArguments("The requested text occurs more than once in the target; no input was emitted.")
    }
    return range
}

func setVerifiedAXTextSelection(
    value: String,
    text: String,
    write: (NSRange) throws -> Void,
    read: () -> (value: String?, range: NSRange?, selectedText: String?)
) throws {
    let range = try uniqueAXTextSelectionRange(value: value, text: text)
    let selected = (value as NSString).substring(with: range)
    try write(range)
    let observed = read()
    guard observed.value == value, observed.range == range,
          observed.selectedText == selected else {
        throw InputDeliveryError(
            message: "AX selection was written, but the text and exact selection readback did not match. Observe the current state before retrying.",
            inputEventsEmitted: true,
            failureCode: "readback_mismatch",
            partialInputPossible: false,
            readbackSampled: true,
            readbackVerified: false,
            actionRoute: "ax.attribute.selected_text_range.set"
        )
    }
}
