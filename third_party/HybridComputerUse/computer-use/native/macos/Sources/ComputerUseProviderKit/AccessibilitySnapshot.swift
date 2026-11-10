import AppKit
import ApplicationServices
import CoreGraphics
import Foundation

final class ElementRecord {
    private(set) var boundProviderObjectPath: String?

    func bindProviderObjectPath(_ path: String) {
        precondition(boundProviderObjectPath == nil)
        boundProviderObjectPath = path
    }
    let index: Int
    let runtimeID: [Int]
    let identifier: String?
    let element: AXUIElement?
    let localFrame: CGRect?
    let rawActions: [String]
    let prettyActions: [String]
    let isSyntheticText: Bool
    let providerRole: String?
    let localizedRole: String?
    let name: String?
    let description: String?
    let placeholder: String?
    let value: String?
    let setValueSupported: Bool?
    let states: String?
    let liveIdentity: AXLiveIdentity?

    init(
        index: Int,
        runtimeID: [Int]? = nil,
        identifier: String?,
        element: AXUIElement?,
        localFrame: CGRect?,
        rawActions: [String],
        prettyActions: [String],
        isSyntheticText: Bool = false,
        providerRole: String? = nil,
        localizedRole: String? = nil,
        name: String? = nil,
        description: String? = nil,
        placeholder: String? = nil,
        value: String? = nil,
        setValueSupported: Bool? = nil,
        states: String? = nil,
        liveIdentity: AXLiveIdentity? = nil
    ) {
        self.index = index
        self.runtimeID = runtimeID ?? [index]
        self.identifier = identifier
        self.element = element
        self.localFrame = localFrame
        self.rawActions = rawActions
        self.prettyActions = prettyActions
        self.isSyntheticText = isSyntheticText
        self.providerRole = providerRole
        self.localizedRole = localizedRole
        self.name = name
        self.description = description
        self.placeholder = placeholder
        self.value = value
        self.setValueSupported = setValueSupported
        self.states = states
        self.liveIdentity = liveIdentity
    }
}

enum SnapshotRecoveryPolicy: Equatable {
    case allowActivation
    case readOnly
}

/// Per-call AX messaging timeout while a snapshot is built. The macOS
/// default is about 6 s per call, so one unresponsive application could hold
/// the provider for minutes.
let snapshotAXMessagingTimeout: Float = 0.8
/// Wall-clock budget for one tree walk, as on Linux; the core times out a
/// provider request after 30 s.
let snapshotTraversalDeadlineSeconds: TimeInterval = 20

/// A part of the window tree that a snapshot could not include.
struct SnapshotOmission: Equatable {
    let count: Int
    let reason: String
    let detail: String
}

func snapshotOmissions(
    deadlineOmitted: Int,
    unresponsiveNodes: Int,
    deadlineSeconds: TimeInterval = snapshotTraversalDeadlineSeconds
) -> [SnapshotOmission] {
    var omissions: [SnapshotOmission] = []
    if deadlineOmitted > 0 {
        omissions.append(SnapshotOmission(
            count: deadlineOmitted,
            reason: "time",
            detail: String(
                format: "%d not visited: time budget exhausted (%.0fs); tree is partial; raising node limits does not fix this",
                deadlineOmitted, deadlineSeconds
            )
        ))
    }
    if unresponsiveNodes > 0 {
        omissions.append(SnapshotOmission(
            count: unresponsiveNodes,
            reason: "provider_probe_error",
            detail: "accessibility provider reads were incomplete; missing descendants are unknown"
        ))
    }
    return omissions
}

public struct AccessibilityTreeLimits: Equatable, Sendable {
    public static let defaultMaxNodeCount = 1200
    public static let defaultMaxDepth = 64
    public static let defaults = AccessibilityTreeLimits(
        maxNodeCount: defaultMaxNodeCount,
        maxDepth: defaultMaxDepth
    )

    public let maxNodeCount: Int
    public let maxDepth: Int

