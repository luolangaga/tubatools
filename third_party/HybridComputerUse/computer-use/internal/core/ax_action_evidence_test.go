package core

import (
	"strings"
	"testing"
)

// TestUnchangedNotesReportOnlyObservedChange checks that notes after an action
// report only what was observed. Pixel comparison is a second signal,
// independent of the tree, which cannot see whole classes of effects such as
// formatting, file state and canvas pixels. An unchanged tree therefore never
// implies that the action had no effect, and even an unchanged screen leaves
// delivery and semantic effect unknown.
func TestUnchangedNotesReportOnlyObservedChange(t *testing.T) {
	changed := strings.Join(unchangedNotes(true, pixelsChanged, false), " ")
	if !strings.Contains(changed, "visual change observed") {
		t.Fatalf("a screen change must be reported only as visual change observed: %s", changed)
	}

	identical := strings.Join(unchangedNotes(true, pixelsIdentical, false), " ")
	if !strings.Contains(identical, "no observed change") ||
		!strings.Contains(identical, "control delivery and semantic effect remain unknown") {
		t.Fatalf("an unchanged screen must be reported only as no observed change, keeping delivery uncertain: %s", identical)
	}
	weaker := strings.Join(unchangedNotes(false, pixelsIdentical, false), " ")
	if !strings.Contains(weaker, "no observed change") {
		t.Fatalf("without window-focus evidence the note must still report only the observation: %s", weaker)
	}

	unknown := strings.Join(unchangedNotes(true, pixelsUnknown, false), " ")
	if !strings.Contains(unknown, "No accessibility change observed") ||
		!strings.Contains(unknown, "not control-delivery evidence") {
		t.Fatalf("without a pixel comparison the note must report only the a11y observation and the window-focus boundary: %s", unknown)
	}
	for name, note := range map[string]string{
		"changed": changed, "identical": identical, "unknown": unknown,
		"attrs":         strings.Join(unchangedNotes(true, pixelsUnknown, true), " "),
		"attrs_changed": strings.Join(unchangedNotes(true, pixelsChanged, true), " "),
	} {
		for _, forbidden := range []string{"took effect", "did nothing", "input arrived and was ignored"} {
			if strings.Contains(strings.ToLower(note), forbidden) {
				t.Fatalf("%s note contains forbidden inference %q: %s", name, forbidden, note)
			}
		}
	}
	if attrsChanged := strings.Join(unchangedNotes(true, pixelsChanged, true), " "); !strings.Contains(attrsChanged, "text attributes changed") ||
		!strings.Contains(attrsChanged, "visual change observed") {
		t.Fatalf("attribute and screen changes must be reported separately, not one swallowed by the other's branch: %s", attrsChanged)
	}

	for _, c := range []struct {
		notes []string
		want  pixelVerdict
	}{
		{[]string{"[pixels] sampled_pixel_change=none: no sampled pixel difference persisted."}, pixelsIdentical},
		{[]string{"[pixels] persistent_change=false: legacy no sampled pixel difference persisted."}, pixelsIdentical},
		{[]string{"[pixels] 4.1% of the window changed on screen. Changes are concentrated in {1,2,3,4}."}, pixelsChanged},
		{[]string{"[pixels] The window changed size or position during this action"}, pixelsChanged},
		{[]string{"[a11y][semantic] something"}, pixelsUnknown},
		{nil, pixelsUnknown},
	} {
		if got := readPixelVerdict(c.notes); got != c.want {
			t.Fatalf("readPixelVerdict(%v) = %v, want %v", c.notes, got, c.want)
		}
	}
}

func TestFindOmitsInternalContinuityDiagnostics(t *testing.T) {
	notes := compactFindNotes([]string{
		"SNAPSHOT UNCHANGED: a long explanation that should not be repeated.",
		"[diagnostic snapshot_unchanged] still present; full explanation unchanged from the previous response.",
		"MODAL DIALOG: Save As",
	})
	joined := strings.Join(notes, "\n")
	for _, forbidden := range []string{
		"continuity", "diagnostic", "long explanation",
		"full explanation unchanged", "MODAL DIALOG",
	} {
		if strings.Contains(joined, forbidden) {
			t.Fatalf("find leaked internal note %q: %v", forbidden, notes)
		}
	}
	if joined != "[state] semantic_state=unchanged\n[state] blocking=modal_dialog" {
		t.Fatalf("find must retain only compact decision state: %v", notes)
	}
}

