import ApplicationServices
import Foundation
import ImageIO

extension ComputerUseService {
    @discardableResult
    public func scroll(app query: String, direction: String, elementIndex: String, pages: Double) throws -> String {
        let normalized = direction.lowercased()
        guard ["up", "down", "left", "right"].contains(normalized) else {
            throw ComputerUseError.message("Invalid scroll direction: \(direction)")
        }
        guard pages.isFinite, pages > 0 else {
            throw ComputerUseError.message("pages must be > 0")
        }

        let snapshot = try currentSnapshot(for: query)
        let record = try lookupElement(snapshot: snapshot, index: elementIndex)

        let route: String
        if let repeatCount = integralScrollPageCount(pages),
           let rawAction = record.rawActions.first(where: { $0.caseInsensitiveCompare("AXScroll\(normalized.capitalized)ByPage") == .orderedSame }),
           let element = record.element {
            route = "ax.action.scroll\(normalized)bypage"
            for index in 0..<repeatCount {
                let result = AXUIElementPerformAction(element, rawAction as CFString)
                guard result == .success else {
                    throw nativeAXActionAttemptError(
                        action: rawAction, result: result,
                        earlierInputEmitted: index > 0
                    )
                }
                Thread.sleep(forTimeInterval: 0.05)
            }
        } else if let point = try globalPoint(for: record, snapshot: snapshot) {
            route = "input.cg_event.scroll"
            try performScrollEvent(
                at: point,
                direction: normalized,
                pages: pages,
                targetDescription: "element_index=\(elementIndex)",
                snapshot: snapshot
            )
        } else {
            throw ComputerUseError.stateUnavailable("element \(elementIndex) has no scrollable frame")
        }
        return route
    }

    public struct TextInputDispatchResult: Sendable {
        public let route: String
        public let fallbackUsed: Bool
        /// nil when the element's text could not be read back.
        public let readbackVerified: Bool?
    }

    public struct SetValueDispatchResult: Sendable {
        public let route: String
    }

    public struct RelativeAdjustmentDispatchResult: Sendable {
        public let readbackSampled: Bool
        public let readbackVerified: Bool?
    }

