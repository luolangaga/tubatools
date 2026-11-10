import ApplicationServices
import CoreGraphics
import Foundation

extension ComputerUseService {
    func lookupElement(snapshot: AppSnapshot, index: String) throws -> ElementRecord {
        guard let parsedIndex = Int(index), let record = snapshot.elements[parsedIndex] else {
            throw ComputerUseError.invalidArguments("unknown element_index '\(index)'")
        }

        return record
    }

    private func performPreferredClick(
        on record: ElementRecord,
        snapshot: AppSnapshot,
        button: MouseButtonKind,
        clickCount: Int,
        allowTransientGlobalPointerFallback: Bool,
        requestedTargetRole: String?
    ) throws -> Bool {
        guard let element = record.element else {
            return false
        }

        switch button {
        case .left:
            let targetRole = stringValue(
                of: element,
                attribute: kAXRoleAttribute
            )
            let hasChildMenu = copyChildren(of: element).contains {
                stringValue(of: $0, attribute: kAXRoleAttribute)
                    == kAXMenuRole as String
            }
            if try performAction(named: kAXPressAction as String, on: element, availableActions: record.rawActions, repeatCount: clickCount) {
                return true
            }

            if try performAction(named: "AXOpen", on: element, availableActions: record.rawActions, repeatCount: clickCount) {
                return true
            }

            if targetRole == kAXCheckBoxRole as String,
               clickCount == 1,
               try performCheckboxValueToggle(on: element)
            {
                return true
            }

            if allowTransientGlobalPointerFallback,
               let transientRow = transientPopoverRowRecord(
                   for: record,
                   records: Array(snapshot.elements.values)
               ),
               canUseGlobalPointerForTransientRow(
                   row: transientRow,
                   button: button,
                   clickCount: clickCount
               )
            {
                try performSemanticallyBoundGlobalClick(
                    on: transientRow,
                    snapshot: snapshot,
                    button: button,
                    clickCount: clickCount
                )
                return true
            }

            if clickCount <= 1,
               !hasAncestorRole("AXWebArea", of: element),
               canUseContainingListSelectionAsClick(
                   requestedTargetRole: requestedTargetRole,
                   candidateRole: targetRole,
                   hasMenuItemAncestor: hasAncestorRole(
                       kAXMenuItemRole as String,
                       of: element
                   ),
                   hasChildMenu: hasChildMenu,
                   hasPopoverAncestor: hasAncestorRole(
                       "AXPopover",
                       of: element
                   )
               ),
               try selectContainingListItem(for: element)
            {
                return true
            }
        case .right:
            if try performAction(named: kAXShowMenuAction as String, on: element, availableActions: record.rawActions, repeatCount: clickCount) {
                return true
            }
        case .middle:
            break
        }

        return false
    }

    private func performCheckboxValueToggle(on element: AXUIElement) throws -> Bool {
        guard
            isSettable(
                element: element,
                attribute: kAXValueAttribute as String
            ),
            let requestedValue = toggledCheckboxValue(
                attributeValue(of: element, attribute: kAXValueAttribute)
            )
        else {
            return false
        }

        let result = AXUIElementSetAttributeValue(
            element,
            kAXValueAttribute as CFString,
            requestedValue
        )
        switch result {
        case .success:
            let observedValue = attributeValue(
                of: element,
                attribute: kAXValueAttribute
            )
            guard checkboxValueReadbackMatches(
                expected: requestedValue,
                observed: observedValue
            ) else {
                throw ComputerUseError.message(
                    "checkbox AXValue write completed but immediate readback did not match"
                )
            }
            return true
        case .failure, .attributeUnsupported, .actionUnsupported,
             .cannotComplete, .noValue, .invalidUIElement, .illegalArgument:
            return false
        default:
            throw ComputerUseError.message(
                "checkbox AXValue write failed with \(result.rawValue)"
            )
        }
    }

    func clickCandidates(at point: CGPoint, in snapshot: AppSnapshot) throws -> [ElementRecord] {
        var candidates: [ElementRecord] = []

        if let bestRecord = bestElement(containing: point, in: snapshot) {
            candidates.append(bestRecord)
        }

        if let hitRecord = try hitTestElement(at: point, in: snapshot) {
            candidates.append(hitRecord)
        }

        return candidates.reduce(into: []) { uniqueCandidates, candidate in
            if !uniqueCandidates.contains(where: { sameElement($0.element, candidate.element) }) {
                uniqueCandidates.append(candidate)
            }
        }
    }

