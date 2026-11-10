import ApplicationServices
import Foundation
import ImageIO

public enum ClickMethod: String, CaseIterable, Sendable {
    case auto
    case accessibility
    case appPost = "app_post"
    case skyClick = "sky_click"
    case global
}

func clickActionSnapshotRecoveryPolicy(for method: ClickMethod) -> SnapshotRecoveryPolicy {
    method == .skyClick ? .readOnly : .allowActivation
}

func parseClickMethod(_ rawValue: String?) throws -> ClickMethod {
    let normalized = rawValue?
        .trimmingCharacters(in: .whitespacesAndNewlines)
        .lowercased() ?? ClickMethod.auto.rawValue

    guard let method = ClickMethod(rawValue: normalized) else {
        let expected = ClickMethod.allCases.map(\.rawValue).joined(separator: ", ")
        throw ComputerUseError.message(
            "Invalid click_method '\(rawValue ?? "")'. Expected one of: \(expected)"
        )
    }

    return method
}

func validateClickMethod(
    _ method: ClickMethod,
    hasElementIndex: Bool,
    environment: [String: String]
) throws {
    if method == .accessibility, !hasElementIndex {
        throw ComputerUseError.message("click_method 'accessibility' requires element_index")
    }

    if method == .global, !globalPointerFallbacksEnabled(environment: environment) {
        throw ComputerUseError.message(
            "click_method 'global' requires COMPUTER_USE_ALLOW_GLOBAL_POINTER_FALLBACKS=1 because it may move the system pointer and change foreground focus"
        )
    }
}

func validateSkyClickArguments(
    method: ClickMethod,
    mouseButton: String,
    clickCount: Int
) throws {
    guard method == .skyClick else {
        return
    }

    guard mouseButton.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() == MouseButtonKind.left.rawValue else {
        throw ComputerUseError.message(
            "click_method 'sky_click' only supports mouse_button 'left'"
        )
    }

    guard (1...2).contains(clickCount) else {
        throw ComputerUseError.message(
            "click_method 'sky_click' supports click_count 1 or 2"
        )
    }
}

func inputEventPoint(
    fromScreenStatePoint point: CGPoint
) -> CGPoint {
    point
}

func inputFallbackDebugEnabled(environment: [String: String]) -> Bool {
    guard let rawValue = environment["COMPUTER_USE_DEBUG_INPUT_FALLBACKS"]?
        .trimmingCharacters(in: .whitespacesAndNewlines)
        .lowercased()
    else {
        return false
    }

    return ["1", "true", "yes", "on"].contains(rawValue)
}

func globalPointerFallbacksEnabled(environment: [String: String]) -> Bool {
    guard let rawValue = environment["COMPUTER_USE_ALLOW_GLOBAL_POINTER_FALLBACKS"]?
        .trimmingCharacters(in: .whitespacesAndNewlines)
        .lowercased()
    else {
        return false
    }

    return ["1", "true", "yes", "on"].contains(rawValue)
}

func canUseContainingListSelectionAsClick(
    requestedTargetRole: String?,
    candidateRole: String?,
    hasMenuItemAncestor: Bool,
    hasChildMenu: Bool,
    hasPopoverAncestor: Bool
) -> Bool {
    if requestedTargetRole == kAXButtonRole as String ||
        requestedTargetRole == kAXCheckBoxRole as String
    {
        return false
    }
    if candidateRole == kAXButtonRole as String ||
        candidateRole == kAXCheckBoxRole as String
    {
        return false
    }
    if candidateRole == kAXMenuItemRole as String {
        return hasChildMenu
    }
    return !hasMenuItemAncestor && !hasPopoverAncestor
}

func toggledCheckboxValue(_ value: CFTypeRef?) -> NSNumber? {
    guard let number = value as? NSNumber else {
        return nil
    }
    switch number.intValue {
    case 0:
        return NSNumber(value: 1)
    case 1:
        return NSNumber(value: 0)
    default:
        return nil
    }
}

func checkboxValueReadbackMatches(
    expected: NSNumber,
    observed: CFTypeRef?
) -> Bool {
    guard let observed = observed as? NSNumber else {
        return false
    }
    return observed.intValue == expected.intValue
}

