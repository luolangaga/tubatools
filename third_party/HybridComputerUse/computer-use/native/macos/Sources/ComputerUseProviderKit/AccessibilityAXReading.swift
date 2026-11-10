import ApplicationServices
import Foundation

func readAXExpansion(of element: AXUIElement) throws -> Bool? {
    let cache = AXNodeFactsCache.current
    // A batched read reports an unsupported attribute directly. Such an
    // element does not advertise AXExpanded, so the names read is skipped.
    if let cache,
       cache.attribute(element, kAXExpandedAttribute).status == .attributeUnsupported {
        return nil
    }
    return try readAXExpansion(
        readNames: {
            if let cache {
                let read = cache.attributeNames(element)
                return (read.status, read.value)
            }
            var names: CFArray?
            let status = AXUIElementCopyAttributeNames(element, &names)
            return (status, names)
        },
        readValue: {
            let read = readAttribute(element, kAXExpandedAttribute)
            return (read.status, read.value)
        },
        reportFailure: { facts in
            providerFailureDiagnostic("expansion_read_failure") {
                var payload = facts
                var pid: pid_t = 0
                payload["pid_status"] = Int(AXUIElementGetPid(element, &pid).rawValue)
                payload["pid"] = Int(pid)
                payload["object_hash"] = String(CFHash(element))
                return payload
            }
        }
    )
}

func readAXExpansion(
    readNames: () -> (status: AXError, value: CFTypeRef?),
    readValue: () -> (status: AXError, value: CFTypeRef?),
    reportFailure: ([String: Any]) -> Void = { _ in }
) throws -> Bool? {
    func failure(_ stage: String, _ status: AXError, _ value: CFTypeRef?) {
        reportFailure([
            "stage": stage, "status": Int(status.rawValue),
            "value_nil": value == nil,
            "value_type": value.map { String(CFGetTypeID($0)) } ?? "nil",
            "array_type": String(CFArrayGetTypeID()),
            "boolean_type": String(CFBooleanGetTypeID()),
            "string_array_cast": value is [String],
        ])
    }
    let namesRead = readNames()
    guard namesRead.status == .success,
          let rawNames = namesRead.value,
          CFGetTypeID(rawNames) == CFArrayGetTypeID(),
          let names = rawNames as? [String] else {
        failure("names", namesRead.status, namesRead.value)
        throw ComputerUseError.stateUnavailable(
            "AX expansion applicability could not be read; snapshot rejected."
        )
    }
    // Some providers return false even for an attribute they do not advertise.
    guard names.contains(kAXExpandedAttribute as String) else { return nil }
    let valueRead = readValue()
    guard valueRead.status == .success,
          let value = valueRead.value,
          CFGetTypeID(value) == CFBooleanGetTypeID(),
          let expanded = value as? Bool else {
        failure("value", valueRead.status, valueRead.value)
        throw ComputerUseError.stateUnavailable(
            "Advertised AX expansion state could not be read; snapshot rejected."
        )
    }
    return expanded
}

func checkedAXElement(_ value: CFTypeRef?) -> AXUIElement? {
    guard let value, CFGetTypeID(value) == AXUIElementGetTypeID() else {
        return nil
    }
    return unsafeDowncast(value, to: AXUIElement.self)
}

func checkedAXValue(_ value: CFTypeRef?) -> AXValue? {
    guard let value, CFGetTypeID(value) == AXValueGetTypeID() else {
        return nil
    }
    return unsafeDowncast(value, to: AXValue.self)
}

func childTraversalAttributes(role: String?, hasRows: Bool, hasVisibleChildren: Bool) -> [String] {
    var attributes: [String] = []
    if !(hasRows && usesRowsAsPrimaryRole(role)) && !(hasVisibleChildren && usesVisibleChildrenAsPrimaryRole(role)) {
        attributes.append(kAXChildrenAttribute)
    }
    attributes.append(kAXRowsAttribute)
    attributes.append(axContentsAttribute)
    attributes.append(axVisibleChildrenAttribute)
    return attributes
}

