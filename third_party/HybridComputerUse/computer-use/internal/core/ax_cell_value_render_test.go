package core

import (
	"fmt"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// A spreadsheet names its cells by address, so the content is only in the
// value; GTK and Qt item views name a cell by its text.
func cellValueSnapshot() *AppSnapshot {
	long := strings.Repeat("quarterly revenue ", 6)
	return &AppSnapshot{
		App:          AppDescriptor{Name: "Sheets", PID: 42},
		WindowTitle:  "Book",
		WindowHandle: "0x42",
		SnapshotID:   "cells",
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Book"},
			{Index: 1, RuntimeID: []int{0, 0}, ControlType: "table", Name: "Sheet1"},
			{Index: 2, RuntimeID: []int{0, 0, 0, 0}, ControlType: "table cell", Name: "A1", Value: "Year"},
			{Index: 3, RuntimeID: []int{0, 0, 0, 1}, ControlType: "table cell", Name: "B1", Value: long},
			{Index: 4, RuntimeID: []int{0, 0, 0, 2}, ControlType: "table cell", Name: "Alpha", Value: "Alpha"},
			{Index: 5, RuntimeID: []int{0, 0, 0, 3}, ControlType: "table cell", Name: "C1"},
		},
		Scope: contract.AXScopeWindow,
	}
}

func TestCellValueIsShownInlineAfterItsName(t *testing.T) {
	text := typedTreeTextForTest(t, cellValueSnapshot())
	if !strings.Contains(text, `cell "A1" = "Year"`) {
		t.Fatalf("cell value missing from the tree:\n%s", text)
	}
	if !strings.Contains(text, `cell "B1" = "quarterly revenue quarterly revenue`) ||
		!strings.Contains(text, `…"`) ||
		strings.Contains(text, strings.Repeat("quarterly revenue ", 4)) {
		t.Fatalf("long cell value was not clipped inline:\n%s", text)
	}
	if strings.Contains(text, `cell "Alpha" =`) || strings.Contains(text, `cell "C1" =`) {
		t.Fatalf("a value equal to the name or an empty value was rendered:\n%s", text)
	}
	if strings.Contains(text, "/value:") {
		t.Fatalf("cell value rendered as a property line:\n%s", text)
	}
}

func TestFindCellMatchDoesNotRepeatTheInlineValue(t *testing.T) {
	snapshot := cellValueSnapshot()
	typed, err := buildAXSnapshot(snapshot)
	if err != nil {
		t.Fatal(err)
	}
	unit := FindMatchUnit(typed, snapshot.Elements[2], 1)
	if !strings.Contains(unit, `cell "A1" = "Year"`) || strings.Contains(unit, `text="Year"`) {
		t.Fatalf("find unit should show the value once, inline:\n%s", unit)
	}
	clipped := FindMatchUnit(typed, snapshot.Elements[3], 2)
	if !strings.Contains(clipped, `text="quarterly revenue`) {
		t.Fatalf("find unit should keep the longer text of a clipped value:\n%s", clipped)
	}
}

func TestCellValueChangeChangesTheDiffSignature(t *testing.T) {
	before := &AXNode{Role: "cell", Name: "A1", Properties: AXProperties{Value: "2020"}}
	after := &AXNode{Role: "cell", Name: "A1", Properties: AXProperties{Value: "2021"}}
	if axPropertySignature(before) == axPropertySignature(after) {
		t.Fatal("a changed cell value left the diff signature unchanged")
	}
}

func TestStructuralBudgetTrimsALongRunFromItsEnd(t *testing.T) {
	table := &AXNode{ID: "table", Role: "table", Name: "Sheet1"}
	root := &AXNode{ID: "root", Role: "window", Name: "Book", Children: []*AXNode{table}}
	table.Parent = root
	for index := 0; index < 500; index++ {
		table.Children = append(table.Children, &AXNode{
			ID:         fmt.Sprintf("cell-%d", index),
			Role:       "cell",
			Name:       fmt.Sprintf("A%d", index+1),
			Ref:        fmt.Sprintf("r%d", index+1),
			Properties: AXProperties{Value: fmt.Sprintf("row %d", index+1)},
			Parent:     table,
		})
	}
	context := &DistillationContext{}
	applyAXStructuralBudget(context, []*AXNode{root})

	children := table.Children
	last := children[len(children)-1]
	if children[0].ID != "cell-0" || last.BudgetOmittedNodes == 0 {
		t.Fatalf("the run was not trimmed from its end: first=%s last=%s",
			children[0].ID, last.ID)
	}
	kept := len(children) - 1
	if kept < 200 || last.BudgetOmittedNodes != 500-kept {
		t.Fatalf("kept %d cells, summary omits %d", kept, last.BudgetOmittedNodes)
	}
	if context.StageBudgetCharsAfter > axStructuralTreeBudget() {
		t.Fatalf("tree still over budget: %d chars", context.StageBudgetCharsAfter)
	}
}
