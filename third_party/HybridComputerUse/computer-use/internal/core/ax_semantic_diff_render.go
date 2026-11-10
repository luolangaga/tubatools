package core

import (
	"fmt"
	"sort"
	"strings"
)

func projectedAXNodesByID(snapshot *AXSnapshot) map[string]*AXNode {
	out := map[string]*AXNode{}
	if snapshot == nil {
		return out
	}
	var visit func([]*AXNode)
	visit = func(nodes []*AXNode) {
		for _, node := range nodes {
			if node.ID != "" {
				out[node.ID] = node
			}
			visit(node.Children)
		}
	}
	visit(snapshot.Roots)
	return out
}

func axChangedAncestor(
	node *AXNode,
	changed map[string]bool,
) bool {
	for parent := node.Parent; parent != nil; parent = parent.Parent {
		if changed[parent.ID] {
			return true
		}
	}
	return false
}

func axSubtreeContainsChanged(node *AXNode, changed map[string]AXDiffKind) bool {
	if node == nil {
		return false
	}
	if _, ok := changed[node.ID]; ok {
		return true
	}
	for _, child := range node.Children {
		if axSubtreeContainsChanged(child, changed) {
			return true
		}
	}
	return false
}

func renderAXCurrentChangeBranch(
	lines *[]string,
	node *AXNode,
	depth int,
	position int,
	changed map[string]AXDiffKind,
	orderChanges map[string]AXDiffChange,
) {
	if node == nil {
		return
	}
	kind, directChange := changed[node.ID]
	properties := renderAXProperties(node, AXRenderOptions{})
	includeFullSubtree := directChange && kind == AXDiffAdded
	hasChangedChild := false
	for _, child := range node.Children {
		if axSubtreeContainsChanged(child, changed) {
			hasChangedChild = true
			break
		}
	}
	hasChildren := len(properties) > 0 || includeFullSubtree || hasChangedChild
	orderChange, orderChanged := orderChanges[node.ID]
	hasChildren = hasChildren || orderChanged
	prefix := ""
	if directChange {
		prefix = "<changed> "
	}
	label := renderAXNodeLabel(node)
	if position > 0 {
		label += fmt.Sprintf(" [position=%d]", position)
	}
	line := strings.Repeat(" ", depth) + "- " + prefix + label
	if hasChildren {
		line += ":"
	}
	*lines = append(*lines, line)
	if directChange {
		for _, property := range properties {
			*lines = append(*lines, renderAXTreePropertyLine(property, depth+1))
		}
	}
	if orderChanged {
		renderAXOrderChange(
			lines, depth+1, orderChange, changed, orderChanges,
		)
		return
	}
	if includeFullSubtree {
		var renderChildren func([]*AXNode, int)
		renderChildren = func(nodes []*AXNode, childDepth int) {
			for _, child := range nodes {
				childProperties := renderAXProperties(child, AXRenderOptions{})
				childHasChildren := len(childProperties) > 0 ||
					len(child.Children) > 0
				*lines = append(
					*lines,
					renderAXTreeNodeLine(child, childDepth, childHasChildren),
				)
				for _, property := range childProperties {
					*lines = append(
						*lines,
						renderAXTreePropertyLine(property, childDepth+1),
					)
				}
				renderChildren(child.Children, childDepth+1)
			}
		}
		renderChildren(node.Children, depth+1)
		return
	}
	for _, child := range node.Children {
		if axSubtreeContainsChanged(child, changed) {
			renderAXCurrentChangeBranch(
				lines, child, depth+1, 0, changed, orderChanges,
			)
		}
	}
}

