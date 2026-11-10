package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

type linuxRecordingBackend struct {
	*recordingPlatformBackend
}

func (linuxRecordingBackend) HostInfo() HostBackendInfo {
	return HostBackendInfo{Platform: "linux"}
}

func findWalkSnapshot(target string, omitted bool) ProviderAccessibilityResult {
	snapshot := &AppSnapshot{
		App:          AppDescriptor{Name: "Editor", PID: 7},
		WindowTitle:  "Editor",
		WindowHandle: "window-7",
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Editor"},
			{Index: 1, RuntimeID: []int{0, 0}, ControlType: "push button", Name: target},
		},
	}
	if omitted {
		snapshot.Annotations = []axProviderAnnotation{{
			ParentRuntimeID: []int{0}, Kind: "provider_omission",
			Count: 900, Reason: "time",
		}}
	}
	return ProviderAccessibilityResult{OK: true, Snapshot: snapshot}
}

func findWithWalk(
	t *testing.T, platform, mode string, responses ...ProviderAccessibilityResult,
) ([]bool, string) {
	t.Helper()
	recording := &recordingPlatformBackend{accessibilityResponses: responses}
	var backend PlatformBackend = recording
	if platform == "linux" {
		backend = linuxRecordingBackend{recording}
	}
	s := NewServiceWithBackend(backend)
	args := map[string]any{"mode": mode, "app": "Editor"}
	if mode == contract.AXModeFind {
		args["query"] = map[string]any{"name_contains": "Save"}
	}
	result := s.CallForModel(contract.PublicToolAX, args, false)
	if result.IsError {
		t.Fatalf("%s failed: %s", mode, ResultText(result))
	}
	var walks []bool
	for _, request := range recording.observeRequests {
		walks = append(walks, request.Observation.FindWalk)
	}
	return walks, ResultText(result)
}

func TestLinuxFindWalksPastTheRenderBudgetOnlyWhenTheSnapshotHasNoMatch(t *testing.T) {
	for _, tc := range []struct {
		name, platform, mode string
		responses            []ProviderAccessibilityResult
		walks                []bool
		matches              string
	}{
		{"match in partial snapshot", "linux", contract.AXModeFind,
			[]ProviderAccessibilityResult{findWalkSnapshot("Save", true)},
			[]bool{false}, "matches: 1"},
		{"no match in complete snapshot", "linux", contract.AXModeFind,
			[]ProviderAccessibilityResult{findWalkSnapshot("Open", false)},
			[]bool{false}, "matches: 0"},
		{"no match in partial snapshot", "linux", contract.AXModeFind,
			[]ProviderAccessibilityResult{findWalkSnapshot("Open", true), findWalkSnapshot("Save", false)},
			[]bool{false, true}, "matches: 1"},
		{"other providers reject the field", "test", contract.AXModeFind,
			[]ProviderAccessibilityResult{findWalkSnapshot("Open", true)},
			[]bool{false}, "matches: 0"},
		{"observe", "linux", contract.AXModeObserve,
			[]ProviderAccessibilityResult{findWalkSnapshot("Open", true)},
			[]bool{false}, ""},
	} {
		walks, text := findWithWalk(t, tc.platform, tc.mode, tc.responses...)
		if len(walks) != len(tc.walks) {
			t.Fatalf("%s: find_walk per read %v, want %v", tc.name, walks, tc.walks)
		}
		for index := range walks {
			if walks[index] != tc.walks[index] {
				t.Fatalf("%s: find_walk per read %v, want %v", tc.name, walks, tc.walks)
			}
		}
		if !strings.Contains(text, tc.matches) {
			t.Fatalf("%s: want %q in %s", tc.name, tc.matches, text)
		}
	}
}

func TestFindWithMatchesReportsPartialCoverageWhenTheWalkStopped(t *testing.T) {
	for _, omitted := range []bool{true, false} {
		_, text := findWithWalk(t, "test", contract.AXModeFind, findWalkSnapshot("Save", omitted))
		if !strings.Contains(text, "matches: 1") {
			t.Fatalf("find failed: %s", text)
		}
		if partial := strings.Contains(text, `"coverage":"partial"`); partial != omitted {
			t.Fatalf("omitted=%v: partial coverage marker=%v in %s", omitted, partial, text)
		}
		if strings.Contains(text, `"target_resolution"`) {
			t.Fatalf("a find with matches must not claim the target is unresolved: %s", text)
		}
	}
}