    private func sameElement(_ lhs: AXUIElement?, _ rhs: AXUIElement?) -> Bool {
        guard let lhs, let rhs else {
            return false
        }

        return CFEqual(lhs, rhs)
    }

    private func selectContainingListItem(for element: AXUIElement) throws -> Bool {
        guard let target = selectableListItem(containing: element) else {
            return false
        }

        let result = AXUIElementSetAttributeValue(
            target.container,
            target.selectionAttribute as CFString,
            [target.item] as CFArray
        )

        switch result {
        case .success:
            Thread.sleep(forTimeInterval: 0.15)
            return true
        case .failure, .attributeUnsupported, .actionUnsupported, .cannotComplete, .noValue, .invalidUIElement, .illegalArgument:
            return false
        default:
            throw ComputerUseError.message("AXUIElementSetAttributeValue(\(target.selectionAttribute)) failed with \(result.rawValue)")
        }
    }

    private func selectableListItem(
        containing element: AXUIElement
    ) -> (container: AXUIElement, item: AXUIElement, selectionAttribute: String)? {
        var current = element
        var directChild = element

        for _ in 0..<8 {
            guard let parent = copyParent(of: current) else {
                return nil
            }

            if let selectionAttribute = selectionAttributeForContainerRole(
                stringValue(of: parent, attribute: kAXRoleAttribute)
            ),
               isSettable(element: parent, attribute: selectionAttribute)
            {
                return (parent, directChild, selectionAttribute)
            }

            directChild = parent
            current = parent
        }

        return nil
    }

    func performAXClickSequence(
        on record: ElementRecord,
        snapshot: AppSnapshot,
        button: MouseButtonKind,
        clickCount: Int,
        includeNearbyHitTesting: Bool,
        allowActivationFallback: Bool,
        allowTransientGlobalPointerFallback: Bool
    ) throws -> Bool {
        debugClickDecision("record=\(clickDebugDescription(record))")
        let requestedTargetRole = record.element.flatMap {
            stringValue(of: $0, attribute: kAXRoleAttribute)
        }

        if try performPreferredClick(
            on: record,
            snapshot: snapshot,
            button: button,
            clickCount: clickCount,
            allowTransientGlobalPointerFallback: allowTransientGlobalPointerFallback,
            requestedTargetRole: requestedTargetRole
        ) {
            debugClickDecision("handled by preferred target \(clickDebugDescription(record))")
            Thread.sleep(forTimeInterval: 0.15)
            return true
        }

        for candidate in descendantClickCandidates(for: record, snapshot: snapshot) {
            if try performPreferredClick(
                on: candidate,
                snapshot: snapshot,
                button: button,
                clickCount: clickCount,
                allowTransientGlobalPointerFallback: allowTransientGlobalPointerFallback,
                requestedTargetRole: requestedTargetRole
            ) {
                debugClickDecision("handled by descendant \(clickDebugDescription(candidate))")
                Thread.sleep(forTimeInterval: 0.15)
                return true
            }
        }

        if includeNearbyHitTesting {
            for localPoint in clickActionPoints(for: record, snapshot: snapshot) {
                guard let hitRecord = try hitTestElement(at: localPoint, in: snapshot) ?? bestElement(containing: localPoint, in: snapshot) else {
                    continue
                }

                if !isLikelySyntheticSideAction(hitRecord, in: record),
                   try performPreferredClick(
                       on: hitRecord,
                       snapshot: snapshot,
                       button: button,
                       clickCount: clickCount,
                       allowTransientGlobalPointerFallback: allowTransientGlobalPointerFallback,
                       requestedTargetRole: requestedTargetRole
                   )
                {
                    debugClickDecision("handled by hit record \(clickDebugDescription(hitRecord))")
                    Thread.sleep(forTimeInterval: 0.15)
                    return true
                }

                if shouldScanDescendantsOfHitRecord(
                    originalFrame: clickFrame(for: record, snapshot: snapshot),
                    hitFrame: hitRecord.localFrame
                ) {
                    for candidate in descendantClickCandidates(
                        for: hitRecord,
                        snapshot: snapshot,
                        sideActionScope: record
                    ) {
                        if try performPreferredClick(
                            on: candidate,
                            snapshot: snapshot,
                            button: button,
                            clickCount: clickCount,
                            allowTransientGlobalPointerFallback: allowTransientGlobalPointerFallback,
                            requestedTargetRole: requestedTargetRole
                        ) {
                            debugClickDecision("handled by hit descendant \(clickDebugDescription(candidate))")
                            Thread.sleep(forTimeInterval: 0.15)
                            return true
                        }
                    }
                }
            }
        }

        guard
            allowActivationFallback,
            !record.isSyntheticText,
            button == .left,
            let element = record.element,
            canUseActivationOnlyClickFallback(role: stringValue(of: element, attribute: kAXRoleAttribute))
        else {
            return false
        }

        if try activateClickTarget(element: element, availableActions: record.rawActions) {
            debugClickDecision("handled by activation fallback \(clickDebugDescription(record))")
            Thread.sleep(forTimeInterval: 0.15)
            return true
        }

        return false
    }

