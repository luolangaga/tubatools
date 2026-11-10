@testable import ComputerUseProviderKit
import Testing

@Test(arguments: 0..<8)
func permissionDiagnosticsPreserveEachRuntimeResult(
    grantedMask: Int
) {
    let accessibility = grantedMask & 1 != 0
    let eventPosting = grantedMask & 2 != 0
    let screenCapture = grantedMask & 4 != 0
    var probes: [String] = []
    let diagnostics = PermissionDiagnostics.current(
        accessibility: {
            probes.append("accessibility")
            return accessibility
        },
        eventPosting: {
            probes.append("eventPosting")
            return eventPosting
        },
        screenCapture: {
            probes.append("screenCapture")
            return screenCapture
        }
    )

    #expect(diagnostics.accessibilityTrusted == accessibility)
    #expect(diagnostics.eventPostingGranted == eventPosting)
    #expect(diagnostics.screenCaptureGranted == screenCapture)
    #expect(probes == ["accessibility", "eventPosting", "screenCapture"])
}

@Test
func permissionDiagnosticsDoNotRetainAnEarlierGrant() {
    var runtimeGranted = true
    let probe = { runtimeGranted }
    let granted = PermissionDiagnostics.current(
        accessibility: probe,
        eventPosting: probe,
        screenCapture: probe
    )
    #expect(granted.accessibilityTrusted && granted.eventPostingGranted && granted.screenCaptureGranted)

    runtimeGranted = false
    let revoked = PermissionDiagnostics.current(
        accessibility: probe,
        eventPosting: probe,
        screenCapture: probe
    )
    #expect(!revoked.accessibilityTrusted && !revoked.eventPostingGranted && !revoked.screenCaptureGranted)

    runtimeGranted = true
    let restored = PermissionDiagnostics.current(
        accessibility: probe,
        eventPosting: probe,
        screenCapture: probe
    )
    #expect(restored.accessibilityTrusted && restored.eventPostingGranted && restored.screenCaptureGranted)
}
