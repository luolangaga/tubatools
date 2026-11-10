import AppKit
import Darwin
import Foundation

public struct RunningAppDescriptor: Sendable {
    public let name: String
    public let bundleIdentifier: String?
    public let pid: pid_t
    public let runningApplication: NSRunningApplication
}

enum AppDiscovery {
    static let internalProviderBundleIdentifiers: Set<String> = [
        "org.computer-use.provider.v2",
    ]

    static func runningApps(
        readWorkspace: @MainActor () -> [NSRunningApplication] = {
            NSWorkspace.shared.runningApplications
        }
    ) -> [RunningAppDescriptor] {
        // A worker can retain an older workspace snapshot across app launches
        // and exits. Read and materialize each inventory on the main run loop;
        // only Sendable descriptors leave the main actor.
        onMainActor {
            discoverableRunningApps(readWorkspace())
        }
    }

    /// Runs `body` on the main actor and blocks the caller until it returns.
    /// The main thread must not wait on a caller of this function.
    static func onMainActor<T: Sendable>(_ body: @MainActor () -> T) -> T {
        if Thread.isMainThread {
            return MainActor.assumeIsolated(body)
        }
        return DispatchQueue.main.sync {
            MainActor.assumeIsolated(body)
        }
    }

    private static func discoverableRunningApps(
        _ workspaceApps: [NSRunningApplication]
    ) -> [RunningAppDescriptor] {
        workspaceApps
            .filter {
                isDiscoverableRunningApplication(
                    isTerminated: $0.isTerminated,
                    bundleIdentifier: $0.bundleIdentifier
                ) && !processIsKnownMissing($0.processIdentifier)
            }
            .sorted { lhs, rhs in
                if lhs.isActive != rhs.isActive {
                    return lhs.isActive && !rhs.isActive
                }

                return appName(lhs).localizedCaseInsensitiveCompare(appName(rhs)) == .orderedAscending
            }
            .map { app in
                RunningAppDescriptor(
                    name: appName(app),
                    bundleIdentifier: app.bundleIdentifier,
                    pid: app.processIdentifier,
                    runningApplication: app
                )
            }
    }

    static func resolve(_ query: String) throws -> RunningAppDescriptor {
        let normalizedQuery = query.trimmingCharacters(in: .whitespacesAndNewlines)
        let running = runningApps()

        if let bundleIdentifier = blockedBundleIdentifier(forQuery: normalizedQuery) {
            throw AppSafetyPolicy.permissionDenied(bundleIdentifier: bundleIdentifier)
        }

        if let match = resolvedRunningApp(in: running, matching: normalizedQuery) {
            return match
        }

        // Observation and actions are read-only towards the application
        // list: the provider never launches an application.
        throw ComputerUseError.appNotFound(normalizedQuery)
    }

    private static func resolvedRunningApp(in descriptors: [RunningAppDescriptor], matching query: String) -> RunningAppDescriptor? {
        if isBundleIdentifierQuery(query) {
            return descriptors.first(where: { descriptor in
                descriptor.bundleIdentifier?.caseInsensitiveCompare(query) == .orderedSame
            })
        }

        return descriptors.first(where: { descriptor in
            guard !AppSafetyPolicy.isBlocked(bundleIdentifier: descriptor.bundleIdentifier) else {
                return false
            }

            return descriptor.name.caseInsensitiveCompare(query) == .orderedSame
                || descriptor.runningApplication.executableURL?.deletingPathExtension().lastPathComponent.caseInsensitiveCompare(query) == .orderedSame
        })
    }

    static func isDiscoverableBundleIdentifier(_ bundleIdentifier: String) -> Bool {
        !internalProviderBundleIdentifiers.contains(bundleIdentifier.lowercased())
    }

    static func isDiscoverableRunningApplication(
        isTerminated: Bool,
        bundleIdentifier: String?
    ) -> Bool {
        !isTerminated && bundleIdentifier.map(isDiscoverableBundleIdentifier) != false
    }

    static func processIsKnownMissing(
        _ pid: pid_t,
        probe: (pid_t) -> (result: Int32, error: Int32) = { pid in
            let result = Darwin.kill(pid, 0)
            return (result, errno)
        }
    ) -> Bool {
        // NSWorkspace can retain an exited process until its run loop updates.
        // Only ESRCH proves absence; denied or unavailable probes remain unknown.
        guard pid > 0 else { return false }
        let result = probe(pid)
        return result.result != 0 && result.error == ESRCH
    }

    private static func blockedBundleIdentifier(forQuery query: String) -> String? {
        guard isBundleIdentifierQuery(query), AppSafetyPolicy.isBlocked(bundleIdentifier: query) else {
            return nil
        }

        return query
    }

    private static func isBundleIdentifierQuery(_ query: String) -> Bool {
        query.contains(".")
    }

    private static func bundleDisplayName(_ bundle: Bundle?) -> String? {
        guard let bundle else {
            return nil
        }

        let displayName = bundle.object(forInfoDictionaryKey: "CFBundleDisplayName") as? String
        let bundleName = bundle.object(forInfoDictionaryKey: kCFBundleNameKey as String) as? String
        return displayName ?? bundleName
    }

    static func appName(_ app: NSRunningApplication) -> String {
        app.localizedName
            ?? bundleDisplayName(Bundle(url: app.bundleURL ?? URL(fileURLWithPath: "/")))
            ?? app.bundleURL?.deletingPathExtension().lastPathComponent
            ?? app.executableURL?.lastPathComponent
            ?? "pid-\(app.processIdentifier)"
    }
}
