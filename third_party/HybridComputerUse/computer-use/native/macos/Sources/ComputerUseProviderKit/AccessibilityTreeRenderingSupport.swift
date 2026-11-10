import ApplicationServices
import CoreGraphics
import Foundation

func preferredDisplayTitle(
    for element: AXUIElement,
    role: String,
    label: String?,
    identifier: String?,
    explicitValue: String?,
    rowTexts: [String],
    textLimit: SnapshotTextLimit = .defaults,
    readTitle: (() -> String?)? = nil
) -> String? {
    let rawTitle: String?
    if let readTitle {
        rawTitle = readTitle()
    } else {
        rawTitle = stringValue(of: element, attribute: kAXTitleAttribute)
    }
    if let title = rawTitle, !title.isEmpty {
        return sanitizeText(title, textLimit: textLimit)
    }

    if role == kAXRowRole as String {
        return rowTexts.first
    }

    if (role == kAXOutlineRole as String || role == kAXListRole as String), let identifier {
        return identifier
    }

    if (role == kAXButtonRole as String || role == kAXPopUpButtonRole as String), let label, !label.isEmpty {
        return sanitizeText(label, textLimit: textLimit)
    }

    if role == kAXImageRole as String, let label, !label.isEmpty {
        return sanitizeText(label, textLimit: textLimit)
    }

    if (role == kAXGroupRole as String || role == kAXUnknownRole as String || role == "AXWebArea"),
       let label,
       !label.isEmpty
    {
        return sanitizeText(label, textLimit: textLimit)
    }

    guard roleDescription(of: element, role: role, subrole: stringValue(of: element, attribute: kAXSubroleAttribute)) == "search text field" else {
        return nil
    }

    return explicitValue
}

func markdownLinkText(
    for element: AXUIElement,
    title: String?,
    label: String?,
    value: String?,
    textLimit: SnapshotTextLimit = .defaults
) -> String? {
    guard let url = urlValue(of: element, attribute: kAXURLAttribute, textLimit: textLimit), !url.isEmpty else {
        return nil
    }

    let text = [label, title, value]
        .compactMap { candidate -> String? in
            guard let candidate else {
                return nil
            }
            let sanitized = sanitizeText(candidate, textLimit: textLimit)
            return sanitized.isEmpty ? nil : sanitized
        }
        .first

    guard let text else {
        return nil
    }

    return "[\(markdownEscapedLinkText(text))](\(url))"
}

func markdownEscapedLinkText(_ text: String) -> String {
    text
        .replacingOccurrences(of: "\\", with: "\\\\")
        .replacingOccurrences(of: "[", with: "\\[")
        .replacingOccurrences(of: "]", with: "\\]")
}

func outlineRowSummary(for element: AXUIElement, role: String) -> String? {
    guard role == kAXOutlineRole as String || role == kAXListRole as String else {
        return nil
    }

    guard let allRows = copyArray(element, attribute: kAXRowsAttribute), !allRows.isEmpty else {
        return nil
    }

    let visibleRows = visibleRows(in: allRows, parent: element)
    guard !visibleRows.isEmpty, visibleRows.count < allRows.count else {
        return nil
    }

    return "(showing 0-\(visibleRows.count - 1) of \(allRows.count) items)"
}

func formattedValueSegment(for element: AXUIElement, roleText: String, title: String?, value: String?) -> String {
    guard let value, !value.isEmpty else {
        return ""
    }

    if roleText == "search text field", title == value {
        return ""
    }

    if title == nil, let role = stringValue(of: element, attribute: kAXRoleAttribute), role == kAXStaticTextRole as String {
        return " \(value)"
    }

    if ["scroll bar", "value indicator"].contains(roleText) {
        return " \(value)"
    }

    if roleText == "text entry area" {
        return " \(value)"
    }

    return " Value: \(value)"
}

