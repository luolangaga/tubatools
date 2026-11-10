import ApplicationServices
import AppKit
import CoreGraphics
import Foundation

public struct PermissionDiagnostics: Sendable {
    public let accessibilityTrusted: Bool
    public let eventPostingGranted: Bool
    public let screenCaptureGranted: Bool

    public static func current() -> PermissionDiagnostics {
        current(
            accessibility: AXIsProcessTrusted,
            eventPosting: CGPreflightPostEventAccess,
            screenCapture: CGPreflightScreenCaptureAccess
        )
    }

    static func current(
        accessibility: () -> Bool,
        eventPosting: () -> Bool,
        screenCapture: () -> Bool
    ) -> PermissionDiagnostics {
        // TCC rows can outlive the executable's signing identity. Only the
        // non-prompting runtime checks establish this process's permissions.
        PermissionDiagnostics(
            accessibilityTrusted: accessibility(),
            eventPostingGranted: eventPosting(),
            screenCaptureGranted: screenCapture()
        )
    }
}

public enum PermissionSupport {
    public static let bundleDisplayName = "Computer Use Provider"
}

