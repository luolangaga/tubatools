package core

import (
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestActionPostconditionReconciliationSamplesAfterPolling(t *testing.T) {
	service, appRef, windowRef, _ := sheetBatchService(t)
	handle := "w2:0123456789abcdef0123456789abcdef"
	snapshot := &AppSnapshot{
		App: AppDescriptor{
			Name:              "LibreOffice",
			PID:               10,
			ProviderBusName:   ":1.84",
			ProcessGeneration: "pid:10:start:100",
		},
		WindowTitle:  "Sheet",
		WindowHandle: handle,
		SnapshotID:   "c2",
		Elements: []ElementRecord{
			{
				Index:                           0,
				RuntimeID:                       []int{0},
				ControlType:                     "frame",
				Name:                            "Sheet",
				States:                          "[active showing visible]",
				ProviderBusName:                 ":1.84",
				ProviderObjectPath:              "/window/1",
				ProcessGeneration:               "pid:10:start:100",
				ProviderWindowBusName:           ":1.84",
				ProviderWindowObjectPath:        "/window/1",
				ProviderWindowProcessGeneration: "pid:10:start:100",
				ProviderWindowRole:              "frame",
				ProviderWindowName:              "Sheet",
			},
			{
				Index:                           1,
				RuntimeID:                       []int{0, 0},
				ControlType:                     "button",
				Name:                            "Save",
				States:                          "[enabled showing visible]",
				Actions:                         []string{"click"},
				ProviderBusName:                 ":1.84",
				ProviderObjectPath:              "/window/1/save",
				ProcessGeneration:               "pid:10:start:100",
				ProviderWindowBusName:           ":1.84",
				ProviderWindowObjectPath:        "/window/1",
				ProviderWindowProcessGeneration: "pid:10:start:100",
				ProviderWindowRole:              "frame",
				ProviderWindowName:              "Sheet",
			},
		},
		Scope: contract.AXScopeWindow,
	}
	backend := &recordingPlatformBackend{
		accessibilityResponse: &ProviderAccessibilityResult{
			OK:       true,
			Snapshot: snapshot,
		},
	}
	service.Backend = backend
	noScreenshot := false
	initial, _, initialResult := service.refreshSnapshot(
		appRef,
		AXServiceRequest{
			Operation:          axServiceObserve,
			App:                appRef,
			IncludeScreenshot:  &noScreenshot,
			TargetWindowHandle: handle,
			SourceRef:          windowRef,
			Scope:              contract.AXScopeWindow,
			TreeView:           contract.TreeViewWorking,
		},
	)
	if initialResult.IsError || initial == nil ||
		initial.AXSnapshot == nil ||
		len(initial.AXSnapshot.CanonicalRoots) != 1 ||
		len(initial.AXSnapshot.CanonicalRoots[0].Children) != 1 {
		t.Fatalf("initial canonical observation failed: %#v snapshot=%#v",
			initialResult, initial)
	}
	elementRef := initial.AXSnapshot.CanonicalRoots[0].Children[0].Ref
	if elementRef == "" {
		t.Fatal("initial canonical observation did not issue an element ref")
	}
	snapshot.Elements[1].States = "[checked enabled showing visible]"
	backend.observeRequests = nil
	incomplete := axAssertionResult(
		"assert_ax observation_incomplete: live AX target incomplete",
		true,
		"observation_incomplete",
		1,
		0,
	)
	reconciled := service.reconcileAXActionPostcondition(
		&AXBatchContext{
			AppRef:       appRef,
			AppToken:     appRef,
			WindowRef:    windowRef,
			WindowHandle: handle,
		},
		axRefPredicate{ref: elementRef, goal: verifyGoal{state: "checked"}},
		incomplete,
	)
	if reconciled.IsError ||
		reconciled.StructuredContent["assertion_status"] != "pass" ||
		reconciled.StructuredContent["postcondition_reconciled_by_canonical_observation"] != true ||
		reconciled.StructuredContent["postcondition_recovered_by_final_observation"] == true {
		t.Fatalf("action guard did not reconcile from a fresh post-poll snapshot: %#v",
			reconciled)
	}
	if len(backend.observeRequests) != 1 {
		t.Fatalf("canonical reconciliation observe calls=%d, want 1",
			len(backend.observeRequests))
	}
}

func TestPostconditionFailureClassificationPreservesUnderlyingCause(t *testing.T) {
	predicateFalse := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"assertion_status":          "predicate_false",
			"assertion_sampled_live_ui": true,
		},
	}
	classification, underlying := classifyAXPostconditionFailure(
		predicateFalse,
	)
	if classification != "postcondition_not_satisfied" ||
		underlying != "predicate_false" {
		t.Fatalf(
			"predicate failure classification = %q, %q",
			classification,
			underlying,
		)
	}

	incomplete := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"failure_code":              "observation_incomplete",
			"assertion_sampled_live_ui": false,
		},
	}
	classification, underlying = classifyAXPostconditionFailure(incomplete)
	if classification != "postcondition_unknown" ||
		underlying != "observation_incomplete" {
		t.Fatalf(
			"infrastructure failure classification = %q, %q",
			classification,
			underlying,
		)
	}
}
