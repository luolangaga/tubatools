package core

import "testing"

func TestRuntimeVerificationFlagsKeepUnreportedChecksAndRequireReadback(t *testing.T) {
	verified := true
	evidence := initialActionabilityEvidence("click", nil, nil)
	evidence.Enabled = TriTrue
	copyRuntimeVerificationFlags(&evidence, &RuntimeActionEvidence{
		ReadbackSampled:  false,
		ReadbackVerified: &verified,
	})
	if evidence.Enabled != TriTrue {
		t.Fatalf("unreported enabled flag overwrote prior evidence: %#v", evidence)
	}
	if evidence.DeliveryVerified != TriUnknown {
		t.Fatalf("delivery claimed without a sampled readback: %#v", evidence)
	}
	copyRuntimeVerificationFlags(&evidence, &RuntimeActionEvidence{
		ReadbackSampled:  true,
		ReadbackVerified: &verified,
	})
	if evidence.DeliveryVerified != TriTrue {
		t.Fatalf("sampled readback was not recorded: %#v", evidence)
	}
}

func TestFailedActionReportsResolveFailureWithoutLaterStages(t *testing.T) {
	evidence := initialActionabilityEvidence("type", nil, nil)
	final := finalizeActionabilityEvidence(
		evidence,
		AXServiceRequest{Action: axTypeCommand{}},
		ToolCallResult{StructuredContent: map[string]any{
			"failure_code":         "semantic_target_ambiguous",
			"input_events_emitted": false,
		}},
		false,
		nil,
	)
	if final.Resolution != "failed" || final.InputEmitted != TriFalse {
		t.Fatalf("failure evidence = %#v", final)
	}
	statuses := map[ActionabilityStage]string{}
	for _, stage := range final.Stages {
		statuses[stage.Stage] = stage.Status
	}
	if statuses[ActionResolve] != "failed" ||
		statuses[ActionEmit] != "not_executed" {
		t.Fatalf("failure stages = %#v", final.Stages)
	}
	for _, stage := range []ActionabilityStage{
		ActionVerifyFocus, ActionReadBack, ActionWaitStable, ActionPostcondition,
	} {
		if _, present := statuses[stage]; present {
			t.Fatalf("failed action reported unreached stage %s: %#v", stage, final.Stages)
		}
	}
}
