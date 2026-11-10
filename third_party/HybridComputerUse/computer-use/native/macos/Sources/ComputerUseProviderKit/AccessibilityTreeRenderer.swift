import ApplicationServices
import CoreGraphics
import Foundation

struct RenderContext {
    let windowBounds: CGRect?
    let focusedElement: AXUIElement?
    let focusedAncestry: [AXUIElement]
    let textLimit: SnapshotTextLimit
    let treeLimits: AccessibilityTreeLimits
    /// System uptime after which no further node is visited.
    var traversalDeadline: TimeInterval? = nil
}

struct SemanticPresentationOverride {
    let name: String
    let description: String?
}

struct TreeRenderer {
    let context: RenderContext
    private let selectedRoot: AXUIElement?
    private let readTitle: (AXUIElement) -> String?
    private let readExpansion: (AXUIElement) throws -> Bool?
    private(set) var selectedRootTitle: String?
    private var selectedRootTitleSampled = false
    var nextIndex = 0
    var lines: [String] = []
    var records: [Int: ElementRecord] = [:]
    var identifierIndex: [String: String] = [:]
    var focusedSummary: String?
    /// Nodes skipped because the traversal deadline passed. Their
    /// descendants were not visited either, so this is a lower bound.
    var deadlineOmittedCount = 0
    private var nodeLimitOmittedCount = 0
    private var depthLimitOmittedCount = 0

    init(
        context: RenderContext,
        selectedRoot: AXUIElement? = nil,
        readTitle: @escaping (AXUIElement) -> String? = {
            stringValue(of: $0, attribute: kAXTitleAttribute)
        },
        readExpansion: @escaping (AXUIElement) throws -> Bool? = {
            try readAXExpansion(of: $0)
        }
    ) {
        self.context = context
        self.selectedRoot = selectedRoot
        self.readTitle = readTitle
        self.readExpansion = readExpansion
    }

    func omissions(unresponsiveNodes: Int) -> [SnapshotOmission] {
        var result = snapshotOmissions(
            deadlineOmitted: deadlineOmittedCount, unresponsiveNodes: unresponsiveNodes
        )
        for (count, reason) in [
            (nodeLimitOmittedCount, "max_tree_nodes"),
            (depthLimitOmittedCount, "max_tree_depth")
        ] where count > 0 {
            result.append(SnapshotOmission(
                count: count, reason: reason,
                detail: "At least \(count) entries were not visited because \(reason) was reached; their descendants are unknown."
            ))
        }
        return result
    }

    private mutating func acceptsEntry(depth: Int) -> Bool {
        if shouldContinueRendering(nextIndex: nextIndex, depth: depth, limits: context.treeLimits) {
            return true
        }
        if nextIndex >= context.treeLimits.maxNodeCount {
            nodeLimitOmittedCount += 1
        } else {
            depthLimitOmittedCount += 1
        }
        return false
    }

    mutating func sampledDisplayTitle(
        for element: AXUIElement,
        role: String,
        label: String?,
        identifier: String?,
        explicitValue: String?,
        rowTexts: [String]
    ) -> String? {
        let rawTitle: String?
        if let selectedRoot, CFEqual(selectedRoot, element) {
            // Repeated paths share one sample, including a missing title.
            if !selectedRootTitleSampled {
                selectedRootTitle = readTitle(element)
                selectedRootTitleSampled = true
            }
            rawTitle = selectedRootTitle
        } else {
            rawTitle = readTitle(element)
        }
        return preferredDisplayTitle(
            for: element,
            role: role,
            label: label,
            identifier: identifier,
            explicitValue: explicitValue,
            rowTexts: rowTexts,
            textLimit: context.textLimit,
            readTitle: { rawTitle }
        )
    }

