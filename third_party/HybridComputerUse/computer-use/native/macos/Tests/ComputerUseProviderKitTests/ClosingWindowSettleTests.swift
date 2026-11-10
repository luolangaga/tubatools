import AppKit
import CoreGraphics
import Testing
@testable import ComputerUseProviderKit

private func closingWindowTestResult(
    states: [(BoundWindowLiveness, BoundWindowLiveness)],
    nativeComparable: Bool = true,
    timeout: TimeInterval = 0.2
) -> (result: [String: Any], samples: Int, elapsed: TimeInterval, identityReads: Int) {
    let app = RunningAppDescriptor(name: "Closing Test", bundleIdentifier: nil,
        pid: getpid(), runningApplication: NSRunningApplication.current)
    let identity: [String: Any] = ["providerBusName": "mac:\(app.pid)",
        "processGeneration": macProcessGeneration(pid: app.pid)]
    let handle = macWindowHandle(pid: app.pid, windowID: 41)
    var elapsed: TimeInterval = 0
    var samples = 0
    var identityReads = 0
    let result = windowActionFinalObservation(
        tool: "close", app: app, appIdentity: identity, windowHandle: handle,
        capture: { throw ComputerUseError.stateUnavailable("closing window has no AX snapshot") },
        validateWindow: {
            try validateClosingWindow(timeout: timeout, pollInterval: 0.05,
                now: { elapsed }, wait: { elapsed += $0 }, validateWindow: {
                    try validateBoundWindowLiveness(appIdentity: identity, windowHandle: handle,
                        resolveApp: { identityReads += 1; return app },
                        inspectWindow: { owner, requested in
                            #expect(owner == app.pid)
                            #expect(requested == handle)
                            let state = states[min(samples, states.count - 1)]
                            samples += 1
                            return corroboratedWindowLiveness(
                                accessibility: state.0, windowServer: state.1,
                                nativeIdentityComparable: nativeComparable,
                                nativeInventoryEmpty: false)
                        })
                })
        })
    return (result, samples, elapsed, identityReads)
}

@Test func closingWindowWaitsForBothInventoriesToProveAbsence() {
    let observed = closingWindowTestResult(states: [(.absent, .present), (.absent, .absent)])
    #expect(observed.samples == 2)
    #expect(observed.identityReads == 4)
    #expect(observed.elapsed == 0.05)
    #expect(observed.result["ok"] as? Bool == true)
    let snapshot = observed.result["snapshot"] as? [String: Any]
    #expect(snapshot?["windowAbsent"] as? Bool == true)
    #expect(snapshot?["closedWindowHandle"] as? String == macWindowHandle(pid: getpid(), windowID: 41))
}

@Test func closingWindowPersistentConflictAndUnknownStayBounded() {
    for states in [[(BoundWindowLiveness.absent, BoundWindowLiveness.present)],
                   [(.absent, .unknown)], [(.unknown, .absent)],
                   [(.present, .absent)], [(.present, .present)]] {
        let observed = closingWindowTestResult(states: states)
        #expect(observed.samples == 4)
        #expect(observed.identityReads == 2 * observed.samples)
        #expect(observed.elapsed == 0.2)
        #expect(observed.result["ok"] as? Bool == false)
        #expect(observed.result["failureCode"] as? String == "effect_unknown")
        #expect(observed.result["mutation_unknown"] as? Bool == true)
        #expect(observed.result["snapshot"] == nil)
    }
}

@Test func closingWindowDoesNotWeakenNativeIdentityOrContradictReplacement() {
    let unregistered = closingWindowTestResult(states: [(.absent, .absent)], nativeComparable: false)
    #expect(unregistered.result["ok"] as? Bool == false)
    #expect(unregistered.result["snapshot"] == nil)
    let reappeared = closingWindowTestResult(states: [(.absent, .present), (.present, .present)])
    #expect(reappeared.result["ok"] as? Bool == false)
    #expect(reappeared.result["snapshot"] == nil)
}

@Test func closingWindowPropagatesProcessAndProviderErrorsWithoutRetry() {
    for failure in [ComputerUseError.staleAppRef("process generation changed"),
                    .permissionDenied("permission revoked"), .providerResetRequired("provider timeout")] {
        var samples = 0
        var waits = 0
        do {
            try validateClosingWindow(timeout: 0.2, now: { 0 }, wait: { _ in waits += 1 }, validateWindow: {
                samples += 1
                throw failure
            })
            Issue.record("Process or provider error was hidden")
        } catch {
            #expect(String(describing: error) == String(describing: failure))
        }
        #expect(samples == 1)
        #expect(waits == 0)
    }
}

@Test func closingWindowCountsReadTimeAgainstMonotonicDeadline() throws {
    var now: TimeInterval = 100
    var reads = 0
    var waits: [TimeInterval] = []
    try validateClosingWindow(timeout: 0.2, now: { now }, wait: { waits.append($0); now += $0 }, validateWindow: {
        reads += 1
        now += 0.25
    })
    #expect(reads == 1)
    #expect(waits.isEmpty)
    let immediate = closingWindowTestResult(states: [(.absent, .absent)], timeout: 0)
    #expect(immediate.samples == 1)
    #expect(immediate.elapsed == 0)
    #expect(immediate.result["ok"] as? Bool == true)
}
