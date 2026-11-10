package core

import "testing"

func TestIndexedSemanticSelectorsMatchFullScan(t *testing.T) {
	records := []ElementRecord{
		{ControlType: "frame", Name: "Editor"},
		{ControlType: "push button", Name: "Save", Description: "Save draft"},
		{ControlType: "push button", Name: "Save", Description: "Save copy"},
		{ControlType: "push button", Name: "Open"},
		{ControlType: "link", Name: "Item 7"},
		{ControlType: "table cell", Name: "Item 7"},
		{ControlType: "label", Name: "Two  words"},
		{ControlType: "label", Name: "Two words"},
		{ControlType: "toggle button", Description: "View options"},
		{ControlType: "toggle button", Description: "Sort"},
		{ControlType: "toggle button", Description: "Sort"},
		{ControlType: "entry", Placeholder: "Search"},
		{ControlType: "push button", Description: "Open"},
		{ControlType: "menu item", Name: "Close", Description: "Editor"},
	}
	snapshot := &AppSnapshot{WindowHandle: "window-1"}
	for index, record := range records {
		record.Index = index
		record.RuntimeID = []int{0, index}
		snapshot.Elements = append(snapshot.Elements, record)
	}
	identities := newSemanticIdentityIndex(snapshot)
	unique := 0
	for _, record := range snapshot.Elements {
		want := semanticSelectorForRecord(snapshot, record)
		if got := indexedSemanticSelector(snapshot, identities, record); got != want {
			t.Fatalf("record %d: indexed selector %q, full scan %q", record.Index, got, want)
		}
		if want == "" {
			continue
		}
		unique++
		match, err := lookupBySelector(snapshot, want, "")
		if err != nil || match.Index != record.Index {
			t.Fatalf("record %d: selector %q does not resolve back: %v", record.Index, want, err)
		}
	}
	if unique < 8 {
		t.Fatalf("only %d records got a selector; the fixture no longer covers the cases", unique)
	}
}
