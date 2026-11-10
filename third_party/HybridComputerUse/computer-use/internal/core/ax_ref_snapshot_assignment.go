package core

import (
	"strings"
)

func axNodeProviderIdentityComplete(node *AXNode) bool {
	return node != nil &&
		strings.TrimSpace(node.ProviderBusName) != "" &&
		strings.TrimSpace(node.ProviderObjectPath) != "" &&
		strings.TrimSpace(node.ProcessGeneration) != ""
}

func axNodeSemanticIdentityComplete(node *AXNode) bool {
	return node != nil &&
		node.LocatorKind == AXLocatorSemantic &&
		strings.TrimSpace(node.SemanticTarget) != ""
}

func axNodeFindIdentityKey(node *AXNode) string {
	if node == nil {
		return ""
	}
	return strings.ToLower(strings.TrimSpace(string(node.Role))) + "\x00" +
		normalizedTreeText(node.Name)
}

func axNodeHasReliableGeometry(node *AXNode) bool {
	return node != nil &&
		node.SourceRecord != nil &&
		node.SourceRecord.Frame != nil &&
		node.SourceRecord.Frame.Width > 0 &&
		node.SourceRecord.Frame.Height > 0
}

func axNodeHasPointerEvidence(node *AXNode) bool {
	if node == nil || !axNodeHasReliableGeometry(node) {
		return false
	}
	for _, state := range recordStateTokens(node.SourceRecord) {
		if state == "has-click-action" {
			return true
		}
	}
	return false
}

func axNodeHasInteractionSemantics(node *AXNode) bool {
	if node == nil {
		return false
	}
	if axNodeHasCapability(node) || axNodeHasPointerEvidence(node) {
		return true
	}
	switch node.SemanticClass {
	case "control":
		return true
	case "item":
		// Desktop providers frequently omit action metadata for selectable
		// cells, tabs and tree/list items. Reliable visible geometry is enough
		// to issue an identity ref; action support is checked at execution.
		return axNodeHasReliableGeometry(node)
	default:
		return false
	}
}

func axNodeRefEligibility(
	snapshot *AXSnapshot,
	node *AXNode,
) (bool, string, AXLocatorKind) {
	if snapshot == nil || node == nil {
		return false, "provider_identity_incomplete", ""
	}
	if strings.TrimSpace(snapshot.AppRef) == "" ||
		strings.TrimSpace(snapshot.WindowRef) == "" {
		return false, "owner_identity_incomplete", ""
	}

	locatorKind := node.LocatorKind
	if locatorKind == "" && node.SemanticTarget != "" {
		locatorKind = AXLocatorSemantic
	}
	if snapshot.IdentityCoverageIncomplete {
		if axNodeProviderIdentityComplete(node) {
			locatorKind = AXLocatorProviderBound
		} else {
			return false, "identity_coverage_incomplete", ""
		}
	}

	for parent := node.Parent; parent != nil; parent = parent.Parent {
		if parent.States.Expanded == TriFalse {
			return false, "hidden_under_collapsed_ancestor", ""
		}
	}
	if node.Showing != nil && !*node.Showing && !node.ForceKeep {
		return false, "not_currently_showing", ""
	}
	if node.Visible != nil && !*node.Visible && !node.ForceKeep {
		return false, "not_currently_visible", ""
	}

	// A find match or observation root explicitly selects this canonical
	// object. Identity and actionability are separate: reuse a unique,
	// window-scoped semantic locator when available, otherwise use a complete
	// provider identity. Action support is checked before execution.
	if node.FindMatch || node.ExplicitRoot {
		if node.ExplicitRoot && locatorKind == AXLocatorProviderBound &&
			axNodeProviderIdentityComplete(node) {
			return true, "", AXLocatorProviderBound
		}
		if axNodeSemanticIdentityComplete(node) &&
			!node.SemanticIdentityAmbiguous {
			return true, "", AXLocatorSemantic
		}
		if axNodeProviderIdentityComplete(node) {
			return true, "", AXLocatorProviderBound
		}
		return false, "stable_identity_unavailable", ""
	}

	if !axNodeHasInteractionSemantics(node) {
		return false, "interaction_semantics_unknown", ""
	}
	if axNodeIsUnnamedEditor(node) {
		// An unnamed text area or entry (gedit, Bluefish and Kate editors,
		// unlabeled GTK entries) is identified by its provider object, not
		// by text: the model sees its value and needs a ref to type into it.
		return true, "", AXLocatorProviderBound
	}
	if locatorKind == "" {
		return false, "semantic_identity_ambiguous", ""
	}
	if strings.TrimSpace(node.Name) == "" &&
		strings.TrimSpace(node.Properties.Description) == "" &&
		strings.TrimSpace(node.Properties.Placeholder) == "" {
		return false, "unnamed_target", ""
	}
	return true, "", locatorKind
}

// axNodeIsUnnamedEditor reports an unnamed control that edits text and has
// a complete provider identity. Unnamed buttons stay unaddressed: one
// unnamed button is indistinguishable from the next, while an editor is
// identified by its content and position in the window.
func axNodeIsUnnamedEditor(node *AXNode) bool {
	return node != nil &&
		node.Capabilities.EditText &&
		strings.TrimSpace(node.Name) == "" &&
		strings.TrimSpace(node.Properties.Description) == "" &&
		strings.TrimSpace(node.Properties.Placeholder) == "" &&
		axNodeProviderIdentityComplete(node)
}

