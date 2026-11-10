package core

import (
	"strings"
	"testing"
)

// TestElementIntentCrossCheck covers the cross-check between a declared intent
// and the resolved element. The declaration helps readability and makes
// intent verifiable. It guards against a silent failure: after a dialog opens, all
// indices shift, and a click on an index from an earlier snapshot lands on a
// different control, such as a menu instead of a spin button, without any
// error.
func TestElementIntentCrossCheck(t *testing.T) {
	spin := &ElementRecord{Index: 5, ControlType: "spin button", Description: "Enter the vertical distance"}
	menu := &ElementRecord{Index: 5, ControlType: "menu", Name: "Insert"}

	// The declaration names a spin button, but the index resolves to the
	// Insert menu.
	mismatch := elementIntentMismatch(menu, "Position Y spin button")
	if mismatch == "" {
		t.Fatal("an index that resolves to an entirely different control must be rejected")
	}
	for _, fragment := range []string{`menu "Insert"`, "Position Y spin button", "earlier snapshot"} {
		if !strings.Contains(mismatch, fragment) {
			t.Fatalf("rejection must describe both sides and the cause, missing %q: %s", fragment, mismatch)
		}
	}

	// Role words must not count as matches. Nearly every
	// declaration and every role contains "button", so matching on the role
	// would let "the Save button" pass against the New button.
	if elementIntentMismatch(&ElementRecord{Index: 8, ControlType: "push button", Name: "New",
		Description: "Create a new document"}, "the Save button") == "" {
		t.Fatal("the generic word button must not defeat the check")
	}

	// An unnamed element usually takes its identity from a nearby label and has
	// nothing of its own to compare; the spin buttons in LibreOffice's Position
	// and Size dialog have no names at all. Skip the check rather than guess.
	if got := elementIntentMismatch(spin, "Position Y spin button"); got != "" {
		t.Fatalf("unnamed elements must skip the check, which could only reject falsely: %s", got)
	}

	// The same declaration must pass against the matching element.
	if got := elementIntentMismatch(&ElementRecord{ControlType: "push button", Name: "Save",
		Description: "Save the current file"}, "the Save button"); got != "" {
		t.Fatalf("a declaration matching the element must not be rejected: %s", got)
	}

	// A false rejection is worse than a miss, because a rejected agent concludes
	// that the target does not exist. All of the following must pass.
	for _, ok := range []struct {
		record   *ElementRecord
		declared string
		why      string
	}{
		{menu, "", "no declared intent disables the check"},
		{menu, "插入菜单", "a non-ASCII intent cannot be compared with an English role or name"},
		{menu, "the", "only stop words, nothing to compare"},
		{&ElementRecord{ControlType: "push button", Name: "OK"}, "OK", "exact name match"},
		{&ElementRecord{ControlType: "push button", Name: "Save"}, "save button", "case-insensitive"},
		{&ElementRecord{ControlType: "table cell", Name: "A1"}, "the A1 cell", "a content word matches besides stop words"},
		{&ElementRecord{ControlType: "spin button"}, "spin button with no name", "unnamed elements skip the check"},
		{&ElementRecord{ControlType: "push button", Name: "OK"}, "the button", "only generic words, no distinguishing content word"},
	} {
		if got := elementIntentMismatch(ok.record, ok.declared); got != "" {
			t.Fatalf("false rejection (%s): declared=%q -> %s", ok.why, ok.declared, got)
		}
	}
}