    /// Types `text` into an editable element of the application without
    /// taking focus or the clipboard when it can: first an AX selected-text
    /// write, then Unicode key events posted to the process, and the
    /// clipboard only when neither reached the element or the element's
    /// value cannot be read. Readback compares the element's value with the
    /// value the insertion should produce. Key events and paste reach only
    /// the active application.
    public func typeTextWithRoute(
        app query: String,
        text: String,
        elementIndex: String? = nil
    ) throws -> TextInputDispatchResult {
        let snapshot = try currentSnapshot(for: query)
        let pid = snapshot.app.pid
        let start: AXUIElement?
        if let elementIndex {
            start = try lookupElement(snapshot: snapshot, index: elementIndex).element
        } else {
            start = copyElement(
                AXUIElementCreateApplication(pid),
                attribute: kAXFocusedUIElementAttribute
            ) ?? snapshot.focusedElement
        }
        guard let target = try focusedEditableTextTarget(from: start, in: snapshot) else {
            throw ComputerUseError.stateUnavailable("type_text requires a focused editable text element. Click a text entry area first, or use set_value on a settable text element.")
        }
        guard !text.isEmpty else {
            return TextInputDispatchResult(route: "ax.none", fallbackUsed: false, readbackVerified: nil)
        }
        let baseline = textInsertionBaseline(of: target, inserting: text)

        let axRoute = "ax.attribute.selected_text.set"
        if let baseline, axAttributeIsSettable(target, kAXSelectedTextAttribute),
           AXUIElementSetAttributeValue(
               target, kAXSelectedTextAttribute as CFString, text as CFString
           ) == .success {
            switch waitForTextReadback(target, baseline: baseline, timeout: 0.3) {
            case .expected:
                return TextInputDispatchResult(route: axRoute, fallbackUsed: false, readbackVerified: true)
            case .unchanged:
                break
            case .other(let observed):
                throw textReadbackMismatch(route: axRoute, expected: baseline.expected, observed: observed)
            }
        }

        let pasteRoute = "input.pasteboard.pid_command_v"
        guard let baseline else {
            // Without a readable value no route can be verified; paste is
            // the route that reached text fields in testing, and the
            // clipboard is restored afterwards.
            let retentionBarrier = AXPasteboardRetentionBarrier(pid: pid, target: target)
            try InputSimulation.pasteTextToProcess(text, pid: pid) {
                retentionBarrier.waitForSafeRestore()
            }
            return TextInputDispatchResult(route: pasteRoute, fallbackUsed: true, readbackVerified: nil)
        }

        let keyRoute = "input.cg_event.unicode_pid"
        try InputSimulation.typeTextToProcess(text, pid: pid)
        switch waitForTextReadback(target, baseline: baseline, timeout: 1.0) {
        case .expected:
            return TextInputDispatchResult(route: keyRoute, fallbackUsed: false, readbackVerified: true)
        case .unchanged:
            // No key event reached the element, so pasting cannot duplicate it.
            break
        case .other(let observed):
            throw textReadbackMismatch(route: keyRoute, expected: baseline.expected, observed: observed)
        }

        let retentionBarrier = AXPasteboardRetentionBarrier(pid: pid, target: target)
        try InputSimulation.pasteTextToProcess(text, pid: pid) {
            retentionBarrier.waitForSafeRestore()
        }
        switch waitForTextReadback(target, baseline: baseline, timeout: 0.3) {
        case .expected:
            return TextInputDispatchResult(route: pasteRoute, fallbackUsed: true, readbackVerified: true)
        case .unchanged:
            // Key events and paste reach only the active application.
            throw InputDeliveryError(
                message: "Text input did not change the element: the application ignored the AX selected-text write, Unicode key events and Command-V. Key events reach only the active application; bring its window to the front with a GUI click on the field, then type again.",
                inputEventsEmitted: true,
                failureCode: "readback_mismatch",
                partialInputPossible: true,
                readbackSampled: true,
                readbackVerified: false,
                actionRoute: pasteRoute
            )
        case .other(let observed):
            throw textReadbackMismatch(route: pasteRoute, expected: baseline.expected, observed: observed)
        }
    }

    public func pressKey(app query: String, key: String) throws {
        let snapshot = try currentSnapshot(for: query)
        try InputSimulation.pressKey(key, pid: snapshot.app.pid)
    }

    public func setValueWithRoute(
        app query: String,
        elementIndex: String,
        value: String
    ) throws -> SetValueDispatchResult {
        let snapshot = try currentSnapshot(for: query)
        let record = try lookupElement(snapshot: snapshot, index: elementIndex)

        guard let element = record.element else {
            throw ComputerUseError.stateUnavailable("element \(elementIndex) has no backing accessibility object")
        }

        guard try isSettableForSetValue(element: element, attribute: kAXValueAttribute) else {
            throw ComputerUseError.message(nonSettableSetValueErrorMessage)
        }

        try performNativeAXMutation(
            operation: "AXUIElementSetAttributeValue(AXValue)",
            route: "ax.attribute.value.set"
        ) {
            AXUIElementSetAttributeValue(
                element,
                kAXValueAttribute as CFString,
                value as CFString
            )
        }

        Thread.sleep(forTimeInterval: 0.1)
        let observedValue = attributeValue(
            of: element,
            attribute: kAXValueAttribute
        )
        guard exactStringReadbackMatches(
            requested: value,
            observed: observedValue
        ) else {
            let observedDescription = (observedValue as? String)
                ?? "<missing or non-string>"
            throw InputDeliveryError(
                message: "AX value write returned success, but exact readback did not match: requested \(String(reflecting: value)), observed \(String(reflecting: observedDescription))",
                inputEventsEmitted: true,
                failureCode: "readback_mismatch",
                partialInputPossible: false,
                readbackSampled: true,
                readbackVerified: false,
                actionRoute: "ax.attribute.value.set"
            )
        }
        return SetValueDispatchResult(route: "ax.attribute.value.set")
    }

