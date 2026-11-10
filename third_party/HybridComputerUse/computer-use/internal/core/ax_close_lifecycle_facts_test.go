package core

import "testing"

// A provider proves a window's retirement only with complete lifecycle facts.
// Windows reports a window it could not sample as unknown with incomplete
// facts; that must neither claim close nor be rejected as a malformed reply.
func TestCloseLifecycleNeedsCompleteFacts(t *testing.T) {
	context := &AXBatchContext{WindowRef: "w1"}
	for _, tc := range []struct {
		name          string
		status        string
		factsComplete bool
		want          bool
	}{
		{"gone with complete facts", "gone", true, true},
		{"gone with incomplete facts", "gone", false, false},
		{"present", "present", true, false},
		{"unknown", "unknown", false, false},
	} {
		result := ToolCallResult{IsError: true, StructuredContent: map[string]any{
			"failure_code":                "stale_window_ref",
			"source_window_status":        tc.status,
			"recovery_inventory_complete": true,
			"failure_facts_complete":      tc.factsComplete,
		}}
		if got := closedWindowLifecycleProven(context, result); got != tc.want {
			t.Errorf("%s: retirement proven=%v want %v", tc.name, got, tc.want)
		}
	}
}

func TestObserveRecoveryAcceptsExplicitlyUnknownWindowFacts(t *testing.T) {
	for _, tc := range []struct {
		name          string
		subject       string
		status        string
		factsComplete bool
		valid         bool
	}{
		{"complete gone", "window", "gone", true, true},
		{"complete present", "window", "present", true, true},
		{"unknown window", "window", "unknown", false, true},
		{"incomplete gone", "window", "gone", false, false},
		{"incomplete present", "window", "present", false, false},
		{"unknown without window subject", "app", "unknown", false, false},
	} {
		failure := &ProviderFailure{
			Code: "stale_window_ref", Phase: "precondition",
			FactsComplete: tc.factsComplete, Subject: tc.subject,
		}
		recovery := &ProviderRecoveryContext{
			SourceAppStatus: "alive", SourceWindowStatus: tc.status, InventoryComplete: true,
		}
		err := validateProviderRecoveryEnvelope(ProviderWireObserve, false, failure, nil, recovery)
		if (err == nil) != tc.valid {
			t.Errorf("%s: err=%v want valid=%v", tc.name, err, tc.valid)
		}
	}
}

// The Windows provider's own envelopes for a stale observe: the decoder
// accepts all three lifecycle states, and only a complete gone proves close.
func TestWindowsLifecycleEnvelopesDecodeAndProveOnlyGone(t *testing.T) {
	fixtures := nativeFailureFixtures(t)
	for _, state := range []string{"alive", "gone", "unknown"} {
		response, err := DecodeProviderWireResponse(fixtures["observe_lifecycle_"+state])
		if err != nil {
			t.Fatalf("%s: decoder rejected the production envelope: %v", state, err)
		}
		response.Accessibility.PrepareForService()
		result := NewServiceWithBackend(&recordingPlatformBackend{}).renderRuntimeReportedFailure(
			"Editor", AXServiceRequest{Operation: axServiceObserve}, *response.Accessibility, "window", nil, nil)
		proven := closedWindowLifecycleProven(&AXBatchContext{WindowRef: "w1"}, result)
		if proven != (state == "gone") {
			t.Fatalf("%s: retirement proven=%v facts=%#v", state, proven, result.StructuredContent)
		}
	}
}