// TestSynthesisNotesStayWithinTheirEvidence covers two messages whose wording misled
// an agent: the note after synthesized input with no tree change, and the error
// for an element index that is missing from the snapshot.
func TestSynthesisNotesStayWithinTheirEvidence(t *testing.T) {
	// 1. After ctrl+s, calling the result "delivered but ignored" over-claims:
	//    the file was saved, yet the note sent the agent on extra steps to prove
	//    it. Formatting changes and file state never reach the accessibility
	//    tree, so an unchanged tree only shows that the tree cannot see the
	//    effect, not that nothing happened.
	for _, fragment := range []string{
		"No accessibility change observed",
		"Visual change was not measured",
		"that is not control-delivery evidence",
	} {
		if !strings.Contains(unchangedSynthesisNote(), fragment) {
			t.Fatalf("note after input with no observed change must not claim failure: missing %q", fragment)
		}
	}

	// 2. A missing index is as stale as one that resolves to a different
	//    control, so its error must be just as helpful and explain the cause
	//    and the way forward instead of a bare `unknown element_index "128"`.
	snapshot := &AppSnapshot{Elements: []ElementRecord{{Index: 0}, {Index: 59}}}
	_, err := lookupElement(snapshot, "128")
	if err == nil {
		t.Fatal("a missing index must be an error")
	}
	// Error shape: tool prefix, structured fields, a call log and the next step.
	for _, fragment := range []string{
		"indices 0..59",
		"Call log:",
		"resolving target.index=128",
		"Next:",
		"AX mode=observe or mode=find again",
		"returned rN ref",
	} {
		if !strings.Contains(err.Error(), fragment) {
			t.Fatalf("missing-index error must give the full chain of facts and the next step, missing %q:\n%s", fragment, err)
		}
	}
	// Snapshot age is a desktop-specific addition: a snapshot can be tens of
	// seconds stale without the agent noticing.
	if !strings.Contains(err.Error(), "Snapshot:") {
		t.Fatalf("error must state the snapshot age:\n%s", err)
	}
}

// unchangedSynthesisNote returns the note for synthesized input that produced
// no accessibility change, so tests need not know which branch emits it.
func unchangedSynthesisNote() string {
	return strings.Join(unchangedNotes(true, pixelsUnknown, false), " ")
}

// TestSelectorLookup checks that a selector can replace a numeric index, using
// exactly the syntax of snapshot lines. Numeric indices can shift on every
// observation and are the agent's most common mistake; intent declarations only
// make that mistake loud. A selector copied from a snapshot line works as is.
func TestSelectorLookup(t *testing.T) {
	snapshot := &AppSnapshot{Elements: []ElementRecord{
		{Index: 4, ControlType: "push button", Name: "Save"},
		{Index: 8, ControlType: "push button", Name: "New"},
		{Index: 9, ControlType: "menu", Name: "Save"},
		{Index: 12, ControlType: "label", Name: ""},
	}}

	for _, ok := range []struct {
		selector string
		want     int
	}{
		{`push button "Save"`, 4},
		{`menu "Save"`, 9},
		{`push button "New"`, 8},
	} {
		record, err := lookupElement(snapshot, ok.selector)
		if err != nil {
			t.Fatalf("%s should match: %v", ok.selector, err)
		}
		if record.Index != ok.want {
			t.Fatalf("%s matched %d, want %d", ok.selector, record.Index, ok.want)
		}
	}

	// An ambiguous selector must fail instead of picking one; a silent pick is
	// exactly the kind of error these checks exist to prevent.
	_, err := lookupElement(snapshot, `"Save"`)
	if err == nil {
		t.Fatal(`"Save" matches both a push button and a menu and must be reported as ambiguous`)
	}
	// Candidate lines carry index, description, and an `aka` selector that can
	// be used directly.
	for _, fragment := range []string{"ambiguous", "matches 2 elements",
		`4    push button "Save"`, `9    menu "Save"`, "aka", "adding the role"} {
		if !strings.Contains(err.Error(), fragment) {
			t.Fatalf("ambiguity error must list the candidates and how to narrow them, missing %q: %s", fragment, err)
		}
	}

	// The suggestion depends on which half is missing: a role-only selector
	// should be told to add the name, not the role it already has.
	_, err = lookupElement(snapshot, "push button")
	if err == nil {
		t.Fatal("a role-only selector with several matches must be reported as ambiguous")
	}
	if !strings.Contains(err.Error(), `adding the name in quotes`) ||
		!strings.Contains(err.Error(), "push button \"Save\"") {
		t.Fatalf("a role-only selector should be told to add the name: %s", err)
	}

	// A selector without a match must be reported explicitly.
	_, err = lookupElement(snapshot, `push button "Nope"`)
	if err == nil || !strings.Contains(err.Error(), "no element matches") {
		t.Fatalf("a selector without a match must be reported explicitly: %v", err)
	}

	// Numeric indices keep working.
	if record, err := lookupElement(snapshot, "8"); err != nil || record.Index != 8 {
		t.Fatalf("numeric indices must keep working: %v", err)
	}
}

