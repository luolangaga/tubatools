package core

type ProviderDiagnosticContext struct {
	PhaseTiming bool `json:"phase_timing,omitempty"`
}

func ProviderDiagnosticContextFromLookup(lookup func(string) string) *ProviderDiagnosticContext {
	if lookup == nil || lookup("COMPUTER_USE_DIAGNOSTIC_TIMING") != "1" {
		return nil
	}
	return &ProviderDiagnosticContext{PhaseTiming: true}
}

func ProviderTargetDiagnosticEnabled(lookup func(string) string) bool {
	return lookup != nil && lookup("COMPUTER_USE_DIAGNOSTIC_TARGET_RESOLUTION") == "1"
}
