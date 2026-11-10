package core

import (
	"strconv"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestFindAcceptsCanonicalModelRole(t *testing.T) {
	record := &ElementRecord{
		ControlType:          "entry",
		LocalizedControlType: "entrada",
		Name:                 "Name",
	}
	if !recordMatchesFind(
		record, "textbox", "Name", "", "", true, false,
	) {
		t.Fatal("find rejected canonical textbox role for provider entry")
	}
}

func TestFindExactMatchingHandlesShortLabels(t *testing.T) {
	records := []ElementRecord{
		{ControlType: "check box", Name: "L"},
		{ControlType: "label", Name: "Large"},
		{ControlType: "label", Name: "Sale"},
		{ControlType: "entry", Placeholder: "To"},
		{ControlType: "label", Name: "Tokyo"},
	}
	count := func(name, text string, nameExact, textExact bool) int {
		total := 0
		for index := range records {
			if recordMatchesFind(
				&records[index],
				"",
				name,
				text,
				"",
				nameExact,
				textExact,
			) {
				total++
			}
		}
		return total
	}
	if got := count("L", "", false, false); got != 3 {
		t.Fatalf("substring name matches=%d want 3", got)
	}
	if got := count("L", "", true, false); got != 1 {
		t.Fatalf("exact name matches=%d want 1", got)
	}
	if got := count("", "To", false, false); got != 2 {
		t.Fatalf("substring text matches=%d want 2", got)
	}
	if got := count("", "To", false, true); got != 1 {
		t.Fatalf("exact text matches=%d want 1", got)
	}
}

func TestFindFiltersAreAnded(t *testing.T) {
	snapshot := &AppSnapshot{
		WindowTitle: "Untitled 1",
		Elements: []ElementRecord{
			{Index: 3, ControlType: "push button", Name: "Save", States: "enabled focused"},
			{Index: 4, ControlType: "push button", Name: "Save As…", States: "enabled"},
			{Index: 5, ControlType: "toggle button", Name: "Bold", States: "enabled checked"},
			{Index: 6, ControlType: "text", Placeholder: "Search files"},
		},
	}

	cases := []struct {
		role, name, text, state string
		want                    []int
	}{
		// role matches as a substring, so "button" finds both kinds of button.
		{role: "button", want: []int{3, 4, 5}},
		// Multiple filters are combined with AND.
		{role: "button", state: "checked", want: []int{5}},
		{name: "save", want: []int{3, 4}},
		// text also matches the placeholder: the same search box exposes its
		// label as a placeholder in GTK and as the name in Electron, and the
		// agent should not need to know which.
		{text: "search files", want: []int{6}},
		{role: "button", name: "nonexistent", want: nil},
	}
	for _, c := range cases {
		got := []int{}
		for i := range snapshot.Elements {
			if recordMatchesFind(&snapshot.Elements[i], c.role, c.name, c.text, c.state, false, false) {
				got = append(got, snapshot.Elements[i].Index)
			}
		}
		if len(got) != len(c.want) {
			t.Fatalf("query %+v: want %v, got %v", c, c.want, got)
		}
		for i := range got {
			if got[i] != c.want[i] {
				t.Fatalf("query %+v: want %v, got %v", c, c.want, got)
			}
		}
	}
}

func TestFindTextMatchReportsOriginatingField(t *testing.T) {
	record := &ElementRecord{
		Name:        "",
		Description: "SMTP user synthetic-user@example.invalid",
		Value:       "unrelated",
	}
	field, value, ok := MatchedFindTextField(
		record,
		"synthetic-user",
		false,
	)
	if !ok || field != "description" ||
		value != record.Description {
		t.Fatalf("find match origin=%q %q %t", field, value, ok)
	}
}

func TestFindPageRendersOriginatingTextField(t *testing.T) {
	service := NewServiceWithBackend(&recordingPlatformBackend{})
	snapshot := editorFormSnapshot()
	snapshot.Elements[3].Description = "Account synthetic-user@example.invalid"
	if err := service.BuildAndRenderAXSnapshot(
		snapshot,
		contract.TreeViewWorking,
	); err != nil {
		t.Fatal(err)
	}
	matched := snapshot.Elements[3]
	matched.FindMatchedField = "description"
	matched.FindMatchedText = matched.Description
	result := service.RenderFindPage(
		snapshot,
		[]ElementRecord{matched},
		0,
		1,
		`text_contains="synthetic-user"`,
		nil,
		false,
	)
	text := ResultText(result)
	if result.IsError ||
		!strings.Contains(text, "matched_field=description") ||
		!strings.Contains(text, strconv.Quote(matched.Description)) {
		t.Fatalf("find output hid match origin:\n%s", text)
	}
}