// TestSelectorFallsBackToDescription covers description as the second identity
// source for selectors. Role plus name ranks first, description second, and
// positional selection last; an element index is
// the equivalent of nth. Adding descriptions raised the share of uniquely
// identifiable nodes across seven desktop apps from 55% to 66%.
func TestSelectorFallsBackToDescription(t *testing.T) {
	snapshot := &AppSnapshot{Elements: []ElementRecord{
		{Index: 3, ControlType: "push button", Name: "", Description: "Go back"},
		{Index: 4, ControlType: "push button", Name: "Save", Description: "Save the current file"},
		{Index: 9, ControlType: "push button", Name: "", Description: "Go forward"},
	}}

	// A button without a name is identified by its description.
	record, err := lookupElement(snapshot, `push button "Go back"`)
	if err != nil || record.Index != 3 {
		t.Fatalf("description should match element 3: %v %v", record, err)
	}

	// A name match must not be diluted into ambiguity by description matches.
	// "Save" is both the name of element 4 and part of its description; if the
	// two levels were merged, an exact name match could become ambiguous.
	record, err = lookupElement(snapshot, `push button "Save"`)
	if err != nil || record.Index != 4 {
		t.Fatalf("a name match takes precedence and must not be ambiguous: %v %v", record, err)
	}

	// The description level must also report ambiguity instead of silently
	// picking one.
	ambiguous := &AppSnapshot{Elements: []ElementRecord{
		{Index: 1, ControlType: "push button", Description: "Close"},
		{Index: 2, ControlType: "menu item", Description: "Close"},
	}}
	if _, err := lookupElement(ambiguous, `"Close"`); err == nil ||
		!strings.Contains(err.Error(), "ambiguous") {
		t.Fatalf("multiple description matches must also be reported as ambiguous: %v", err)
	}
}

func TestSemanticRefRoundTripUsesUniqueSemanticIdentity(t *testing.T) {
	snapshot := &AppSnapshot{
		WindowHandle: "pid145/win1/dialog",
		Elements: []ElementRecord{
			{Index: 3, ControlType: "push button", Name: "Save"},
			{Index: 4, ControlType: "toggle button", Name: "Menu", Description: "View options"},
			{Index: 5, ControlType: "toggle button", Name: "Menu", Description: "Show operations"},
		},
	}

	for _, index := range []int{3, 4, 5} {
		record := snapshot.Elements[index-3]
		ref := semanticRefForRecord(snapshot, record)
		if !strings.HasPrefix(ref, scopedSemanticRefPrefix) {
			t.Fatalf("record %d did not receive a semantic ref: %q", index, ref)
		}
		_, windowHandle, err := decodeSemanticRefScope(ref)
		if err != nil || windowHandle != snapshot.WindowHandle {
			t.Fatalf("semantic ref lost window scope: %q %q err=%v",
				ref, windowHandle, err)
		}
		resolved, err := lookupElement(snapshot, ref)
		if err != nil || resolved.Index != index {
			t.Fatalf("semantic ref %q resolved to %#v, err=%v", ref, resolved, err)
		}
	}

	duplicate := &AppSnapshot{Elements: []ElementRecord{
		{Index: 1, ControlType: "push button", Name: "OK"},
		{Index: 2, ControlType: "push button", Name: "OK"},
	}}
	if ref := semanticRefForRecord(duplicate, duplicate.Elements[0]); ref != "" {
		t.Fatalf("ambiguous identity must not receive a semantic ref: %q", ref)
	}
}

