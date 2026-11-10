@testable import ComputerUseProviderKit
import Testing

@Test
func diagnosticPhaseTimingRequiresRequestContext() {
    #expect(
        diagnosticPhaseTimingEnabled(
            operation: ["diagnostics": ["phase_timing": true]]
        )
    )
    #expect(!diagnosticPhaseTimingEnabled(operation: [:]))
    #expect(
        !diagnosticPhaseTimingEnabled(
            operation: ["diagnostics": ["phase_timing": false]]
        )
    )
    #expect(
        !diagnosticPhaseTimingEnabled(
            operation: ["diagnostics": ["phase_timing": "true"]]
        )
    )
}
