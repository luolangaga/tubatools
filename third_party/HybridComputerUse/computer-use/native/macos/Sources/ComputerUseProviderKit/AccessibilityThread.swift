import CoreFoundation
import Foundation

/// Runs every accessibility read, accessibility write, and input-synthesis job
/// on one long-lived thread, one job at a time.
///
/// The provider's AX and input code is synchronous: it sleeps between input
/// events and spins the run loop while it waits for an app to react. Running
/// that code on the Swift concurrency pool would block pool threads. This
/// thread owns those waits instead, and callers only suspend.
///
/// Between jobs the thread runs its run loop in the default mode. An
/// `AXObserver` whose run-loop source a job adds to `CFRunLoopGetCurrent()`
/// therefore keeps delivering notifications after the job returns. Only the
/// outer loop starts jobs, so a job that spins the run loop itself never runs
/// the next job inside its own wait.
final class AccessibilityThread: @unchecked Sendable {
    static let shared = AccessibilityThread(name: "computer-use.accessibility")

    private let lock = NSLock()
    private var jobs: [() -> Void] = []
    private var runLoop: CFRunLoop?
    private var wakeSource: CFRunLoopSource?
    private var thread: Thread!

    init(name: String) {
        // The thread never exits and keeps its owner alive for the process.
        let thread = Thread { self.main() }
        thread.name = name
        thread.qualityOfService = .userInitiated
        // AX tree walks recurse once per element depth; give them the same
        // stack size the main thread has.
        thread.stackSize = 8 << 20
        self.thread = thread
        thread.start()
    }

    var isCurrent: Bool {
        Thread.current === thread
    }

    /// Runs `body` on the accessibility thread and suspends the caller until
    /// it returns. With a `timeout`, the caller gets a provider-reset error
    /// once the timeout passes; the job itself cannot be interrupted, so the
    /// provider must restart before it can serve another request.
    func run<T>(
        timeout: TimeInterval? = nil,
        _ body: @escaping () throws -> T
    ) async throws -> T {
        let job = Job(body)
        let box = try await withCheckedThrowingContinuation {
            (continuation: CheckedContinuation<JobResultBox<T>, Error>) in
            job.attach(continuation)
            enqueue { job.execute() }
            if let timeout {
                DispatchQueue.global(qos: .userInitiated).asyncAfter(
                    deadline: .now() + timeout
                ) {
                    job.finish(.failure(ComputerUseError.providerResetRequired(
                        "macOS accessibility request timed out after \(timeout) seconds. The native provider must restart before another request."
                    )))
                }
            }
        }
        return try box.result.get()
    }

    private func enqueue(_ job: @escaping () -> Void) {
        let (source, loop) = lock.withLock {
            jobs.append(job)
            return (wakeSource, runLoop)
        }
        // Before the thread has published its run loop it drains the queue
        // without waiting, so a job enqueued that early is not lost.
        if let source, let loop {
            CFRunLoopSourceSignal(source)
            CFRunLoopWakeUp(loop)
        }
    }

    private func nextJob() -> (() -> Void)? {
        lock.withLock {
            jobs.isEmpty ? nil : jobs.removeFirst()
        }
    }

    private func main() {
        // The wake source only ends the outer loop's wait. A nested run-loop
        // spin inside a job may consume the signal; the outer loop checks the
        // queue again before it waits, so no job is missed.
        var context = CFRunLoopSourceContext()
        context.perform = { _ in }
        let source = CFRunLoopSourceCreate(nil, 0, &context)!
        let loop = CFRunLoopGetCurrent()!
        CFRunLoopAddSource(loop, source, .defaultMode)
        lock.withLock {
            wakeSource = source
            runLoop = loop
        }
        while true {
            if let job = nextJob() {
                autoreleasepool { job() }
                continue
            }
            _ = CFRunLoopRunInMode(.defaultMode, 60, true)
        }
    }
}

private final class JobResultBox<T>: @unchecked Sendable {
    let result: Result<T, Error>

    init(_ result: Result<T, Error>) {
        self.result = result
    }
}

private final class Job<T>: @unchecked Sendable {
    private let lock = NSLock()
    private let body: () throws -> T
    private var continuation: CheckedContinuation<JobResultBox<T>, Error>?

    init(_ body: @escaping () throws -> T) {
        self.body = body
    }

    func attach(_ continuation: CheckedContinuation<JobResultBox<T>, Error>) {
        lock.withLock { self.continuation = continuation }
    }

    func execute() {
        finish(Result { try body() })
    }

    /// Resumes the caller with the first result: the job's own result or the
    /// timeout, whichever comes first.
    func finish(_ result: Result<T, Error>) {
        let continuation = lock.withLock {
            let pending = self.continuation
            self.continuation = nil
            return pending
        }
        switch result {
        case .success:
            continuation?.resume(returning: JobResultBox(result))
        case .failure(let error):
            continuation?.resume(throwing: error)
        }
    }
}
