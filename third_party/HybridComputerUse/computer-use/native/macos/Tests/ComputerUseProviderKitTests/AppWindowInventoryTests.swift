@testable import ComputerUseProviderKit
import AppKit
import ApplicationServices
import CoreGraphics
import Testing

@Test func unnumberedWindowsKeepTitlesWithoutInventingHandles() throws {
    let windowIDs: [CGWindowID?] = [nil, 0]
    for windowID in windowIDs {
        let record = macWindowInventoryRecord(pid: getpid(), generation: "test", windowID: windowID, title: "Unsaved", focusedHandle: nil, modal: false)
        let window = try #require(appListingWindows([record]).first)
        #expect(window["title"] as? String == "Unsaved")
        #expect(window["handle"] as? String == "")
        #expect(window["providerObjectPath"] as? String == "")
        #expect(window["inputTarget"] as? Bool == true)
    }
}

@Test func appWindowIdentityMatchesObservationAndFocusIsNotArrayOrder() {
    let handle = macWindowHandle(pid: getpid(), windowID: 77)
    let unfocused = macWindowInventoryRecord(pid: getpid(), generation: "test", windowID: 66, title: "First", focusedHandle: handle, modal: false)
    let focused = macWindowInventoryRecord(pid: getpid(), generation: "test", windowID: 77, title: "Second", focusedHandle: handle, modal: false)
    let windows = appListingWindows([unfocused, focused])
    #expect(windows[0]["active"] as? Bool == false)
    #expect(windows[1]["active"] as? Bool == true)
    #expect(windows[1]["handle"] as? String == handle)
    #expect(windows[1]["providerObjectPath"] as? String == "window:\(handle)")
    #expect(windows[1]["processGeneration"] as? String == "test")
}

@Test func aliasedWindowsDoNotAcquireIndependentExecutableRefs() {
    let record = macWindowInventoryRecord(pid: getpid(), generation: "test", windowID: 77, title: "Shared", focusedHandle: nil, modal: false)
    let windows = appListingWindows([record, record])
    #expect(windows.count == 2)
    #expect(windows.allSatisfy { $0["handle"] as? String == "" })
    #expect(windows.allSatisfy { $0["inputTarget"] as? Bool == true })
}

@Test func incompleteWindowReadPreservesDiscoveryWithoutProvingAbsence() throws {
    let app = RunningAppDescriptor(name: "Test", bundleIdentifier: "test.inventory", pid: getpid(), runningApplication: NSRunningApplication.current)
    let empty = try macApplicationListing([app], accessibilityTrusted: { true }, inputCapable: { _ in true }, windows: { _ in [] })
    #expect((empty.apps[0]["windows"] as? [[String: Any]])?.isEmpty == true)
    #expect(empty.issues.isEmpty)
    let healthy = RunningAppDescriptor(name: "Editor", bundleIdentifier: "test.editor", pid: getpid(), runningApplication: NSRunningApplication.current)
    let result = try macApplicationListing([app, healthy], accessibilityTrusted: { true }, inputCapable: { _ in true }, windows: { current in
        if current.name == app.name {
            throw ComputerUseError.stateUnavailable("AXWindows unavailable (AX -25204)")
        }
        return [macWindowInventoryRecord(pid: getpid(), generation: "test", windowID: 77, title: "Document", focusedHandle: nil, modal: false)]
    })
    #expect(result.apps.count == 2)
    #expect((result.apps[1]["windows"] as? [[String: Any]])?.first?["title"] as? String == "Document")
    #expect(result.issues == [["app": "Test", "reason": "AXWindows unavailable (AX -25204)"]])
    let wire = ProviderResponseBuilder.domainResult(["ok": true, "apps": result.apps, "inventory_issues": result.issues], operation: "observe_apps")
    #expect(wire["inventory_issues"] as? [[String: String]] == result.issues)
}

@Test func applicationDiscoveryDoesNotHideGlobalPermissionFailures() throws {
    let app = RunningAppDescriptor(name: "Test", bundleIdentifier: "test.inventory", pid: getpid(), runningApplication: NSRunningApplication.current)
    var reads = 0
    do {
        _ = try macApplicationListing([app], accessibilityTrusted: { false }, inputCapable: { _ in true }, windows: { _ in
            reads += 1
            return []
        })
        Issue.record("Denied Accessibility must fail before enumeration")
    } catch {
        #expect(runtimeFailureResult(error)["failureCode"] as? String == "permission_denied")
    }
    #expect(reads == 0)
    for failure in [ComputerUseError.permissionDenied("permission revoked"), .invalidArguments("invalid provider state")] {
        #expect(throws: (any Error).self) {
            try macApplicationListing([app], accessibilityTrusted: { true }, inputCapable: { _ in true }, windows: { _ in throw failure })
        }
    }
    var checks = 0
    #expect(throws: (any Error).self) {
        try macApplicationListing([app], accessibilityTrusted: {
            checks += 1
            return checks == 1
        }, inputCapable: { _ in true }, windows: { _ in [] })
    }
}

@Test func failedTitleReadsCannotBecomeSuccessfulEmptyTitles() throws {
    #expect(try completeWindowTitle(status: .success, value: "Document" as CFString) == "Document")
    #expect(try completeWindowTitle(status: .success, value: nil) == "")
    #expect(try completeWindowTitle(status: .attributeUnsupported, value: nil) == "")
    #expect(try completeWindowTitle(status: .noValue, value: nil) == "")
    for status in [AXError.cannotComplete, .apiDisabled, .invalidUIElement] {
        #expect(throws: (any Error).self) { try completeWindowTitle(status: status, value: nil) }
    }
    #expect(throws: (any Error).self) { try completeWindowTitle(status: .success, value: kCFBooleanTrue) }
}