private func usesRowsAsPrimaryRole(_ role: String?) -> Bool {
    return [
        kAXOutlineRole as String,
        kAXListRole as String,
        kAXTableRole as String,
        "AXBrowser",
    ].contains(role)
}

private func usesVisibleChildrenAsPrimaryRole(_ role: String?) -> Bool {
    role == kAXListRole as String
}

func shouldSkipChild(_ child: AXUIElement, of parent: AXUIElement) -> Bool {
    let parentRole = stringValue(of: parent, attribute: kAXRoleAttribute)
    guard parentRole == kAXMenuBarRole as String else {
        return false
    }

    return stringValue(of: child, attribute: kAXTitleAttribute) == "Apple"
}

func shouldContinueRendering(
    nextIndex: Int,
    depth: Int,
    limits: AccessibilityTreeLimits = .defaults
) -> Bool {
    nextIndex < limits.maxNodeCount && depth < limits.maxDepth
}

func summarizeTraits(
    of element: AXUIElement,
    expansion: Bool?,
    valueSettable: Bool? = nil
) -> [String] {
    var values: [String] = []

    if boolValue(of: element, attribute: kAXSelectedAttribute) == true {
        values.append("selected")
    }

    if expansion == true {
        values.append("expanded")
    }

    if boolValue(of: element, attribute: kAXEnabledAttribute) == false {
        values.append("disabled")
    }

    if let valueType = valueTypeTrait(
        of: element,
        valueSettable: valueSettable
            ?? axValueAttributeIsSettable(element)
    ) {
        values.append(valueType)
    }

    return values
}

private func valueTypeTrait(
    of element: AXUIElement,
    valueSettable: Bool
) -> String? {
    guard valueSettable else {
        return nil
    }

    guard let value = attributeValue(of: element, attribute: kAXValueAttribute) else {
        return nil
    }

    if CFGetTypeID(value) == CFStringGetTypeID() {
        return "string"
    }

    if value is NSNumber {
        if numericValueRepresentsBoolean(for: element, value: value) {
            return "boolean"
        }

        return "float"
    }

    return nil
}

func axValueAttributeIsSettable(_ element: AXUIElement) -> Bool {
    isSettable(of: element, attribute: kAXValueAttribute)
}

func publicStringSetValueSupported(
    settable: Bool,
    value: CFTypeRef?,
    role: String?
) -> Bool {
    guard settable, let value, let role,
          !roleUsesRelativeValueMutation(role) else {
        return false
    }
    return CFGetTypeID(value) == CFStringGetTypeID()
}

enum RelativeAdjustmentScalarReadback: Equatable, Sendable {
    case string(String)
    case number(String)

    var description: String {
        switch self {
        case .string(let value), .number(let value):
            return String(reflecting: value)
        }
    }
}

enum RelativeAdjustmentReadbackOutcome: Equatable, Sendable {
    case changed
    case unchanged
    case unavailable
}

func relativeAdjustmentScalarReadback(
    _ value: CFTypeRef?
) -> RelativeAdjustmentScalarReadback? {
    guard let value else {
        return nil
    }
    if CFGetTypeID(value) == CFStringGetTypeID(),
       let string = value as? String {
        return .string(string)
    }
    if let number = value as? NSNumber {
        return .number(number.stringValue)
    }
    return nil
}

func relativeAdjustmentReadbackOutcome(
    before: RelativeAdjustmentScalarReadback?,
    after: RelativeAdjustmentScalarReadback?
) -> RelativeAdjustmentReadbackOutcome {
    guard let before, let after else {
        return .unavailable
    }
    return before == after ? .unchanged : .changed
}

func roleUsesRelativeValueMutation(_ role: String) -> Bool {
    switch role {
    case kAXSliderRole,
         kAXIncrementorRole,
         kAXScrollBarRole,
         kAXProgressIndicatorRole:
        return true
    default:
        return false
    }
}

