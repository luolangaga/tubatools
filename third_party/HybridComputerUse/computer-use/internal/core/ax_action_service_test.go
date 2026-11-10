package core

import (
	"strings"
	"testing"
)

func TestSemanticTargetFromRefPreservesSelectorPredicates(t *testing.T) {
	ref := encodeScopedSemanticRef(
		`toggle button "Menu" [desc="View options"] [focused] [a=click]`,
		"pid145/win1/dialog",
	)
	target, err := semanticTargetFromAddress(ref)
	if err != nil {
		t.Fatal(err)
	}
	if target.Role != "toggle button" || target.Name != "Menu" || !target.HasName {
		t.Fatalf("semantic target head was not preserved: %#v", target)
	}
	if target.WindowHandle != "pid145/win1/dialog" {
		t.Fatalf("semantic target lost window scope: %#v", target)
	}
	got := map[string]string{}
	for _, predicate := range target.Predicates {
		got[predicate.Kind] = predicate.Value
	}
	for kind, want := range map[string]string{
		"desc": "View options", "state": "focused", "action": "click",
	} {
		if got[kind] != want {
			t.Fatalf("predicate %s = %q, want %q in %#v", kind, got[kind], want, target)
		}
	}
}

func TestSemanticTargetIntentMismatchFailsBeforeRuntime(t *testing.T) {
	target, err := semanticTargetFromAddress(`push button "New"`)
	if err != nil {
		t.Fatal(err)
	}
	mismatch := semanticTargetIntentMismatch(target, "Save the document")
	if !strings.Contains(strings.ToLower(mismatch), "no input was emitted") {
		t.Fatalf("semantic intent mismatch must fail closed: %q", mismatch)
	}
	if mismatch := semanticTargetIntentMismatch(target, "Click New"); mismatch != "" {
		t.Fatalf("matching semantic intent was rejected: %q", mismatch)
	}
}

func TestCurrentNumericIndexPromotesOnlyWhenSemanticIdentityIsUnique(t *testing.T) {
	service := NewService()
	unique := &AppSnapshot{
		App:          AppDescriptor{Name: "Editor", PID: 7},
		WindowTitle:  "Question",
		WindowHandle: "pid7/win1/dialog",
		sourceApp:    "editor",
		Elements: []ElementRecord{
			{Index: 5, RuntimeID: []int{1, 2}, ControlType: "push button", Name: "OK"},
		},
	}
	service.rememberSnapshot("editor", unique)
	resolution, result := service.resolveActionElement("editor", "5", "", "click")
	if result.IsError {
		t.Fatalf("unique current index was rejected: %#v", result)
	}
	if resolution.semantic == nil || resolution.element != nil {
		t.Fatalf("unique index was not promoted: %#v", resolution)
	}
	if resolution.semantic.WindowHandle != unique.WindowHandle ||
		resolution.semantic.SourceIndex == nil ||
		*resolution.semantic.SourceIndex != 5 {
		t.Fatalf("promotion lost window/index provenance: %#v", resolution.semantic)
	}

	ambiguous := &AppSnapshot{
		App:          AppDescriptor{Name: "Editor", PID: 7},
		WindowTitle:  "Question",
		WindowHandle: "pid7/win1/dialog",
		sourceApp:    "editor",
		Elements: []ElementRecord{
			{Index: 5, RuntimeID: []int{1, 2}, ControlType: "push button", Name: "OK"},
			{Index: 6, RuntimeID: []int{1, 3}, ControlType: "push button", Name: "OK"},
		},
	}
	service.rememberSnapshot("editor", ambiguous)
	resolution, result = service.resolveActionElement("editor", "5", "", "click")
	if result.IsError {
		t.Fatalf("ambiguous current index should retain strict path resolution: %#v", result)
	}
	if resolution.element == nil || resolution.semantic != nil {
		t.Fatalf("ambiguous identity must not be promoted: %#v", resolution)
	}
}

func TestStructuredSelectorBindsToCurrentSnapshotWindow(t *testing.T) {
	service := NewService()
	snapshot := &AppSnapshot{
		App:          AppDescriptor{Name: "Editor", PID: 7},
		WindowTitle:  "Insert Table",
		WindowHandle: "pid7/win2/dialog",
		sourceApp:    "editor",
	}
	service.rememberSnapshot("editor", snapshot)
	resolution, result := service.resolveActionElement(
		"editor",
		`push button "OK"`,
		"",
		"click",
	)
	if result.IsError || resolution == nil || resolution.semantic == nil {
		t.Fatalf("structured selector was rejected: resolution=%#v result=%#v", resolution, result)
	}
	if resolution.semantic.WindowHandle != snapshot.WindowHandle {
		t.Fatalf(
			"structured selector was not bound to current window: got %q want %q",
			resolution.semantic.WindowHandle,
			snapshot.WindowHandle,
		)
	}
}