func TestGeneratedSemanticRefPreservesDescriptionIdentitySource(t *testing.T) {
	snapshot := &AppSnapshot{
		WindowHandle: "pid145/win1/dialog",
		Elements: []ElementRecord{
			{
				Index:       3,
				ControlType: "list item",
				Description: "Open the contents of your desktop in a folder",
			},
		},
	}
	ref := semanticRefForRecord(snapshot, snapshot.Elements[0])
	if !strings.HasPrefix(ref, scopedSemanticRefPrefix) {
		t.Fatalf("description-only record did not receive a semantic ref: %q", ref)
	}
	selector, _, err := decodeSemanticRefScope(ref)
	if err != nil {
		t.Fatal(err)
	}
	target, err := semanticTargetFromAddress(selector)
	if err != nil {
		t.Fatal(err)
	}
	if target.HasName || target.Name != "" {
		t.Fatalf("description was recoded as name: %#v", target)
	}
	if len(target.Predicates) != 1 ||
		target.Predicates[0].Kind != "desc" ||
		target.Predicates[0].Value !=
			"Open the contents of your desktop in a folder" {
		t.Fatalf("description provenance was lost: %#v", target)
	}
	resolved, err := lookupElement(snapshot, ref)
	if err != nil || resolved.Index != 3 {
		t.Fatalf("description ref %q resolved to %#v, err=%v",
			ref, resolved, err)
	}
}

func TestSemanticRefFailsClosedAfterIdentityBecomesAmbiguous(t *testing.T) {
	initial := &AppSnapshot{
		WindowHandle: "pid145/win0/frame",
		Elements: []ElementRecord{
			{Index: 1, ControlType: "push button", Name: "Save"},
		},
	}
	ref := semanticRefForRecord(initial, initial.Elements[0])
	current := &AppSnapshot{
		WindowHandle: initial.WindowHandle,
		Elements: []ElementRecord{
			{Index: 7, ControlType: "push button", Name: "Save"},
			{Index: 8, ControlType: "push button", Name: "Save"},
		},
	}
	if _, err := lookupElement(current, ref); err == nil ||
		!strings.Contains(err.Error(), "ambiguous") {
		t.Fatalf("semantic ref must fail closed after identity becomes ambiguous: %v", err)
	}
	current.WindowHandle = "pid145/win1/dialog"
	if _, err := lookupElement(current, ref); err == nil ||
		!strings.Contains(err.Error(), "different top-level window") {
		t.Fatalf("window-scoped semantic ref must fail across windows: %v", err)
	}
}

