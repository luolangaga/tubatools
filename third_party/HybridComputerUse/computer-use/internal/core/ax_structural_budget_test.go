package core

import (
	"fmt"
	"testing"
)

func TestSelectAXBudgetPlanRejectsNestedCandidates(t *testing.T) {
	leaf := &AXNode{ID: "leaf", Role: "paragraph"}
	child := &AXNode{ID: "child", Role: "group", Children: []*AXNode{leaf}}
	leaf.Parent = child
	root := &AXNode{ID: "root", Role: "window", Children: []*AXNode{child}}
	child.Parent = root

	plan := selectAXBudgetPlan(
		[]*AXNode{root},
		[]axBudgetCandidate{
			{
				parentID: "root",
				nodeIDs:  []string{"child"},
				priority: 10,
				savings:  5,
			},
			{
				parentID: "child",
				nodeIDs:  []string{"leaf"},
				priority: 10,
				savings:  10,
			},
		},
		10,
	)
	if len(plan) != 1 || plan[0].nodeIDs[0] != "leaf" {
		t.Fatalf("nested candidates were selected together: %#v", plan)
	}
}

func TestStructuralBudgetTreatsForceKeepAsProtectedContext(t *testing.T) {
	node := &AXNode{ID: "context", Role: "paragraph", ForceKeep: true}
	if !axSubtreeContainsCriticalOrBoundary(node) {
		t.Fatal("force-kept context was eligible for structural omission")
	}
}

func budgetTestNode(id string, role CanonicalRole, name, ref string, children ...*AXNode) *AXNode {
	node := &AXNode{ID: id, Role: role, Name: name, Ref: ref, Children: children}
	for _, child := range children {
		child.Parent = node
	}
	return node
}

func budgetTestFind(nodes []*AXNode, match func(*AXNode) bool) []*AXNode {
	found := []*AXNode{}
	for _, node := range nodes {
		if match(node) {
			found = append(found, node)
		}
		found = append(found, budgetTestFind(node.Children, match)...)
	}
	return found
}

// A browser window as Chromium exports it: the navigation buttons sit in an
// unnamed wrapper inside the toolbar, and the page is a long run of card
// groups. The budget used to rank a run by its own top nodes only, so the
// unnamed wrapper ranked lowest and the toolbar lost every ref, while each
// card group (a semantic boundary) kept its shell and got its own summary.
func TestStructuralBudgetKeepsWrappedControlsAndTrimsLongRunsFromTheEnd(t *testing.T) {
	navigation := []*AXNode{}
	for index, name := range []string{"Back", "Forward", "Reload", "Address and search bar", "Bookmark", "Extensions", "Profile", "Chrome"} {
		navigation = append(navigation, budgetTestNode(fmt.Sprintf("nav-%d", index), "button", name, fmt.Sprintf("r%d", index+1)))
	}
	toolbar := budgetTestNode("toolbar", "toolbar", "", "", budgetTestNode("wrapper", RoleGeneric, "", "", navigation...))
	cards := []*AXNode{}
	for index := 0; index < 150; index++ {
		cards = append(cards, budgetTestNode(fmt.Sprintf("card-%d", index), "group", "", "",
			budgetTestNode(fmt.Sprintf("title-%d", index), "link", fmt.Sprintf("Video title number %d about topic %d", index, index%7), fmt.Sprintf("r%d", 100+2*index)),
			budgetTestNode(fmt.Sprintf("channel-%d", index), "text", fmt.Sprintf("Channel %d", index%5), ""),
			budgetTestNode(fmt.Sprintf("views-%d", index), "text", fmt.Sprintf("%d views", index*13), ""),
			budgetTestNode(fmt.Sprintf("more-%d", index), "button", "More options", fmt.Sprintf("r%d", 101+2*index)),
		))
	}
	// Chromium flattens the page landmarks, so the header controls and the
	// navigation links are siblings of the cards.
	page := []*AXNode{
		budgetTestNode("menu", "button", "Menu", "r40"),
		budgetTestNode("search", "textbox", "Search", "r41"),
	}
	for index := 0; index < 30; index++ {
		page = append(page, budgetTestNode(fmt.Sprintf("section-%d", index), "link", fmt.Sprintf("Section %d", index), fmt.Sprintf("r%d", 500+index)))
	}
	document := budgetTestNode("document", "document", "FeedView", "r50", append(page, cards...)...)
	document.States.Active = TriTrue
	root := budgetTestNode("window", "window", "FeedView - Google Chrome", "r60", toolbar, document)

	// Distillation marks each card as the context that tells its "More
	// options" button apart from the others.
	markAXDistinguishingAncestors([]*AXNode{root})
	if !cards[0].ForceKeep {
		t.Fatal("fixture: cards are not marked as distinguishing context")
	}
	context := &DistillationContext{}
	roots := applyAXStructuralBudget(context, []*AXNode{root})

	if context.StageBudgetExhausted || context.StageBudgetCharsAfter > axStructuralTreeBudget() {
		t.Fatalf("tree still over budget: %d chars", context.StageBudgetCharsAfter)
	}
	if kept := budgetTestFind(roots, func(node *AXNode) bool { return node.Name == "Address and search bar" }); len(kept) != 1 {
		t.Fatal("the toolbar controls inside an unnamed wrapper were omitted")
	}
	if first := budgetTestFind(roots, func(node *AXNode) bool { return node.ID == "more-0" }); len(first) != 1 {
		t.Fatal("the first card lost its controls")
	}
	summaries := budgetTestFind(roots, func(node *AXNode) bool { return node.Properties.Omitted != "" })
	if len(summaries) > 3 {
		t.Fatalf("%d omission summaries; a long run should be cut from its end", len(summaries))
	}
	shells := budgetTestFind(roots, func(node *AXNode) bool {
		return node.Role == "group" && len(node.Children) == 1 && node.Children[0].Properties.Omitted != ""
	})
	if len(shells) != 0 {
		t.Fatalf("%d card shells kept only to hold a summary", len(shells))
	}
}