func findWalkService(responses ...ProviderAccessibilityResult) (*Service, *recordingPlatformBackend) {
	recording := &recordingPlatformBackend{accessibilityResponses: responses}
	return NewServiceWithBackend(linuxRecordingBackend{recording}), recording
}

func TestFindWalkSendsTheNameAndTextQuery(t *testing.T) {
	for _, tc := range []struct {
		query map[string]any
		want  ProviderFindQuery
	}{
		{map[string]any{"name_equals": "Save"}, ProviderFindQuery{Name: "Save", NameExact: true}},
		{map[string]any{"text_contains": "draft", "role": "button"}, ProviderFindQuery{Text: "draft"}},
		{map[string]any{"role": "checkbox"}, ProviderFindQuery{}},
	} {
		s, recording := findWalkService(findWalkSnapshot("Open", true), findWalkSnapshot("Open", false))
		s.CallForModel(contract.PublicToolAX, map[string]any{
			"mode": contract.AXModeFind, "app": "Editor", "query": tc.query,
		}, false)
		if len(recording.observeRequests) != 2 {
			t.Fatalf("%v: %d reads, want the observe read and the walk", tc.query, len(recording.observeRequests))
		}
		if recording.observeRequests[0].Observation.FindQuery != nil {
			t.Fatalf("%v: the observe read carried a find query", tc.query)
		}
		got := recording.observeRequests[1].Observation.FindQuery
		if got == nil || *got != tc.want {
			t.Fatalf("%v: walk query %+v, want %+v", tc.query, got, tc.want)
		}
	}
}

func TestCandidateWalkKeepsTheObserveReadAndAddsItsRefs(t *testing.T) {
	observed := findWalkSnapshot("Open", true)
	observed.Snapshot.Refs = map[string]elementRef{
		"0":   {Index: 0, Role: "frame", Name: "Editor"},
		"0.0": {Index: 1, Role: "push button", Name: "Open"},
	}
	walked := findWalkSnapshot("Save", false)
	walked.Snapshot.Elements[1].RuntimeID = []int{0, 9}
	walked.Snapshot.Elements[1].Index = 2
	walked.Snapshot.Refs = map[string]elementRef{
		"0":   {Index: 0, Role: "frame", Name: "Editor"},
		"0.9": {Index: 2, Role: "push button", Name: "Save"},
	}
	walked.Snapshot.FindCandidatesOnly = true
	s, _ := findWalkService(observed, walked)
	result := s.CallForModel(contract.PublicToolAX, map[string]any{
		"mode": contract.AXModeFind, "app": "Editor",
		"query": map[string]any{"name_equals": "Save"},
	}, false)
	if !strings.Contains(ResultText(result), "matches: 1") {
		t.Fatalf("find failed: %s", ResultText(result))
	}
	current := s.currentSnapshot("Editor")
	if len(current.Elements) != 2 || current.Elements[1].Name != "Open" {
		t.Fatalf("current snapshot after the walk = %+v, want the observe read", current.Elements)
	}
	if refs := current.Refs; refs["0.0"].Index != 1 || refs["0.9"].Index != 2 {
		t.Fatalf("refs after the walk = %v, want the observed Open and the walked Save", refs)
	}
}

func TestFindWalkWithoutMatchListsClosestElementsFromTheObserveRead(t *testing.T) {
	walked := findWalkSnapshot("unused", false)
	walked.Snapshot.Elements = walked.Snapshot.Elements[:1]
	s, _ := findWalkService(findWalkSnapshot("Save", true), walked)
	result := s.CallForModel(contract.PublicToolAX, map[string]any{
		"mode": contract.AXModeFind, "app": "Editor",
		"query": map[string]any{"name_equals": "Save As"},
	}, false)
	text := ResultText(result)
	if !strings.Contains(text, "matches: 0") || !strings.Contains(text, `button "Save"`) {
		t.Fatalf("want zero matches and the observed Save button as closest: %s", text)
	}
}

func TestFindCandidateSnapshotIsNotACompleteIdentityInventory(t *testing.T) {
	for _, candidatesOnly := range []bool{false, true} {
		snapshot := findWalkSnapshot("Save", false).Snapshot
		snapshot.FindCandidatesOnly = candidatesOnly
		header := newAXSnapshotHeader(snapshot)
		if header.IdentityCoverageIncomplete != candidatesOnly || header.AcquisitionIncomplete {
			t.Fatalf("candidatesOnly=%v: identity incomplete=%v acquisition incomplete=%v",
				candidatesOnly, header.IdentityCoverageIncomplete, header.AcquisitionIncomplete)
		}
	}
}