    public func performRelativeAdjustment(
        app query: String,
        elementIndex: String,
        direction: String
    ) throws -> RelativeAdjustmentDispatchResult {
        try performRelativeAdjustment(
            app: query, elementIndex: elementIndex, direction: direction, diagnostic: nil
        )
    }

    func performRelativeAdjustment(
        app query: String,
        elementIndex: String,
        direction: String,
        diagnostic: RelativeReadbackDiagnostic?
    ) throws -> RelativeAdjustmentDispatchResult {
        guard let action = Self.nativeRelativeAdjustmentAction(
            for: direction
        ) else {
            throw ComputerUseError.invalidArguments(
                "unsupported relative adjustment \(direction)"
            )
        }

        let snapshot = try currentSnapshot(for: query)
        let record = try lookupElement(
            snapshot: snapshot,
            index: elementIndex
        )
        guard let element = record.element else {
            throw ComputerUseError.stateUnavailable(
                "element \(elementIndex) has no backing accessibility object"
            )
        }
        guard record.rawActions.contains(where: {
            $0.caseInsensitiveCompare(action) == .orderedSame
        }) else {
            throw ComputerUseError.stateUnavailable(
                "element \(elementIndex) does not expose \(direction); no input was emitted"
            )
        }
        diagnostic?.bind(snapshot: snapshot, target: record)
        return try dispatchRelativeAdjustment(
            action: action, direction: direction, diagnostic: diagnostic,
            readValue: { attributeValue(of: element, attribute: kAXValueAttribute) },
            performAction: { AXUIElementPerformAction(element, action as CFString) }
        )
    }

    static func nativeRelativeAdjustmentAction(
        for direction: String
    ) -> String? {
        switch direction {
        case "increment":
            return kAXIncrementAction as String
        case "decrement":
            return kAXDecrementAction as String
        default:
            return nil
        }
    }

    func currentSnapshot(for query: String) throws -> AppSnapshot {
        if let snapshot = snapshotsByApp[query.lowercased()] {
            return snapshot
        }

        return try refreshSnapshot(for: query)
    }

    func cachedSnapshot(for query: String) -> AppSnapshot? {
        if let snapshot = snapshotsByApp[query.lowercased()] {
            return snapshot
        }
        guard let app = try? resolveRuntimeApplication(query: query, identity: activeAppIdentity) else {
            return nil
        }
        return snapshotsByApp[app.name.lowercased()]
            ?? app.bundleIdentifier.flatMap {
                snapshotsByApp[$0.lowercased()]
            }
    }

    @discardableResult
    func refreshSnapshot(
        for query: String,
        textLimit: SnapshotTextLimit = .defaults,
        treeLimits: AccessibilityTreeLimits = .defaults,
        recoveryPolicy: SnapshotRecoveryPolicy = .allowActivation,
        fallbackWindow: AXUIElement? = nil,
        expectedWindowHandle: String? = nil
    ) throws -> AppSnapshot {
        let windowHandle = try snapshotWindowHandle(requested: expectedWindowHandle)
        let app = try resolveRuntimeApplication(query: query, identity: activeAppIdentity)
        let snapshot = try SnapshotBuilder.build(
            for: app,
            textLimit: textLimit,
            treeLimits: treeLimits,
            recoveryPolicy: recoveryPolicy,
            fallbackWindow: fallbackWindow,
            expectedWindowHandle: windowHandle
        )
        if let identity = activeAppIdentity {
            _ = try resolveRuntimeApplication(query: query, identity: identity)
            try validateRequestedIdentity(["appIdentity": identity], app: snapshot.app)
        }

        let keys = Set([
            query.lowercased(),
            app.name.lowercased(),
            (app.bundleIdentifier ?? "").lowercased(),
        ].filter { !$0.isEmpty })

        for key in keys {
            snapshotsByApp[key] = snapshot
        }
        rememberNativeWindowIdentity(snapshot)

        return snapshot
    }


    func snapshotResult(for snapshot: AppSnapshot, style: SnapshotTextStyle) -> ToolCallResult {
        ToolCallResult(content: [.text(snapshot.renderedText(style: style))])
    }
}

