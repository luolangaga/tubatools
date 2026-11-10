package core

import (
	"sort"
	"strings"
)

type AXDiffKind string

const (
	AXDiffAdded           AXDiffKind = "added"
	AXDiffRemoved         AXDiffKind = "removed"
	AXDiffChanged         AXDiffKind = "changed"
	AXDiffMoved           AXDiffKind = "moved"
	AXDiffPropertyChanged AXDiffKind = "property_changed"
	AXDiffStateChanged    AXDiffKind = "state_changed"
)

type AXDiffChange struct {
	Kind            AXDiffKind
	Key             string
	Before          *AXNode
	After           *AXNode
	OrderChildren   []*AXNode
	OrderStart      int
	OrderEnd        int
	OrderMovedCount int
	RootOrder       bool
}

func axNodeDiffKey(node *AXNode) string {
	if node == nil {
		return ""
	}
	if node.Ref != "" {
		return node.Ref
	}
	if strings.Trim(node.ProviderIdentity, "\x00") != "" {
		return "provider:" + node.ProviderIdentity
	}
	return node.SemanticFingerprint
}

func axStateSignature(states AXStates) string {
	return strings.Join(renderAXStateTokens(states), ",")
}

func axPropertySignature(node *AXNode) string {
	if node == nil {
		return ""
	}
	parts := []string{string(node.Role), node.Name}
	if value, _ := axInlineCellValue(node); value != "" {
		parts = append(parts, "=", value)
	}
	for _, property := range renderAXProperties(node, AXRenderOptions{}) {
		parts = append(parts, property[0], property[1])
	}
	return strings.Join(parts, "\x00")
}

func axDiffLayoutWrapper(node *AXNode) bool {
	if node == nil || (node.Role != RoleGeneric && node.Role != "group") {
		return false
	}
	return !axNodeHasContent(node) &&
		!axNodeHasState(node) &&
		!axNodeHasCapability(node)
}

type axLCSMatch struct {
	before int
	after  int
}

func axNodeFullSignature(node *AXNode) string {
	if node == nil {
		return ""
	}
	return strings.Join([]string{
		string(node.Role),
		axStateSignature(node.States),
		axPropertySignature(node),
	}, "\x00")
}

func axNodeSubtreeSignature(node *AXNode, memo map[*AXNode]string) string {
	if node == nil {
		return ""
	}
	if signature, ok := memo[node]; ok {
		return signature
	}
	if strong := axDiffStrongKey(node); strong != "" {
		signature := "strong:" + strong
		memo[node] = signature
		return signature
	}
	parts := []string{axNodeFullSignature(node)}
	for _, child := range axVisibleDiffChildren(node.Children) {
		parts = append(parts, axNodeSubtreeSignature(child, memo))
	}
	signature := strings.Join(parts, "\x01")
	memo[node] = signature
	return signature
}

func axFullSignatureLCS(before, after []*AXNode) []axLCSMatch {
	rows := len(before) + 1
	cols := len(after) + 1
	memo := map[*AXNode]string{}
	beforeSignatures := make([]string, len(before))
	afterSignatures := make([]string, len(after))
	for index, node := range before {
		beforeSignatures[index] = axNodeSubtreeSignature(node, memo)
	}
	for index, node := range after {
		afterSignatures[index] = axNodeSubtreeSignature(node, memo)
	}
	table := make([][]int, rows)
	for row := range table {
		table[row] = make([]int, cols)
	}
	for row := len(before) - 1; row >= 0; row-- {
		for col := len(after) - 1; col >= 0; col-- {
			if beforeSignatures[row] == afterSignatures[col] {
				table[row][col] = table[row+1][col+1] + 1
			} else if table[row+1][col] >= table[row][col+1] {
				table[row][col] = table[row+1][col]
			} else {
				table[row][col] = table[row][col+1]
			}
		}
	}
	matches := []axLCSMatch{}
	for row, col := 0, 0; row < len(before) && col < len(after); {
		switch {
		case beforeSignatures[row] == afterSignatures[col]:
			matches = append(matches, axLCSMatch{before: row, after: col})
			row++
			col++
		case table[row+1][col] >= table[row][col+1]:
			row++
		default:
			col++
		}
	}
	return matches
}

func axDiffStrongKey(node *AXNode) string {
	if node == nil {
		return ""
	}
	if node.Ref != "" {
		return "ref:" + node.Ref
	}
	if strings.Trim(node.ProviderIdentity, "\x00") != "" {
		return "provider:" + node.ProviderIdentity
	}
	return ""
}