func TestModelFacingAXNotesKeepDecisionFactsOnly(t *testing.T) {
	notes := modelFacingAXNotes([]string{
		`[semantic-resolution] fresh=true unique=true selector="Save"`,
		"ROOT WINDOW CHANGED: verbose internal identity explanation.",
		"SNAPSHOT UNCHANGED, APP REPORTS BUSY: verbose timing explanation.",
		"DIALOG IN FRONT: verbose explanation.",
		`[expect] {"predicate_verified":false}`,
	})
	joined := strings.Join(notes, "\n")
	for _, forbidden := range []string{
		"semantic-resolution", "ROOT WINDOW CHANGED",
		"SNAPSHOT UNCHANGED", "DIALOG IN FRONT", "verbose",
	} {
		if strings.Contains(joined, forbidden) {
			t.Fatalf("model note leaked %q: %s", forbidden, joined)
		}
	}
	for _, required := range []string{
		"window_changed=true",
		"busy=true",
		"recovery=wait_then_observe",
		"blocking=dialog_in_front",
		"predicate_verified",
	} {
		if !strings.Contains(joined, required) {
			t.Fatalf("model note lost %q: %s", required, joined)
		}
	}
}

func TestObserveNoneKeepsOnlyVerificationAndInvalidation(t *testing.T) {
	notes := observeNoneNotes([]string{
		`[a11y] Resolved element_index 7 to push button "Save".`,
		"[pixels] sampled_pixel_change=tiny: one point changed.",
		`[expect] {"predicate_verified":false,"settled_after_ms":0}`,
		"[canonical-address] serializer-hidden ref changed.",
		"ROOT WINDOW CHANGED: old refs are invalid.",
		"SNAPSHOT UNCHANGED: long explanation.",
	})
	joined := strings.Join(notes, "\n")
	if !strings.Contains(joined, "predicate_verified") ||
		!strings.Contains(joined, "[canonical-address]") ||
		!strings.Contains(joined, "ROOT WINDOW CHANGED") {
		t.Fatalf("observe=none must keep verification and invalidation: %v", notes)
	}
	for _, forbidden := range []string{"Resolved element_index", "sampled_pixel_change", "SNAPSHOT UNCHANGED"} {
		if strings.Contains(joined, forbidden) {
			t.Fatalf("observe=none leaked optional diagnostic %q: %v", forbidden, notes)
		}
	}
}

func TestObserveNoneKeepsSettledPhaseDiagnosticsOnly(t *testing.T) {
	notes := observeNoneNotes([]string{
		"[observation phase=immediate snapshot_id=a superseded_by=b] " +
			"ROOT WINDOW CHANGED: immediate dialog.",
		"[observation phase=settled snapshot_id=b] " +
			"MODAL DIALOG: final warning.",
	})
	joined := strings.Join(notes, "\n")
	if strings.Contains(joined, "immediate dialog") {
		t.Fatalf("observe=none retained a superseded immediate diagnostic: %v", notes)
	}
	if !strings.Contains(joined, "final warning") {
		t.Fatalf("observe=none dropped the settled final diagnostic: %v", notes)
	}
}

func TestObserveNoneKeepsFreshSemanticResolutionEvidence(t *testing.T) {
	notes := []string{
		"[semantic-resolution] semantic_ref was resolved against one fresh pre-action snapshot.",
		"SNAPSHOT UNCHANGED: verbose continuity text.",
	}
	kept := observeNoneNotes(notes)
	if len(kept) != 1 ||
		!strings.HasPrefix(kept[0], "[semantic-resolution]") {
		t.Fatalf("observe=none hid semantic resolution evidence: %#v", kept)
	}
}