    private func performAction(named action: String, on element: AXUIElement, availableActions: [String], repeatCount: Int = 1) throws -> Bool {
        guard availableActions.contains(where: { $0.caseInsensitiveCompare(action) == .orderedSame }) else {
            return false
        }

        let attempts = max(repeatCount, 1)
        for index in 0..<attempts {
            let result = AXUIElementPerformAction(element, action as CFString)
            switch result {
            case .success:
                if index < attempts - 1 {
                    Thread.sleep(forTimeInterval: 0.05)
                }
            case .attributeUnsupported where action.caseInsensitiveCompare("AXOpen") == .orderedSame:
                return true
            default:
                throw nativeAXActionAttemptError(
                    action: action,
                    result: result,
                    earlierInputEmitted: index > 0
                )
            }
        }

        return true
    }

    private func activateClickTarget(element: AXUIElement, availableActions: [String]) throws -> Bool {
        var activated = false

        if try performAction(named: kAXRaiseAction as String, on: element, availableActions: availableActions) {
            activated = true
        }

        if try setBoolAttribute(named: kAXMainAttribute, on: element) {
            activated = true
        }

        if try setBoolAttribute(named: kAXFocusedAttribute, on: element) {
            activated = true
        }

        return activated
    }

    private func setBoolAttribute(named attribute: String, on element: AXUIElement) throws -> Bool {
        let result = AXUIElementSetAttributeValue(element, attribute as CFString, kCFBooleanTrue)
        switch result {
        case .success:
            return true
        case .failure, .attributeUnsupported, .actionUnsupported, .cannotComplete, .noValue, .invalidUIElement, .illegalArgument:
            return false
        default:
            throw ComputerUseError.message("AXUIElementSetAttributeValue(\(attribute)) failed with \(result.rawValue)")
        }
    }

    private func isSettable(element: AXUIElement, attribute: String) -> Bool {
        var settable: DarwinBoolean = false
        let result = AXUIElementIsAttributeSettable(element, attribute as CFString, &settable)
        return result == .success && settable.boolValue
    }

    func isSettableForSetValue(element: AXUIElement, attribute: String) throws -> Bool {
        var settable = DarwinBoolean(false)
        let result = AXUIElementIsAttributeSettable(element, attribute as CFString, &settable)
        let attributeSettable = try setValueAttributeIsSettable(
            result: result,
            settable: settable.boolValue,
            attribute: attribute
        )
        guard attribute == kAXValueAttribute as String else {
            return attributeSettable
        }
        return publicStringSetValueSupported(
            settable: attributeSettable,
            value: attributeValue(of: element, attribute: attribute),
            role: stringValue(of: element, attribute: kAXRoleAttribute)
        )
    }

    private func bestElement(containing point: CGPoint, in snapshot: AppSnapshot) -> ElementRecord? {
        snapshot.elements.values
            .filter { $0.localFrame?.contains(point) ?? false }
            .sorted { lhs, rhs in
                let lhsPriority = clickPriority(for: lhs)
                let rhsPriority = clickPriority(for: rhs)
                if lhsPriority != rhsPriority {
                    return lhsPriority < rhsPriority
                }

                return frameArea(of: lhs) < frameArea(of: rhs)
            }
            .first
    }

    private func hitTestElement(at point: CGPoint, in snapshot: AppSnapshot) throws -> ElementRecord? {
        let appElement = AXUIElementCreateApplication(snapshot.app.pid)
        let globalPoint = try windowPointToGlobalPoint(snapshot: snapshot, point: point)
        var hitElement: AXUIElement?
        let result = AXUIElementCopyElementAtPosition(appElement, Float(globalPoint.x), Float(globalPoint.y), &hitElement)
        guard result == .success, let hitElement else {
            return nil
        }

        let rawActions = copyActions(for: hitElement) ?? []
        return ElementRecord(
            index: -1,
            identifier: nil,
            element: hitElement,
            localFrame: localFrame(of: hitElement, windowBounds: snapshot.windowBounds),
            rawActions: rawActions,
            prettyActions: rawActions
        )
    }

