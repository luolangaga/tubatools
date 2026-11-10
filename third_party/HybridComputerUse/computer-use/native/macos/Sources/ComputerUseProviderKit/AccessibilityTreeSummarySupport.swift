import ApplicationServices
import CoreGraphics
import Foundation

func summarizedGenericText(
    of element: AXUIElement,
    role: String,
    childElements: [AXUIElement],
    textLimit: SnapshotTextLimit = .defaults,
    minimumTextCount: Int = 2
) -> String? {
    guard role == kAXGroupRole as String || role == kAXUnknownRole as String else {
        return nil
    }

    guard !childElements.isEmpty else {
        return nil
    }

    guard isPlainGenericTextContainer(element, children: childElements) else {
        return nil
    }

    let texts = descendantTextsForSummary(of: element, textLimit: textLimit)
    guard texts.count >= minimumTextCount else {
        return nil
    }

    guard shouldMergeTextOnlySiblings(texts) else {
        return nil
    }

    let joined = sanitizeText(texts.joined(separator: " "), textLimit: textLimit)
        .replacingOccurrences(of: " : ", with: " :  ")
    return joined.isEmpty ? nil : joined
}

func summaryImageDescendants(of element: AXUIElement, depth: Int = 0) -> [TraversalChild<AXUIElement>] {
    guard depth < 4 else {
        return []
    }

    let children = copyArray(element, attribute: kAXChildrenAttribute) ?? []
    var images: [TraversalChild<AXUIElement>] = []

    for child in structuralChildren(children, source: .children) {
        let role = stringValue(of: child.element, attribute: kAXRoleAttribute) ?? ""
        if role == kAXImageRole as String {
            if !images.contains(where: { CFEqual($0.element, child.element) }) {
                images.append(child)
            }
        } else {
            for image in summaryImageDescendants(of: child.element, depth: depth + 1) {
                if !images.contains(where: { CFEqual($0.element, image.element) }) {
                    images.append(TraversalChild(
                        element: image.element,
                        relativePath: child.relativePath + image.relativePath
                    ))
                }
            }
        }

        if images.count >= 4 {
            return Array(images.prefix(4))
        }
    }

    return images
}

func shouldRenderGenericTextSummaryAsChildren(_ genericTextSummary: String?, summaryImageCount: Int) -> Bool {
    genericTextSummary != nil && summaryImageCount > 0
}

func shouldMergeTextOnlySiblings(_ texts: [String]) -> Bool {
    if texts.contains("日期") && texts.contains("时间") {
        return false
    }

    if texts.contains(where: isSiblingCounterText(_:)) {
        return false
    }

    if texts.contains(where: isStandaloneTimeRangeText(_:)) {
        return false
    }

    let totalLength = texts.reduce(0) { $0 + $1.count }
    return texts.count <= 8 && totalLength <= 220
}

