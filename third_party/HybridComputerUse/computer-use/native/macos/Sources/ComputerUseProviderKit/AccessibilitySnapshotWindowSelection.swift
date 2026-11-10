import ApplicationServices
import Foundation

func cachedWindowFallbackMatches(
    appPID: pid_t,
    candidatePID: pid_t?,
    candidateWindowHandle: String,
    expectedWindowHandle: String?
) -> Bool {
    guard candidatePID == appPID else {
        return false
    }
    guard let expectedWindowHandle, !expectedWindowHandle.isEmpty else {
        return true
    }
    return candidateWindowHandle == expectedWindowHandle
}

func nearestLogicalWindow<Node>(
    from start: Node?,
    roleOf: (Node) -> String?,
    parentOf: (Node) -> Node?
) -> Node? {
    var current = start
    for _ in 0..<64 {
        guard let element = current else {
            return nil
        }
        if roleOf(element) == kAXWindowRole as String {
            return element
        }
        current = parentOf(element)
    }
    return nil
}

func containingAXWindow(from element: AXUIElement?) -> AXUIElement? {
    nearestLogicalWindow(
        from: element,
        roleOf: {
            stringValue(of: $0, attribute: kAXRoleAttribute)
        },
        parentOf: {
            copyElement($0, attribute: kAXParentAttribute)
        }
    )
}

func strictFocusedWindowCandidate<Node>(
    targetPID: pid_t,
    systemFocusedWindow: Node?,
    systemFocusedElement: Node?,
    applicationFocusedWindow: Node?,
    pidOf: (Node) -> pid_t?,
    containingWindow: (Node) -> Node?,
    isUsableWindow: (Node) -> Bool
) -> Node? {
    if let systemFocusedWindow,
       pidOf(systemFocusedWindow) == targetPID,
       let window = containingWindow(systemFocusedWindow),
       isUsableWindow(window)
    {
        return window
    }
    if let systemFocusedElement,
       pidOf(systemFocusedElement) == targetPID,
       let window = containingWindow(systemFocusedElement),
       isUsableWindow(window)
    {
        return window
    }
    if let applicationFocusedWindow,
       pidOf(applicationFocusedWindow) == targetPID,
       let window = containingWindow(applicationFocusedWindow),
       isUsableWindow(window)
    {
        return window
    }
    return nil
}

func strictFocusedAXWindow(
    appElement: AXUIElement,
    appPID: pid_t,
    systemWide: AXUIElement
) -> AXUIElement? {
    strictFocusedWindowCandidate(
        targetPID: appPID,
        systemFocusedWindow: copyElement(
            systemWide,
            attribute: kAXFocusedWindowAttribute
        ),
        systemFocusedElement: copyElement(
            systemWide,
            attribute: kAXFocusedUIElementAttribute
        ),
        applicationFocusedWindow: copyElement(
            appElement,
            attribute: kAXFocusedWindowAttribute
        ),
        pidOf: pid,
        containingWindow: containingAXWindow,
        isUsableWindow: {
            stringValue(of: $0, attribute: kAXRoleAttribute) ==
                kAXWindowRole as String &&
                boolValue(of: $0, attribute: kAXMinimizedAttribute) != true
        }
    )
}

func uniqueUsableWindowCandidate<Node>(
    _ windows: [Node],
    isUsableWindow: (Node) -> Bool
) -> Node? {
    let usable = windows.filter(isUsableWindow)
    return usable.count == 1 ? usable[0] : nil
}

func boundedAncestry<Node>(
    from start: Node?,
    parentOf: (Node) -> Node?,
    isSame: (Node, Node) -> Bool
) -> [Node] {
    var ancestry: [Node] = []
    var current = start
    for _ in 0..<64 {
        guard let element = current else {
            break
        }
        guard !ancestry.contains(where: { isSame($0, element) }) else {
            break
        }
        ancestry.append(element)
        current = parentOf(element)
    }
    return ancestry
}

func prioritizeFocusedBranchChild<Node>(
    _ children: [Node],
    parent: Node,
    focusedAncestry: [Node],
    isSame: (Node, Node) -> Bool
) -> [Node] {
    prioritizeFocusedBranchChild(
        children, parent: parent, focusedAncestry: focusedAncestry,
        elementOf: { $0 }, isSame: isSame
    )
}

func prioritizeFocusedBranchChild<Node, Child>(
    _ children: [Child],
    parent: Node,
    focusedAncestry: [Node],
    elementOf: (Child) -> Node,
    isSame: (Node, Node) -> Bool
) -> [Child] {
    guard let parentIndex = focusedAncestry.firstIndex(
        where: { isSame($0, parent) }
    ),
    parentIndex > 0
    else {
        return children
    }

    let focusedChild = focusedAncestry[parentIndex - 1]
    guard let childIndex = children.firstIndex(
        where: { isSame(elementOf($0), focusedChild) }
    ),
    childIndex > 0
    else {
        return children
    }

    var prioritized = children
    prioritized.remove(at: childIndex)
    prioritized.insert(children[childIndex], at: 0)
    return prioritized
}

func focusedAXAncestry(from element: AXUIElement?) -> [AXUIElement] {
    boundedAncestry(
        from: element,
        parentOf: {
            copyElement($0, attribute: kAXParentAttribute)
        },
        isSame: {
            CFEqual($0, $1)
        }
    )
}

enum TargetWindowLiveness: Equatable {
    case present
    case absent
    case unknown
}

func classifyTargetWindowDiscovery(
    hasExpectedWindowHandle: Bool,
    exactWindowFound: Bool,
    appWindowInventoryComplete: Bool,
    systemFocusInventoryComplete: Bool
) -> TargetWindowLiveness {
    guard hasExpectedWindowHandle else {
        return .unknown
    }
    if exactWindowFound {
        return .present
    }
    return appWindowInventoryComplete && systemFocusInventoryComplete
        ? .absent
        : .unknown
}

func postActionTargetWindowTransitionProven(
    targetDestroyed: Bool,
    currentLiveness: TargetWindowLiveness
) -> Bool {
    targetDestroyed || currentLiveness == .absent
}

func selectSnapshotWindow<Node>(
    requestedHandle: String?,
    inventory: () -> [Node]?,
    handleOf: (Node) -> String,
    unboundWindow: () -> Node?
) throws -> Node? {
    guard let requestedHandle, !requestedHandle.isEmpty else {
        return unboundWindow()
    }
    guard let windows = inventory() else {
        throw ComputerUseError.stateUnavailable(
            "The requested window cannot be selected from an incomplete accessibility inventory."
        )
    }
    let matches = windows.filter { handleOf($0) == requestedHandle }
    guard matches.count == 1 else {
        throw ComputerUseError.stateUnavailable(
            "The requested window has no unique match in the accessibility inventory."
        )
    }
    return matches[0]
}

func focusedElementInSnapshotWindow<Node>(
    _ candidate: Node?,
    selectedHandle: String,
    windowHandleOf: (Node) -> String?
) -> Node? {
    guard let candidate, !selectedHandle.isEmpty,
          windowHandleOf(candidate) == selectedHandle else {
        return nil
    }
    return candidate
}
