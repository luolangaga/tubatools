@testable import ComputerUseProviderKit
import CoreFoundation
import Foundation
import Testing

private final class Recorder: @unchecked Sendable {
    private let lock = NSLock()
    private var values: [String] = []

    func append(_ value: String) {
        lock.withLock { values.append(value) }
    }

    var snapshot: [String] {
        lock.withLock { values }
    }
}

@Test
func accessibilityThreadReturnsTheJobValueFromOneDedicatedThread() async throws {
    let thread = AccessibilityThread(name: "test.accessibility.value")
    let first = try await thread.run {
        (Thread.isMainThread, thread.isCurrent, Thread.current.name ?? "")
    }
    let second = try await thread.run { Thread.current.name ?? "" }
    #expect(first.0 == false)
    #expect(first.1)
    #expect(first.2 == "test.accessibility.value")
    #expect(second == first.2)
    #expect(!thread.isCurrent)
}

@Test
func accessibilityThreadPropagatesJobErrors() async {
    let thread = AccessibilityThread(name: "test.accessibility.error")
    do {
        _ = try await thread.run { () throws -> Int in
            throw ComputerUseError.message("boom")
        }
        Issue.record("the failing job returned a value")
    } catch {
        #expect(String(describing: error).contains("boom"))
    }
}

@Test
func accessibilityThreadRunsJobsOneAtATimeEvenWhenAJobSpinsTheRunLoop() async throws {
    let thread = AccessibilityThread(name: "test.accessibility.serial")
    let recorder = Recorder()
    async let first: Void = thread.run {
        recorder.append("first-start")
        // A nested run-loop spin (as AXChangeWake and text input do) must not
        // start the next queued job inside this one.
        RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.15))
        recorder.append("first-end")
    }
    try await Task.sleep(for: .milliseconds(30))
    async let second: Void = thread.run { recorder.append("second") }
    _ = try await (first, second)
    #expect(recorder.snapshot == ["first-start", "first-end", "second"])
}

@Test
func accessibilityThreadRequestsResetWhenABoundedJobOverruns() async {
    let thread = AccessibilityThread(name: "test.accessibility.timeout")
    let started = Date()
    do {
        _ = try await thread.run(timeout: 0.05) {
            Thread.sleep(forTimeInterval: 0.5)
            return "late"
        }
        Issue.record("the overrunning job unexpectedly completed")
    } catch {
        #expect(requiresProviderReset(error))
        #expect(Date().timeIntervalSince(started) < 0.3)
    }
}

@Test
func accessibilityThreadDeliversRunLoopSourcesWhileIdle() async throws {
    // An AXObserver delivers notifications through a run-loop source on the
    // thread that added it. Prove that a source added from a job fires on the
    // accessibility thread after the job has returned and the thread is idle.
    let thread = AccessibilityThread(name: "test.accessibility.source")
    let firedOnThread = Recorder()
    final class Callback: @unchecked Sendable {
        let action: () -> Void
        init(_ action: @escaping () -> Void) { self.action = action }
    }
    let callback = Callback {
        firedOnThread.append(thread.isCurrent ? "accessibility" : "other")
    }
    let (source, runLoop) = try await thread.run {
        var context = CFRunLoopSourceContext()
        context.info = Unmanaged.passUnretained(callback).toOpaque()
        context.perform = { info in
            Unmanaged<Callback>.fromOpaque(info!).takeUnretainedValue().action()
        }
        let source = CFRunLoopSourceCreate(nil, 0, &context)!
        CFRunLoopAddSource(CFRunLoopGetCurrent(), source, .defaultMode)
        return (source, CFRunLoopGetCurrent()!)
    }
    CFRunLoopSourceSignal(source)
    CFRunLoopWakeUp(runLoop)
    let deadline = Date(timeIntervalSinceNow: 2)
    while firedOnThread.snapshot.isEmpty, Date() < deadline {
        try await Task.sleep(for: .milliseconds(5))
    }
    #expect(firedOnThread.snapshot == ["accessibility"])
    try await thread.run {
        CFRunLoopRemoveSource(CFRunLoopGetCurrent(), source, .defaultMode)
    }
    withExtendedLifetime(callback) {}
}

@Test
func onlyReadOnlyProviderToolsUseTheInnerRuntimeBound() {
    for tool in [
        "list_apps",
        "get_app_state",
        "list_app_windows",
        "resolve_target",
    ] {
        #expect(providerReadOnlyToolNeedsRuntimeBound(tool))
    }
    for tool in ["click", "scroll", "type", "keypress", "set_value", "gui"] {
        #expect(!providerReadOnlyToolNeedsRuntimeBound(tool))
    }
}
