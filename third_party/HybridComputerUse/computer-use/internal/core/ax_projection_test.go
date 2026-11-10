package core

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestContentScopeKeepsOneTopLevelContentSubtree(t *testing.T) {
	snapshot := &AppSnapshot{
		TreeLines: []string{
			"\t0 frame \"Browser\"",
			"\t\t1 tool bar \"Navigation\"",
			"\t\t\t2 push button \"Reload\" [has-click-action]",
			"\t\t3 document web \"Example\"",
			"\t\t\t4 heading \"Welcome\"",
			"\t\t\t5 document web \"Embedded\"",
			"\t\t\t\t6 link \"Continue\" [has-click-action]",
		},
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{}, ControlType: "frame", Name: "Browser"},
			{Index: 1, RuntimeID: []int{0}, ControlType: "tool bar", Name: "Navigation"},
			{Index: 2, RuntimeID: []int{0, 0}, ControlType: "push button", Name: "Reload"},
			{
				Index: 3, RuntimeID: []int{1},
				ControlType:          "document web",
				LocalizedControlType: "documento web",
				Name:                 "Example",
			},
			{Index: 4, RuntimeID: []int{1, 0}, ControlType: "heading", Name: "Welcome"},
			{Index: 5, RuntimeID: []int{1, 1}, ControlType: "document web", Name: "Embedded"},
			{Index: 6, RuntimeID: []int{1, 1, 0}, ControlType: "link", Name: "Continue"},
		},
	}

	if err := applyAXScope(snapshot, contract.AXScopeContent); err != nil {
		t.Fatalf("content scope failed: %v", err)
	}
	if snapshot.scopeRoot == nil || snapshot.scopeRoot.Index != 3 {
		t.Fatalf("selected content root = %#v, want index 3", snapshot.scopeRoot)
	}
	if got := scopedCanonicalElementCount(snapshot); got != 4 {
		t.Fatalf("scoped canonical count = %d, want 4", got)
	}
	text := typedTreeTextForTest(t, snapshot)
	for _, absent := range []string{"Navigation", "Reload", "0 frame"} {
		if strings.Contains(text, absent) {
			t.Fatalf("window chrome leaked into content scope via %q:\n%s", absent, text)
		}
	}
	for _, present := range []string{
		`document "Example"`,
		`heading "Welcome"`,
		`document "Embedded"`,
		`link "Continue"`,
	} {
		if !strings.Contains(text, present) {
			t.Fatalf("content subtree lost %q:\n%s", present, text)
		}
	}
	if len(snapshot.Elements) != 7 {
		t.Fatal("content scope must preserve the complete canonical action records")
	}
}

func TestContentScopeUsesFocusToDisambiguateTopLevelRoots(t *testing.T) {
	snapshot := &AppSnapshot{
		TreeLines: []string{
			"\t0 frame \"Split\"",
			"\t\t1 document web \"Left\"",
			"\t\t\t2 heading \"Left title\" [focused]",
			"\t\t3 document web \"Right\"",
			"\t\t\t4 heading \"Right title\"",
		},
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{}, ControlType: "frame", Name: "Split"},
			{Index: 1, RuntimeID: []int{0}, ControlType: "document web", Name: "Left"},
			{Index: 2, RuntimeID: []int{0, 0}, ControlType: "heading", Name: "Left title", States: "[focused]"},
			{Index: 3, RuntimeID: []int{1}, ControlType: "document web", Name: "Right"},
			{Index: 4, RuntimeID: []int{1, 0}, ControlType: "heading", Name: "Right title"},
		},
	}

	if err := applyAXScope(snapshot, contract.AXScopeContent); err != nil {
		t.Fatalf("focused content scope failed: %v", err)
	}
	if snapshot.scopeRoot == nil || snapshot.scopeRoot.Index != 1 {
		t.Fatalf("focused root = %#v, want left document", snapshot.scopeRoot)
	}
	if strings.Contains(typedTreeTextForTest(t, snapshot), "Right") {
		t.Fatal("unfocused sibling content root leaked into scoped tree")
	}
}