func dispatchRelativeAdjustment(
    action: String,
    direction: String,
    diagnostic: RelativeReadbackDiagnostic?,
    readValue: () -> CFTypeRef?,
    performAction: () -> AXError,
    now: () -> TimeInterval = { ProcessInfo.processInfo.systemUptime },
    sleep: (TimeInterval) -> Void = { Thread.sleep(forTimeInterval: $0) }
) throws -> ComputerUseService.RelativeAdjustmentDispatchResult {
    diagnostic?.willReadBefore()
    let before = relativeAdjustmentScalarReadback(readValue())
    diagnostic?.didReadBefore(before)
    diagnostic?.willPerformAction()
    let result = performAction()
    diagnostic?.didPerformAction(result)
    guard result == .success else {
        throw nativeAXActionAttemptError(action: action, result: result)
    }

    guard let before else {
        diagnostic?.finish("before_unavailable", sampled: false, verified: nil)
        return ComputerUseService.RelativeAdjustmentDispatchResult(
            readbackSampled: false, readbackVerified: nil
        )
    }

    let deadline = now() + 0.12
    var after: RelativeAdjustmentScalarReadback?
    repeat {
        diagnostic?.willReadAfter()
        after = relativeAdjustmentScalarReadback(readValue())
        diagnostic?.didReadAfter(after)
        if relativeAdjustmentReadbackOutcome(before: before, after: after) == .changed {
            diagnostic?.finish("changed", sampled: true, verified: true)
            return ComputerUseService.RelativeAdjustmentDispatchResult(
                readbackSampled: true, readbackVerified: true
            )
        }
        if now() < deadline {
            sleep(0.02)
        }
    } while now() < deadline

    guard after != nil else {
        diagnostic?.finish("after_unavailable", sampled: false, verified: nil)
        return ComputerUseService.RelativeAdjustmentDispatchResult(
            readbackSampled: false, readbackVerified: nil
        )
    }

    diagnostic?.finish("unchanged", sampled: true, verified: false)
    throw InputDeliveryError(
        message: "AX \(direction) returned success, but scalar AX value readback remained unchanged at \(before.description)",
        inputEventsEmitted: true,
        failureCode: "readback_unchanged",
        partialInputPossible: false,
        readbackSampled: true,
        readbackVerified: false,
        actionRoute: "ax.action.\(direction)"
    )
}

func nativeAXActionAttemptError(
    action: String,
    result: AXError,
    earlierInputEmitted: Bool = false
) -> InputDeliveryError {
    let routeName = action
        .replacingOccurrences(of: "AX", with: "")
        .lowercased()
    return nativeAXMutationAttemptError(
        operation: "AXUIElementPerformAction(\(action))",
        route: "ax.action.\(routeName)",
        result: result,
        earlierInputEmitted: earlierInputEmitted
    )
}

func performNativeAXMutation(
    operation: String,
    route: String,
    action: () -> AXError
) throws {
    let result = action()
    guard result == .success else {
        throw nativeAXMutationAttemptError(
            operation: operation, route: route, result: result
        )
    }
}

func nativeAXMutationAttemptError(
    operation: String,
    route: String,
    result: AXError,
    earlierInputEmitted: Bool = false
) -> InputDeliveryError {
    let rejected: Bool
    switch result {
    case .illegalArgument, .invalidUIElement, .attributeUnsupported,
         .actionUnsupported, .notImplemented, .apiDisabled:
        rejected = true
    default:
        // AX cannotComplete includes callbacks that acted but did not reply.
        // Generic or unknown native errors do not prove rejection either.
        rejected = false
    }
    let possibleInput = earlierInputEmitted || !rejected
    let explanation = possibleInput
        ? "The request may have taken effect or earlier input was emitted. Observe the current state before retrying."
        : "The native request was rejected; no input was emitted."
    return InputDeliveryError(
        message: "\(operation) returned \(result.rawValue). \(explanation)",
        inputEventsEmitted: possibleInput,
        failureCode: possibleInput ? "effect_unknown" : nil,
        partialInputPossible: possibleInput,
        actionRoute: route
    )
}