func renderAXOrderChange(
	lines *[]string,
	depth int,
	change AXDiffChange,
	changed map[string]AXDiffKind,
	orderChanges map[string]AXDiffChange,
) {
	children := change.OrderChildren
	start := max(0, change.OrderStart)
	end := min(len(children)-1, change.OrderEnd)
	if start > end || len(children) == 0 {
		*lines = append(
			*lines,
			strings.Repeat(" ", depth)+"- /order_changed: true",
		)
		return
	}

	const maxOrderItems = 12
	candidates := map[int]bool{}
	semanticChanges := []int{}
	for index := start; index <= end; index++ {
		candidates[index] = true
	}
	for index, child := range children {
		if axSubtreeContainsChanged(child, changed) {
			semanticChanges = append(semanticChanges, index)
			candidates[index] = true
		}
		if orderChanges[child.ID].Kind == AXDiffMoved {
			candidates[index] = true
		}
	}
	selected := candidates
	if len(candidates) > maxOrderItems {
		selected = map[int]bool{}
		addEdges := func(indices []int, limit int) {
			for left, right := 0, len(indices)-1; left <= right &&
				len(selected) < limit; left, right = left+1, right-1 {
				selected[indices[left]] = true
				if right != left && len(selected) < limit {
					selected[indices[right]] = true
				}
			}
		}
		if len(semanticChanges) > 0 {
			addEdges(semanticChanges, min(maxOrderItems, len(semanticChanges)))
		}
		orderedCandidates := make([]int, 0, len(candidates))
		for index := 0; index < len(children); index++ {
			if candidates[index] {
				orderedCandidates = append(orderedCandidates, index)
			}
		}
		addEdges(orderedCandidates, maxOrderItems)
		*lines = append(*lines, fmt.Sprintf(
			"%s- /order_changed: moved=%d range=%d..%d",
			strings.Repeat(" ", depth),
			change.OrderMovedCount,
			start+1,
			end+1,
		))
	} else {
		*lines = append(
			*lines,
			strings.Repeat(" ", depth)+"- /order_changed: true",
		)
	}

	omitted := len(candidates) - len(selected)
	semanticOmitted := 0
	for _, index := range semanticChanges {
		if !selected[index] {
			semanticOmitted++
		}
	}
	for index, child := range children {
		if !selected[index] {
			continue
		}
		if axSubtreeContainsChanged(child, changed) ||
			orderChanges[child.ID].Kind == AXDiffMoved {
			renderAXCurrentChangeBranch(
				lines,
				child,
				depth,
				index+1,
				changed,
				orderChanges,
			)
			continue
		}
		*lines = append(*lines, fmt.Sprintf(
			"%s- %s [position=%d]",
			strings.Repeat(" ", depth),
			renderAXNodeLabel(child),
			index+1,
		))
	}
	if omitted > 0 {
		*lines = append(*lines, fmt.Sprintf(
			"%s- /positions_omitted: %d",
			strings.Repeat(" ", depth),
			omitted,
		))
	}
	if semanticOmitted > 0 {
		*lines = append(*lines, fmt.Sprintf(
			"%s- /semantic_changes_omitted: %d recover=observe|find",
			strings.Repeat(" ", depth),
			semanticOmitted,
		))
	}
}

func canonicalAXRefs(snapshot *AXSnapshot) map[string]bool {
	refs := map[string]bool{}
	if snapshot == nil {
		return refs
	}
	var visit func([]*AXNode)
	visit = func(nodes []*AXNode) {
		for _, node := range nodes {
			if ref := strings.TrimSpace(node.Ref); ref != "" {
				refs[ref] = true
			}
			visit(node.Children)
		}
	}
	visit(axDiffRoots(snapshot))
	return refs
}

func compactAbsentRefs(changes []AXDiffChange, after *AXSnapshot) []string {
	seen := map[string]bool{}
	refs := []string{}
	current := canonicalAXRefs(after)
	var collect func(*AXNode)
	collect = func(node *AXNode) {
		if node == nil {
			return
		}
		if ref := strings.TrimSpace(node.Ref); ref != "" &&
			!current[ref] && !seen[ref] {
			seen[ref] = true
			refs = append(refs, ref)
		}
		for _, child := range node.Children {
			collect(child)
		}
	}
	for _, change := range changes {
		if change.Kind == AXDiffRemoved {
			collect(change.Before)
		}
	}
	sort.SliceStable(refs, func(i, j int) bool {
		left, leftOK := parseMonotonicAXRef(refs[i])
		right, rightOK := parseMonotonicAXRef(refs[j])
		if leftOK && rightOK {
			return left < right
		}
		return refs[i] < refs[j]
	})
	return refs
}