    public init(maxNodeCount: Int = defaultMaxNodeCount, maxDepth: Int = defaultMaxDepth) {
        self.maxNodeCount = maxNodeCount
        self.maxDepth = maxDepth
    }
}

@usableFromInline
let defaultTextLimit = 500

public struct SnapshotTextLimit: Equatable, Sendable {
    public static let defaults = SnapshotTextLimit(maxCount: defaultTextLimit)
    public static let max = SnapshotTextLimit(maxCount: nil)

    public let maxCount: Int?

    public init(maxCount: Int = defaultTextLimit) {
        precondition(maxCount > 0, "text limit must be positive")
        self.maxCount = maxCount
    }

    private init(maxCount: Int?) {
        self.maxCount = maxCount
    }
}
let screenshotCaptureTimeout: TimeInterval = 5
let screenshotResultMaxPNGBytes = 900_000
let screenshotResultMaxDimension: CGFloat = 1280
let screenshotResultMinScale: CGFloat = 0.25
// The GUI screenshot is delivered at the size the model sees: GPT-5.4 at
// detail high shrinks 1920x1080 to 1365x768, which would otherwise put the
// model's coordinates in a smaller space than the delivered pixels.
let guiScreenshotResultMaxWidth: CGFloat = 1366
let guiScreenshotResultMaxHeight: CGFloat = 768
private let windowVisibilityRecoveryDelay: TimeInterval = 0.7
let axWebAreaRole = "AXWebArea"
let axContentsAttribute = "AXContents"
let axVisibleChildrenAttribute = "AXVisibleChildren"
let compactGenericActionTargetMaxWidth: CGFloat = 240
let compactGenericActionTargetMaxHeight: CGFloat = 120

public struct AppSnapshot {
    public let app: RunningAppDescriptor
    public let windowTitle: String?
    public let windowBounds: CGRect?
    let rootWindowElement: AXUIElement
    let targetWindowID: CGWindowID?
    let targetWindowHandle: String?
    let targetWindowLayer: Int?
    let treeLines: [String]
    let focusedSummary: String?
    let focusedElement: AXUIElement?
    let selectedText: String?

    let elements: [Int: ElementRecord]
    var providerProcessGeneration: String? = nil
    var diagnosticSnapshotSerial: Int = 0
    var omissions: [SnapshotOmission] = []
    var rootWindowIdentity: AXLiveIdentity? = nil

    public var renderedText: String {
        renderedText(style: .fullState)
    }

    public func renderedText(style: SnapshotTextStyle) -> String {
        var lines: [String] = []
        let displayTitle = displayWindowTitle(windowTitle, appName: app.name)
        let appReference = app.bundleIdentifier ?? app.name

        lines.append("App=\(appReference) (pid \(app.pid))")
        lines.append("Window: \(quoted(displayTitle)), App: \(app.name).")
        lines.append(contentsOf: treeLines)

        if let selectedText, !selectedText.isEmpty {
            lines.append("")
            lines.append("Selected text: [\(selectedText)]")
        } else if let focusedSummary {
            lines.append("")
            lines.append("The focused UI element is \(focusedSummary).")
        }

        return lines.joined(separator: "\n")
    }
}

public enum SnapshotTextStyle {
    case fullState
    case actionResult
}

