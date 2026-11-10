import ApplicationServices
import Foundation

final class AXChangeWake {
    private let signal = DispatchSemaphore(value: 0)
    private let stateLock = NSLock()
    private var observer: AXObserver?
    private let appElement: AXUIElement
    private let targetElement: AXUIElement?
    private var registrations: [(AXUIElement, CFString)] = []
    private var observedTargetDestruction = false
    private var observedTargetMutation = false

    init?(pid: pid_t, targetElement: AXUIElement? = nil) {
        appElement = AXUIElementCreateApplication(pid)
        self.targetElement = targetElement
        var created: AXObserver?
        let result = AXObserverCreate(
            pid,
            { _, element, notification, refcon in
                guard let refcon else {
                    return
                }
                let wake = Unmanaged<AXChangeWake>
                    .fromOpaque(refcon)
                    .takeUnretainedValue()
                if notification as String ==
                    kAXUIElementDestroyedNotification as String,
                   let target = wake.targetElement,
                   CFEqual(element, target) {
                    wake.stateLock.lock()
                    wake.observedTargetDestruction = true
                    wake.stateLock.unlock()
                }
                let notificationName = notification as String
                let isTargetMutation =
                    notificationName ==
                        kAXValueChangedNotification as String ||
                    notificationName ==
                        kAXSelectedTextChangedNotification as String
                if let target = wake.targetElement,
                   CFEqual(element, target),
                   isTargetMutation {
                    wake.stateLock.lock()
                    wake.observedTargetMutation = true
                    wake.stateLock.unlock()
                }
                wake.signal.signal()
            },
            &created
        )
        guard result == .success, let created else {
            return nil
        }
        observer = created
        let refcon = Unmanaged.passUnretained(self).toOpaque()
        let requested: [CFString] = [
            kAXValueChangedNotification as CFString,
            kAXFocusedUIElementChangedNotification as CFString,
            kAXWindowCreatedNotification as CFString,
            kAXUIElementDestroyedNotification as CFString,
            kAXSelectedTextChangedNotification as CFString,
        ]
        for notification in requested {
            if AXObserverAddNotification(
                created,
                appElement,
                notification,
                refcon
            ) == .success {
                registrations.append((appElement, notification))
            }
        }
        if let targetElement,
           AXObserverAddNotification(
               created,
               targetElement,
               kAXUIElementDestroyedNotification as CFString,
               refcon
           ) == .success {
            registrations.append(
                (targetElement, kAXUIElementDestroyedNotification as CFString)
            )
        }
        if let targetElement {
            for notification in [
                kAXValueChangedNotification as CFString,
                kAXSelectedTextChangedNotification as CFString,
            ] {
                if AXObserverAddNotification(
                    created,
                    targetElement,
                    notification,
                    refcon
                ) == .success {
                    registrations.append((targetElement, notification))
                }
            }
        }
        CFRunLoopAddSource(
            CFRunLoopGetCurrent(),
            AXObserverGetRunLoopSource(created),
            .defaultMode
        )
    }

    var targetWasDestroyed: Bool {
        stateLock.lock()
        defer { stateLock.unlock() }
        return observedTargetDestruction
    }

    var targetDidMutate: Bool {
        stateLock.lock()
        defer { stateLock.unlock() }
        return observedTargetMutation
    }

    func waitForTargetDestruction(timeout: TimeInterval) -> Bool {
        let deadline = Date(timeIntervalSinceNow: timeout)
        while !targetWasDestroyed {
            if Date() >= deadline {
                return false
            }
            _ = signal.wait(timeout: .now() + 0.01)
            RunLoop.current.run(
                mode: .default,
                before: Date(timeIntervalSinceNow: 0.01)
            )
        }
        return true
    }

    func wait(timeout: TimeInterval) -> Bool {
        let deadline = Date(timeIntervalSinceNow: timeout)
        while signal.wait(timeout: .now()) == .timedOut {
            if Date() >= deadline {
                return false
            }
            RunLoop.current.run(
                mode: .default,
                before: Date(timeIntervalSinceNow: 0.01)
            )
        }
        return true
    }

    deinit {
        guard let observer else {
            return
        }
        for (element, notification) in registrations {
            AXObserverRemoveNotification(
                observer,
                element,
                notification
            )
        }
        CFRunLoopRemoveSource(
            CFRunLoopGetCurrent(),
            AXObserverGetRunLoopSource(observer),
            .defaultMode
        )
    }
}