    mutating func render(
        _ root: AXUIElement,
        depth: Int = 0,
        ancestors: [AXUIElement] = [],
        parentRuntimeID: [Int] = [],
        relativeRuntimeID: [Int] = [0],
        presentationOverride: SemanticPresentationOverride? = nil
    ) throws {
        guard acceptsEntry(depth: depth) else {
            return
        }

        guard !ancestors.contains(where: { CFEqual($0, root) }) else {
            return
        }
        // The first node is exempt, so a late snapshot still names the
        // window it observed.
        if nextIndex > 0, let deadline = context.traversalDeadline,
           ProcessInfo.processInfo.systemUptime >= deadline {
            deadlineOmittedCount += 1
            return
        }
        let nextAncestors = ancestors + [root]

        let index = nextIndex
        let runtimeID = parentRuntimeID + relativeRuntimeID
        // One sample supplies both presentation traits and canonical state.
        let expansion = try readExpansion(root)

        let identityAttributes = copyAttributeValues(
            root,
            attributes: [
                kAXRoleAttribute,
                kAXSubroleAttribute,
                kAXDescriptionAttribute,
                kAXHelpAttribute,
                kAXIdentifierAttribute,
            ]
        )
        let role = stringValue(
            identityAttributes[kAXRoleAttribute]
        ) ?? "AXUnknown"
        let subrole = stringValue(identityAttributes[kAXSubroleAttribute])
        let baseRoleText = roleDescription(of: root, role: role, subrole: subrole)
        let nativeLabel = stringValue(identityAttributes[kAXDescriptionAttribute])
            .map { sanitizeText($0, textLimit: context.textLimit) }
        let label = presentationOverride?.description ?? nativeLabel
        let help = stringValue(identityAttributes[kAXHelpAttribute])
            .map { sanitizeText($0, textLimit: context.textLimit) }
        let structuredValue = semanticValue(
            of: root,
            textLimit: context.textLimit
        )
        let value = structuredValue.map {
            sanitizeText($0, textLimit: context.textLimit)
        }
        let axIdentifier = displayIdentifier(
            stringValue(identityAttributes[kAXIdentifierAttribute])
        )
        let valueAttributeSettable = axValueAttributeIsSettable(root)
        let setValueSupported = publicStringSetValueSupported(
            settable: valueAttributeSettable,
            value: attributeValue(of: root, attribute: kAXValueAttribute),
            role: role
        )
        let traits = summarizeTraits(
            of: root,
            expansion: expansion,
            valueSettable: valueAttributeSettable
        )
        let actions = copyActions(root) ?? []
        let exposesPrimaryClickAction = hasPrimaryClickAction(actions)
        let prettyActions = meaningfulActions(actions, role: role)
        let placeholder = placeholderValue(of: root, textLimit: context.textLimit)
        let webAreaDepth = webAreaDepth(role: role, ancestors: ancestors)
        let localFrame = resolveLocalFrame(of: root, windowBounds: context.windowBounds)
        let rendersCompactGenericActionTarget = shouldRenderCompactGenericActionTarget(
            role: role,
            hasPrimaryClickAction: exposesPrimaryClickAction,
            localFrame: localFrame
        )
        let rowTexts = role == kAXRowRole as String ? flattenedRowTexts(of: root, textLimit: context.textLimit) : []
        let childRecords = children(of: root)
        let childElements = childRecords.map(\.element)
        let genericTextSummary = summarizedGenericText(
            of: root,
            role: role,
            childElements: childElements,
            textLimit: context.textLimit,
            minimumTextCount: rendersCompactGenericActionTarget ? 1 : 2
        )
        let summaryImageChildren = genericTextSummary == nil ? [] : summaryImageDescendants(of: root)
        let rendersSummaryAsChildren = !rendersCompactGenericActionTarget
            && shouldRenderGenericTextSummaryAsChildren(
                genericTextSummary,
                summaryImageCount: summaryImageChildren.count
            )
        let nativeTitle = sampledDisplayTitle(
            for: root,
            role: role,
            label: nativeLabel,
            identifier: axIdentifier,
            explicitValue: value,
            rowTexts: rowTexts
        )
        let title = presentationOverride?.name ?? nativeTitle
        let linkText = role == "AXLink" ? markdownLinkText(for: root, title: title, label: label, value: value, textLimit: context.textLimit) : nil
        let displayTitle = linkText ?? title
        let inlineRowSummary = outlineRowSummary(for: root, role: role)
        if presentationOverride == nil,
           let proxy = semanticControlProxy(
               parent: root,
               role: role,
               name: displayTitle ?? label ?? help,
               hasPrimaryAction: exposesPrimaryClickAction,
               valueSettable: valueAttributeSettable,
               children: childElements
           ),
           let proxyRecord = childRecords.first(where: { CFEqual($0.element, proxy) })
        {
            try render(
                proxy,
                depth: depth,
                ancestors: nextAncestors,
                parentRuntimeID: runtimeID,
                relativeRuntimeID: proxyRecord.relativePath,
                presentationOverride: SemanticPresentationOverride(
                    name: displayTitle ?? label ?? help ?? "",
                    description: label
                )
            )
            for child in childRecords where !CFEqual(child.element, proxy) {
                try render(
                    child.element,
                    depth: depth,
                    ancestors: nextAncestors,
                    parentRuntimeID: runtimeID,
                    relativeRuntimeID: child.relativePath
                )
            }
            return
        }
        let hidesChildren = shouldSuppressChildren(
            role: role,
            title: displayTitle,
            label: label,
            help: help,
            value: structuredValue,
            identifier: axIdentifier,
            traits: traits,
            actions: prettyActions,
            children: childElements,
            genericTextSummary: genericTextSummary
        )
        let roleText = displayRoleText(
            baseRoleText: baseRoleText,
            role: role,
            title: displayTitle,
            label: label,
            suppressChildren: hidesChildren
        )

        if shouldElideNode(
            role: role,
            title: displayTitle,
            label: label,
            value: value,
            identifier: axIdentifier,
            traits: traits,
            actions: prettyActions,
            childCount: childElements.count,
            genericTextSummary: genericTextSummary,
            webAreaDepth: webAreaDepth,
            preservesCompactGenericActionTarget: rendersCompactGenericActionTarget
        ) {
            for child in childRecords {
                try render(
                    child.element,
                    depth: depth,
                    ancestors: nextAncestors,
                    parentRuntimeID: runtimeID,
                    relativeRuntimeID: child.relativePath
                )
            }
            return
        }

        nextIndex += 1

        let traitsSegment = traits.isEmpty ? "" : " (\(traits.joined(separator: ", ")))"
        let titleSegment = displayTitle.map { " \($0)" } ?? ""
        let rowSummary = inlineRowSummary ?? (rendersSummaryAsChildren ? nil : genericTextSummary)
        let rowSummarySegment = rowSummary.map { " \($0)" } ?? ""
        let labelSegment = formattedLabelSegment(label, title: displayTitle, linkText: linkText, textLimit: context.textLimit)
        let helpSegment = {
            guard let help else {
                return ""
            }
            if help == displayTitle || help == label {
                return ""
            }
            return " Help: \(help)"
        }()
        let urlSegment = formattedURLSegment(for: root, title: displayTitle, label: label, textLimit: context.textLimit)
        let identifierSegment = displayIdentifierSegment(for: root, role: role, identifier: axIdentifier, title: displayTitle)
        let rawValueSegment = formattedValueSegment(for: root, roleText: roleText, title: displayTitle, value: value)
        let valueSegment = formattedValueSegmentWithSeparator(
            rawValueSegment,
            precedingSegments: [labelSegment, helpSegment, urlSegment, identifierSegment]
        )
        let placeholderSegment = formattedPlaceholderSegment(
            placeholder,
            title: displayTitle,
            label: label,
            value: value,
            precedingSegments: [labelSegment, helpSegment, urlSegment, identifierSegment, valueSegment]
        )
        let frameSegment = rendersCompactGenericActionTarget
            ? localFrame.map { " Frame: \($0.renderedLocalFrame)" } ?? ""
            : ""
        let actionsPrefix = shouldCommaSeparateActions(
            title: displayTitle,
            inlineRowSummary: inlineRowSummary,
            genericTextSummary: genericTextSummary,
            segments: [labelSegment, helpSegment, urlSegment, identifierSegment, valueSegment, placeholderSegment]
        ) ? ", Secondary Actions: " : " Secondary Actions: "
        let actionsSegment = prettyActions.isEmpty ? "" : "\(actionsPrefix)\(prettyActions.joined(separator: ", "))"
        let renderedRoleText = rendersCompactGenericActionTarget ? "button" : roleText
        let linePrefix = renderedRoleText.isEmpty ? "\(index)" : "\(index) \(renderedRoleText)"

        let lineBody = "\(linePrefix)\(traitsSegment)\(titleSegment)\(rowSummarySegment)\(labelSegment)\(helpSegment)\(urlSegment)\(identifierSegment)\(valueSegment)\(placeholderSegment)\(frameSegment)"
        lines.append("\(String(repeating: "\t", count: depth))\(lineBody)\(actionsSegment)")

        let record = ElementRecord(
            index: index,
            runtimeID: runtimeID,
            identifier: axIdentifier,
            element: root,
            localFrame: localFrame,
            rawActions: actions,
            prettyActions: prettyActions,
            providerRole: canonicalAXRole(role, subrole: subrole),
            localizedRole: baseRoleText,
            name: title ?? label,
            description: label,
            placeholder: placeholder,
            value: structuredValue,
            setValueSupported: setValueSupported,
            states: structuredStateTokens(
                of: root,
                role: role,
                focusedElement: context.focusedElement,
                expansion: expansion
            ),
            liveIdentity: AXLiveIdentity.sample(root)
        )
        records[index] = record

        if let axIdentifier, let localFrame {
            identifierIndex[axIdentifier] = "\(axIdentifier) -> \(index) @ \(localFrame.renderedLocalFrame)"
        }

        if let focusedElement = context.focusedElement, CFEqual(focusedElement, root) {
            focusedSummary = lineBody
        }

        if role == kAXRowRole as String, boolValue(of: root, attribute: kAXSelectedAttribute) != true {
            for text in Array(rowTexts.dropFirst()) {
                lines.append(text)
            }
            return
        }

        if rendersSummaryAsChildren, let genericTextSummary {
            renderSyntheticText(
                genericTextSummary,
                representedBy: root,
                depth: depth + 1,
                parentRuntimeID: runtimeID
            )
            for image in summaryImageChildren {
                try render(
                    image.element,
                    depth: depth + 1,
                    ancestors: nextAncestors,
                    parentRuntimeID: runtimeID,
                    relativeRuntimeID: image.relativePath
                )
            }
            return
        }

        if hidesChildren {
            return
        }

        for child in childRecords {
            try render(
                child.element,
                depth: depth + 1,
                ancestors: nextAncestors,
                parentRuntimeID: runtimeID,
                relativeRuntimeID: child.relativePath
            )
        }
    }