enum SnapshotBuilder {
    static func build(
        for app: RunningAppDescriptor,
        textLimit: SnapshotTextLimit = .defaults,
        treeLimits: AccessibilityTreeLimits = .defaults,
        recoveryPolicy: SnapshotRecoveryPolicy = .allowActivation,
        fallbackWindow: AXUIElement? = nil,
        expectedWindowHandle: String? = nil
    ) throws -> AppSnapshot {
        let diagnosticContext = Thread.current.threadDictionary
        if diagnosticContext["computer-use.liveness.request"] != nil {
            diagnosticContext["computer-use.failure.snapshot"] =
                (diagnosticContext["computer-use.failure.snapshot"] as? Int ?? 0) + 1
        }
        let permissions = PermissionDiagnostics.current()
        guard permissions.accessibilityTrusted else {
            throw ComputerUseError.permissionDenied("Accessibility permission is required. Run `computer-use doctor` and grant access to Computer Use.")
        }

        let appElement = AXUIElementCreateApplication(app.pid)
        let systemWide = AXUIElementCreateSystemWide()
        // Setting the timeout on the system-wide element sets it for every
        // element of this process; 0 restores the system default.
        AXUIElementSetMessagingTimeout(systemWide, snapshotAXMessagingTimeout)
        defer { AXUIElementSetMessagingTimeout(systemWide, 0) }
        // One probe before the window reads: each later read of a hung
        // application would otherwise wait for its own timeout.
        guard singleAttributeRead(appElement, kAXRoleAttribute).status != .cannotComplete else {
            throw ComputerUseError.stateUnavailable(
                "\(app.name) did not answer accessibility requests within \(snapshotAXMessagingTimeout) s; it may be busy or not responding. Retry the observation, or use GUI screenshots."
            )
        }
        enableBestEffortAccessibilityModes(appElement)
        var focusedApplication = copyElement(systemWide, attribute: kAXFocusedApplicationAttribute)
        let hasRequestedWindow = !(expectedWindowHandle ?? "").isEmpty
        var focusedWindow = try selectSnapshotWindow(
            requestedHandle: expectedWindowHandle,
            inventory: {
                let inventory = axWindowInventory(pid: app.pid)
                return inventory.complete ? inventory.windows : nil
            },
            handleOf: { canonicalMacAXWindowHandle(pid: app.pid, window: $0) },
            unboundWindow: {
                preferredFocusedWindow(
                    appElement: appElement,
                    appPID: app.pid,
                    focusedApplication: focusedApplication,
                    systemWide: systemWide
                ) ?? firstAnyWindow(for: appElement)
            }
        )
        if focusedWindow == nil {
            focusedWindow = reusableFallbackWindow(
                fallbackWindow,
                appPID: app.pid,
                expectedWindowHandle: expectedWindowHandle
            )
        }
        if !hasRequestedWindow, focusedWindow == nil,
           recoveryPolicy == .allowActivation,
           recoverVisibleWindow(for: app, appElement: appElement, preferredWindow: nil) {
            focusedApplication = copyElement(systemWide, attribute: kAXFocusedApplicationAttribute)
            focusedWindow = preferredFocusedWindow(
                appElement: appElement,
                appPID: app.pid,
                focusedApplication: focusedApplication,
                systemWide: systemWide
            ) ?? firstAnyWindow(for: appElement)
        }

        guard let resolvedFocusedWindow = focusedWindow else {
            windowLivenessDiagnostic("snapshot_no_ax_root", [:])
            throw ComputerUseError.stateUnavailable(computerUseNoWindowFoundMessage(appName: app.name))
        }
        let rootWindow = resolvedFocusedWindow

        let windowCapture = WindowCapture.resolve(
            for: app.pid,
            windowIDHint: windowNumber(rootWindow),
            titleHint: stringValue(of: rootWindow, attribute: kAXTitleAttribute)
        )

        return try buildAccessibilitySnapshot(
            app: app,
            appElement: appElement,
            rootElement: rootWindow,
            windowCapture: windowCapture,
            focusedApplication: focusedApplication,
            systemWide: systemWide,
            textLimit: textLimit,
            treeLimits: treeLimits
        )
    }