    private func clickPriority(for record: ElementRecord) -> Int {
        if record.rawActions.contains(where: {
            $0.caseInsensitiveCompare(kAXPressAction as String) == .orderedSame ||
            $0.caseInsensitiveCompare(kAXShowMenuAction as String) == .orderedSame ||
            $0.caseInsensitiveCompare(kAXRaiseAction as String) == .orderedSame
        }) {
            return 0
        }

        if let element = record.element,
           isSettable(element: element, attribute: kAXMainAttribute) ||
           isSettable(element: element, attribute: kAXFocusedAttribute) {
            return 1
        }

        return 2
    }

    private func frameArea(of record: ElementRecord) -> CGFloat {
        guard let frame = record.localFrame else {
            return .greatestFiniteMagnitude
        }

        return frame.width * frame.height
    }

    func localCenter(for record: ElementRecord) -> CGPoint? {
        guard let frame = record.localFrame, hasUsableClickGeometry(frame) else {
            return nil
        }

        return CGPoint(x: frame.midX, y: frame.midY)
    }

    func clickActionPoints(for record: ElementRecord, snapshot: AppSnapshot) -> [CGPoint] {
        guard let frame = clickFrame(for: record, snapshot: snapshot) else {
            return []
        }

        return localClickActionPoints(frame: frame, isSyntheticText: record.isSyntheticText)
    }

    private func descendantClickCandidates(
        for record: ElementRecord,
        snapshot: AppSnapshot,
        sideActionScope: ElementRecord? = nil
    ) -> [ElementRecord] {
        guard let element = record.element else {
            return []
        }

        let sideActionParent = sideActionScope ?? record
        return descendantClickCandidates(of: element, windowBounds: snapshot.windowBounds)
            .filter { candidate in
                !isLikelySyntheticSideAction(candidate, in: sideActionParent)
            }
            .sorted { lhs, rhs in
                let lhsPriority = clickPriority(for: lhs)
                let rhsPriority = clickPriority(for: rhs)
                if lhsPriority != rhsPriority {
                    return lhsPriority < rhsPriority
                }

                return frameArea(of: lhs) < frameArea(of: rhs)
            }
    }

    private func descendantClickCandidates(of element: AXUIElement, windowBounds: CGRect?, depth: Int = 0) -> [ElementRecord] {
        guard depth < 3 else {
            return []
        }

        var results: [ElementRecord] = []
        for child in copyChildren(of: element) {
            let rawActions = copyActions(for: child) ?? []
            results.append(
                ElementRecord(
                    index: -1,
                    identifier: nil,
                    element: child,
                    localFrame: localFrame(of: child, windowBounds: windowBounds),
                    rawActions: rawActions,
                    prettyActions: rawActions
                )
            )
            results.append(contentsOf: descendantClickCandidates(of: child, windowBounds: windowBounds, depth: depth + 1))
        }

        return results
    }

    private func isLikelySyntheticSideAction(_ candidate: ElementRecord, in parent: ElementRecord) -> Bool {
        isLikelySyntheticSideActionCandidate(
            parentFrame: parent.localFrame,
            candidateFrame: candidate.localFrame,
            hasPrimaryAction: hasPrimaryClickAction(candidate),
            labels: accessibilityLabels(for: candidate.element)
        )
    }

    private func hasPrimaryClickAction(_ record: ElementRecord) -> Bool {
        record.rawActions.contains { action in
            action.caseInsensitiveCompare(kAXPressAction as String) == .orderedSame ||
                action.caseInsensitiveCompare("AXOpen") == .orderedSame ||
                action.caseInsensitiveCompare(kAXShowMenuAction as String) == .orderedSame
        }
    }

    private func hasAncestorRole(_ role: String, of element: AXUIElement) -> Bool {
        var current = element

        for _ in 0..<12 {
            guard let parent = copyParent(of: current) else {
                return false
            }

            if stringValue(of: parent, attribute: kAXRoleAttribute) == role {
                return true
            }

            current = parent
        }

        return false
    }

    private func accessibilityLabels(for element: AXUIElement?) -> [String] {
        guard let element else {
            return []
        }

        return [
            kAXTitleAttribute as String,
            kAXDescriptionAttribute as String,
            kAXHelpAttribute as String,
            kAXValueAttribute as String,
            "AXIdentifier"
        ].compactMap { attribute in
            stringValue(of: element, attribute: attribute)
        }
    }
}
