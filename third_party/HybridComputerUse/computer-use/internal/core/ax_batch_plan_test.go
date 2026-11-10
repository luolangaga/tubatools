package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func testAXBatchPlanContext() *AXBatchContext {
	return &AXBatchContext{
		WindowRef:    "w1",
		WindowHandle: "private-window-1",
	}
}

func TestAXBatchPlanGroupsOnlyUntargetedKeypresses(t *testing.T) {
	plan, err := compileAXBatchExecutionPlan(
		testAXBatchPlanContext(),
		[]axBatchAction{
			{actionType: "keypress", key: "DOWN"},
			{actionType: "keypress", key: "ENTER"},
			{actionType: "keypress", key: "A"},
		},
		observation{mode: contract.ObserveDiff},
	)
	if err != nil {
		t.Fatal(err)
	}
	if plan.initialConsistency != axPlanCanonicalWindow ||
		!plan.requiresMutationLease ||
		plan.finalObservation != contract.ObserveDiff {
		t.Fatalf("unexpected plan header: %#v", plan)
	}
	for index, step := range plan.steps {
		if step.executionClass != axPlanOrderedInput ||
			step.preConsistency != axPlanWindowIdentity ||
			step.orderedInputRun != 1 {
			t.Fatalf("step %d was not in one ordered input run: %#v", index, step)
		}
	}
	if plan.steps[0].boundary != axPlanNoBoundary ||
		plan.steps[1].boundary != axPlanNoBoundary ||
		plan.steps[2].boundary != axPlanTransactionBoundary ||
		plan.steps[2].postConsistency != axPlanCanonicalWindow {
		t.Fatalf("ordered run has incorrect boundaries: %#v", plan.steps)
	}
}

func TestAXBatchPlanSeparatesRunsAtCanonicalActions(t *testing.T) {
	plan, err := compileAXBatchExecutionPlan(
		testAXBatchPlanContext(),
		[]axBatchAction{
			{actionType: "click", targetRef: "r1"},
			{actionType: "keypress", key: "DOWN"},
			{actionType: "keypress", key: "ENTER"},
			{actionType: "set_value", targetRef: "r2", value: "6"},
			{actionType: "keypress", key: "TAB"},
		},
		observation{mode: contract.ObserveNone},
	)
	if err != nil {
		t.Fatal(err)
	}
	wantClasses := []axPlanExecutionClass{
		axPlanSingleAction,
		axPlanOrderedInput,
		axPlanOrderedInput,
		axPlanSingleAction,
		axPlanOrderedInput,
	}
	for index, want := range wantClasses {
		if plan.steps[index].executionClass != want {
			t.Fatalf("step %d class=%q, want %q", index,
				plan.steps[index].executionClass, want)
		}
	}
	if plan.steps[0].boundary != axPlanTargetedBoundary ||
		plan.steps[2].boundary != axPlanNextActionBoundary ||
		plan.steps[3].boundary != axPlanTargetedBoundary ||
		plan.steps[1].orderedInputRun != 1 ||
		plan.steps[2].orderedInputRun != 1 ||
		plan.steps[4].orderedInputRun != 2 {
		t.Fatalf("canonical actions did not split ordered runs: %#v", plan.steps)
	}
}

func TestAXBatchPlanKeepsEffectSensitiveActionsOnFullPath(t *testing.T) {
	actions := []axBatchAction{
		{actionType: "keypress", targetRef: "r1", key: "ENTER"},
		{actionType: "wait", wait: &axWait{window: "Done"}},
		{actionType: "type", text: "literal"},
		{actionType: "scroll", direction: "down"},
		{actionType: "activate", targetRef: "w1"},
	}
	plan, err := compileAXBatchExecutionPlan(
		testAXBatchPlanContext(),
		actions,
		observation{mode: contract.ObserveScreenshot},
	)
	if err != nil {
		t.Fatal(err)
	}
	for index, step := range plan.steps {
		if step.executionClass != axPlanSingleAction ||
			step.preConsistency != axPlanCanonicalWindow {
			t.Fatalf("effect-sensitive step %d entered ordered path: %#v",
				index, step)
		}
	}
	if plan.steps[0].target != axPlanElementTarget ||
		plan.steps[0].boundary != axPlanTargetedBoundary ||
		plan.steps[1].boundary != axPlanWaitBoundary ||
		plan.steps[2].boundary != axPlanCanonicalBoundary ||
		plan.steps[3].boundary != axPlanCanonicalBoundary ||
		plan.steps[4].target != axPlanWindowTarget ||
		plan.steps[4].boundary != axPlanWindowBoundary {
		t.Fatalf("effect-sensitive boundaries are wrong: %#v", plan.steps)
	}
}

func TestAXBatchPlanEndsOrderedRunAtAWait(t *testing.T) {
	plan, err := compileAXBatchExecutionPlan(
		testAXBatchPlanContext(),
		[]axBatchAction{
			{actionType: "keypress", key: "ENTER"},
			{actionType: "wait", wait: &axWait{window: "Done"}},
		},
		observation{mode: contract.ObserveDiff},
	)
	if err != nil {
		t.Fatal(err)
	}
	if plan.steps[0].boundary != axPlanNextActionBoundary ||
		plan.steps[0].postConsistency != axPlanCanonicalWindow ||
		plan.steps[1].executionClass != axPlanSingleAction {
		t.Fatalf("a wait did not close the ordered run: %#v", plan)
	}
}

func TestAXBatchPlanRejectsUnboundOrMalformedInput(t *testing.T) {
	tests := []struct {
		name    string
		context *AXBatchContext
		actions []axBatchAction
		want    string
	}{
		{
			name:    "nil context",
			context: nil,
			actions: []axBatchAction{{actionType: "keypress", key: "A"}},
			want:    "one bound logical window",
		},
		{
			name:    "empty actions",
			context: testAXBatchPlanContext(),
			want:    "at least one action",
		},
		{
			name:    "malformed target",
			context: testAXBatchPlanContext(),
			actions: []axBatchAction{{
				actionType: "click",
				targetRef:  `button "Save"`,
			}},
			want: "current rN or wN ref",
		},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			_, err := compileAXBatchExecutionPlan(
				test.context,
				test.actions,
				observation{},
			)
			if err == nil || !strings.Contains(err.Error(), test.want) {
				t.Fatalf("error=%v, want substring %q", err, test.want)
			}
		})
	}
}