// A window without exactly one content area is read whole, and the read
// says why it is wider than asked: a dialog is all content, a settings
// window has no document, and two documents without focus in either have
// no single one. Without this, content finds on dialogs fail.
func TestContentScopeReadsTheWindowWithoutOneContentArea(t *testing.T) {
	for name, test := range map[string]struct {
		snapshot *AppSnapshot
		reason   string
	}{
		"dialog with a preview": {&AppSnapshot{Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{}, ControlType: "dialog", Name: "Paragraph"},
			{Index: 1, RuntimeID: []int{0}, ControlType: "spin button", Name: "Above paragraph"},
			{Index: 2, RuntimeID: []int{1}, ControlType: "drawing area", Name: "Example"},
		}}, "a dialog has no separate content area"},
		"no content area": {&AppSnapshot{Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{}, ControlType: "frame", Name: "Settings"},
			{Index: 1, RuntimeID: []int{0}, ControlType: "push button", Name: "OK"},
		}}, "no document, page, or canvas area"},
		"ambiguous": {&AppSnapshot{Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{}, ControlType: "frame", Name: "Split"},
			{Index: 1, RuntimeID: []int{0}, ControlType: "document web", Name: "Left"},
			{Index: 2, RuntimeID: []int{1}, ControlType: "document web", Name: "Right"},
		}}, "2 content areas"},
	} {
		t.Run(name, func(t *testing.T) {
			if err := applyAXScope(test.snapshot, contract.AXScopeContent); err != nil {
				t.Fatalf("content scope failed instead of reading the window: %v", err)
			}
			if test.snapshot.scopeRoot != nil ||
				scopedCanonicalElementCount(test.snapshot) != len(test.snapshot.Elements) {
				t.Fatalf("content read did not cover the window: %#v", test.snapshot.scopeRoot)
			}
			for _, record := range test.snapshot.Elements {
				if !recordInAXScope(test.snapshot, &record) {
					t.Fatalf("%s is outside the read", record.Name)
				}
			}
			line := test.snapshot.scopeFallbackLine()
			if !strings.HasPrefix(line, "scope: window (") || !strings.Contains(line, test.reason) {
				t.Fatalf("fallback line %q lacks %q", line, test.reason)
			}
			if !strings.Contains(test.snapshot.renderedText(), line) {
				t.Fatalf("observe text lacks the fallback line:\n%s", test.snapshot.renderedText())
			}
		})
	}
}

func TestContentScopeAndRootIgnoreLegacyTreeBudget(t *testing.T) {
	snapshot := &AppSnapshot{
		TreeLines: []string{`\t0 frame "Browser"`},
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{}, ControlType: "frame", Name: "Browser"},
			{Index: 1, RuntimeID: []int{0}, ControlType: "document web", Name: "Page"},
			{Index: 2, RuntimeID: []int{0, 0}, ControlType: "heading", Name: "Deep target"},
		},
	}
	if err := applyAXScope(snapshot, contract.AXScopeContent); err != nil {
		t.Fatalf("canonical content scope depended on rendered text: %v", err)
	}
	if err := applyObservationRoot(snapshot, map[string]any{
		"selector": map[string]any{
			"role": "heading", "name": "Deep target",
		},
	}); err != nil {
		t.Fatalf("canonical root depended on rendered text: %v", err)
	}
	text := typedTreeTextForTest(t, snapshot)
	if !strings.Contains(text, `heading "Deep target"`) {
		t.Fatalf("typed root missing despite canonical record:\n%s", text)
	}
}

func TestFocusedAndTargetRootPreserveCanonicalAddressSpace(t *testing.T) {
	focused := editorFormSnapshot()
	if err := applyObservationRoot(
		focused,
		map[string]any{"focused": true},
	); err != nil {
		t.Fatal(err)
	}
	if focused.observationRoot == nil || focused.observationRoot.Index != 1 {
		t.Fatalf("focused root = %#v, want nearby panel 1", focused.observationRoot)
	}
	text := typedTreeTextForTest(t, focused)
	if !strings.Contains(text, `textbox "Name"`) ||
		!strings.Contains(text, `button "Save"`) ||
		strings.Contains(text, `status "Ready"`) {
		t.Fatalf("focused subtree has wrong local context:\n%s", text)
	}
	if len(focused.Elements) != 5 {
		t.Fatal("root projection changed canonical elements")
	}

	target := editorFormSnapshot()
	if err := applyObservationRoot(
		target,
		map[string]any{
			"selector": map[string]any{
				"role": "push button",
				"name": "Save",
			},
		},
	); err != nil {
		t.Fatal(err)
	}
	if target.observationRoot == nil || target.observationRoot.Index != 3 {
		t.Fatalf("target root = %#v, want Save button", target.observationRoot)
	}
	targetText := typedTreeTextForTest(t, target)
	if !strings.Contains(targetText, `button "Save"`) ||
		strings.Contains(targetText, `textbox "Name"`) {
		t.Fatalf("target subtree is not exact:\n%s", targetText)
	}
}

func TestObservationRootAcceptsExpandedShortRefInternally(t *testing.T) {
	snapshot := editorFormSnapshot()
	internalRef := encodeScopedSemanticRef(
		`push button "Save"`,
		snapshot.WindowHandle,
	)
	if _, err := DecodeAXRequest(map[string]any{
		"mode": "observe", "window_ref": "w1", "root": internalRef,
	}); err == nil {
		t.Fatal("public observation root accepted an internal semantic ref")
	}
	if err := applyObservationRoot(
		snapshot,
		map[string]any{"ref": internalRef},
	); err != nil {
		t.Fatalf("expanded short ref did not resolve internally: %v", err)
	}
	if snapshot.observationRoot == nil || snapshot.observationRoot.Index != 3 {
		t.Fatalf(
			"expanded short-ref root = %#v, want Save button index 3",
			snapshot.observationRoot,
		)
	}
}