func selectionAttributeForContainerRole(_ role: String?) -> String? {
    if role == kAXListRole as String {
        return kAXSelectedChildrenAttribute as String
    }
    if role == kAXOutlineRole as String || role == kAXTableRole as String {
        return kAXSelectedRowsAttribute as String
    }
    return nil
}

func runtimeID(_ candidate: [Int], isAncestorOf descendant: [Int]) -> Bool {
    candidate.count < descendant.count &&
        descendant.starts(with: candidate)
}

func nearestSnapshotRecord(
    withRole role: String,
    for record: ElementRecord,
    records: [ElementRecord],
    includeTarget: Bool = false
) -> ElementRecord? {
    records
        .filter { candidate in
            candidate.providerRole == role &&
                (
                    (includeTarget && candidate.runtimeID == record.runtimeID) ||
                    runtimeID(candidate.runtimeID, isAncestorOf: record.runtimeID)
                )
        }
        .max { lhs, rhs in
            lhs.runtimeID.count < rhs.runtimeID.count
        }
}

func transientPopoverRowRecord(
    for record: ElementRecord,
    records: [ElementRecord]
) -> ElementRecord? {
    guard nearestSnapshotRecord(
        withRole: "popover",
        for: record,
        records: records
    ) != nil else {
        return nil
    }

    return nearestSnapshotRecord(
        withRole: "listitem",
        for: record,
        records: records,
        includeTarget: true
    )
}

func canUseGlobalPointerForTransientRow(
    row: ElementRecord,
    button: MouseButtonKind,
    clickCount: Int
) -> Bool {
    guard button == .left, clickCount == 1 else {
        return false
    }

    let activationActions = [
        kAXPressAction as String,
        "AXOpen",
    ]
    return !row.rawActions.contains { action in
        activationActions.contains {
            $0.caseInsensitiveCompare(action) == .orderedSame
        }
    }
}

let nonSettableSetValueErrorMessage = "Cannot set a value for an element that is not settable"

func setValueAttributeIsSettable(result: AXError, settable: Bool, attribute: String) throws -> Bool {
    guard result == .success else {
        throw ComputerUseError.message("AXUIElementIsAttributeSettable(\(attribute)) failed with \(result.rawValue)")
    }

    return settable
}

func localClickActionPoints(frame: CGRect, isSyntheticText: Bool) -> [CGPoint] {
    guard hasUsableClickGeometry(frame) else {
        return []
    }

    let center = CGPoint(x: frame.midX, y: frame.midY)
    let leading = CGPoint(
        x: frame.minX + min(max(frame.width * 0.3, 20), max(frame.width - 4, 20)),
        y: frame.midY
    )

    if isSyntheticText {
        return [leading]
    }

    if abs(leading.x - center.x) < 1 {
        return [center]
    }

    return [center, leading]
}

func hasUsableClickGeometry(_ frame: CGRect?) -> Bool {
    guard let frame else {
        return false
    }
    return frame.width > 0 &&
        frame.height > 0 &&
        frame.minX.isFinite &&
        frame.minY.isFinite &&
        frame.maxX.isFinite &&
        frame.maxY.isFinite
}

func isLikelySyntheticSideActionCandidate(
    parentFrame: CGRect?,
    candidateFrame: CGRect?,
    hasPrimaryAction: Bool,
    labels: [String]
) -> Bool {
    let hasSideActionLabel = labels.contains { label in
        let normalized = label.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !normalized.isEmpty else {
            return false
        }

        if normalized == "完成" || normalized == "done" || normalized == "complete" || normalized == "archive" {
            return true
        }

        if normalized.count <= 24 {
            if normalized.contains("完成") {
                return true
            }

            if normalized.contains("mark") && (normalized.contains("done") || normalized.contains("complete")) {
                return true
            }
        }

        return false
    }

    guard let parentFrame, let candidateFrame else {
        return false
    }

    let trailingBandWidth = min(max(parentFrame.width * 0.22, 56), 140)
    let isTrailing = candidateFrame.midX >= parentFrame.maxX - trailingBandWidth
    let compactWidth = candidateFrame.width <= max(88, parentFrame.width * 0.18)
    let compactHeight = candidateFrame.height <= max(44, parentFrame.height * 1.2)
    let isCompact = compactWidth && compactHeight

    if hasSideActionLabel && hasPrimaryAction && isCompact {
        return true
    }

    return isTrailing && isCompact && (hasPrimaryAction || hasSideActionLabel)
}

