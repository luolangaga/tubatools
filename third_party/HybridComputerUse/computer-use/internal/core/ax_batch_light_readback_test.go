package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// rootOnlyBackend is a reportBackend whose post-action reads behave like the
// Linux provider's: they select the active modal window when there is one,
// and with max_tree_nodes=1 they return the window root and the window list
// only.
type rootOnlyBackend struct {
	*reportBackend
	// ignoreLimit reads the whole tree whatever the request asks, as the
	// code did before root-only reads.
	ignoreLimit   bool
	rootOnlyReads int
	fullReads     int
}

func (b *rootOnlyBackend) ResolveAndAct(request ProviderActionRequest) (ProviderAccessibilityResult, error) {
	if b.onAction != nil {
		b.onAction(b.reportBackend)
	}
	for _, window := range b.windows {
		if window.role == "dialog" && window.active {
			request.WindowHandle = window.handle
		}
	}
	onAction := b.onAction
	b.onAction = nil
	result, err := b.reportBackend.ResolveAndAct(request)
	b.onAction = onAction
	if result.Snapshot == nil {
		return result, err
	}
	if request.Observation.MaxTreeNodes == 1 && !b.ignoreLimit {
		b.rootOnlyReads++
		snapshot := *result.Snapshot
		snapshot.Elements = snapshot.Elements[:1]
		snapshot.TreeLines = snapshot.TreeLines[:1]
		snapshot.Annotations = []axProviderAnnotation{{
			ParentRuntimeID: []int{0}, Kind: "provider_omission",
			Count: 3, Reason: "budget",
		}}
		result.Snapshot = &snapshot
	} else {
		b.fullReads++
	}
	return result, err
}

func rootOnlyService(t *testing.T, ignoreLimit bool, windows ...reportWindow) (*Service, *rootOnlyBackend) {
	t.Helper()
	s, report := reportService(t, windows...)
	backend := &rootOnlyBackend{reportBackend: report, ignoreLimit: ignoreLimit}
	s.Backend = backend
	return s, backend
}

func keys(names ...string) []any {
	actions := []any{}
	for _, name := range names {
		actions = append(actions, map[string]any{"type": "keypress", "keys": []any{name}})
	}
	return actions
}

func TestLightReadbackOnlyBetweenRefLessInputs(t *testing.T) {
	key := axBatchAction{actionType: "keypress", key: "Tab"}
	text := axBatchAction{actionType: "type", text: "x"}
	click := axBatchAction{actionType: "click", targetRef: "r3"}
	refKey := axBatchAction{actionType: "keypress", key: "Tab", targetRef: "r2"}
	wait := axBatchAction{actionType: "wait", wait: &axWait{text: "x"}}
	windowWait := axBatchAction{actionType: "wait", wait: &axWait{window: "Report"}}
	windowGone := axBatchAction{actionType: "wait", wait: &axWait{window: "Report", gone: true}}
	settle := axBatchAction{actionType: "wait", wait: &axWait{settle: true}}
	for _, tc := range []struct {
		name    string
		actions []axBatchAction
		want    []bool
	}{
		// The read before the last action stays full: its notes compare
		// the trees around the last action.
		{"keys", []axBatchAction{key, text, key, text}, []bool{true, true, false, false}},
		{"two keys", []axBatchAction{key, key}, []bool{false, false}},
		// A click needs the element it names in the read before it.
		{"click next", []axBatchAction{key, click, key, key, key}, []bool{false, false, true, false, false}},
		{"ref key next", []axBatchAction{key, refKey, key, key, key}, []bool{false, false, true, false, false}},
		{"after a click", []axBatchAction{click, key, key, key}, []bool{false, true, false, false}},
		// A wait consults the latest read.
		{"wait next", []axBatchAction{key, wait, key, key, key}, []bool{false, false, true, false, false}},
		{"wait before last", []axBatchAction{key, key, key, wait, key}, []bool{true, true, false, false, false}},
		// Waits for windows only read the window list, and the final
		// observation reads the tree after them.
		{"keys then window wait", []axBatchAction{key, key, windowWait}, []bool{true, true, false}},
		{"one key then window waits", []axBatchAction{key, windowWait, windowGone}, []bool{true, false, false}},
		{"click then window wait", []axBatchAction{key, click, windowWait}, []bool{false, false, false}},
		{"keys then window and text waits", []axBatchAction{key, key, windowWait, wait}, []bool{false, false, false, false}},
		{"keys then settle", []axBatchAction{key, key, settle}, []bool{false, false, false}},
	} {
		lastInput := -1
		for index, action := range tc.actions {
			if action.actionType != "wait" {
				lastInput = index
			}
		}
		for index := range tc.actions {
			if got := axBatchLightReadback(tc.actions, index, lastInput); got != tc.want[index] {
				t.Errorf("%s: step %d light=%v want %v", tc.name, index, got, tc.want[index])
			}
		}
	}
}