func TestRootProjectionFailsClosed(t *testing.T) {
	snapshot := editorFormSnapshot()
	err := applyObservationRoot(
		snapshot,
		map[string]any{
			"selector": map[string]any{
				"role": "push button",
				"name": "Missing",
			},
		},
	)
	if err == nil || !strings.Contains(err.Error(), "no element matches") {
		t.Fatalf("missing root did not fail closed: %v", err)
	}
}

func TestObservationRootProviderBoundRefNarrowsSharedProviderIdentity(t *testing.T) {
	entry := &AXRefEntry{
		LocatorKind:        AXLocatorProviderBound,
		WindowHandle:       "pid7/win0/frame",
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/table/1",
		ProcessGeneration:  "pid:7:start:10",
		RuntimeID:          []int{0, 2},
	}
	snapshot := &AppSnapshot{
		WindowHandle: "pid7/win0/frame",
		Elements: []ElementRecord{
			{
				Index: 2, RuntimeID: []int{0, 1},
				ProviderBusName:    ":1.42",
				ProviderObjectPath: "/org/a11y/table/1",
				ProcessGeneration:  "pid:7:start:10",
			},
			{
				Index: 3, RuntimeID: []int{0, 2},
				ProviderBusName:    ":1.42",
				ProviderObjectPath: "/org/a11y/table/1",
				ProcessGeneration:  "pid:7:start:10",
			},
		},
	}
	record, err := resolveProviderBoundObservationRoot(snapshot, entry)
	if err != nil {
		t.Fatalf("resolve shared provider identity: %v", err)
	}
	if record.Index != 3 {
		t.Fatalf("resolved index=%d, want 3", record.Index)
	}
}

func TestObservationRootProviderBoundRefFailsClosed(t *testing.T) {
	entry := &AXRefEntry{
		LocatorKind:        AXLocatorProviderBound,
		WindowHandle:       "pid7/win0/frame",
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/table/1",
		ProcessGeneration:  "pid:7:start:10",
	}
	t.Run("missing", func(t *testing.T) {
		_, err := resolveProviderBoundObservationRoot(
			&AppSnapshot{WindowHandle: "pid7/win0/frame"},
			entry,
		)
		if err == nil || !strings.Contains(err.Error(), "stale_element_ref") {
			t.Fatalf("missing provider root error=%v", err)
		}
	})
	t.Run("ambiguous", func(t *testing.T) {
		snapshot := &AppSnapshot{
			WindowHandle: "pid7/win0/frame",
			Elements: []ElementRecord{
				{
					Index: 2, RuntimeID: []int{0, 1},
					ProviderBusName:    ":1.42",
					ProviderObjectPath: "/org/a11y/table/1",
					ProcessGeneration:  "pid:7:start:10",
				},
				{
					Index: 3, RuntimeID: []int{0, 2},
					ProviderBusName:    ":1.42",
					ProviderObjectPath: "/org/a11y/table/1",
					ProcessGeneration:  "pid:7:start:10",
				},
			},
		}
		_, err := resolveProviderBoundObservationRoot(snapshot, entry)
		if err == nil || !strings.Contains(err.Error(), "ambiguous_target") {
			t.Fatalf("ambiguous provider root error=%v", err)
		}
	})
}

// Probe #18/#19: the published observe root reused the act target schema, so
// it advertised {"ref":"wN"}, which the runtime rejects, and an index
// described as "action 1 only". The root now uses the read-only element target.
func TestObserveRootSchemaMatchesRuntimeRefRule(t *testing.T) {
	properties := findToolDefinition(t, "AX").InputSchema["properties"].(map[string]any)
	root := properties["root"].(map[string]any)
	encoded, err := json.Marshal(root)
	if err != nil {
		t.Fatal(err)
	}
	text := string(encoded)
	if strings.Contains(text, "w[1-9]") {
		t.Fatalf("observe root schema still advertises wN refs: %s", text)
	}
	if strings.Contains(text, "action 1 only") {
		t.Fatalf("observe root index still claims to be act-only: %s", text)
	}
	observe := func(root string) *axArgError {
		_, err := DecodeAXRequest(map[string]any{
			"mode": "observe", "window_ref": "w1", "root": root,
		})
		return err
	}
	if err := observe("w1"); err == nil {
		t.Fatal("runtime accepted a wN observe root")
	}
	if err := observe("r1"); err != nil {
		t.Fatalf("runtime rejected an rN observe root: %#v", err)
	}
}