func axSiblingMatchKey(node *AXNode) string {
	if strong := axDiffStrongKey(node); strong != "" {
		return strong
	}
	if semantic := strings.TrimSpace(node.SemanticFingerprint); semantic != "" {
		return "semantic:" + semantic
	}
	return ""
}

func axVisibleDiffChildren(nodes []*AXNode) []*AXNode {
	out := make([]*AXNode, 0, len(nodes))
	var appendNode func(*AXNode)
	appendNode = func(node *AXNode) {
		if axDiffLayoutWrapper(node) {
			for _, child := range node.Children {
				appendNode(child)
			}
			return
		}
		out = append(out, node)
	}
	for _, node := range nodes {
		appendNode(node)
	}
	return out
}

func matchAXSiblings(before, after []*AXNode) (map[int]int, map[int]bool, map[int]bool) {
	before = axVisibleDiffChildren(before)
	after = axVisibleDiffChildren(after)
	matched := map[int]int{}
	usedBefore := map[int]bool{}
	usedAfter := map[int]bool{}

	beforeByKey := map[string][]int{}
	afterByKey := map[string][]int{}
	for index, node := range before {
		if key := axSiblingMatchKey(node); key != "" {
			beforeByKey[key] = append(beforeByKey[key], index)
		}
	}
	for index, node := range after {
		if key := axSiblingMatchKey(node); key != "" {
			afterByKey[key] = append(afterByKey[key], index)
		}
	}
	for key, beforeIndices := range beforeByKey {
		afterIndices := afterByKey[key]
		if len(beforeIndices) == 1 && len(afterIndices) == 1 {
			matched[afterIndices[0]] = beforeIndices[0]
			usedBefore[beforeIndices[0]] = true
			usedAfter[afterIndices[0]] = true
		}
	}

	for key, beforeIndices := range beforeByKey {
		afterIndices := afterByKey[key]
		if len(beforeIndices) <= 1 || len(afterIndices) <= 1 {
			continue
		}
		memo := map[*AXNode]string{}
		beforeBySignature := map[string][]int{}
		afterBySignature := map[string][]int{}
		for _, index := range beforeIndices {
			if !usedBefore[index] {
				signature := axNodeSubtreeSignature(before[index], memo)
				beforeBySignature[signature] = append(
					beforeBySignature[signature],
					index,
				)
			}
		}
		for _, index := range afterIndices {
			if !usedAfter[index] {
				signature := axNodeSubtreeSignature(after[index], memo)
				afterBySignature[signature] = append(
					afterBySignature[signature],
					index,
				)
			}
		}
		for signature, oldIndices := range beforeBySignature {
			newIndices := afterBySignature[signature]
			if len(oldIndices) == 1 && len(newIndices) == 1 {
				matched[newIndices[0]] = oldIndices[0]
				usedBefore[oldIndices[0]] = true
				usedAfter[newIndices[0]] = true
			}
		}
		remainingBefore := []int{}
		remainingAfter := []int{}
		beforeGroup := []*AXNode{}
		afterGroup := []*AXNode{}
		for _, index := range beforeIndices {
			if !usedBefore[index] {
				remainingBefore = append(remainingBefore, index)
				beforeGroup = append(beforeGroup, before[index])
			}
		}
		for _, index := range afterIndices {
			if !usedAfter[index] {
				remainingAfter = append(remainingAfter, index)
				afterGroup = append(afterGroup, after[index])
			}
		}
		for _, pair := range axFullSignatureLCS(beforeGroup, afterGroup) {
			beforeIndex := remainingBefore[pair.before]
			afterIndex := remainingAfter[pair.after]
			matched[afterIndex] = beforeIndex
			usedBefore[beforeIndex] = true
			usedAfter[afterIndex] = true
		}
	}
	return matched, usedBefore, usedAfter
}