func TestKeyBatchUsesRootOnlyReadsBetweenActions(t *testing.T) {
	s, backend := rootOnlyService(t, false, reportMain())
	window, _ := reportObserve(t, s)
	wantElements := len(s.currentSnapshot("Editor").Elements)
	result := s.callTool(contract.PublicToolAX, map[string]any{
		"mode": "act", "window_ref": window, "actions": keys("a", "b", "c", "d", "e"),
	})
	if result.IsError {
		t.Fatalf("batch failed: %s", ResultText(result))
	}
	// Steps 1-3 read the root only; the read before the last action and
	// the final read are full.
	if backend.rootOnlyReads != 3 || backend.fullReads != 2 {
		t.Fatalf("root-only reads %d, full reads %d", backend.rootOnlyReads, backend.fullReads)
	}
	// The full ref map survives the root-only reads.
	if snapshot := s.currentSnapshot("Editor"); snapshot == nil || snapshot.lightReadback || len(snapshot.Elements) != wantElements {
		t.Fatalf("final snapshot is not a full read: %#v", snapshot)
	}
}

// rootOnlyDesktop also lists the windows that window waits poll.
type rootOnlyDesktop struct {
	*rootOnlyBackend
}

func (b *rootOnlyDesktop) ObserveApps(request ProviderObserveAppsRequest) (ProviderAccessibilityResult, error) {
	return (&reportDesktop{b.reportBackend}).ObserveApps(request)
}

// A batch whose input ends with window waits reads the tree once, after
// the waits: each key reads the window root only, so that final read is the
// batch's only tree and cannot be skipped.
func TestWindowWaitBatchReadsTheTreeOnceAfterTheWaits(t *testing.T) {
	s, backend := rootOnlyService(t, false, reportMain())
	s.Backend = &rootOnlyDesktop{backend}
	window, _ := reportObserve(t, s)
	observes := len(backend.observeRequests)
	actions := append(keys("a", "b"),
		map[string]any{"type": "wait", "window": "Report", "timeout_ms": 1000})
	result := s.callTool(contract.PublicToolAX, map[string]any{
		"mode": "act", "window_ref": window, "actions": actions,
	})
	if result.IsError {
		t.Fatalf("batch failed: %s", ResultText(result))
	}
	if backend.rootOnlyReads != 2 || backend.fullReads != 0 {
		t.Fatalf("root-only reads %d, full action reads %d", backend.rootOnlyReads, backend.fullReads)
	}
	if got := len(backend.observeRequests) - observes; got != 1 {
		t.Fatalf("tree reads after the waits: %d, want 1", got)
	}
}

// A dialog that opens after a step must still stop the batch before the
// next key, although the read after that step covered the root only.
func TestRootOnlyReadStillStopsWhenTheWindowChanges(t *testing.T) {
	main := reportMain()
	s, backend := rootOnlyService(t, false, main)
	window, _ := reportObserve(t, s)
	actions := 0
	backend.onAction = func(b *reportBackend) {
		actions++
		if actions == 2 {
			b.windows[0].active = false
			b.windows = append(b.windows, reportWindow{
				handle: reportDialogHandle, role: "dialog", title: "Save changes?",
				path: "/window/2", active: true,
			})
		}
	}
	result := s.callTool(contract.PublicToolAX, map[string]any{
		"mode": "act", "window_ref": window, "actions": keys("a", "b", "c", "d", "e"),
	})
	text := ResultText(result)
	if !result.IsError || !strings.Contains(text, "batch_window_changed") {
		t.Fatalf("batch did not stop at the window change:\n%s", text)
	}
	if actions != 2 {
		t.Fatalf("%d actions were sent; the batch must stop after the second", actions)
	}
	if backend.rootOnlyReads != 2 {
		t.Fatalf("the reads after steps 1 and 2 should be root-only, got %d", backend.rootOnlyReads)
	}
	batch, _ := result.StructuredContent["actions_completed_without_call_error"].(int)
	if batch != 2 {
		t.Fatalf("completed=%v want 2: %#v", result.StructuredContent["actions_completed_without_call_error"], result.StructuredContent)
	}
}

// The model-visible result of a key batch is the same with root-only reads
// between actions as with full reads.
func TestRootOnlyReadsKeepModelVisibleOutput(t *testing.T) {
	run := func(rootOnly bool) string {
		s, _ := rootOnlyService(t, !rootOnly, reportMain())
		window, _ := reportObserve(t, s)
		line, rest := visibleAct(t, s, map[string]any{
			"mode": "act", "window_ref": window, "actions": keys("a", "b", "c", "d"),
		})
		return line + "\n" + strings.Join(rest, "\n")
	}
	full, light := run(false), run(true)
	if full != light {
		t.Fatalf("model-visible output differs\nfull reads:\n%s\nroot-only reads:\n%s", full, light)
	}
}

