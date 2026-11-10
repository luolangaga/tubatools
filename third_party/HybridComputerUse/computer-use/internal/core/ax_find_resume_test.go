package core

import (
	"reflect"
	"regexp"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestRuntimePathAfter(t *testing.T) {
	resume := []int{0, 2, 5}
	for _, tc := range []struct {
		path []int
		want bool
	}{
		{[]int{0}, false},
		{[]int{0, 2}, false},
		{[]int{0, 1, 9}, false},
		{[]int{0, 2, 4, 7}, false},
		{[]int{0, 2, 5}, true},
		{[]int{0, 2, 5, 0}, true},
		{[]int{0, 2, 6}, true},
		{[]int{0, 3}, true},
	} {
		if got := runtimePathAfter(tc.path, resume); got != tc.want {
			t.Fatalf("runtimePathAfter(%v, %v) = %v, want %v", tc.path, resume, got, tc.want)
		}
	}
}

// resumeSnapshot is a walk of window-7 that returned the given buttons and
// stopped before resume (nil: the walk finished).
func resumeSnapshot(resume []int, buttons map[int]string) ProviderAccessibilityResult {
	snapshot := &AppSnapshot{
		App:                AppDescriptor{Name: "Editor", PID: 7},
		WindowTitle:        "Editor",
		WindowHandle:       "window-7",
		FindCandidatesOnly: true,
		FindResume:         resume,
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Editor"},
		},
	}
	for path := 0; path < 100; path++ {
		if name, ok := buttons[path]; ok {
			snapshot.Elements = append(snapshot.Elements, ElementRecord{
				Index: 10 + path, RuntimeID: []int{0, path},
				ControlType: "push button", Name: name,
			})
		}
	}
	if resume != nil {
		snapshot.Annotations = []axProviderAnnotation{{
			ParentRuntimeID: []int{0}, Kind: "provider_omission", Count: 50, Reason: "time",
		}}
	}
	return ProviderAccessibilityResult{OK: true, Snapshot: snapshot}
}

var findCursorPattern = regexp.MustCompile(`next_cursor=(f[0-9]+)`)

// findCursor returns the next_cursor a find answer shows, or "".
func findCursor(result ToolCallResult) string {
	if match := findCursorPattern.FindStringSubmatch(ResultText(result)); match != nil {
		return match[1]
	}
	return ""
}

func TestFindCursorResumesAWalkThatStopped(t *testing.T) {
	s, recording := findWalkService(
		findWalkSnapshot("Open", true),
		resumeSnapshot([]int{0, 5}, nil),
		resumeSnapshot([]int{0, 9}, map[int]string{7: "Save 7"}),
		resumeSnapshot(nil, map[int]string{12: "Save 12"}),
	)
	call := func(args map[string]any) ToolCallResult {
		args["mode"] = contract.AXModeFind
		return s.CallForModel(contract.PublicToolAX, args, false)
	}
	first := call(map[string]any{"app": "Editor", "query": map[string]any{"name_contains": "Save"}})
	cursor := findCursor(first)
	recovery, _ := findDeliveryModelEvidence(t, first)["recover"].(map[string]any)
	if !strings.Contains(ResultText(first), "matches: 0") || cursor == "" ||
		!strings.Contains(ResultText(first), `{"mode":"find","cursor":"`+cursor+`"}`) ||
		recovery["target"] != "cursor|GUI" {
		t.Fatalf("a walk that stopped needs a cursor: %s", ResultText(first))
	}
	second := call(map[string]any{"cursor": cursor})
	text := ResultText(second)
	if !strings.Contains(text, "matches: 1") || !strings.Contains(text, `"Save 7"`) {
		t.Fatalf("resumed walk: %s", text)
	}
	next := findCursor(second)
	if next == "" || next == cursor {
		t.Fatalf("the resumed walk stopped again and needs a new cursor: %s", text)
	}
	third := call(map[string]any{"cursor": next})
	if text := ResultText(third); !strings.Contains(text, `"Save 12"`) || strings.Contains(text, `"Save 7"`) ||
		findCursor(third) != "" {
		t.Fatalf("last walk: %s", text)
	}
	requests := recording.observeRequests
	if len(requests) != 4 {
		t.Fatalf("%d reads, want observe, walk and two resumed walks", len(requests))
	}
	for index, want := range [][]int{nil, nil, {0, 5}, {0, 9}} {
		got := requests[index].Observation
		if !reflect.DeepEqual(got.FindResumeAfter, want) {
			t.Fatalf("read %d resumes after %v, want %v", index, got.FindResumeAfter, want)
		}
		if index > 0 && (got.FindQuery == nil || got.FindQuery.Name != "Save" || !got.FindWalk) {
			t.Fatalf("read %d = %+v", index, got)
		}
		if index > 1 && requests[index].WindowHandle != "window-7" {
			t.Fatalf("resumed read %d targets window %q", index, requests[index].WindowHandle)
		}
	}
}

func TestResumedWalkDoesNotRepeatTheStopNodeAncestors(t *testing.T) {
	walk := resumeSnapshot([]int{0, 5}, nil)
	walk.Snapshot.Elements[0].Name = "Save dialog"
	resumed := resumeSnapshot(nil, map[int]string{6: "Save 6"})
	resumed.Snapshot.Elements[0].Name = "Save dialog"
	s, _ := findWalkService(findWalkSnapshot("Open", true), walk, resumed)
	first := s.CallForModel(contract.PublicToolAX, map[string]any{
		"mode": contract.AXModeFind, "app": "Editor",
		"query": map[string]any{"name_contains": "Save", "role": "button"},
	}, false)
	cursor := findCursor(first)
	second := s.CallForModel(contract.PublicToolAX, map[string]any{
		"mode": contract.AXModeFind, "cursor": cursor,
	}, false)
	if text := ResultText(second); !strings.Contains(text, "matches: 1") || !strings.Contains(text, `"Save 6"`) {
		t.Fatalf("resumed walk: %s", text)
	}
}

func TestFindPageEndsWithAResumeCursorAfterItsLastMatch(t *testing.T) {
	s, recording := findWalkService(
		findWalkSnapshot("Open", true),
		resumeSnapshot([]int{0, 4}, map[int]string{3: "Save 3"}),
		resumeSnapshot(nil, map[int]string{8: "Save 8"}),
	)
	first := s.CallRecord(contract.PublicToolAX, map[string]any{
		"mode": "find", "app": "Editor", "query": map[string]any{"name_contains": "Save"},
	}, CallOptions{})
	if !first.OK || first.Page == nil || first.Page.Total != 1 || first.Page.NextCursor == "" {
		t.Fatalf("first page = %+v", first.Page)
	}
	second := s.CallRecord(contract.PublicToolAX, map[string]any{
		"mode": "find", "cursor": first.Page.NextCursor,
	}, CallOptions{})
	if !second.OK || second.Page == nil || second.Page.Total != 1 || second.Page.NextCursor != "" ||
		len(second.Matches) != 1 || second.Matches[0].Name != "Save 8" {
		t.Fatalf("resumed page = %+v %+v", second.Page, second.Matches)
	}
	if got := recording.observeRequests[2].Observation.FindResumeAfter; !reflect.DeepEqual(got, []int{0, 4}) {
		t.Fatalf("resumed after %v", got)
	}
}