func shouldScanDescendantsOfHitRecord(originalFrame: CGRect?, hitFrame: CGRect?) -> Bool {
    guard let originalFrame, let hitFrame else {
        return true
    }

    let originalArea = max(originalFrame.width * originalFrame.height, 1)
    let hitArea = hitFrame.width * hitFrame.height
    if hitArea > max(originalArea * 12, 20_000) {
        return false
    }

    if hitFrame.height > max(originalFrame.height * 4, 96),
       hitFrame.width > max(originalFrame.width * 2, 240)
    {
        return false
    }

    return true
}

func canUseActivationOnlyClickFallback(role: String?) -> Bool {
    guard let role else {
        return false
    }

    return role == kAXWindowRole as String
}

func canUseKeyboardTextFallback(role: String?, roleDescription: String?, isValueSettable: Bool) -> Bool {
    if isValueSettable {
        return true
    }

    guard let role else {
        return false
    }

    if role == kAXTextFieldRole as String || role == "AXTextArea" || role == "AXTextView" {
        return true
    }

    guard let roleDescription = roleDescription?.lowercased() else {
        return false
    }

    return roleDescription.contains("text field")
        || roleDescription.contains("text area")
        || roleDescription.contains("text entry")
}

func nearestFocusedEditableTarget<Node>(
    from focused: Node?,
    root: Node,
    parentOf: (Node) -> Node?,
    isSame: (Node, Node) -> Bool,
    isEditable: (Node) throws -> Bool
) rethrows -> Node? {
    var current = focused
    var visited: [Node] = []

    for _ in 0..<64 {
        guard let candidate = current else {
            return nil
        }
        guard !visited.contains(where: { isSame($0, candidate) }) else {
            return nil
        }
        visited.append(candidate)

        if try isEditable(candidate) {
            return candidate
        }
        if isSame(candidate, root) {
            return nil
        }
        current = parentOf(candidate)
    }

    return nil
}

public final class ComputerUseService {
    var snapshotsByApp: [String: AppSnapshot] = [:]
    // ProviderServer awaits requests serially. The synchronous request owns
    // this binding, including captures
    // performed inside service actions. Never rediscover a bound app by name
    // or replace its requested window with a focused sibling.
    var activeAppIdentity: [String: Any]?
    var activeWindowHandle: String?

    func withAppIdentity<T>(
        _ identity: [String: Any]?,
        windowHandle: String? = nil,
        operation: () throws -> T
    ) rethrows -> T {
        let previous = activeAppIdentity
        let previousWindow = activeWindowHandle
        activeAppIdentity = identity
        activeWindowHandle = windowHandle.flatMap { $0.isEmpty ? nil : $0 }
        defer {
            activeAppIdentity = previous
            activeWindowHandle = previousWindow
        }
        return try operation()
    }

    func snapshotWindowHandle(requested: String?) throws -> String? {
        let explicit = requested.flatMap { $0.isEmpty ? nil : $0 }
        if let explicit, let activeWindowHandle, explicit != activeWindowHandle {
            throw ComputerUseError.stateUnavailable(
                "The snapshot window conflicts with the current request's bound window."
            )
        }
        return activeWindowHandle ?? explicit
    }
    var nativeWindowHandles: Set<String> = []

    public init() {}

    public func click(
        app query: String,
        elementIndex: String?,
        x: Double?,
        y: Double?,
        clickCount: Int,
        mouseButton: String,
        clickMethod: ClickMethod = .auto
    ) throws -> ToolCallResult {
        try performClick(
            app: query,
            elementIndex: elementIndex,
            x: x,
            y: y,
            clickCount: clickCount,
            mouseButton: mouseButton,
            clickMethod: clickMethod
        )

        return snapshotResult(
            for: try refreshSnapshot(
                for: query,
                recoveryPolicy: clickActionSnapshotRecoveryPolicy(for: clickMethod)
            ),
            style: .actionResult
        )
    }