// A spreadsheet window: a long run of toolbars beside the sheet. The run is
// not a collection, so the sheet keeps its frame and only its cells are cut.
func TestStructuralBudgetDoesNotFoldATableInAMixedRun(t *testing.T) {
	siblings := []*AXNode{}
	for index := 0; index < 12; index++ {
		siblings = append(siblings, budgetTestNode(fmt.Sprintf("bar-%d", index), "toolbar", fmt.Sprintf("Toolbar %d", index), "",
			budgetTestNode(fmt.Sprintf("bar-%d-button", index), "button", fmt.Sprintf("Tool %d", index), fmt.Sprintf("r%d", 900+index))))
	}
	cells := []*AXNode{}
	for index := 0; index < 1200; index++ {
		cells = append(cells, budgetTestNode(fmt.Sprintf("cell-%d", index), "cell", fmt.Sprintf("A%d", index+1), fmt.Sprintf("r%d", 1000+index)))
		cells[index].Properties.Value = fmt.Sprintf("row %d value", index)
	}
	table := budgetTestNode("table", "table", "Sheet1", "", cells...)
	siblings = append(siblings, table)
	root := budgetTestNode("window", "window", "big.csv - Calc", "r1", siblings...)

	context := &DistillationContext{}
	roots := applyAXStructuralBudget(context, []*AXNode{root})

	if kept := budgetTestFind(roots, func(node *AXNode) bool { return node.ID == "cell-0" }); len(kept) != 1 {
		t.Fatal("the sheet was folded as a whole")
	}
	// The sheet is the largest run, so it is cut first, but its first cells
	// (the top-left of the view) stay.
	if kept := budgetTestFind(roots, func(node *AXNode) bool { return node.Role == "cell" }); len(kept) < 40 {
		t.Fatalf("only %d cells kept", len(kept))
	}
	// Adjacent chunks of one run share a single summary, so the plan must
	// not remove far more than the budget requires.
	if after := context.StageBudgetCharsAfter; after > axStructuralTreeBudget() || after < axStructuralTreeBudget()*3/4 {
		t.Fatalf("%d chars after the budget of %d", after, axStructuralTreeBudget())
	}
}