func formattedLabelSegment(
    _ label: String?,
    title: String?,
    linkText: String?,
    textLimit: SnapshotTextLimit = .defaults
) -> String {
    guard let label, label != title else {
        return ""
    }

    let sanitizedLabel = sanitizeText(label, textLimit: textLimit)
    guard !sanitizedLabel.isEmpty, sanitizedLabel != title else {
        return ""
    }

    let comparableLabel = markdownEscapedLinkText(sanitizedLabel)
    if let linkText, linkText.hasPrefix("[\(comparableLabel)](") {
        return ""
    }

    return " Description: \(sanitizedLabel)"
}

func formattedValueSegmentWithSeparator(_ valueSegment: String, precedingSegments: [String]) -> String {
    guard valueSegment.hasPrefix(" Value:"), precedingSegments.contains(where: { !$0.isEmpty }) else {
        return valueSegment
    }

    return ",\(valueSegment)"
}

func formattedPlaceholderSegment(_ placeholder: String?, title: String?, label: String?, value: String?, precedingSegments: [String]) -> String {
    guard let placeholder, !placeholder.isEmpty else {
        return ""
    }

    if placeholder == title || placeholder == label || placeholder == value {
        return ""
    }

    let prefix = precedingSegments.contains(where: { !$0.isEmpty }) || title != nil ? ", Placeholder: " : " Placeholder: "
    return "\(prefix)\(placeholder)"
}

func shouldCommaSeparateActions(
    title: String?,
    inlineRowSummary: String?,
    genericTextSummary: String?,
    segments: [String]
) -> Bool {
    title != nil
        || inlineRowSummary != nil
        || genericTextSummary != nil
        || segments.contains(where: { !$0.isEmpty })
}

func formattedURLSegment(
    for element: AXUIElement,
    title: String?,
    label: String?,
    textLimit: SnapshotTextLimit = .defaults
) -> String {
    guard stringValue(of: element, attribute: kAXRoleAttribute) == "AXWebArea" else {
        return ""
    }

    guard let url = urlValue(of: element, attribute: kAXURLAttribute, textLimit: textLimit), !url.isEmpty else {
        return ""
    }

    if url == title || url == label {
        return ""
    }

    return ", URL: \(url)"
}

func urlValue(
    of element: AXUIElement,
    attribute: String,
    textLimit: SnapshotTextLimit = .defaults
) -> String? {
    guard let value = attributeValue(of: element, attribute: attribute) else {
        return nil
    }

    if CFGetTypeID(value) == CFStringGetTypeID(), let string = value as? String {
        let sanitized = sanitizeText(string, textLimit: textLimit)
        return sanitized.isEmpty ? nil : sanitized
    }

    if CFGetTypeID(value) == CFURLGetTypeID(), let url = value as? URL {
        let sanitized = sanitizeText(url.absoluteString, textLimit: textLimit)
        return sanitized.isEmpty ? nil : sanitized
    }

    return nil
}

func displayIdentifierSegment(for element: AXUIElement, role: String, identifier: String?, title: String?) -> String {
    guard let identifier else {
        return ""
    }

    if (role == kAXOutlineRole as String || role == kAXListRole as String), title == identifier {
        return ""
    }

    return " ID: \(identifier)"
}

func resolveLocalFrame(of element: AXUIElement, windowBounds: CGRect?) -> CGRect? {
    let positionRead = readAttribute(element, kAXPositionAttribute)
    let sizeRead = readAttribute(element, kAXSizeAttribute)
    guard
        positionRead.status == .success,
        sizeRead.status == .success,
        let positionValue = positionRead.value,
        let sizeValue = sizeRead.value
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

    guard let frame = finiteAccessibilityFrame(
        CGRect(origin: position, size: size)
    ) else {
        return nil
    }

    guard let windowBounds else {
        return frame
    }

    guard let finiteWindowBounds = finiteAccessibilityFrame(windowBounds) else {
        return nil
    }
    return finiteAccessibilityFrame(
        windowRelativeFrame(
            elementFrame: frame,
            windowBounds: finiteWindowBounds
        )
    )
}

func finiteAccessibilityFrame(_ frame: CGRect) -> CGRect? {
    guard frame.origin.x.isFinite,
          frame.origin.y.isFinite,
          frame.size.width.isFinite,
          frame.size.height.isFinite else {
        return nil
    }
    return frame
}

