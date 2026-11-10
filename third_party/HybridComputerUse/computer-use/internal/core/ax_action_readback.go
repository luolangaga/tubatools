package core

import (
	"fmt"
)

// incrementalTree replaces the full tree with only the changed lines when
// that is safe and cheaper. An incremental view is not always smaller: when
// the structure changes, removed and added lines together can exceed the full
// tree. It then reports false and the caller returns the full tree.
func incrementalTree(before, after *AppSnapshot) ([]string, bool) {
	if before != nil && after != nil &&
		before.AXSnapshot != nil && after.AXSnapshot != nil {
		return incrementalAXSnapshot(before, after)
	}
	if before == nil || after == nil {
		return nil, false
	}
	if before.WindowHandle != "" && after.WindowHandle != "" &&
		before.WindowHandle != after.WindowHandle {
		return nil, false
	}
	if normalizedTreeView(before.projection.View) !=
		normalizedTreeView(after.projection.View) {
		return nil, false
	}
	if len(after.TreeLines) == 0 {
		return nil, false
	}
	old, now := before.TreeLines, after.TreeLines

	// Trim the common prefix and suffix; the remaining middle span is what the
	// action changed. This expresses insertions and deletions, so the line
	// count may differ, as it does for the most common action effects: opening
	// a menu, showing a dialog, or adding or removing a list row. When changes
	// sit at both ends, the middle span covers the whole tree and the ratio
	// checks below fall back to the full tree.
	prefix := 0
	for prefix < len(old) && prefix < len(now) && old[prefix] == now[prefix] {
		prefix++
	}
	suffix := 0
	for suffix < len(old)-prefix && suffix < len(now)-prefix &&
		old[len(old)-1-suffix] == now[len(now)-1-suffix] {
		suffix++
	}
	removed := old[prefix : len(old)-suffix]
	added := now[prefix : len(now)-suffix]

	// With equal lengths, compare line by line: often only a few lines of the
	// middle span changed. Long lines whose content alone changed are condensed
	// here so that a -old/+new pair cannot outgrow the tree; see
	// condenseLinePair.
	var changed []string
	if len(removed) == len(added) {
		var r, a []string
		for i := range added {
			if removed[i] == added[i] {
				continue
			}
			if summary, ok := condenseLinePair(removed[i], added[i]); ok {
				changed = append(changed, summary)
				continue
			}
			r = append(r, removed[i])
			a = append(a, added[i])
		}
		removed, added = r, a
	}

	if len(removed)+len(added)+len(changed) == 0 {
		return []string{
			fmt.Sprintf(
				"Incremental view: 0 changes across %d tree lines. The full tree is "+
					"byte-identical to the previous delivered tree state and is not repeated; every "+
					"previously displayed rN ref remains bound unless separately reported stale. "+
					"This is already a fresh post-action observation; request a rooted "+
					"observation only when the next decision needs omitted local context.",
				len(now),
			),
		}, true
	}
	// When a third or more of the tree changed, the full tree reads better.
	// Count affected tree lines (the larger side), not output lines: a
	// replaced line prints as -old and +new but changes one tree line, and
	// counting both would reject every single-line change on a small tree.
	affected := len(removed)
	if len(added) > affected {
		affected = len(added)
	}
	affected += len(changed)

	// Byte limits complement the line-count rule, because line count is a
	// poor proxy for cost. A control that holds all its content in one value
	// (a terminal, log view or large text field) is a single tree line that
	// may carry most of the tree's bytes. Counting lines alone either resends
	// that line in full (one changed line in a three-line tree trips the
	// one-third rule) or emits -old and +new copies that together exceed the
	// full tree. Hence the hard invariant that the incremental payload is
	// never longer than the full tree, and clearly cheaper byte counts bypass
	// the line-count rule. The fixed header is excluded from the comparison,
	// or it would outweigh any increment on a small tree.
	payload := 0
	for _, line := range removed {
		payload += len(line) + 2
	}
	for _, line := range added {
		payload += len(line) + 2
	}
	for _, line := range changed {
		payload += len(line) + 1
	}
	full := 0
	for _, line := range now {
		full += len(line) + 1
	}
	if payload >= full {
		return nil, false
	}
	const (
		maxActionDiffChars   = 6000
		maxActionDiffPercent = 35
	)
	if payload > maxActionDiffChars ||
		(len(now) >= 50 &&
			full > 0 &&
			payload*100 > full*maxActionDiffPercent) {
		return nil, false
	}
	if affected*3 >= len(now) && payload*2 > full {
		return nil, false
	}

	out := make([]string, 0, len(removed)+len(added)+len(changed)+1)
	header := fmt.Sprintf(
		"Incremental view: %d line(s) gone, %d line(s) new, out of %d lines now in the "+
			"tree. Every line not shown here is byte-identical to the previous "+
			"delivered tree state. Existing rN refs remain bound unless separately reported "+
			"stale. Lines are prefixed - for "+
			"gone and + for new. This is already a fresh post-action observation; "+
			"use a rooted observe or find only when the next decision needs context "+
			"that is not present here.",
		len(removed), len(added), len(now))
	if len(changed) > 0 {
		header += fmt.Sprintf(
			" %d line(s) kept their identity and only changed content: those are "+
				"prefixed ~ and are shown as a single summary line each, with the "+
				"unchanged head kept for identification and the edit itself written "+
				"inline as [-removed][+added]. Nothing outside the marked span moved.",
			len(changed))
	}
	out = append(out, header)
	for _, line := range changed {
		out = append(out, line)
	}
	for _, line := range removed {
		out = append(out, "-"+line)
	}
	for _, line := range added {
		out = append(out, "+"+line)
	}
	return out, true
}