func TestRootOnlyBatchDoesNotReuseRefAfterControlDisappears(t *testing.T) {
	s, backend := rootOnlyService(t, false, reportMain())
	backend.menuOpen = true
	window, refs := reportObserve(t, s, "Preferences")
	backend.onAction = func(b *reportBackend) { b.menuOpen = false }
	actions := append(keys("a", "b", "c"), map[string]any{
		"type": "click", "ref": refs["Preferences"],
	})
	result := s.callTool(contract.PublicToolAX, map[string]any{
		"mode": "act", "window_ref": window, "actions": actions,
	})
	if !result.IsError {
		t.Fatalf("disappeared control was accepted: %s", ResultText(result))
	}
	if backend.rootOnlyReads != 2 || backend.fullReads != 1 {
		t.Fatalf("expected two root reads followed by a full read before ref use: root=%d full=%d",
			backend.rootOnlyReads, backend.fullReads)
	}
	if len(backend.actionRequests) != 3 {
		t.Fatalf("stale control reached provider input: %#v", backend.actionRequests)
	}
	batch, _ := result.StructuredContent["a11y_batch_result"].(map[string]any)
	if batch["actions_completed_without_call_error"] != 3 ||
		batch["failed_action_index"] != 3 {
		t.Fatalf("completed prefix was lost: %#v", result.StructuredContent)
	}
}

type partialRootOnlyBackend struct {
	*rootOnlyBackend
	failAt int
}

func (b *partialRootOnlyBackend) ResolveAndAct(request ProviderActionRequest) (ProviderAccessibilityResult, error) {
	if len(b.actionRequests)+1 != b.failAt {
		return b.rootOnlyBackend.ResolveAndAct(request)
	}
	b.actionRequests = append(b.actionRequests, request)
	// The provider emitted a prefix before reporting failure. A fresh
	// recovery read must show that state without retrying the input.
	b.value = "partially typed"
	return ProviderAccessibilityResult{
		OK: false, Error: "input emission stopped after a prefix",
		Failure: &ProviderFailure{
			Code: "input_emission_failed", Phase: "emission", FactsComplete: true,
		},
		Mutation: &ProviderMutationFacts{
			InputEventsEmitted: true, PartialInputPossible: true,
			DesktopStateMutated: true, MutationUnknown: true,
		},
	}, nil
}

func TestRootOnlyLongBatchPartialInputKeepsEvidenceAndFullRecovery(t *testing.T) {
	s, root := rootOnlyService(t, false, reportMain())
	backend := &partialRootOnlyBackend{rootOnlyBackend: root, failAt: 40}
	s.Backend = backend
	window, _ := reportObserve(t, s)
	initialReads := len(backend.observeRequests)
	actions := make([]any, 64)
	for index := range actions {
		actions[index] = map[string]any{"type": "type", "text": "text"}
	}
	result := s.callTool(contract.PublicToolAX, map[string]any{
		"mode": "act", "window_ref": window, "actions": actions,
	})
	if !result.IsError || len(backend.actionRequests) != 40 || backend.rootOnlyReads != 39 {
		t.Fatalf("partial input was retried or later input ran: requests=%d root=%d result=%s",
			len(backend.actionRequests), backend.rootOnlyReads, ResultText(result))
	}
	batch, _ := result.StructuredContent["a11y_batch_result"].(map[string]any)
	if batch["actions_completed_without_call_error"] != 39 || batch["failed_action_index"] != 39 {
		t.Fatalf("partial failure lost the completed prefix: %#v", batch)
	}
	for key, want := range map[string]any{
		"partial_input_possible":     true,
		"input_events_emitted":       true,
		"mutation_unknown":           true,
		"final_observation_returned": true,
	} {
		if result.StructuredContent[key] != want {
			t.Errorf("%s=%v, want %v", key, result.StructuredContent[key], want)
		}
	}
	if len(backend.observeRequests) <= initialReads {
		t.Fatal("no independent recovery observation was attempted")
	}
	for _, request := range backend.observeRequests[initialReads:] {
		if request.Observation.MaxTreeNodes == 1 {
			t.Fatal("recovery inherited the root-only limit")
		}
	}
	snapshot := s.currentSnapshot("Editor")
	if snapshot == nil || snapshot.lightReadback || len(snapshot.Elements) <= 1 {
		t.Fatalf("recovery did not restore full state: %#v", snapshot)
	}
	found := false
	for _, element := range snapshot.Elements {
		found = found || element.Name == "Size" && element.Value == "partially typed"
	}
	if !found {
		t.Fatal("recovery omitted the state left by partial input")
	}
}