func exactStringReadbackMatches(
    requested: String,
    observed: CFTypeRef?
) -> Bool {
    guard let observed, CFGetTypeID(observed) == CFStringGetTypeID(),
          let observedString = observed as? String else {
        return false
    }
    return observedString == requested
}

func copyElement(_ element: AXUIElement, attribute: String) -> AXUIElement? {
    let read = readAttribute(element, attribute)
    guard read.status == .success, let value = read.value else {
        return nil
    }

    return checkedAXElement(value)
}

func copyArray(_ element: AXUIElement, attribute: String) -> [AXUIElement]? {
    let read = readAttribute(element, attribute)
    guard read.status == .success, let value = read.value else {
        return nil
    }

    return value as? [AXUIElement]
}

func copyActions(_ element: AXUIElement) -> [String]? {
    if let cache = AXNodeFactsCache.current {
        let read = cache.actionNames(element)
        return read.status == .success ? read.value as? [String] : nil
    }
    var actions: CFArray?
    let error = AXUIElementCopyActionNames(element, &actions)
    guard error == .success else {
        return nil
    }

    return actions as? [String]
}

func attributeValue(of element: AXUIElement, attribute: String) -> CFTypeRef? {
    let read = readAttribute(element, attribute)
    return read.status == .success ? read.value : nil
}

func copyAttributeValues(
    _ element: AXUIElement,
    attributes: [String]
) -> [String: Any] {
    if let cache = AXNodeFactsCache.current {
        var result: [String: Any] = [:]
        for attribute in attributes {
            let read = cache.attribute(element, attribute)
            if read.status == .success, let value = read.value,
               CFGetTypeID(value) != AXValueGetTypeID() {
                result[attribute] = value
            }
        }
        return result
    }
    var values: CFArray?
    let error = AXUIElementCopyMultipleAttributeValues(
        element,
        attributes as CFArray,
        [],
        &values
    )
    guard error == .success,
          let rawValues = values as? [Any],
          rawValues.count == attributes.count else {
        return Dictionary(
            uniqueKeysWithValues: attributes.compactMap { attribute in
                attributeValue(of: element, attribute: attribute).map {
                    (attribute, $0 as Any)
                }
            }
        )
    }
    return Dictionary(
        uniqueKeysWithValues: zip(attributes, rawValues).filter { _, value in
            CFGetTypeID(value as CFTypeRef) != AXValueGetTypeID()
        }
    )
}

func elementArray(_ value: Any?) -> [AXUIElement] {
    value as? [AXUIElement] ?? []
}

func stringValue(_ value: Any?) -> String? {
    guard let value,
          CFGetTypeID(value as CFTypeRef) == CFStringGetTypeID(),
          let string = value as? String else {
        return nil
    }
    return string.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        ? nil
        : string
}

func stringValue(of element: AXUIElement, attribute: String) -> String? {
    guard let value = attributeValue(of: element, attribute: attribute) else {
        return nil
    }

    if CFGetTypeID(value) == CFStringGetTypeID() {
        guard let string = value as? String else {
            return nil
        }

        return string.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : string
    }

    return nil
}

func copySelectedText(_ element: AXUIElement, textLimit: SnapshotTextLimit = .defaults) -> String? {
    guard let value = stringValue(of: element, attribute: kAXSelectedTextAttribute) else {
        return nil
    }

    let sanitized = sanitizeText(value, textLimit: textLimit)
    return sanitized.isEmpty ? nil : sanitized
}

func boolValue(of element: AXUIElement, attribute: String) -> Bool? {
    guard let value = attributeValue(of: element, attribute: attribute) else {
        return nil
    }

    return value as? Bool
}