    private mutating func renderSyntheticText(
        _ text: String,
        representedBy element: AXUIElement,
        depth: Int,
        parentRuntimeID: [Int]
    ) {
        guard acceptsEntry(depth: depth) else {
            return
        }

        let index = nextIndex
        nextIndex += 1
        lines.append("\(String(repeating: "\t", count: depth))\(index) text \(text)")

        records[index] = ElementRecord(
            index: index,
            runtimeID: parentRuntimeID + [-1],
            identifier: nil,
            element: element,
            localFrame: resolveLocalFrame(of: element, windowBounds: context.windowBounds),
            rawActions: [],
            prettyActions: [],
            isSyntheticText: true,
            providerRole: "text",
            localizedRole: "text",
            name: text,
            setValueSupported: false
        )
    }

    private func webAreaDepth(role: String, ancestors: [AXUIElement]) -> Int? {
        if role == axWebAreaRole {
            return 0
        }

        guard let webAreaIndex = ancestors.firstIndex(where: { ancestor in
            stringValue(of: ancestor, attribute: kAXRoleAttribute) == axWebAreaRole
        }) else {
            return nil
        }

        return ancestors.count - webAreaIndex
    }

    private func children(of element: AXUIElement) -> [TraversalChild<AXUIElement>] {
        let attributeValues = copyAttributeValues(
            element,
            attributes: [
                kAXRoleAttribute,
                kAXChildrenAttribute,
                kAXRowsAttribute,
                axContentsAttribute,
                axVisibleChildrenAttribute,
            ]
        )
        let role = stringValue(attributeValues[kAXRoleAttribute])
        let rows = elementArray(attributeValues[kAXRowsAttribute])
        let visibleChildren = elementArray(
            attributeValues[axVisibleChildrenAttribute]
        )
        let traversalAttributes = childTraversalAttributes(
            role: role,
            hasRows: !rows.isEmpty,
            hasVisibleChildren: !visibleChildren.isEmpty
        )
        var children: [TraversalChild<AXUIElement>] = []

        for attribute in traversalAttributes {
            let sourceValues: [AXUIElement]
            if attribute == kAXRowsAttribute {
                sourceValues = rows
            } else if attribute == axVisibleChildrenAttribute {
                sourceValues = visibleChildren
            } else {
                sourceValues = elementArray(attributeValues[attribute])
            }

            let source: TraversalSource
            switch attribute {
            case kAXRowsAttribute: source = .rows
            case axContentsAttribute: source = .contents
            case axVisibleChildrenAttribute: source = .visibleChildren
            default: source = .children
            }
            let visible = attribute == kAXRowsAttribute ? visibleRows(in: sourceValues, parent: element) : nil
            // Slots belong to the original acquisition, never to filtered output.
            for child in structuralChildren(sourceValues, source: source) {
                if let visible, !visible.contains(where: { CFEqual($0, child.element) }) {
                    continue
                }
                if shouldSkipChild(child.element, of: element) {
                    continue
                }

                if !children.contains(where: { CFEqual($0.element, child.element) }) {
                    children.append(child)
                }
            }
        }

        return prioritizeFocusedBranchChild(
            children,
            parent: element,
            focusedAncestry: context.focusedAncestry,
            elementOf: { $0.element },
            isSame: {
                CFEqual($0, $1)
            }
        )
    }