// TestSelectorPredicatesApplyOrFailLoudly checks that trailing selector
// constraints either take effect or produce an error; they must never be
// silently dropped. A parser that stops after the quoted name would accept
// each of these as a bare role and name:
//
//	push button "Save" [checked]
//	push button "OK" >> nth=0
//	menu item "Ruler" (12)
//	push button "Save" and then some garbage
//
// The caller would then believe the result honours a constraint it ignored.
func TestSelectorPredicatesApplyOrFailLoudly(t *testing.T) {
	snapshot := &AppSnapshot{Elements: []ElementRecord{
		{Index: 1, ControlType: "check menu item", Name: "Ruler", States: " [checked]"},
		{Index: 2, ControlType: "check menu item", Name: "Ruler"},
		{Index: 5, ControlType: "toggle button", Name: "Menu", Description: "View options"},
		{Index: 6, ControlType: "toggle button", Name: "Menu", Description: "Show operations"},
		{Index: 8, ControlType: "push button", Name: "Go", Actions: []string{"click", "press"}},
		{Index: 9, ControlType: "entry", Placeholder: "Search files"},
		{Index: 11, ControlType: "list item", Name: "Row", States: " [selected focused]"},
	}}

	// The predicate narrows the candidates: without it the selector is
	// ambiguous, with it the match is unique.
	if _, err := lookupElement(snapshot, `check menu item "Ruler"`); err == nil {
		t.Fatal("two elements named Ruler should be ambiguous")
	}
	record, err := lookupElement(snapshot, `check menu item "Ruler" [checked]`)
	if err != nil || record.Index != 1 {
		t.Fatalf("[checked] should narrow to element 1: %v %v", record, err)
	}

	// Snapshots print several states inside one bracket, so a group splits
	// into several constraints; this is the shape an agent copies from a line.
	if record, err = lookupElement(snapshot, `list item "Row" [selected focused]`); err != nil ||
		record.Index != 11 {
		t.Fatalf("a multi-word group should match word by word: %v %v", record, err)
	}
	if _, err = lookupElement(snapshot, `list item "Row" [selected expanded]`); err == nil {
		t.Fatal("a group with one unmatched word must not match")
	}

	// Elements sharing role and name can only be told apart by desc, as with
	// the three "Menu" toggle buttons in Nautilus.
	if record, err = lookupElement(snapshot, `toggle button "Menu" [desc="View options"]`); err != nil ||
		record.Index != 5 {
		t.Fatalf("[desc=…] should narrow to element 5: %v %v", record, err)
	}
	// desc alone, without a quoted name, must also work as an identity.
	if record, err = lookupElement(snapshot, `toggle button [desc="Show operations"]`); err != nil ||
		record.Index != 6 {
		t.Fatalf("role plus desc alone should be unique: %v %v", record, err)
	}
	if record, err = lookupElement(snapshot, `[placeholder="Search files"]`); err != nil ||
		record.Index != 9 {
		t.Fatalf("placeholder alone should be unique: %v %v", record, err)
	}
	if _, err = lookupElement(snapshot, `push button "Go" [actions=press]`); err == nil {
		t.Fatal("legacy [actions=…] selector must be rejected")
	}
	if record, err = lookupElement(snapshot, `push button "Go" [a=press]`); err != nil ||
		record.Index != 8 {
		t.Fatalf("[a=…] should match element 8: %v %v", record, err)
	}

	// Anything unrecognised is an error that names the unread remainder.
	for _, selector := range []string{
		`push button "OK" >> nth=0`,
		`menu item "Ruler" (12)`,
		`push button "Save" and then some garbage`,
		`push button "Save" [disabled]`,
		`push button "Save" [level=2]`,
		`push button "Save" [desc=View options]`, // unquoted value
		`push button "Save" [checked`,            // unclosed bracket
	} {
		_, err := lookupElement(snapshot, selector)
		if err == nil {
			t.Fatalf("%q should fail instead of silently dropping a constraint", selector)
		}
		if !strings.Contains(err.Error(), "trailing part I cannot read") &&
			!strings.Contains(err.Error(), "no element matches") {
			t.Fatalf("error for %q does not say the trailing part is unreadable: %v", selector, err)
		}
	}

	// The error must list the supported syntax and state that >>, nth= and
	// :right-of do not exist; otherwise the model invents web-locator syntax.
	_, err = lookupElement(snapshot, `push button "OK" >> nth=0`)
	for _, want := range []string{"[checked]", "desc=", "nth=", ">>", ":right-of"} {
		if !strings.Contains(err.Error(), want) {
			t.Fatalf("error is missing %q: %v", want, err)
		}
	}

	// When predicates filter out every candidate, the likeliest cause is a
	// state change rather than a mistyped name, so the hint says that first.
	_, err = lookupElement(snapshot, `check menu item "Ruler" [expanded]`)
	if err == nil || !strings.Contains(err.Error(), "may have changed since the snapshot") {
		t.Fatalf("when predicates filter out every candidate, the hint should say the state may have changed: %v", err)
	}
	// The parsed predicates are echoed so the caller sees how the selector was
	// read.
	if !strings.Contains(err.Error(), "predicates [expanded]") {
		t.Fatalf("call log does not echo the parsed predicates: %v", err)
	}
}