// TestTextAttributeChanges checks that text attribute changes are reported.
// The snapshot renders only a few semantic properties; exact style queries
// belong to assertions.
// AT-SPI's Atspi.Text.get_default_attributes() exposes justification, size
// and weight, so a paragraph alignment change is reported as
// `justification: left -> right`.
func TestTextAttributeChanges(t *testing.T) {
	before := &AppSnapshot{Elements: []ElementRecord{
		{Index: 26, RuntimeID: []int{0, 0, 1}, ControlType: "paragraph",
			TextAttributes: map[string]string{"justification": "left", "weight": "400"}},
		{Index: 49, RuntimeID: []int{0, 0, 2}, ControlType: "text",
			TextAttributes: map[string]string{"bg-color": "64764,64764,64764"}},
	}}
	after := &AppSnapshot{Elements: []ElementRecord{
		{Index: 26, RuntimeID: []int{0, 0, 1}, ControlType: "paragraph",
			TextAttributes: map[string]string{"justification": "right", "weight": "400"}},
		{Index: 49, RuntimeID: []int{0, 0, 2}, ControlType: "text",
			TextAttributes: map[string]string{"bg-color": "64764,64764,64764"}},
	}}

	changes := textAttributeChanges(before, after)
	if len(changes) != 1 {
		t.Fatalf("only the paragraph changed, so exactly one change should be reported: %v", changes)
	}
	if !strings.Contains(changes[0], "justification: left -> right") {
		t.Fatalf("change must state the old and new values: %s", changes[0])
	}
	// Unchanged attributes must not be included.
	if strings.Contains(changes[0], "weight") {
		t.Fatalf("unchanged attributes must not be reported: %s", changes[0])
	}

	// Elements are paired by runtime ID path, not by role and name.
	// The LibreOffice sidebar has several unnamed `text` nodes whose role/name
	// keys collide, so one node's hover highlight would be attributed to another
	// and even a no-op would report a formatting change.
	collide := &AppSnapshot{Elements: []ElementRecord{
		{Index: 1, RuntimeID: []int{0, 1}, ControlType: "text",
			TextAttributes: map[string]string{"bg-color": "a"}},
		{Index: 2, RuntimeID: []int{0, 2}, ControlType: "text",
			TextAttributes: map[string]string{"bg-color": "b"}},
	}}
	same := &AppSnapshot{Elements: []ElementRecord{
		{Index: 1, RuntimeID: []int{0, 1}, ControlType: "text",
			TextAttributes: map[string]string{"bg-color": "a"}},
		{Index: 2, RuntimeID: []int{0, 2}, ControlType: "text",
			TextAttributes: map[string]string{"bg-color": "b"}},
	}}
	if got := textAttributeChanges(collide, same); len(got) != 0 {
		t.Fatalf("unnamed nodes with the same role must not borrow each other's attributes: %v", got)
	}

	replaced := &AppSnapshot{Elements: []ElementRecord{
		{Index: 1, RuntimeID: []int{0, 1}, ControlType: "table cell", Name: "B1",
			TextAttributes: map[string]string{"family-name": "B2:B6"}},
	}}
	replacement := &AppSnapshot{Elements: []ElementRecord{
		{Index: 1, RuntimeID: []int{0, 1}, ControlType: "paragraph", Name: "Title",
			TextAttributes: map[string]string{"family-name": "Söhne"}},
	}}
	if got := textAttributeChanges(replaced, replacement); len(got) != 0 {
		t.Fatalf("attributes must not be paired across a role/name change at the same path: %v", got)
	}

	implausible := &AppSnapshot{Elements: []ElementRecord{
		{Index: 26, RuntimeID: []int{0, 0, 1}, ControlType: "paragraph",
			TextAttributes: map[string]string{"family-name": "B2:B6"}},
	}}
	plausibleAfter := &AppSnapshot{Elements: []ElementRecord{
		{Index: 26, RuntimeID: []int{0, 0, 1}, ControlType: "paragraph",
			TextAttributes: map[string]string{"family-name": "Söhne"}},
	}}
	if got := textAttributeChanges(implausible, plausibleAfter); len(got) != 0 {
		t.Fatalf("a cell range must not pass as a font family in attribute evidence: %v", got)
	}

	uiBefore := &AppSnapshot{Elements: []ElementRecord{
		{Index: 8, RuntimeID: []int{0, 4}, ControlType: "push button", Name: "Tab",
			TextAttributes: map[string]string{"bg-color": "255,255,255"}},
	}}
	uiAfter := &AppSnapshot{Elements: []ElementRecord{
		{Index: 8, RuntimeID: []int{0, 4}, ControlType: "push button", Name: "Tab",
			TextAttributes: map[string]string{"bg-color": "0,120,215"}},
	}}
	if got := textAttributeChanges(uiBefore, uiAfter); len(got) != 0 {
		t.Fatalf("UI control highlight must not be presented as document formatting evidence: %v", got)
	}

	uiTextBefore := &AppSnapshot{Elements: []ElementRecord{
		{Index: 9, RuntimeID: []int{0, 5}, ControlType: "text", Name: "Sidebar label",
			TextAttributes: map[string]string{"fg-color": "0,0,0"}},
	}}
	uiTextAfter := &AppSnapshot{Elements: []ElementRecord{
		{Index: 9, RuntimeID: []int{0, 5}, ControlType: "text", Name: "Sidebar label",
			TextAttributes: map[string]string{"fg-color": "255,255,255"}},
	}}
	if got := textAttributeChanges(uiTextBefore, uiTextAfter); len(got) != 0 {
		t.Fatalf("text role without a document/paragraph ancestor is UI presentation, not document formatting: %v", got)
	}

	documentTextBefore := &AppSnapshot{Elements: []ElementRecord{
		{Index: 1, RuntimeID: []int{0, 1}, ControlType: "document text", Name: "Document"},
		{Index: 2, RuntimeID: []int{0, 1, 0}, ControlType: "text", Name: "Body",
			TextAttributes: map[string]string{"weight": "400"}},
	}}
	documentTextAfter := &AppSnapshot{Elements: []ElementRecord{
		{Index: 1, RuntimeID: []int{0, 1}, ControlType: "document text", Name: "Document"},
		{Index: 2, RuntimeID: []int{0, 1, 0}, ControlType: "text", Name: "Body",
			TextAttributes: map[string]string{"weight": "700"}},
	}}
	if got := textAttributeChanges(documentTextBefore, documentTextAfter); len(got) != 1 {
		t.Fatalf("text under a document ancestor must retain formatting evidence: %v", got)
	}

	// Without a snapshot from before the action, no change may be invented.
	if got := textAttributeChanges(nil, after); got != nil {
		t.Fatalf("without a pre-action snapshot no change may be reported: %v", got)
	}
}