func (registry *RefRegistry) AssignSnapshotRefs(snapshot *AXSnapshot) {
	if registry == nil || snapshot == nil {
		return
	}
	seenRefs := map[string]bool{}
	semanticCounts := map[string]int{}
	findIdentityCounts := map[string]int{}
	var countFindTargets func([]*AXNode)
	countFindTargets = func(nodes []*AXNode) {
		for _, node := range nodes {
			if node.FindMatch && axNodeSemanticIdentityComplete(node) {
				semanticCounts[strings.TrimSpace(node.SemanticTarget)]++
			}
			if node.FindMatch {
				findIdentityCounts[axNodeFindIdentityKey(node)]++
			}
			countFindTargets(node.Children)
		}
	}
	countFindTargets(snapshot.Roots)

	var visit func([]*AXNode)
	visit = func(nodes []*AXNode) {
		for _, node := range nodes {
			node.SemanticIdentityAmbiguous =
				node.FindMatch &&
					(semanticCounts[strings.TrimSpace(node.SemanticTarget)] > 1 ||
						findIdentityCounts[axNodeFindIdentityKey(node)] > 1)
			if node.FindMatch {
				// Find context clones may inherit a ref issued for the broader
				// working tree. Re-evaluate addressability in the exact match
				// set so an ambiguous or identity-incomplete candidate is
				// never advertised as copyable.
				node.Ref = ""
			}
			node.AddressabilityEvaluated = true
			node.AddressabilityReason = ""
			if canonical := snapshot.ByID[node.ID]; canonical != nil {
				canonical.AddressabilityEvaluated = true
				canonical.AddressabilityReason = ""
			}
			registry.markExistingNodeRefsPresent(snapshot, node, seenRefs)
			if eligible, reason, locatorKind := axNodeRefEligibility(
				snapshot, node,
			); eligible {
				node.LocatorKind = locatorKind
				node.Ref = registry.AcquireNode(
					snapshot,
					node,
					locatorKind,
				)
				if canonical := snapshot.ByID[node.ID]; canonical != nil {
					canonical.Ref = node.Ref
					canonical.LocatorKind = locatorKind
				}
				if node.Ref != "" {
					seenRefs[node.Ref] = true
				}
			} else {
				node.AddressabilityReason = reason
				if canonical := snapshot.ByID[node.ID]; canonical != nil {
					canonical.AddressabilityReason = reason
				}
			}
			visit(node.Children)
		}
	}
	visit(snapshot.Roots)
	if !snapshot.RefInventoryComplete {
		return
	}
	windowRef := strings.TrimSpace(snapshot.WindowRef)
	for ref, entry := range registry.Entries {
		if entry == nil ||
			entry.LogicalWindowRef != windowRef ||
			entry.Lifecycle == AXRefExpired ||
			seenRefs[ref] {
			continue
		}
		registry.MarkSuperseded(
			ref,
			"absent from complete canonical window inventory",
		)
	}
}

func (registry *RefRegistry) markExistingNodeRefsPresent(
	snapshot *AXSnapshot,
	node *AXNode,
	seenRefs map[string]bool,
) {
	if registry == nil || snapshot == nil || node == nil || seenRefs == nil {
		return
	}
	ownerIdentity := FirstNonEmpty(snapshot.AppRef, snapshot.AppIdentity)
	mark := func(locatorKind AXLocatorKind) {
		key := refIdentityKey(
			locatorKind,
			ownerIdentity,
			snapshot.WindowRef,
			snapshot.WindowHandle,
			node.SemanticTarget,
			node.Role,
			node.Name,
			node.ProviderBusName,
			node.ProviderObjectPath,
			node.ProcessGeneration,
			node.ProviderWindowBusName,
			node.ProviderWindowObjectPath,
			node.ProviderWindowProcessGeneration,
			node.ProviderIdentityScope,
		)
		ref := registry.byIdentity[key]
		entry := registry.Entries[ref]
		if entry == nil || entry.Lifecycle == AXRefExpired {
			return
		}
		if processInstanceBindingChanged(entry, node.ProviderBusName, node.ProviderObjectPath,
			node.ProcessGeneration, node.ProviderIdentityScope) ||
			!providerWindowOwnershipContinuous(entry, node.ProviderWindowBusName,
				node.ProviderWindowObjectPath, node.ProviderWindowProcessGeneration) {
			registry.MarkSuperseded(ref, "provider instance or window continuity lost")
			return
		}
		if locatorKind == AXLocatorSemantic &&
			completeProviderIdentityChanged(
				entry,
				node.ProviderBusName,
				node.ProviderObjectPath,
				node.ProcessGeneration,
			) &&
			!runtimePathMatchesEntry(entry, node.RuntimeID) {
			return
		}
		seenRefs[ref] = true
		entry.LastSeenRevision = snapshot.Revision
	}
	if strings.TrimSpace(node.SemanticTarget) != "" {
		mark(AXLocatorSemantic)
	}
	if axNodeProviderIdentityComplete(node) {
		mark(AXLocatorProviderBound)
	}
}