func shouldElideNode(
    role: String,
    title: String?,
    label: String?,
    value: String?,
    identifier: String?,
    traits: [String],
    actions: [String],
    childCount: Int,
    genericTextSummary: String? = nil,
    webAreaDepth: Int? = nil,
    preservesCompactGenericActionTarget: Bool = false
) -> Bool {
    let genericRoles = [kAXGroupRole as String, kAXUnknownRole as String]
    guard genericRoles.contains(role) else {
        return false
    }

    if preservesCompactGenericActionTarget {
        return false
    }

    if genericTextSummary != nil {
        return false
    }

    if shouldPreserveWebAreaGenericContainer(childCount: childCount, webAreaDepth: webAreaDepth) {
        return false
    }

    if childCount == 1,
       title == nil,
       label == nil,
       value == nil,
       identifier == nil,
       actions.isEmpty,
       traitsAreNonDescriptiveWrapperTraits(traits)
    {
        return true
    }

    return title == nil
        && label == nil
        && value == nil
        && identifier == nil
        && traits.isEmpty
        && actions.isEmpty
}

func shouldPreserveWebAreaGenericContainer(childCount: Int, webAreaDepth: Int?) -> Bool {
    guard childCount > 0, webAreaDepth != nil else {
        return false
    }

    return childCount > 1
}

private func traitsAreNonDescriptiveWrapperTraits(_ traits: [String]) -> Bool {
    traits.isEmpty || traits == ["string"]
}

func hasPrimaryClickAction(_ actions: [String]) -> Bool {
    let primaryActions = [
        kAXPressAction as String,
        "AXOpen",
    ]

    return actions.contains { action in
        primaryActions.contains { $0.caseInsensitiveCompare(action) == .orderedSame }
    }
}

struct SemanticControlProxyFacts {
    let role: String
    let enabled: Bool?
    let hasSemanticLabel: Bool
    let hasPrimaryAction: Bool
    let valueSettable: Bool
}

func semanticControlProxyChildIndex(
    parent: SemanticControlProxyFacts,
    children: [SemanticControlProxyFacts]
) -> Int? {
    let supportedRoles = [
        kAXCheckBoxRole as String,
        kAXRadioButtonRole as String,
    ]
    guard supportedRoles.contains(parent.role),
          parent.enabled == false,
          parent.hasSemanticLabel,
          !parent.hasPrimaryAction,
          !parent.valueSettable else {
        return nil
    }

    let candidates = children.indices.filter { index in
        let child = children[index]
        return child.role == parent.role
            && child.enabled == true
            && !child.hasSemanticLabel
            && (child.hasPrimaryAction || child.valueSettable)
    }
    guard candidates.count == 1 else {
        return nil
    }

    let candidate = candidates[0]
    guard !children.indices.contains(where: { index in
        index != candidate
            && (children[index].hasPrimaryAction || children[index].valueSettable)
    }) else {
        return nil
    }
    return candidate
}

func shouldRenderCompactGenericActionTarget(
    role: String,
    hasPrimaryClickAction: Bool,
    localFrame: CGRect?
) -> Bool {
    guard hasPrimaryClickAction else {
        return false
    }

    guard role == kAXGroupRole as String || role == kAXUnknownRole as String else {
        return false
    }

    guard let localFrame,
          localFrame.width > 0,
          localFrame.height > 0,
          localFrame.width <= compactGenericActionTargetMaxWidth,
          localFrame.height <= compactGenericActionTargetMaxHeight
    else {
        return false
    }

    return true
}

func shouldSuppressChildren(
    role: String,
    title: String?,
    label: String?,
    help: String?,
    value: String?,
    identifier: String?,
    traits: [String],
    actions: [String],
    children: [AXUIElement],
    genericTextSummary: String?
) -> Bool {
    if role == kAXMenuBarItemRole as String {
        return !traits.contains("selected") && !traits.contains("expanded")
    }

    if role == "AXLink", title?.hasPrefix("[") == true {
        return true
    }

    return genericTextSummary != nil
}