// joinAXRefRuns joins sorted refs and writes three or more consecutive refs
// as rA..rB, which includes both endpoints and every ref between them.
func joinAXRefRuns(refs []string) string {
	parts := make([]string, 0, len(refs))
	for index := 0; index < len(refs); {
		start, ok := parseMonotonicAXRef(refs[index])
		end := index
		for ok && end+1 < len(refs) {
			next, nextOK := parseMonotonicAXRef(refs[end+1])
			if !nextOK || next != start+uint64(end+1-index) {
				break
			}
			end++
		}
		if end-index >= 2 {
			parts = append(parts, refs[index]+".."+refs[end])
		} else {
			parts = append(parts, refs[index:end+1]...)
		}
		index = end + 1
	}
	return strings.Join(parts, ", ")
}

// renderAXRemovedChunks lists nodes that are gone. A removed line carries
// neither its old ref nor an addressability marker: the node cannot be a
// target any more, its old refs are listed once in absent_refs, and the
// parent context names the parent without asserting anything about its ref.
func renderAXRemovedChunks(
	lines *[]string,
	changes []AXDiffChange,
	before,
	after *AXSnapshot,
) {
	projected := projectedAXNodesByID(before)
	currentProjected := projectedAXNodesByID(after)
	currentByStrongKey := map[string]*AXNode{}
	for _, node := range currentProjected {
		if key := axDiffStrongKey(node); key != "" {
			if _, exists := currentByStrongKey[key]; exists {
				currentByStrongKey[key] = nil
			} else {
				currentByStrongKey[key] = node
			}
		}
	}
	removed := map[string]bool{}
	currentRefs := canonicalAXRefs(after)
	for _, change := range changes {
		if change.Kind == AXDiffRemoved && change.Before != nil {
			removed[change.Before.ID] = true
		}
	}
	type removedGroup struct {
		parent     *AXNode
		nodes      []*AXNode
		historical bool
	}
	groups := []removedGroup{}
	groupIndex := map[string]int{}
	rootRemoved := []*AXNode{}
	for _, change := range changes {
		if change.Kind != AXDiffRemoved || change.Before == nil ||
			axChangedAncestor(change.Before, removed) {
			continue
		}
		node := projected[change.Before.ID]
		if node == nil {
			continue
		}
		if node.Ref != "" && currentRefs[node.Ref] {
			continue
		}
		parent := node.Parent
		if parent != nil && !removed[parent.ID] {
			var currentParent *AXNode
			if strongKey := axDiffStrongKey(parent); strongKey != "" {
				currentParent = currentByStrongKey[strongKey]
			} else if candidate := currentProjected[parent.ID]; candidate != nil &&
				candidate.Role == parent.Role &&
				strings.TrimSpace(parent.SemanticFingerprint) != "" &&
				candidate.SemanticFingerprint == parent.SemanticFingerprint {
				// Runtime IDs are traversal paths, not stable object identities.
				// Use them only when no stronger identity exists and the semantic
				// identity still agrees.
				currentParent = candidate
			}
			historical := currentParent == nil
			if currentParent != nil {
				parent = currentParent
			}
			groupKey := parent.ID
			if historical {
				groupKey = "historical:" + groupKey
			}
			index, exists := groupIndex[groupKey]
			if !exists {
				index = len(groups)
				groupIndex[groupKey] = index
				groups = append(groups, removedGroup{
					parent:     parent,
					historical: historical,
				})
			}
			groups[index].nodes = append(groups[index].nodes, node)
			continue
		}
		rootRemoved = append(rootRemoved, node)
	}
	for _, group := range groups {
		context := ""
		if group.historical {
			context = "<historical-context> "
		}
		*lines = append(
			*lines,
			" - "+context+renderAXNodeIdentityLabel(group.parent)+":",
		)
		for _, node := range group.nodes {
			*lines = append(
				*lines,
				"  - <removed> "+renderAXNodeIdentityLabel(node),
			)
		}
	}
	for _, node := range rootRemoved {
		*lines = append(
			*lines,
			" - <removed> "+renderAXNodeIdentityLabel(node),
		)
	}
}

