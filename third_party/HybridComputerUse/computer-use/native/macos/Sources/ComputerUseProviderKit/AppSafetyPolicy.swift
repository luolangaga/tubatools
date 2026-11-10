import AppKit
import CoreServices
import Foundation

enum AppSafetyPolicy {
    private static let blockedBundleIdentifiers: Set<String> = [
        "com.1password.1password",
        "com.1password.safari",
        "com.bitwarden.desktop",
        "com.dashlane.dashlanephonefinal",
        "com.lastpass.LastPass",
        "com.nordsec.nordpass",
        "me.proton.pass.electron",
        "me.proton.pass.catalyst",
    ]

    static func isBlocked(bundleIdentifier: String?) -> Bool {
        guard let bundleIdentifier else {
            return false
        }

        return blockedBundleIdentifiers.contains(bundleIdentifier)
    }

    static func permissionDenied(bundleIdentifier: String) -> ComputerUseError {
        .permissionDenied("Computer Use is not allowed to use the app '\(bundleIdentifier)' for safety reasons.")
    }
}