    func performClick(
        app query: String,
        elementIndex: String?,
        x: Double?,
        y: Double?,
        clickCount: Int,
        mouseButton: String,
        clickMethod: ClickMethod = .auto
    ) throws {
        try validateClickMethod(
            clickMethod,
            hasElementIndex: elementIndex != nil,
            environment: ProcessInfo.processInfo.environment
        )
        try validateSkyClickArguments(
            method: clickMethod,
            mouseButton: mouseButton,
            clickCount: clickCount
        )

        let snapshot = try currentSnapshot(for: query)
        let button = MouseButtonKind(rawValue: mouseButton.lowercased()) ?? .left
        if let elementIndex {
            let record = try lookupElement(snapshot: snapshot, index: elementIndex)
            guard let windowPoint = clickPoint(for: record, snapshot: snapshot) else {
                throw ComputerUseError.stateUnavailable("element \(elementIndex) has no clickable frame")
            }
            let targetPoint = try windowPointToGlobalPoint(snapshot: snapshot, point: windowPoint)
            // AXPress needs no window preparation; every pointer fallback
            // prepares the window itself, so a semantic click does not
            // raise the window or steal focus.
            switch clickMethod {
            case .auto:
                if !(try performAXClickSequence(
                    on: record,
                    snapshot: snapshot,
                    button: button,
                    clickCount: clickCount,
                    includeNearbyHitTesting: true,
                    allowActivationFallback: true,
                    allowTransientGlobalPointerFallback: true
                )) {
                    try performSemanticallyBoundGlobalClick(
                        on: record,
                        snapshot: snapshot,
                        button: button,
                        clickCount: clickCount
                    )
                }
            case .accessibility:
                guard try performAXClickSequence(
                    on: record,
                    snapshot: snapshot,
                    button: button,
                    clickCount: clickCount,
                    includeNearbyHitTesting: true,
                    allowActivationFallback: true,
                    allowTransientGlobalPointerFallback: false
                ) else {
                    throw ComputerUseError.message(
                        "click_method 'accessibility' could not click element_index=\(elementIndex)"
                    )
                }
            case .appPost, .skyClick, .global:
                try performExplicitMouseClick(
                    method: clickMethod,
                    at: targetPoint,
                    windowPoint: windowPoint,
                    button: button,
                    clickCount: clickCount,
                    targetDescription: "element_index=\(elementIndex)",
                    snapshot: snapshot
                )
            }
        } else if let x, let y {
            let point = CGPoint(x: x, y: y)
            let targetPoint = try windowPointToGlobalPoint(snapshot: snapshot, point: point)
            switch clickMethod {
            case .auto:
                let candidates = try clickCandidates(at: point, in: snapshot)
                var handled = false
                for record in candidates {
                    if try performAXClickSequence(
                        on: record,
                        snapshot: snapshot,
                        button: button,
                        clickCount: clickCount,
                        includeNearbyHitTesting: false,
                        allowActivationFallback: false,
                        allowTransientGlobalPointerFallback: false
                    ) {
                        handled = true
                        break
                    }
                }

                if !handled {
                    // Only a pointer event needs the bound window in front.
                    if semanticAXClickRequiresBoundWindowPreparation(
                        method: clickMethod,
                        hasWindowBounds: snapshot.windowBounds != nil,
                        hasWindowID: snapshot.targetWindowID != nil
                    ) {
                        try InputSimulation.prepareBoundWindowForSemanticPointerInput(
                            snapshot
                        )
                    }
                    try performNonAXClickFallback(
                        at: targetPoint,
                        button: button,
                        clickCount: clickCount,
                        targetDescription: "x=\(Int(point.x)) y=\(Int(point.y))",
                        snapshot: snapshot
                    )
                }
            case .accessibility:
                throw ComputerUseError.message(
                    "click_method 'accessibility' requires element_index"
                )
            case .appPost, .skyClick, .global:
                try performExplicitMouseClick(
                    method: clickMethod,
                    at: targetPoint,
                    windowPoint: point,
                    button: button,
                    clickCount: clickCount,
                    targetDescription: "x=\(Int(point.x)) y=\(Int(point.y))",
                    snapshot: snapshot
                )
            }
        } else {
            throw ComputerUseError.invalidArguments("click requires either element_index or x/y")
        }
    }

}
