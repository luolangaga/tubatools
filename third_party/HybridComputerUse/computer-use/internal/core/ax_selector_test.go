package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestWorkingClickPredicateMatchesCanonicalState(t *testing.T) {
	predicates, err := parsePredicates("[click]")
	if err != nil {
		t.Fatalf("working click predicate did not parse: %v", err)
	}
	record := &ElementRecord{
		ControlType: "push button",
		Name:        "Save",
		States:      "[focused has-click-action]",
	}
	if !recordMatchesPredicates(record, predicates) {
		t.Fatal("[click] must match the canonical has-click-action state")
	}
	if _, err := parsePredicates("[has-click-action]"); err == nil {
		t.Fatal("legacy has-click-action selector must be rejected")
	}
}

func TestStructuredSelectorAcceptsStandardOrProviderRole(t *testing.T) {
	snapshot := &AppSnapshot{
		Elements: []ElementRecord{
			{Index: 1, ControlType: "push button", Name: "Save"},
		},
	}
	for _, selector := range []string{`button "Save"`, `push button "Save"`} {
		record, err := lookupBySelector(snapshot, selector, "")
		if err != nil {
			t.Fatalf("selector %q failed: %v", selector, err)
		}
		if record.Index != 1 {
			t.Fatalf("selector %q resolved %#v", selector, record)
		}
	}
}

// Thunderbird exposes its modal "New Folder" dialog
// as a top-level frame. A selector for role dialog matches the root of a
// window the provider reports as modal, and not the root of a plain one.
func TestDialogSelectorMatchesAModalTopLevelWindow(t *testing.T) {
	root := ElementRecord{Index: 0, ControlType: "frame", Name: "New Folder",
		ProviderBusName: ":1.84", ProviderObjectPath: "/window/2"}
	for _, modal := range []bool{true, false} {
		snapshot := &AppSnapshot{
			Elements: []ElementRecord{root},
			Windows: []TopLevelWindow{{Title: "New Folder", Role: "frame", Modal: modal,
				ProviderBusName: ":1.84", ProviderObjectPath: "/window/2"}},
		}
		record, err := lookupBySelector(snapshot, `dialog "New Folder"`, "")
		if matched := err == nil && record.Index == 0; matched != modal {
			t.Fatalf("modal=%v: record=%#v err=%v", modal, record, err)
		}
	}
}

// TestSelectorFindsTheRealClosingQuote checks that the closing quote of a name
// is its real match rather than the last quote in the selector, including when
// the name contains quotes or backslashes. Taking the last quote only works
// without a suffix; a trailing [desc="..."] would be swallowed into the name.
// Models write role "menu item"; the model sees "menuitem" in
// the tree and models also write "menu_item". Every spelling must name the
// same role, in an until selector and in find, without widening to other
// roles.
func TestSelectorAndFindRoleSpellingsNameTheSameRole(t *testing.T) {
	item := &ElementRecord{Index: 1, ControlType: "menu item", LocalizedControlType: "menu item", Name: "Paragraph..."}
	button := &ElementRecord{Index: 2, ControlType: "push button", Name: "Paragraph..."}
	snapshot := &AppSnapshot{Elements: []ElementRecord{*item, *button}}
	for _, role := range []string{"menu item", "menuitem", "menu_item", "Menu-Item", "MenuItem"} {
		if !selectorRoleMatches(item, role) || selectorRoleMatches(button, role) {
			t.Fatalf("selector role %q", role)
		}
		if !recordMatchesFind(item, role, "", "", "", false, false) ||
			recordMatchesFind(button, role, "", "", "", false, false) {
			t.Fatalf("find role %q", role)
		}
		address, err := axSelectorAddress(map[string]any{"role": role, "name": "Paragraph..."})
		if err != nil {
			t.Fatal(err)
		}
		if record, err := lookupBySelectorFor(snapshot, address, "", "until"); err != nil || record.Index != 1 {
			t.Fatalf("until selector role %q: %v", role, err)
		}
	}
	for _, role := range []string{"push_button", "push-button", "pushbutton", "button"} {
		if !selectorRoleMatches(button, role) || selectorRoleMatches(item, role) {
			t.Fatalf("selector role %q", role)
		}
	}
}

func TestSelectorFindsTheRealClosingQuote(t *testing.T) {
	role, name, hasName, rest := parseSelector(`toggle button "Menu" [desc="View options"]`)
	if role != "toggle button" || name != "Menu" || !hasName {
		t.Fatalf("wrong closing quote: role=%q name=%q", role, name)
	}
	if rest != `[desc="View options"]` {
		t.Fatalf("wrong remainder: %q", rest)
	}

	// The renderer's quoted() escapes quotes as \", so parsing must unescape
	// them.
	_, name, _, rest = parseSelector(`static "say \"hi\"" [focused]`)
	if name != `say "hi"` {
		t.Fatalf("escapes were not restored: %q", name)
	}
	if rest != "[focused]" {
		t.Fatalf("wrong remainder with escapes: %q", rest)
	}

	// A `]` inside a desc value must not end the predicate.
	preds, err := parsePredicates(`[desc="a] b"]`)
	if err != nil || len(preds) != 1 || preds[0].value != "a] b" {
		t.Fatalf("a ] inside quotes must not end the predicate: %v %v", preds, err)
	}
}

func TestWorkingProjectionNormalizedSelectorRoundTrips(t *testing.T) {
	snapshot := &AppSnapshot{
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{}, ControlType: "list", Name: "Results"},
			{
				Index:       1,
				RuntimeID:   []int{0},
				ControlType: "list item",
				Name:        "2024 Sedan\n  Low mileage",
			},
		},
	}
	if err := NewService().BuildAndRenderAXSnapshot(snapshot, contract.TreeViewWorking); err != nil {
		t.Fatalf("working projection failed: %v", err)
	}
	if !strings.Contains(strings.Join(snapshot.TreeLines, "\n"), `listitem "2024 Sedan Low mileage"`) {
		t.Fatalf("working tree did not expose normalized identity: %v", snapshot.TreeLines)
	}
	record, err := lookupBySelectorFor(
		snapshot,
		`list item "2024 Sedan Low mileage"`,
		"",
		"test",
	)
	if err != nil || record == nil || record.Index != 1 {
		t.Fatalf("normalized working selector did not round-trip: record=%#v err=%v", record, err)
	}
}

func TestNormalizedSelectorAmbiguityFailsClosed(t *testing.T) {
	snapshot := &AppSnapshot{Elements: []ElementRecord{
		{Index: 1, ControlType: "entry", Name: "Account\tName"},
		{Index: 2, ControlType: "entry", Name: "Account Name"},
	}}
	record, err := lookupBySelectorFor(
		snapshot,
		`entry "Account Name"`,
		"",
		"test",
	)
	if err == nil || record != nil ||
		!strings.Contains(strings.ToLower(err.Error()), "ambiguous") {
		t.Fatalf("normalized identity collision did not fail closed: record=%#v err=%v", record, err)
	}
}
