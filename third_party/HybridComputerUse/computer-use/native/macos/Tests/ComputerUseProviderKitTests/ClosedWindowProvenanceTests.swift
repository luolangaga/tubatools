import AppKit
import CoreGraphics
import Testing
@testable import ComputerUseProviderKit

@Test
func selectedWindowNativeIdentityRequiresMatchingPositiveID() {
    let service = ComputerUseService()
    let pid = getpid()
    let expected = macWindowHandle(pid: pid, windowID: 41)
    for (number, handle) in [
        (nil as CGWindowID?, expected as String?),
        (0, expected),
        (42, expected),
        (41, "ax:fallback"),
        (41, nil),
    ] {
        service.rememberNativeWindowIdentity(pid: pid, windowID: number, windowHandle: handle)
        #expect(service.nativeWindowHandles.isEmpty)
    }
    service.rememberNativeWindowIdentity(pid: pid, windowID: 41, windowHandle: expected)
    #expect(service.nativeWindowHandles == [expected])
}

@Test
func appsOnlyCloseNeedsNativeProvenanceAndIndependentAbsenceProof() {
    let pid = getpid()
    let handle = macWindowHandle(pid: pid, windowID: 41)
    let app = RunningAppDescriptor(
        name: "Close Test", bundleIdentifier: "test.close", pid: pid,
        runningApplication: NSRunningApplication.current
    )
    let identity: [String: Any] = [
        "providerBusName": "mac:\(pid)",
        "processGeneration": macProcessGeneration(pid: pid),
    ]
    for registered in [false, true] {
        for axState in [BoundWindowLiveness.absent, .present, .unknown] {
          for cgState in [BoundWindowLiveness.absent, .present, .unknown] {
            let bridge = ComputerUseRuntimeBridge()
            if registered {
                bridge.service.rememberNativeWindowIdentity(
                    pid: pid, windowID: 41, windowHandle: handle
                )
            }
            var inspections = 0
            let inspect = bridge.boundWindowInspector(windowHandle: handle) { owner, selected, comparable in
                inspections += 1
                #expect(owner == pid)
                #expect(selected == handle)
                #expect(comparable == registered)
                return corroboratedWindowLiveness(
                    accessibility: axState, windowServer: cgState,
                    nativeIdentityComparable: comparable,
                    nativeInventoryEmpty: false
                )
            }
            let result = windowActionFinalObservation(
                tool: "close", app: app, appIdentity: identity, windowHandle: handle,
                capture: { throw ComputerUseError.stateUnavailable("selected window has no match") },
                validateWindow: {
                    try validateBoundWindowLiveness(
                        appIdentity: identity, windowHandle: handle,
                        resolveApp: { app }, inspectWindow: inspect
                    )
                }
            )
            #expect(inspections == 1)
            #expect(result["input_events_emitted"] as? Bool == true)
            let snapshot = result["snapshot"] as? [String: Any]
            if registered && axState == .absent && cgState == .absent {
                #expect(result["ok"] as? Bool == true)
                #expect(snapshot?["windowAbsent"] as? Bool == true)
                #expect(snapshot?["closedWindowHandle"] as? String == handle)
            } else {
                #expect(result["ok"] as? Bool == false)
                #expect(result["failureCode"] as? String == "effect_unknown")
                #expect(result["mutation_unknown"] as? Bool == true)
                #expect(snapshot == nil)
            }
          }
        }
    }
}