    static func targetWindowLiveness(
        _ snapshot: AppSnapshot,
        expectedWindowHandle: String?
    ) -> TargetWindowLiveness {
        guard let expectedWindowHandle,
              !expectedWindowHandle.isEmpty,
              snapshot.targetWindowHandle == expectedWindowHandle else {
            return .unknown
        }

        let appElement = AXUIElementCreateApplication(snapshot.app.pid)
        var appWindowsValue: CFTypeRef?
        let appWindowsResult = AXUIElementCopyAttributeValue(
            appElement,
            kAXWindowsAttribute as CFString,
            &appWindowsValue
        )
        let appWindows: [AXUIElement]
        let appWindowInventoryComplete: Bool
        switch appWindowsResult {
        case .success:
            guard let windows = appWindowsValue as? [AXUIElement] else {
                return .unknown
            }
            appWindows = windows
            appWindowInventoryComplete = true
        case .noValue, .attributeUnsupported:
            appWindows = []
            appWindowInventoryComplete = true
        default:
            appWindows = []
            appWindowInventoryComplete = false
        }

        let systemWide = AXUIElementCreateSystemWide()
        let focusedWindowRead = readOptionalAXElement(
            systemWide,
            attribute: kAXFocusedWindowAttribute as String
        )
        let focusedElementRead = readOptionalAXElement(
            systemWide,
            attribute: kAXFocusedUIElementAttribute as String
        )
        var candidates = appWindows
        if let focusedWindow = focusedWindowRead.element {
            candidates.append(focusedWindow)
        }
        if let focusedElement = focusedElementRead.element,
           let focusedElementWindow = containingAXWindow(from: focusedElement) {
            candidates.append(focusedElementWindow)
        }
        let exactWindowFound = candidates.contains { candidate in
            pid(of: candidate) == snapshot.app.pid &&
                stringValue(
                    of: candidate,
                    attribute: kAXRoleAttribute
                ) == kAXWindowRole as String &&
                macAXWindowHandle(
                    pid: snapshot.app.pid,
                    window: candidate
                ) == expectedWindowHandle
        }
        return classifyTargetWindowDiscovery(
            hasExpectedWindowHandle: true,
            exactWindowFound: exactWindowFound,
            appWindowInventoryComplete: appWindowInventoryComplete,
            systemFocusInventoryComplete:
                focusedWindowRead.complete && focusedElementRead.complete
        )
    }

    private static func readOptionalAXElement(
        _ element: AXUIElement,
        attribute: String
    ) -> (element: AXUIElement?, complete: Bool) {
        var value: CFTypeRef?
        let result = AXUIElementCopyAttributeValue(
            element,
            attribute as CFString,
            &value
        )
        switch result {
        case .success:
            guard let value,
                  let resolved = checkedAXElement(value) else {
                return (nil, false)
            }
            return (resolved, true)
        case .noValue, .attributeUnsupported:
            return (nil, true)
        default:
            return (nil, false)
        }
    }

    private static func buildAccessibilitySnapshot(
        app: RunningAppDescriptor,
        appElement: AXUIElement,
        rootElement: AXUIElement,
        windowCapture: WindowCapture?,
        focusedApplication: AXUIElement?,
        systemWide: AXUIElement,
        textLimit: SnapshotTextLimit,
        treeLimits: AccessibilityTreeLimits
    ) throws -> AppSnapshot {
        let generation = macProcessGeneration(pid: app.pid)
        return try AXNodeFactsCache.withSnapshotScope {
            try renderAccessibilitySnapshot(
                app: app,
                appElement: appElement,
                rootElement: rootElement,
                windowCapture: windowCapture,
                focusedApplication: focusedApplication,
                systemWide: systemWide,
                textLimit: textLimit,
                treeLimits: treeLimits,
                generation: generation
            )
        }
    }