// condenseLinePair condenses a pair of lines for the same node, whose content
// alone changed, into one change summary.
//
// A line diff has a structural flaw: its unit is the line, while a control can
// hold thousands of characters in one node value (terminals, log views, large
// text fields). Any content change then shows as one line removed and one
// added, both printed in full, so the incremental view costs twice the full
// tree.
//
// Condensing moves the granularity into the node content: it trims the common
// prefix and suffix, reports only the changed span, and keeps enough context
// to locate it. The common prefix begins with the element's index, role and
// name, so the summary still identifies the element.
//
// Short lines are not condensed, because the metadata would outweigh the
// content, and neither are lines that changed entirely, because there is no
// shared prefix or suffix to reuse and a plain -old/+new is more honest.
func condenseLinePair(before, after string) (string, bool) {
	// The threshold is several ordinary tree lines long. Below it, -old/+new
	// is already cheap and the condensing metadata would be a net loss.
	const condenseMinBytes = 400
	if len(before) < condenseMinBytes && len(after) < condenseMinBytes {
		return "", false
	}
	oldRunes, newRunes := []rune(before), []rune(after)
	shorter := len(oldRunes)
	if len(newRunes) < shorter {
		shorter = len(newRunes)
	}
	head := 0
	for head < shorter && oldRunes[head] == newRunes[head] {
		head++
	}
	tail := 0
	for tail < shorter-head && oldRunes[len(oldRunes)-1-tail] == newRunes[len(newRunes)-1-tail] {
		tail++
	}
	oldMid := oldRunes[head : len(oldRunes)-tail]
	newMid := newRunes[head : len(newRunes)-tail]

	// When the changed span is most of the line, condensing gains nothing and
	// the summary reads worse than the original.
	if (len(oldMid)+len(newMid))*2 >= len(oldRunes)+len(newRunes) {
		return "", false
	}

	// The start of the prefix identifies the element (index, role and name),
	// and its end locates the edit; the part in between can be elided.
	const identifyRunes = 120
	const contextRunes = 48
	leading := ""
	if head <= identifyRunes+contextRunes {
		leading = string(oldRunes[:head])
	} else {
		leading = string(oldRunes[:identifyRunes]) +
			fmt.Sprintf("…(%d chars unchanged)…", head-identifyRunes-contextRunes) +
			string(oldRunes[head-contextRunes:head])
	}
	trailing := ""
	if tail <= contextRunes {
		trailing = string(oldRunes[len(oldRunes)-tail:])
	} else {
		trailing = string(oldRunes[len(oldRunes)-tail:len(oldRunes)-tail+contextRunes]) +
			fmt.Sprintf("…(%d more unchanged)…", tail-contextRunes)
	}
	return fmt.Sprintf("~%s[-%s][+%s]%s  (content-only change at char %d; this line was %d chars, now %d)",
		leading, clipRunes(oldMid), clipRunes(newMid), trailing,
		head, len(oldRunes), len(newRunes)), true
}