func structuredStateTokens(
    of element: AXUIElement,
    role: String,
    focusedElement: AXUIElement?,
    expansion: Bool?
) -> String? {
    var tokens: [String] = []

    if let focusedElement, CFEqual(focusedElement, element) {
        tokens.append("focused")
    }
    if let enabled = boolValue(of: element, attribute: kAXEnabledAttribute) {
        tokens.append(enabled ? "enabled" : "disabled")
    }
    if let selected = boolValue(of: element, attribute: kAXSelectedAttribute) {
        tokens.append(selected ? "selected" : "unselected")
    }
    if let expansion {
        tokens.append(expansion ? "expanded" : "collapsed")
    }
    if role == kAXCheckBoxRole as String || role == kAXRadioButtonRole as String {
        if let rawValue = attributeValue(of: element, attribute: kAXValueAttribute) {
            if let number = rawValue as? NSNumber {
                if number.intValue == 2 {
                    tokens.append("mixed")
                } else {
                    tokens.append(number.boolValue ? "checked" : "unchecked")
                }
            }
        }
    }

    return tokens.isEmpty ? nil : "[\(tokens.joined(separator: " "))]"
}

func pid(of element: AXUIElement) -> pid_t {
    var processIdentifier: pid_t = 0
    AXUIElementGetPid(element, &processIdentifier)
    return processIdentifier
}

private func isSettable(of element: AXUIElement, attribute: String) -> Bool {
    if let cache = AXNodeFactsCache.current {
        return cache.isSettable(element, attribute)
    }
    var settable = DarwinBoolean(false)
    let error = AXUIElementIsAttributeSettable(element, attribute as CFString, &settable)
    return error == .success && settable.boolValue
}

func sanitizedValue(of element: AXUIElement, textLimit: SnapshotTextLimit = .defaults) -> String? {
    guard let value = semanticValue(of: element, textLimit: textLimit) else {
        return nil
    }

    let sanitized = sanitizeText(value, textLimit: textLimit)
    return sanitized.isEmpty ? nil : sanitized
}

func semanticValue(of element: AXUIElement, textLimit: SnapshotTextLimit = .defaults) -> String? {
    if let string = stringValue(of: element, attribute: kAXValueAttribute) {
        let semantic = boundedSemanticText(string, textLimit: textLimit)
        return semantic.isEmpty ? nil : semantic
    }

    guard let value = attributeValue(of: element, attribute: kAXValueAttribute) else {
        return nil
    }

    if let number = value as? NSNumber {
        if numericValueRepresentsBoolean(for: element, value: value) {
            return number.boolValue ? "on" : "off"
        }

        return number.stringValue
    }

    return nil
}

func boundedSemanticText(
    _ value: String,
    textLimit: SnapshotTextLimit = .defaults
) -> String {
    let normalized = value
        .replacingOccurrences(of: "\r\n", with: "\n")
        .replacingOccurrences(of: "\r", with: "\n")
        .trimmingCharacters(in: .whitespacesAndNewlines)

    if let maxCount = textLimit.maxCount, normalized.count > maxCount {
        return String(normalized.prefix(maxCount)) + "..."
    }

    return normalized
}

func placeholderValue(of element: AXUIElement, textLimit: SnapshotTextLimit = .defaults) -> String? {
    for attribute in ["AXPlaceholderValue", "AXPlaceholder"] {
        if let string = stringValue(of: element, attribute: attribute) {
            let sanitized = sanitizeText(string, textLimit: textLimit)
            if !sanitized.isEmpty {
                return sanitized
            }
        }
    }

    return nil
}

private func numericValueRepresentsBoolean(for element: AXUIElement, value: CFTypeRef) -> Bool {
    guard let number = value as? NSNumber else {
        return false
    }

    guard number == 0 || number == 1 else {
        return false
    }

    let role = stringValue(of: element, attribute: kAXRoleAttribute) ?? ""
    let roleText = roleDescription(
        of: element,
        role: role,
        subrole: stringValue(of: element, attribute: kAXSubroleAttribute)
    )

    return roleText == "tab"
        || role == kAXCheckBoxRole as String
        || role == kAXRadioButtonRole as String
}