    private static func renderAccessibilitySnapshot(
        app: RunningAppDescriptor,
        appElement: AXUIElement,
        rootElement: AXUIElement,
        windowCapture: WindowCapture?,
        focusedApplication: AXUIElement?,
        systemWide: AXUIElement,
        textLimit: SnapshotTextLimit,
        treeLimits: AccessibilityTreeLimits,
        generation: String
    ) throws -> AppSnapshot {
        let windowBounds = windowCapture?.bounds ??
            resolveLocalFrame(of: rootElement, windowBounds: nil)
        let focusedElement = focusedElementInSnapshotWindow(
            preferredFocusedElement(appElement: appElement, appPID: app.pid, focusedApplication: focusedApplication, systemWide: systemWide),
            selectedHandle: canonicalMacAXWindowHandle(pid: app.pid, window: rootElement),
            windowHandleOf: { element in
                containingAXWindow(from: element).map {
                    canonicalMacAXWindowHandle(pid: app.pid, window: $0)
                }
            }
        )
        let selectedText = focusedElement.flatMap { copySelectedText($0, textLimit: textLimit) }
        let context = RenderContext(
            windowBounds: windowBounds,
            focusedElement: focusedElement,
            focusedAncestry: focusedAXAncestry(from: focusedElement),
            textLimit: textLimit,
            treeLimits: treeLimits,
            traversalDeadline: ProcessInfo.processInfo.systemUptime
                + snapshotTraversalDeadlineSeconds
        )

        var renderer = TreeRenderer(context: context, selectedRoot: rootElement)
        try renderer.render(rootElement)
        if let menuBar = copyElement(appElement, attribute: kAXMenuBarAttribute),
           !CFEqual(menuBar, rootElement)
        {
            try renderer.render(menuBar, relativeRuntimeID: [1])
        }
        let omissions = renderer.omissions(
            unresponsiveNodes: AXNodeFactsCache.current?.unresponsiveNodeCount ?? 0
        )
        var treeLines = renderer.lines
        if let timeOmission = omissions.first(where: { $0.reason == "time" }) {
            treeLines.append("(\(timeOmission.count) node(s) omitted — \(timeOmission.detail))")
        }

        try bindMacProviderObjectIdentities(
            Array(renderer.records.values),
            scope: "mac:\(app.pid):\(generation)"
        )
        guard generation == macProcessGeneration(pid: app.pid) else {
            throw ComputerUseError.staleAppRef(
                "Application process changed during native identity binding."
            )
        }
        return AppSnapshot(
            app: app,
            windowTitle: renderer.selectedRootTitle,
            windowBounds: windowBounds,
            rootWindowElement: rootElement,
            targetWindowID: windowCapture?.windowID ?? windowNumber(rootElement),
            targetWindowHandle: canonicalMacAXWindowHandle(
                pid: app.pid,
                window: rootElement
            ),
            targetWindowLayer: windowCapture?.layer,
            treeLines: treeLines,
            focusedSummary: renderer.focusedSummary,
            focusedElement: focusedElement,
            selectedText: selectedText,
            elements: renderer.records,
            providerProcessGeneration: generation,
            diagnosticSnapshotSerial: Thread.current.threadDictionary[
                "computer-use.failure.snapshot"
            ] as? Int ?? 0,
            omissions: omissions,
            rootWindowIdentity: AXLiveIdentity.sample(rootElement)
        )
    }

    private static func recoverVisibleWindow(for app: RunningAppDescriptor, appElement: AXUIElement, preferredWindow: AXUIElement?) -> Bool {
        var recovered = false

        if let runningApplication = NSRunningApplication(processIdentifier: app.pid) {
            recovered = runningApplication.unhide() || recovered
            recovered = runningApplication.activate(options: [.activateAllWindows]) || recovered
        }

        if let bundleIdentifier = app.bundleIdentifier {
            recovered = openBundleIdentifier(bundleIdentifier) || recovered
        }

        if let window = preferredWindow ?? firstAnyWindow(for: appElement) {
            recovered = unminimize(window) || recovered
            recovered = raise(window) || recovered
            recovered = setBoolAttribute(named: kAXMainAttribute as String, on: window) || recovered
            recovered = setBoolAttribute(named: kAXFocusedAttribute as String, on: window) || recovered
        }

        if recovered {
            Thread.sleep(forTimeInterval: windowVisibilityRecoveryDelay)
        }

        return recovered
    }

    private static func openBundleIdentifier(_ bundleIdentifier: String) -> Bool {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/open")
        process.arguments = ["-b", bundleIdentifier]
        process.standardOutput = Pipe()
        process.standardError = Pipe()

        do {
            try process.run()
            process.waitUntilExit()
            return process.terminationStatus == 0
        } catch {
            return false
        }
    }

    private static func firstWindow(for appElement: AXUIElement) -> AXUIElement? {
        guard let windows = copyArray(appElement, attribute: kAXWindowsAttribute) else {
            return nil
        }

        return windows.first(where: isUsableWindowElement(_:))
    }