// clipRunes caps the changed span itself. An edit can be thousands of
// characters on its own (a single terminal refresh can be); keeping both ends
// and stating how much was elided is more useful than dumping it.
func clipRunes(runes []rune) string {
	const keepEachSide = 240
	if len(runes) <= keepEachSide*2 {
		return string(runes)
	}
	return string(runes[:keepEachSide]) +
		fmt.Sprintf("…(%d chars elided)…", len(runes)-keepEachSide*2) +
		string(runes[len(runes)-keepEachSide:])
}

func compactActionObservationLines(
	snapshot *AppSnapshot,
	baseline ...*AppSnapshot,
) []string {
	if snapshot == nil {
		return nil
	}
	if popup := openedPopupRoot(snapshot); popup != nil {
		if lines, err := projectedSubtreeLines(snapshot, popup); err == nil && len(lines) > 0 {
			return append([]string{"popup:"}, lines...)
		}
	}
	root := focusedObservationRoot(snapshot)
	if root == nil {
		return nil
	}
	label := "focus:"
	var delivered *AppSnapshot
	if len(baseline) > 0 {
		delivered = baseline[0]
	}
	if snapshot.contentRoot != nil &&
		!runtimePathContains(
			snapshot.contentRoot.RuntimeID,
			root.RuntimeID,
		) &&
		(delivered == nil || contentRevisionChanged(delivered, snapshot)) {
		root = snapshot.contentRoot
		label = "content:"
	}
	lines, err := projectedSubtreeLines(snapshot, root)
	if err != nil || len(lines) == 0 {
		return nil
	}
	return append([]string{label}, lines...)
}

// Roles that open a popup of items under themselves when clicked, and the
// item roles such a popup holds.
var (
	popupOwnerRoles = map[CanonicalRole]bool{
		"menu": true, "menuitem": true, "combobox": true, "button": true,
	}
	popupItemRoles = map[CanonicalRole]bool{
		"menuitem": true, "menuitemcheckbox": true, "menuitemradio": true,
		"option": true, "listitem": true,
	}
)

// openedPopupRoot returns the acted element when its popup is open in this
// snapshot: the element is a menu, menu item, combo box or button, is not
// collapsed, and has menu or option items under it. Closed menus and combo
// boxes render no items, so items under the acted element mean the action
// opened (or kept open) its popup, and the compact result must show them
// instead of the focused element, which stays on the opener or the canvas.
func openedPopupRoot(snapshot *AppSnapshot) *ElementRecord {
	if snapshot == nil || snapshot.actedRef == "" {
		return nil
	}
	node := axSnapshotNodeByRef(snapshot, snapshot.actedRef)
	if node == nil || !popupOwnerRoles[node.Role] ||
		node.States.Expanded == TriFalse ||
		!axSubtreeHasRole(node.Children, popupItemRoles) {
		return nil
	}
	return recordByIndex(snapshot, node.ProviderIndex)
}

func axSubtreeHasRole(nodes []*AXNode, roles map[CanonicalRole]bool) bool {
	for _, node := range nodes {
		if node == nil {
			continue
		}
		if roles[node.Role] || axSubtreeHasRole(node.Children, roles) {
			return true
		}
	}
	return false
}
