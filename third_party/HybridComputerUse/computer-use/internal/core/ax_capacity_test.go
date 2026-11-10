package core

import (
	"testing"
)

func TestAXCapacityAndWaitBudget(t *testing.T) {
	for _, tc := range []struct {
		name      string
		count     int
		wait      bool
		timeout   int
		wantError bool
	}{
		{"64 inputs", 64, false, 0, false},
		{"65 inputs", 65, false, 0, true},
		{"default waits at budget", 12, true, 0, false},
		{"default waits over budget", 13, true, 0, true},
		{"explicit waits at budget", 2, true, 30000, false},
		{"explicit waits over budget", 2, true, 30001, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			steps := make([]any, tc.count)
			for i := range steps {
				step := map[string]any{"type": "keypress", "keys": []any{"TAB"}}
				if tc.wait {
					step = map[string]any{"type": "wait", "window": "Document"}
					if tc.timeout > 0 {
						step["timeout_ms"] = tc.timeout
					}
				}
				steps[i] = step
			}
			_, err := decodeAXActions(steps)
			if (err != nil) != tc.wantError {
				t.Fatalf("error = %v, wantError %v", err, tc.wantError)
			}
		})
	}
}

func TestAXWaitBudgetRejectsBeforeEarlierInput(t *testing.T) {
	d := newRecoveryDesktop()
	s := NewServiceWithBackend(d)
	args := decodeTestArguments(t, `{"mode":"act","actions":[{"type":"keypress","keys":["TAB"]},{"type":"wait","window":"Document","timeout_ms":30000},{"type":"wait","window":"Document","timeout_ms":30001}]}`)
	r := s.dispatch("AX", args)
	if !r.IsError {
		t.Fatal("over-budget call accepted")
	}
	if len(d.actionRequests) != 0 {
		t.Fatalf("sent input before rejecting wait budget: %v", d.actionRequests)
	}
}

func TestAXCapacityExecutesAll64StepsThroughPublicCall(t *testing.T) {
	d := newRecoveryDesktop()
	s := NewServiceWithBackend(d)
	steps := make([]any, 64)
	for i := range steps {
		steps[i] = map[string]any{"type": "keypress", "keys": []any{"TAB"}}
	}
	r := s.callTool("AX", map[string]any{"mode": "act", "observe": "none", "actions": steps})
	if r.IsError || len(d.actionRequests) != 64 {
		t.Fatalf("64-step delivery failed: result=%+v emitted=%d", r, len(d.actionRequests))
	}
	for i, request := range d.actionRequests {
		if request.WindowHandle != recoveryMainHandle {
			t.Fatalf("step %d changed window: %s", i, request.WindowHandle)
		}
	}
}