    private static func firstAnyWindow(for appElement: AXUIElement) -> AXUIElement? {
        copyElement(appElement, attribute: kAXFocusedWindowAttribute)
            ?? copyArray(appElement, attribute: kAXWindowsAttribute)?.first
    }

    private static func unminimize(_ window: AXUIElement) -> Bool {
        guard boolValue(of: window, attribute: kAXMinimizedAttribute) == true else {
            return false
        }

        return AXUIElementSetAttributeValue(window, kAXMinimizedAttribute as CFString, kCFBooleanFalse) == .success
    }

    private static func raise(_ window: AXUIElement) -> Bool {
        guard copyActions(window)?.contains(where: { $0.caseInsensitiveCompare(kAXRaiseAction as String) == .orderedSame }) == true else {
            return false
        }

        return AXUIElementPerformAction(window, kAXRaiseAction as CFString) == .success
    }

    private static func setBoolAttribute(named attribute: String, on element: AXUIElement) -> Bool {
        AXUIElementSetAttributeValue(element, attribute as CFString, kCFBooleanTrue) == .success
    }

    private static func preferredFocusedWindow(appElement: AXUIElement, appPID: pid_t, focusedApplication: AXUIElement?, systemWide: AXUIElement) -> AXUIElement? {
        if let systemWindow = strictFocusedAXWindow(
            appElement: appElement,
            appPID: appPID,
            systemWide: systemWide
        ) {
            return systemWindow
        }

        if let focusedApplication, pid(of: focusedApplication) == appPID {
            return usableWindowElement(from: copyElement(focusedApplication, attribute: kAXFocusedWindowAttribute))
                ?? firstWindow(for: focusedApplication)
                ?? usableWindowElement(from: copyElement(appElement, attribute: kAXFocusedWindowAttribute))
                ?? firstWindow(for: appElement)
        }

        return usableWindowElement(from: copyElement(appElement, attribute: kAXFocusedWindowAttribute)) ?? firstWindow(for: appElement)
    }

    private static func reusableFallbackWindow(
        _ window: AXUIElement?,
        appPID: pid_t,
        expectedWindowHandle: String?
    ) -> AXUIElement? {
        guard let window, isUsableWindowElement(window) else {
            return nil
        }
        let handle = macAXWindowHandle(pid: appPID, window: window)
        guard cachedWindowFallbackMatches(
            appPID: appPID,
            candidatePID: pid(of: window),
            candidateWindowHandle: handle,
            expectedWindowHandle: expectedWindowHandle
        ) else {
            return nil
        }
        return window
    }

    private static func usableWindowElement(from element: AXUIElement?) -> AXUIElement? {
        guard let element, isUsableWindowElement(element) else {
            return nil
        }

        return element
    }

    private static func isUsableWindowElement(_ element: AXUIElement) -> Bool {
        stringValue(of: element, attribute: kAXRoleAttribute) == kAXWindowRole as String
            && boolValue(of: element, attribute: kAXMinimizedAttribute) != true
    }

    private static func preferredFocusedElement(appElement: AXUIElement, appPID: pid_t, focusedApplication: AXUIElement?, systemWide: AXUIElement) -> AXUIElement? {
        if let focusedApplication, pid(of: focusedApplication) == appPID {
            return copyElement(systemWide, attribute: kAXFocusedUIElementAttribute)
                ?? copyElement(focusedApplication, attribute: kAXFocusedUIElementAttribute)
                ?? copyElement(appElement, attribute: kAXFocusedUIElementAttribute)
        }

        return copyElement(appElement, attribute: kAXFocusedUIElementAttribute)
    }

}

private func displayWindowTitle(_ value: String?, appName: String) -> String {
    guard let value, !value.isEmpty else {
        return appName
    }

    if value.hasPrefix("\(appName) –") {
        return appName
    }

    return value
}

private func quoted(_ value: String) -> String {
    "\"\(value)\""
}