func axMovedSiblingIndices(matched map[int]int, afterCount int) map[int]bool {
	type match struct {
		after  int
		before int
	}
	sequence := make([]match, 0, len(matched))
	for afterIndex := 0; afterIndex < afterCount; afterIndex++ {
		if beforeIndex, ok := matched[afterIndex]; ok {
			sequence = append(sequence, match{after: afterIndex, before: beforeIndex})
		}
	}
	if len(sequence) < 2 {
		return nil
	}

	tailValues := make([]int, 0, len(sequence))
	tailPositions := make([]int, 0, len(sequence))
	previous := make([]int, len(sequence))
	for index := range previous {
		previous[index] = -1
	}
	for index, item := range sequence {
		position := sort.SearchInts(tailValues, item.before)
		if position == len(tailValues) {
			tailValues = append(tailValues, item.before)
			tailPositions = append(tailPositions, index)
		} else {
			tailValues[position] = item.before
			tailPositions[position] = index
		}
		if position > 0 {
			previous[index] = tailPositions[position-1]
		}
	}
	inOrder := map[int]bool{}
	for index := tailPositions[len(tailPositions)-1]; index >= 0; index = previous[index] {
		inOrder[sequence[index].after] = true
	}
	moved := map[int]bool{}
	for _, item := range sequence {
		if !inOrder[item.after] {
			moved[item.after] = true
		}
	}
	return moved
}

func diffAXSnapshots(before, after *AXSnapshot) []AXDiffChange {
	changes := []AXDiffChange{}
	var compareSiblings func(*AXNode, *AXNode, []*AXNode, []*AXNode)
	compareSiblings = func(
		beforeParent,
		afterParent *AXNode,
		beforeNodes,
		afterNodes []*AXNode,
	) {
		old := axVisibleDiffChildren(beforeNodes)
		now := axVisibleDiffChildren(afterNodes)
		matched, usedOld, usedNow := matchAXSiblings(old, now)
		moved := axMovedSiblingIndices(matched, len(now))
		if len(moved) > 0 {
			orderStart := len(now)
			orderEnd := -1
			for afterIndex := range moved {
				beforeIndex := matched[afterIndex]
				orderStart = min(orderStart, min(afterIndex, beforeIndex))
				orderEnd = max(orderEnd, max(afterIndex, beforeIndex))
			}
			change := AXDiffChange{
				Kind:            AXDiffMoved,
				OrderChildren:   now,
				OrderStart:      orderStart,
				OrderEnd:        orderEnd,
				OrderMovedCount: len(moved),
			}
			if beforeParent == nil || afterParent == nil {
				change.Key = "root-order"
				change.RootOrder = true
			} else {
				change.Key = axNodeDiffKey(afterParent)
				change.Before = beforeParent
				change.After = afterParent
			}
			changes = append(changes, AXDiffChange{
				Kind:            change.Kind,
				Key:             change.Key,
				Before:          change.Before,
				After:           change.After,
				OrderChildren:   change.OrderChildren,
				OrderStart:      change.OrderStart,
				OrderEnd:        change.OrderEnd,
				OrderMovedCount: change.OrderMovedCount,
				RootOrder:       change.RootOrder,
			})
		}
		for afterIndex, current := range now {
			beforeIndex, exists := matched[afterIndex]
			if !exists {
				changes = append(changes, AXDiffChange{
					Kind: AXDiffAdded, Key: axNodeDiffKey(current), After: current,
				})
				continue
			}
			previous := old[beforeIndex]
			stateBefore := axStateSignature(previous.States)
			stateAfter := axStateSignature(current.States)
			propBefore := axPropertySignature(previous)
			propAfter := axPropertySignature(current)
			switch {
			case stateBefore != stateAfter && propBefore == propAfter:
				changes = append(changes, AXDiffChange{
					Kind: AXDiffStateChanged, Key: axNodeDiffKey(current),
					Before: previous, After: current,
				})
			case propBefore != propAfter && stateBefore == stateAfter:
				changes = append(changes, AXDiffChange{
					Kind: AXDiffPropertyChanged, Key: axNodeDiffKey(current),
					Before: previous, After: current,
				})
			case stateBefore != stateAfter || propBefore != propAfter:
				changes = append(changes, AXDiffChange{
					Kind: AXDiffChanged, Key: axNodeDiffKey(current),
					Before: previous, After: current,
				})
			}
			compareSiblings(previous, current, previous.Children, current.Children)
		}
		for beforeIndex, previous := range old {
			if !usedOld[beforeIndex] {
				changes = append(changes, AXDiffChange{
					Kind: AXDiffRemoved, Key: axNodeDiffKey(previous), Before: previous,
				})
			}
		}
		_ = usedNow
	}
	compareSiblings(nil, nil, axDiffRoots(before), axDiffRoots(after))
	return changes
}

func axDiffRoots(snapshot *AXSnapshot) []*AXNode {
	if snapshot == nil {
		return nil
	}
	if len(snapshot.CanonicalRoots) > 0 {
		return snapshot.CanonicalRoots
	}
	return snapshot.Roots
}