func TestVerifyAcceptsNumericIndicesNotJustSelectors(t *testing.T) {
	// verify must resolve a numeric index, not only a selector. Treating "62"
	// as a selector reports "no element matches the selector" while the element
	// exists, and a false negative from an assertion is worse than none.
	snapshot := &AppSnapshot{Elements: []ElementRecord{
		{Index: 62, ControlType: "text", Value: "baseline-marker", States: "[focused]"},
	}}
	record, err := lookupElementFor(snapshot, "62", "", "verify")
	if err != nil || record == nil {
		t.Fatalf("numeric index must resolve, got record=%v err=%v", record, err)
	}
	if ok, _ := (verifyGoal{textContains: "baseline"}).check(record, nil); !ok {
		t.Fatal("resolved record should satisfy text_contains")
	}
}

func TestIntentGuardSkipsElementsWithVeryShortNames(t *testing.T) {
	// Tokenisation drops tokens shorter than two characters to filter noise
	// such as "a", so an element named "L" (a size filter, an option letter, a
	// column header) can never match any declaration, and "Size L checkbox"
	// would be rejected against `check box "L"`. The guard is worth an
	// occasional false rejection, not a certain one for a whole class of
	// elements, so such names are skipped.
	short := &ElementRecord{Name: "L", ControlType: "check box"}
	if got := elementIntentMismatch(short, "Size L checkbox"); got != "" {
		t.Fatalf("a one-character name cannot be checked and must not be rejected: %s", got)
	}
	if got := elementIntentMismatch(short, "the Save button"); got != "" {
		t.Fatalf("one-character names must always skip the check, even for a mismatched declaration: %s", got)
	}
	// With a description the check is still possible and must still apply.
	withDesc := &ElementRecord{Name: "L", ControlType: "check box", Description: "Large size filter"}
	if elementIntentMismatch(withDesc, "the Save button") == "" {
		t.Fatal("an element with a description should still be checked")
	}
	// Names of normal length keep the usual check.
	normal := &ElementRecord{Name: "Save", ControlType: "push button"}
	if elementIntentMismatch(normal, "the New button") == "" {
		t.Fatal("guard must still reject mismatches on names of normal length")
	}
}

func TestObservationRootShortRefExpandsThroughRegistry(t *testing.T) {
	service := NewService()
	ref := service.RefRegistry.Acquire(
		"editor",
		"w2:stable-window",
		`group "Features"`,
		CanonicalRole("group"),
		"Features",
		"c1",
		":1.42", "/org/a11y/atspi/accessible/features", "99123",
		":1.42", "/org/a11y/atspi/accessible/window/1", "99123",
		"frame", "Editor",
	)
	expanded, err := service.expandObservationRootShortRef(
		"editor",
		map[string]any{"ref": ref},
	)
	if err != nil {
		t.Fatal(err)
	}
	target, ok := expanded["ref"].(string)
	if !ok || !isSemanticRef(target) {
		t.Fatalf("root ref did not expand to a scoped semantic ref: %#v", expanded)
	}
	selector, window, err := decodeSemanticRefScope(target)
	if err != nil {
		t.Fatal(err)
	}
	if selector != `group "Features"` || window != "w2:stable-window" {
		t.Fatalf(
			"unexpected expanded root: selector=%q window=%q",
			selector,
			window,
		)
	}
}

// Words such as "the" and "dialog" name no element, so an intent made only
// of them lists no closest elements.
func TestClosestElementsIgnoresGenericWords(t *testing.T) {
	snapshot := &AppSnapshot{Elements: []ElementRecord{
		{Index: 1, ControlType: "dialog", Name: "The dialog"},
		{Index: 2, ControlType: "push button", Name: "Save"},
	}}
	if got := closestElements(snapshot, "the dialog", 5); got != nil {
		t.Fatalf("generic words listed %q", got)
	}
	if got := closestElements(snapshot, "save dialog", 5); len(got) != 1 || !strings.Contains(got[0], "Save") {
		t.Fatalf("closest elements for save = %q", got)
	}
}