// TestWithoutRepaintNoise checks that [text-attrs] does not present repaint
// noise as evidence. When a web font finishes loading asynchronously, the
// fallback family is replaced (for example Ubuntu -> All-Pro Sans) together
// with the size and weight metrics of the new font. Reporting that as
// "Formatting changed ... direct evidence the action took effect" after an
// unrelated keypress is worse than giving no evidence.
func TestWithoutRepaintNoise(t *testing.T) {
	t.Run("web font load is not evidence", func(t *testing.T) {
		// When the family changes, size and weight change with it; those are
		// the new font's metrics.
		got := withoutRepaintNoise([]string{
			"family-name: Ubuntu -> All-Pro Sans",
			"size: 18pt -> 22.5pt",
			"weight: unset -> 700",
		})
		if len(got) != 0 {
			t.Fatalf("a font swap should be filtered out entirely, but %v remained", got)
		}
	})

	t.Run("imperceptible color change is not evidence", func(t *testing.T) {
		got := withoutRepaintNoise([]string{"bg-color: 244,248,248 -> 246,249,249"})
		if len(got) != 0 {
			t.Fatalf("a 2/255 color difference should be filtered out, but %v remained", got)
		}
	})

	t.Run("real bold change is still reported", func(t *testing.T) {
		// This is what the note is for: bold in LibreOffice changes formatting,
		// not structure, so the tree looks unchanged. Noise filtering must
		// keep such changes.
		got := withoutRepaintNoise([]string{"weight: 400 -> 700"})
		if len(got) != 1 {
			t.Fatalf("a real bold change should be kept, got %v", got)
		}
	})

	t.Run("visible color change is kept", func(t *testing.T) {
		got := withoutRepaintNoise([]string{"fg-color: 0,0,0 -> 255,0,0"})
		if len(got) != 1 {
			t.Fatalf("black to red should be kept, got %v", got)
		}
	})

	t.Run("size change without a font swap is kept", func(t *testing.T) {
		// fontSwap is decided per node: without evidence of a font swap, a size
		// change may be a genuine font size change.
		got := withoutRepaintNoise([]string{"size: 12pt -> 24pt"})
		if len(got) != 1 {
			t.Fatalf("size should be kept without a font swap, got %v", got)
		}
	})
}
