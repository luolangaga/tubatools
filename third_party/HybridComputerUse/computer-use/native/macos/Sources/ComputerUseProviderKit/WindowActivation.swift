import AppKit
import ApplicationServices
import Darwin
import Foundation

struct WindowServerForegroundObservation: Equatable {
    let matchesTarget: Bool?
}

struct WindowServerBoundWindowObservation: Equatable {
    let matchesOwner: Bool
    let isOnScreen: Bool
    let bounds: CGRect?
}

func windowServerForegroundObservation(
    targetPID: pid_t
) -> WindowServerForegroundObservation {
    WindowServerForegroundObservation(
        matchesTarget: SkyLightSPI.shared.isFrontmostProcess(pid: targetPID)
    )
}

func windowServerBoundWindowObservation(
    windowID: CGWindowID,
    targetPID: pid_t
) -> WindowServerBoundWindowObservation? {
    let windowInfo = CGWindowListCopyWindowInfo(
        [.optionIncludingWindow],
        windowID
    ) as? [[String: Any]] ?? []
    guard let info = windowInfo.first(where: {
        ($0[kCGWindowNumber as String] as? NSNumber)?.uint32Value == windowID
    }) else {
        return nil
    }

    let ownerPID = (info[kCGWindowOwnerPID as String] as? NSNumber)?.int32Value
    let isOnScreen = (info[kCGWindowIsOnscreen as String] as? NSNumber)?.boolValue
    let bounds = (info[kCGWindowBounds as String] as? NSDictionary)
        .flatMap(CGRect.init(dictionaryRepresentation:))
    return WindowServerBoundWindowObservation(
        matchesOwner: ownerPID == targetPID,
        isOnScreen: isOnScreen == true,
        bounds: bounds
    )
}

func boundWindowMatchesSnapshot(
    foreground: WindowServerForegroundObservation,
    window: WindowServerBoundWindowObservation?,
    snapshotBounds: CGRect,
    tolerance: CGFloat = 2
) -> Bool {
    guard foreground.matchesTarget == true,
          let window,
          window.matchesOwner,
          window.isOnScreen,
          let bounds = window.bounds
    else {
        return false
    }

    return abs(bounds.minX - snapshotBounds.minX) <= tolerance
        && abs(bounds.minY - snapshotBounds.minY) <= tolerance
        && abs(bounds.width - snapshotBounds.width) <= tolerance
        && abs(bounds.height - snapshotBounds.height) <= tolerance
}

func focusedApplicationPID() -> pid_t? {
    let systemWide = AXUIElementCreateSystemWide()
    var value: CFTypeRef?
    guard AXUIElementCopyAttributeValue(
        systemWide,
        kAXFocusedApplicationAttribute as CFString,
        &value
    ) == .success,
    let value
    else {
        return nil
    }
    guard let application = checkedAXElement(value) else {
        return nil
    }
    var pid: pid_t = 0
    guard AXUIElementGetPid(application, &pid) == .success, pid > 0 else {
        return nil
    }
    return pid
}

func waitForFrontmostApplication(
    timeout: TimeInterval,
    pollInterval: TimeInterval,
    foreground: () -> WindowServerForegroundObservation,
    wait: (TimeInterval) -> Void
) -> Bool {
    let deadline = Date().addingTimeInterval(max(0, timeout))
    repeat {
        if foreground().matchesTarget == true {
            return true
        }
        let remaining = deadline.timeIntervalSinceNow
        if remaining <= 0 {
            return false
        }
        wait(min(max(0, pollInterval), remaining))
    } while true
}

func waitForBoundWindowReadiness(
    timeout: TimeInterval,
    pollInterval: TimeInterval,
    ready: () -> Bool,
    wait: (TimeInterval) -> Void
) -> Bool {
    let deadline = Date().addingTimeInterval(max(0, timeout))
    repeat {
        if ready() {
            return true
        }
        let remaining = deadline.timeIntervalSinceNow
        if remaining <= 0 {
            return false
        }
        wait(min(max(0, pollInterval), remaining))
    } while true
}

func activatedWindowMatchesRequest(
    foreground: WindowServerForegroundObservation,
    focusedWindowHandle: String?,
    requestedWindowHandle: String
) -> Bool {
    foreground.matchesTarget == true
        && focusedWindowHandle == requestedWindowHandle
}

func prepareBoundWindowForPointerInput(
    activationTimeout: TimeInterval,
    fallbackTimeout: TimeInterval,
    pollInterval: TimeInterval,
    activateAndRaise: () -> Void,
    activateBoundWindow: () -> Bool,
    ready: () -> Bool,
    wait: (TimeInterval) -> Void
) -> Bool {
    if ready() {
        return true
    }

    activateAndRaise()
    if waitForBoundWindowReadiness(
        timeout: activationTimeout,
        pollInterval: pollInterval,
        ready: ready,
        wait: wait
    ) {
        return true
    }

    guard activateBoundWindow() else {
        return false
    }
    return waitForBoundWindowReadiness(
        timeout: fallbackTimeout,
        pollInterval: pollInterval,
        ready: ready,
        wait: wait
    )
}

func prepareApplicationForGlobalPointerInput(
    timeout: TimeInterval,
    pollInterval: TimeInterval,
    raiseWindow: () -> Void,
    activateBoundWindow: () -> Bool,
    activateApplication: () -> Bool,
    windowServerForeground: () -> WindowServerForegroundObservation,
    wait: (TimeInterval) -> Void
) -> Bool {
    if windowServerForeground().matchesTarget == true {
        return true
    }

    raiseWindow()
    if windowServerForeground().matchesTarget == true {
        return true
    }

    if !activateBoundWindow() {
        _ = activateApplication()
    }
    return waitForFrontmostApplication(
        timeout: timeout,
        pollInterval: pollInterval,
        foreground: windowServerForeground,
        wait: wait
    )
}