    private func semanticControlProxy(
        parent: AXUIElement,
        role: String,
        name: String?,
        hasPrimaryAction: Bool,
        valueSettable: Bool,
        children: [AXUIElement]
    ) -> AXUIElement? {
        let childFacts = children.map { child in
            let childRole = stringValue(
                of: child,
                attribute: kAXRoleAttribute
            ) ?? "AXUnknown"
            let childActions = copyActions(child) ?? []
            return SemanticControlProxyFacts(
                role: childRole,
                enabled: boolValue(
                    of: child,
                    attribute: kAXEnabledAttribute
                ),
                hasSemanticLabel: semanticControlHasOwnLabel(
                    child,
                    role: childRole
                ),
                hasPrimaryAction: hasPrimaryClickAction(childActions),
                valueSettable: axValueAttributeIsSettable(child)
            )
        }
        let parentFacts = SemanticControlProxyFacts(
            role: role,
            enabled: boolValue(
                of: parent,
                attribute: kAXEnabledAttribute
            ),
            hasSemanticLabel: !(name?.isEmpty ?? true),
            hasPrimaryAction: hasPrimaryAction,
            valueSettable: valueSettable
        )
        guard let index = semanticControlProxyChildIndex(
            parent: parentFacts,
            children: childFacts
        ) else {
            return nil
        }
        return children[index]
    }

    private func semanticControlHasOwnLabel(
        _ element: AXUIElement,
        role: String
    ) -> Bool {
        let attributes = copyAttributeValues(
            element,
            attributes: [
                kAXTitleAttribute,
                kAXDescriptionAttribute,
                kAXHelpAttribute,
                kAXPlaceholderValueAttribute,
            ]
        )
        if attributes.values.contains(where: {
            guard let value = stringValue($0) else {
                return false
            }
            return !sanitizeText(
                value,
                textLimit: context.textLimit
            ).isEmpty
        }) {
            return true
        }
        return preferredDisplayTitle(
            for: element,
            role: role,
            label: nil,
            identifier: nil,
            explicitValue: nil,
            rowTexts: [],
            textLimit: context.textLimit
        ) != nil
    }
}

private extension CGRect {
    var renderedLocalFrame: String {
        "x=\(Int(origin.x)), y=\(Int(origin.y)), w=\(Int(width)), h=\(Int(height))"
    }
}