private func isSiblingCounterText(_ text: String) -> Bool {
    text.range(of: #"^\d+\s*/\s*\d+$"#, options: .regularExpression) != nil
}

private func isStandaloneTimeRangeText(_ text: String) -> Bool {
    text.range(of: #"^\d{1,2}:\d{2}\s*-\s*\d{1,2}:\d{2}$"#, options: .regularExpression) != nil
}

private func isPlainGenericTextContainer(_ element: AXUIElement, children: [AXUIElement], depth: Int = 0) -> Bool {
    for child in children {
        let childRole = stringValue(of: child, attribute: kAXRoleAttribute) ?? ""

        if childRole == kAXStaticTextRole as String || childRole == kAXImageRole as String {
            continue
        }

        if childRole == "AXLink", summaryTextForLink(child) != nil {
            continue
        }

        if childRole == kAXGroupRole as String || childRole == kAXUnknownRole as String {
            // Crossing this boundary would collapse the actionable child into its parent's text summary.
            if isGenericPrimaryActionSummaryBoundary(
                role: childRole,
                actions: copyActions(child) ?? []
            ) {
                return false
            }

            guard depth < 3 else {
                return false
            }

            if isPlainGenericTextContainer(child, children: copyArray(child, attribute: kAXChildrenAttribute) ?? [], depth: depth + 1) {
                continue
            }
        }

        return false
    }

    return true
}

func isGenericPrimaryActionSummaryBoundary(role: String, actions: [String]) -> Bool {
    let genericRoles = [kAXGroupRole as String, kAXUnknownRole as String]
    return genericRoles.contains(role) && hasPrimaryClickAction(actions)
}

func displayRoleText(
    baseRoleText: String,
    role: String,
    title: String?,
    label: String?,
    suppressChildren: Bool
) -> String {
    if role == kAXMenuBarItemRole as String {
        return ""
    }

    if role == "AXLink" {
        return baseRoleText
    }

    if suppressChildren {
        return "container"
    }

    if baseRoleText == "radio group", role == kAXRadioGroupRole as String, title == nil, label != nil {
        return ""
    }

    return baseRoleText
}

func windowRelativeFrame(elementFrame: CGRect, windowBounds: CGRect) -> CGRect {
    CGRect(
        x: elementFrame.minX - windowBounds.minX,
        y: elementFrame.minY - windowBounds.minY,
        width: elementFrame.width,
        height: elementFrame.height
    )
}

func roleDescription(of element: AXUIElement, role: String, subrole: String?) -> String {
    if role == kAXRowRole as String {
        return "row"
    }

    if role == kAXGroupRole as String {
        return "container"
    }

    if role == kAXMenuBarItemRole as String {
        return ""
    }

    if role == "AXLink" {
        return "link"
    }

    if role == "AXWebArea" {
        return stringValue(of: element, attribute: kAXRoleDescriptionAttribute) ?? "HTML 内容"
    }

    if let roleDescription = stringValue(of: element, attribute: kAXRoleDescriptionAttribute), !roleDescription.isEmpty {
        return roleDescription.lowercased()
    }

    if let subrole, subrole == kAXStandardWindowSubrole as String {
        return "standard window"
    }

    return humanizeAXToken(role)
}

func canonicalAXRole(_ role: String, subrole: String?) -> String {
    if role == kAXWindowRole as String {
        return subrole == kAXDialogSubrole as String ? "dialog" : "window"
    }

    return [
        "AXApplication": "application",
        "AXButton": "button",
        "AXCheckBox": "checkbox",
        "AXCell": "cell",
        "AXComboBox": "combobox",
        "AXGroup": "group",
        "AXImage": "img",
        "AXIncrementor": "spinbutton",
        "AXLink": "link",
        "AXList": "list",
        "AXMenu": "menu",
        "AXMenuBar": "menubar",
        "AXMenuItem": "menuitem",
        "AXOutline": "tree",
        "AXPopUpButton": "combobox",
        "AXProgressIndicator": "progressbar",
        "AXRadioButton": "radio",
        "AXRow": "listitem",
        "AXScrollArea": "group",
        "AXScrollBar": "scrollbar",
        "AXSheet": "dialog",
        "AXSlider": "slider",
        "AXStaticText": "text",
        "AXTabGroup": "tablist",
        "AXTable": "table",
        "AXTextArea": "textbox",
        "AXTextField": "textbox",
        "AXToolbar": "toolbar",
        "AXWebArea": "document",
    ][role] ?? humanizeAXToken(role)
}

func meaningfulActions(_ values: [String], role: String) -> [String] {
    values
        .filter {
            var ignored = [
                kAXPressAction as String,
                "AXShowDefaultUI",
                "AXShowAlternateUI",
                "AXShowMenu",
                "AXConfirm",
                "AXScrollToVisible",
            ]

            if [
                kAXMenuBarRole as String,
                kAXMenuBarItemRole as String,
                kAXMenuRole as String,
                kAXMenuItemRole as String,
            ].contains(role) {
                ignored.append(contentsOf: ["AXCancel", "AXPick"])
            }

            return !ignored.contains($0)
        }
        .filter {
            guard role == kAXScrollAreaRole as String else {
                return true
            }

            if values.contains("AXScrollUpByPage") || values.contains("AXScrollDownByPage") {
                return $0 != "AXScrollLeftByPage" && $0 != "AXScrollRightByPage"
            }

            return true
        }
        .map(prettyActionName(_:))
}

private func prettyActionName(_ value: String) -> String {
    if value == "AXZoomWindow" {
        return "zoom the window"
    }

    let stripped = value.hasPrefix("AX") ? String(value.dropFirst(2)) : value
    let withoutPage = stripped.replacingOccurrences(of: "ByPage", with: "")
    return splitCamelCase(withoutPage)
}

private func humanizeAXToken(_ value: String) -> String {
    let stripped = value.hasPrefix("AX") ? String(value.dropFirst(2)) : value
    return splitCamelCase(stripped).lowercased()
}

private func splitCamelCase(_ value: String) -> String {
    var result = ""
    for character in value {
        if character.isUppercase, !result.isEmpty {
            result.append(" ")
        }
        result.append(character)
    }
    return result
}

func sanitizeText(_ value: String, textLimit: SnapshotTextLimit = .defaults) -> String {
    let collapsed = value
        .replacingOccurrences(of: "\n", with: "\\n")
        .trimmingCharacters(in: .whitespacesAndNewlines)

    if let maxCount = textLimit.maxCount, collapsed.count > maxCount {
        return String(collapsed.prefix(maxCount)) + "..."
    }

    return collapsed
}

func flattenedRowTexts(
    of element: AXUIElement,
    textLimit: SnapshotTextLimit = .defaults
) -> [String] {
    let cells = copyArray(element, attribute: kAXChildrenAttribute) ?? []
    let texts = cells
        .flatMap { descendantTexts(of: $0, textLimit: textLimit) }
        .map { sanitizeText($0, textLimit: textLimit) }
        .filter { !$0.isEmpty }

    var unique: [String] = []
    var seen: Set<String> = []
    for text in texts {
        if seen.insert(text).inserted {
            unique.append(text)
        }
    }

    return unique
}

private func descendantTexts(
    of element: AXUIElement,
    depth: Int = 0,
    textLimit: SnapshotTextLimit = .defaults
) -> [String] {
    guard depth < 4 else {
        return []
    }

    var values: [String] = []
    let role = stringValue(of: element, attribute: kAXRoleAttribute) ?? ""
    if role == kAXStaticTextRole as String || role == kAXTextFieldRole as String {
        if let value = sanitizedValue(of: element, textLimit: textLimit) {
            values.append(value)
        } else if let title = stringValue(of: element, attribute: kAXTitleAttribute) {
            values.append(sanitizeText(title, textLimit: textLimit))
        }
    }

    for child in copyArray(element, attribute: kAXChildrenAttribute) ?? [] {
        values.append(contentsOf: descendantTexts(of: child, depth: depth + 1, textLimit: textLimit))
    }

    return values
}

private func descendantTextsForSummary(
    of element: AXUIElement,
    depth: Int = 0,
    textLimit: SnapshotTextLimit = .defaults
) -> [String] {
    guard depth < 8 else {
        return []
    }

    let role = stringValue(of: element, attribute: kAXRoleAttribute) ?? ""
    if role == "AXLink", let linkText = summaryTextForLink(element, textLimit: textLimit) {
        return [linkText]
    }

    if role == kAXStaticTextRole as String || role == kAXTextFieldRole as String {
        if let value = sanitizedValue(of: element, textLimit: textLimit), !value.isEmpty {
            return [value]
        }

        if let title = stringValue(of: element, attribute: kAXTitleAttribute) {
            let sanitized = sanitizeText(title, textLimit: textLimit)
            return sanitized.isEmpty ? [] : [sanitized]
        }
    }

    return (copyArray(element, attribute: kAXChildrenAttribute) ?? [])
        .flatMap { descendantTextsForSummary(of: $0, depth: depth + 1, textLimit: textLimit) }
}

private func summaryTextForLink(
    _ element: AXUIElement,
    textLimit: SnapshotTextLimit = .defaults
) -> String? {
    guard let url = urlValue(of: element, attribute: kAXURLAttribute, textLimit: textLimit), !url.isEmpty else {
        return nil
    }

    let childText = (copyArray(element, attribute: kAXChildrenAttribute) ?? [])
        .flatMap { descendantTextsForSummary(of: $0, textLimit: textLimit) }
        .joined(separator: " ")
    let sanitized = sanitizeText(childText, textLimit: textLimit)
    guard !sanitized.isEmpty else {
        return nil
    }

    return summaryMarkdownLinkText(text: sanitized, url: url)
}

func summaryMarkdownLinkText(text: String, url: String) -> String {
    "[\(markdownEscapedLinkText(text))](\(url))"
}

func visibleRows(in rows: [AXUIElement], parent: AXUIElement) -> [AXUIElement] {
    guard let parentFrame = resolveLocalFrame(of: parent, windowBounds: nil) else {
        return Array(rows.prefix(20))
    }

    let visible = rows.filter { row in
        guard let rowFrame = resolveLocalFrame(of: row, windowBounds: nil) else {
            return false
        }

        return rowFrame.intersects(parentFrame)
    }

    if visible.isEmpty {
        return Array(rows.prefix(20))
    }

    return Array(visible.prefix(20))
}

func displayIdentifier(_ value: String?) -> String? {
    guard let value, !value.isEmpty, !value.hasPrefix("_NS:") else {
        return nil
    }

    return value
}