func renderAXChangedChunks(
	changes []AXDiffChange,
	before,
	after *AXSnapshot,
	acquisitionComplete bool,
) []string {
	lines := []string{"ax_diff:"}
	if len(changes) == 0 {
		if acquisitionComplete {
			return append(lines, " effect: no_ax_change")
		}
		return append(
			lines,
			" effect: no_confirmed_ax_change",
			" observation_incomplete: true",
		)
	}
	projected := projectedAXNodesByID(after)
	changed := map[string]AXDiffKind{}
	orderChanges := map[string]AXDiffChange{}
	rootOrderChanges := []AXDiffChange{}
	for _, change := range changes {
		if change.Kind == AXDiffMoved {
			if change.RootOrder {
				rootOrderChanges = append(rootOrderChanges, change)
				continue
			}
			if change.After != nil {
				orderChanges[change.After.ID] = change
				if _, exists := changed[change.After.ID]; !exists {
					changed[change.After.ID] = change.Kind
				}
			}
			continue
		}
		if change.After != nil {
			changed[change.After.ID] = change.Kind
		}
	}
	for _, change := range rootOrderChanges {
		lines = append(lines, " - <changed> root:")
		renderAXOrderChange(&lines, 2, change, changed, orderChanges)
	}
	rootOrderRendered := len(rootOrderChanges) > 0
	for _, change := range changes {
		if change.After == nil || change.RootOrder {
			continue
		}
		if rootOrderRendered {
			continue
		}
		node := projected[change.After.ID]
		if node == nil {
			continue
		}
		hasChangedAncestor := false
		for parent := change.After.Parent; parent != nil; parent = parent.Parent {
			if _, ok := changed[parent.ID]; ok {
				hasChangedAncestor = true
				break
			}
		}
		if hasChangedAncestor {
			continue
		}
		renderAXCurrentChangeBranch(
			&lines, node, 1, 0, changed, orderChanges,
		)
	}
	if acquisitionComplete {
		renderAXRemovedChunks(&lines, changes, before, after)
		absent := compactAbsentRefs(changes, after)
		if len(absent) > 0 {
			lines = append(
				lines,
				" absent_refs: ["+joinAXRefRuns(absent)+"]",
				" absent_ref_status: absent_from_current_snapshot",
			)
		}
	} else {
		if len(lines) == 1 {
			lines = append(lines, " effect: no_confirmed_ax_change")
		}
		lines = append(lines, " observation_incomplete: true")
	}
	if len(lines) == 1 {
		lines = append(lines, " effect: changed_outside_working_projection")
	}
	return lines
}

func incrementalAXSnapshot(before, after *AppSnapshot) ([]string, bool) {
	if before == nil || after == nil ||
		before.AXSnapshot == nil || after.AXSnapshot == nil {
		return nil, false
	}
	beforeWindowRef := snapshotLogicalWindowRef(before)
	afterWindowRef := snapshotLogicalWindowRef(after)
	if beforeWindowRef == "" || afterWindowRef == "" ||
		beforeWindowRef != afterWindowRef {
		return nil, false
	}
	if providerCoverageSummary(before.Annotations).AcquisitionIncomplete > 0 {
		return nil, false
	}
	changes := diffAXSnapshots(before.AXSnapshot, after.AXSnapshot)
	acquisitionComplete :=
		providerCoverageSummary(after.Annotations).AcquisitionIncomplete == 0
	lines := renderAXChangedChunks(
		changes,
		before.AXSnapshot,
		after.AXSnapshot,
		acquisitionComplete,
	)
	// The diff replaces the current tree only when it is shorter, whatever
	// its absolute size: a long diff on a longer tree still saves output and
	// keeps the changed nodes marked. A no-change diff is always returned.
	diffChars := len(strings.Join(lines, "\n"))
	currentChars := len(strings.Join(after.TreeLines, "\n"))
	if len(changes) > 0 && currentChars > 0 && diffChars >= currentChars {
		return nil, false
	}
	return lines, true
}
