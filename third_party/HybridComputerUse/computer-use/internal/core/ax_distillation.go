package core

import (
	"errors"
	"fmt"
	"strings"
	"unicode/utf8"
)

type DistillationContext struct {
	Snapshot                  *AXSnapshot
	CanonicalTruth            map[string]bool
	StageSuppressedProperties int
	StageBudgetApplied        bool
	StageBudgetCharsBefore    int
	StageBudgetCharsAfter     int
	StageBudgetOmittedNodes   int
	StageBudgetOmittedTargets int
	StageBudgetExhausted      bool
}

type DistillationPlugin interface {
	Name() string
	AllowsTargetOmission() bool
	Apply(*DistillationContext, []*AXNode) ([]*AXNode, int, int, error)
}

type distillationPlugin struct {
	name                string
	allowTargetOmission bool
	fn                  func(*DistillationContext, []*AXNode) ([]*AXNode, int, int, error)
}

func (plugin distillationPlugin) Name() string { return plugin.name }
func (plugin distillationPlugin) AllowsTargetOmission() bool {
	return plugin.allowTargetOmission
}
func (plugin distillationPlugin) Apply(
	context *DistillationContext,
	roots []*AXNode,
) ([]*AXNode, int, int, error) {
	return plugin.fn(context, roots)
}

func countAXNodes(nodes []*AXNode) int {
	count := 0
	for _, node := range nodes {
		count++
		count += countAXNodes(node.Children)
	}
	return count
}

func axNodeHasState(node *AXNode) bool {
	if node == nil {
		return false
	}
	states := node.States
	return states.Active == TriTrue ||
		states.Checked == CheckedTrue ||
		states.Checked == CheckedMixed ||
		states.Expanded == TriTrue ||
		states.Expanded == TriFalse ||
		states.Selected == TriTrue ||
		states.Disabled == TriTrue ||
		states.Pressed == TriTrue ||
		states.Invalid == TriTrue ||
		states.Level != nil
}

func axNodeHasCapability(node *AXNode) bool {
	if node == nil {
		return false
	}
	capabilities := node.Capabilities
	return (capabilities.Activate != "" &&
		capabilities.Activate != ActivationNone) ||
		capabilities.SetValue ||
		capabilities.EditText ||
		capabilities.Scroll ||
		len(capabilities.Actions) > 0
}

func axNodeHasContent(node *AXNode) bool {
	if node == nil {
		return false
	}
	return strings.TrimSpace(node.Name) != "" ||
		strings.TrimSpace(node.Properties.Value) != "" ||
		strings.TrimSpace(node.Properties.Description) != "" ||
		strings.TrimSpace(node.Properties.Placeholder) != "" ||
		len(node.Annotations) > 0
}

func axNodeTargetTruth(node *AXNode) bool {
	// Projection target recall protects product-issued stable addresses. A
	// provider capability without a unique semantic target remains available
	// in canonical find/index records, but it must not force every passive or
	// anonymously actionable provider node into model text.
	return node != nil && !node.Synthetic && node.Ref != ""
}

func axSubtreeHasTarget(node *AXNode) bool {
	if node == nil {
		return false
	}
	if axNodeTargetTruth(node) {
		return true
	}
	for _, child := range node.Children {
		if axSubtreeHasTarget(child) {
			return true
		}
	}
	return false
}

func collectAXTargetIDs(nodes []*AXNode) map[string]bool {
	out := map[string]bool{}
	var visit func([]*AXNode)
	visit = func(current []*AXNode) {
		for _, node := range current {
			if axNodeTargetTruth(node) {
				out[node.ID] = true
			}
			visit(node.Children)
		}
	}
	visit(nodes)
	return out
}

func targetRecall(expected map[string]bool, nodes []*AXNode) int {
	current := collectAXTargetIDs(nodes)
	count := 0
	for id := range expected {
		if current[id] {
			count++
		}
	}
	return count
}

func ambiguityCount(nodes []*AXNode) int {
	counts := map[string]int{}
	var visit func([]*AXNode)
	visit = func(current []*AXNode) {
		for _, node := range current {
			if axNodeTargetTruth(node) && node.Name != "" {
				counts[string(node.Role)+"\x00"+comparableTreeText(node.Name)]++
			}
			visit(node.Children)
		}
	}
	visit(nodes)
	ambiguous := 0
	for _, count := range counts {
		if count > 1 {
			ambiguous += count
		}
	}
	return ambiguous
}

func mapAXForest(
	nodes []*AXNode,
	transform func(*AXNode) ([]*AXNode, int, int, error),
) ([]*AXNode, int, int, error) {
	out := make([]*AXNode, 0, len(nodes))
	removed, unwrapped := 0, 0
	for _, node := range nodes {
		children, childRemoved, childUnwrapped, err := mapAXForest(
			node.Children, transform,
		)
		if err != nil {
			return nil, 0, 0, err
		}
		node.Children = children
		for _, child := range node.Children {
			child.Parent = node
		}
		replacement, ownRemoved, ownUnwrapped, err := transform(node)
		if err != nil {
			return nil, 0, 0, err
		}
		for _, child := range replacement {
			child.Parent = node.Parent
		}
		out = append(out, replacement...)
		removed += childRemoved + ownRemoved
		unwrapped += childUnwrapped + ownUnwrapped
	}
	return out, removed, unwrapped, nil
}

func normalizeWhitespacePlugin() DistillationPlugin {
	return distillationPlugin{name: "normalize_whitespace", fn: func(
		_ *DistillationContext, roots []*AXNode,
	) ([]*AXNode, int, int, error) {
		var visit func([]*AXNode)
		visit = func(nodes []*AXNode) {
			for _, node := range nodes {
				node.Name = normalizedTreeText(node.Name)
				node.Properties.Value = normalizedTreeText(node.Properties.Value)
				node.Properties.Description = normalizedTreeText(node.Properties.Description)
				node.Properties.Placeholder = normalizedTreeText(node.Properties.Placeholder)
				visit(node.Children)
			}
		}
		visit(roots)
		return roots, 0, 0, nil
	}}
}

func removeDecorativeLeavesPlugin() DistillationPlugin {
	return distillationPlugin{name: "remove_empty_decorative_leaves", fn: func(
		_ *DistillationContext, roots []*AXNode,
	) ([]*AXNode, int, int, error) {
		return mapAXForest(roots, func(node *AXNode) ([]*AXNode, int, int, error) {
			if len(node.Children) == 0 && !node.ForceKeep &&
				(node.Role == RoleGeneric || node.Role == "group") &&
				!axNodeHasContent(node) && !axNodeHasState(node) &&
				!axNodeHasCapability(node) && node.Ref == "" {
				return nil, 1, 0, nil
			}
			return []*AXNode{node}, 0, 0, nil
		})
	}}
}

func removeUnaddressableControlsPlugin() DistillationPlugin {
	return distillationPlugin{
		name:                "remove_unaddressable_controls",
		allowTargetOmission: false,
		fn: func(
			context *DistillationContext, roots []*AXNode,
		) ([]*AXNode, int, int, error) {
			if context == nil || context.Snapshot == nil ||
				context.Snapshot.AppRef == "" ||
				context.Snapshot.WindowRef == "" {
				return roots, 0, 0, nil
			}
			return mapAXForest(
				roots,
				func(node *AXNode) ([]*AXNode, int, int, error) {
					if node == nil || !node.AddressabilityEvaluated ||
						node.ForceKeep || node.Ref != "" ||
						!axNodeHasInteractionSemantics(node) ||
						axNodeHasState(node) {
						return []*AXNode{node}, 0, 0, nil
					}
					switch node.AddressabilityReason {
					case "hidden_under_collapsed_ancestor",
						"not_currently_showing",
						"not_currently_visible":
						if len(node.Children) == 0 {
							return nil, 1, 0, nil
						}
						return node.Children, 1, 1, nil
					case "unnamed_target":
						if len(node.Children) == 0 &&
							!axNodeHasContent(node) {
							return nil, 1, 0, nil
						}
					}
					// Unknown addressability is an incomplete fact, not proof
					// that the control is disposable. Preserve it until the
					// provider explains why it cannot be targeted.
					return []*AXNode{node}, 0, 0, nil
				},
			)
		},
	}
}

func coalesceTextPlugin() DistillationPlugin {
	return distillationPlugin{name: "coalesce_text", fn: func(
		_ *DistillationContext, roots []*AXNode,
	) ([]*AXNode, int, int, error) {
		var visit func([]*AXNode) int
		visit = func(nodes []*AXNode) int {
			removed := 0
			for _, parent := range nodes {
				removed += visit(parent.Children)
				compacted := make([]*AXNode, 0, len(parent.Children))
				for _, child := range parent.Children {
					if child.Role == RoleText && child.Ref == "" &&
						len(child.Children) == 0 && len(compacted) > 0 {
						previous := compacted[len(compacted)-1]
						if previous.Role == RoleText && previous.Ref == "" &&
							len(previous.Children) == 0 {
							previousText := FirstNonEmpty(
								previous.Name,
								previous.Properties.Value,
							)
							childText := FirstNonEmpty(
								child.Name,
								child.Properties.Value,
							)
							combined := normalizedTreeText(
								strings.TrimSpace(previousText + " " + childText),
							)
							previous.Name = combined
							previous.Properties.Value = ""
							previous.NameProvenance = AXNameProvenance{
								Source:     "synthetic_text_run",
								Confidence: "inferred",
							}
							removed++
							continue
						}
					}
					compacted = append(compacted, child)
				}
				parent.Children = compacted
			}
			return removed
		}
		removed := visit(roots)
		return roots, removed, 0, nil
	}}
}

func unwrapNeutralPlugin() DistillationPlugin {
	return distillationPlugin{name: "unwrap_neutral_single_child", fn: func(
		_ *DistillationContext, roots []*AXNode,
	) ([]*AXNode, int, int, error) {
		return mapAXForest(roots, func(node *AXNode) ([]*AXNode, int, int, error) {
			if len(node.Children) == 1 && !node.ForceKeep && node.Ref == "" &&
				(node.Role == RoleGeneric || node.Role == "group") &&
				!axNodeHasContent(node) && !axNodeHasState(node) &&
				!axNodeHasCapability(node) {
				return node.Children, 1, 1, nil
			}
			return []*AXNode{node}, 0, 0, nil
		})
	}}
}

func relationDuplicatePlugin() DistillationPlugin {
	return distillationPlugin{name: "remove_relation_proven_duplicate_labels", fn: func(
		_ *DistillationContext, roots []*AXNode,
	) ([]*AXNode, int, int, error) {
		textByIdentity := map[string][]*AXNode{}
		var collect func([]*AXNode)
		collect = func(nodes []*AXNode) {
			for _, node := range nodes {
				if node.Role == RoleText {
					if key := providerObjectIdentityKey(
						node.ProviderBusName,
						node.ProviderObjectPath,
						node.ProcessGeneration,
					); key != "" {
						textByIdentity[key] = append(textByIdentity[key], node)
					}
				}
				collect(node.Children)
			}
		}
		collect(roots)
		redundant := map[string]bool{}
		var scan func([]*AXNode)
		scan = func(nodes []*AXNode) {
			for _, node := range nodes {
				if !node.NameProvenance.RelationsComplete ||
					node.NameProvenance.Source != "labelled_by" ||
					node.NameProvenance.Confidence != "exact" {
					scan(node.Children)
					continue
				}
				for _, target := range node.NameProvenance.Contributors {
					key := providerObjectIdentityKey(
						target.BusName,
						target.ObjectPath,
						target.ProcessGeneration,
					)
					if matches := textByIdentity[key]; len(matches) == 1 {
						redundant[matches[0].ID] = true
					}
				}
				scan(node.Children)
			}
		}
		scan(roots)
		return mapAXForest(roots, func(node *AXNode) ([]*AXNode, int, int, error) {
			if redundant[node.ID] && node.Ref == "" && len(node.Children) == 0 {
				return nil, 1, 0, nil
			}
			return []*AXNode{node}, 0, 0, nil
		})
	}}
}

func providerObjectIdentityKey(
	busName,
	objectPath,
	processGeneration string,
) string {
	busName = strings.TrimSpace(busName)
	objectPath = strings.TrimSpace(objectPath)
	processGeneration = strings.TrimSpace(processGeneration)
	if busName == "" || objectPath == "" || processGeneration == "" {
		return ""
	}
	return strings.Join(
		[]string{processGeneration, busName, objectPath},
		"\x00",
	)
}

func projectionValueIsDecisionBearing(node *AXNode) bool {
	if node == nil || axNodeHasState(node) {
		return true
	}
	if node.Capabilities.SetValue ||
		node.Capabilities.EditText ||
		node.Capabilities.Scroll {
		return true
	}
	switch node.Role {
	case "textbox", "combobox", "slider", "spinbutton",
		"progressbar", "scrollbar", "meter":
		return true
	default:
		return false
	}
}

func suppressCohortPropertiesPlugin() DistillationPlugin {
	return distillationPlugin{name: "suppress_redundant_cohort_properties", fn: func(
		context *DistillationContext, roots []*AXNode,
	) ([]*AXNode, int, int, error) {
		const minimumCohort = 6
		const minimumRepeated = 5
		var visit func([]*AXNode)
		visit = func(nodes []*AXNode) {
			for _, parent := range nodes {
				visit(parent.Children)
				cohorts := map[CanonicalRole][]*AXNode{}
				for _, child := range parent.Children {
					cohorts[child.Role] = append(cohorts[child.Role], child)
				}
				for _, cohort := range cohorts {
					if len(cohort) < minimumCohort {
						continue
					}
					nameCounts := map[string]int{}
					valueCounts := map[string]int{}
					for _, child := range cohort {
						nameCounts[comparableTreeText(child.Name)]++
						value := comparableTreeText(child.Properties.Value)
						if value != "" && !projectionValueIsDecisionBearing(child) {
							valueCounts[value]++
						}
					}
					redundant := map[string]bool{}
					for value, count := range valueCounts {
						if count >= minimumRepeated && count*5 >= len(cohort)*4 {
							redundant[value] = true
						}
					}
					for _, child := range cohort {
						name := comparableTreeText(child.Name)
						value := comparableTreeText(child.Properties.Value)
						if name == "" || nameCounts[name] != 1 ||
							!redundant[value] ||
							projectionValueIsDecisionBearing(child) {
							continue
						}
						child.Properties.Value = ""
						context.StageSuppressedProperties++
					}
				}
			}
		}
		visit(roots)
		return roots, 0, 0, nil
	}}
}

func markAXDistinguishingAncestors(roots []*AXNode) {
	groups := map[string][]*AXNode{}
	var visit func([]*AXNode)
	visit = func(nodes []*AXNode) {
		for _, node := range nodes {
			if axNodeTargetTruth(node) && node.Name != "" {
				key := string(node.Role) + "\x00" + comparableTreeText(node.Name)
				groups[key] = append(groups[key], node)
			}
			visit(node.Children)
		}
	}
	visit(roots)
	for _, duplicates := range groups {
		if len(duplicates) < 2 {
			continue
		}
		paths := make([][]*AXNode, 0, len(duplicates))
		minimum := -1
		for _, node := range duplicates {
			path := []*AXNode{}
			for current := node; current != nil; current = current.Parent {
				path = append(path, current)
			}
			for left, right := 0, len(path)-1; left < right; left, right = left+1, right-1 {
				path[left], path[right] = path[right], path[left]
			}
			paths = append(paths, path)
			if minimum < 0 || len(path) < minimum {
				minimum = len(path)
			}
		}
		prefix := 0
		for prefix < minimum {
			id := paths[0][prefix].ID
			same := true
			for _, path := range paths[1:] {
				if path[prefix].ID != id {
					same = false
					break
				}
			}
			if !same {
				break
			}
			prefix++
		}
		for _, path := range paths {
			if prefix < len(path)-1 {
				path[prefix].ForceKeep = true
				path[prefix].DistinguishesDuplicates = true
			}
		}
	}
}

func summarizeCollectionsPlugin() DistillationPlugin {
	return distillationPlugin{name: "summarize_homogeneous_collections", fn: func(
		_ *DistillationContext, roots []*AXNode,
	) ([]*AXNode, int, int, error) {
		const threshold = 10
		removed := 0
		var visit func([]*AXNode)
		visit = func(nodes []*AXNode) {
			for _, parent := range nodes {
				visit(parent.Children)
				groups := map[CanonicalRole][]int{}
				for index, child := range parent.Children {
					if isAXCollectionItemRole(child.Role) {
						groups[child.Role] = append(groups[child.Role], index)
					}
				}
				hide := map[int]bool{}
				summaryAt := map[int]*AXNode{}
				for role, indices := range groups {
					if len(indices) < threshold {
						continue
					}
					firstHidden := -1
					hidden := 0
					for order, index := range indices {
						child := parent.Children[index]
						if order < 3 || order >= len(indices)-2 ||
							child.ForceKeep ||
							axSubtreeHasTarget(child) || axNodeHasState(child) {
							continue
						}
						hide[index] = true
						hidden += countAXNodes([]*AXNode{child})
						if firstHidden < 0 {
							firstHidden = index
						}
					}
					if hidden > 0 {
						removed += hidden
						summaryAt[firstHidden] = &AXNode{
							ID:        fmt.Sprintf("%s/collection:%s", parent.ID, role),
							Role:      RoleGeneric,
							Synthetic: true,
							Properties: AXProperties{Omitted: fmt.Sprintf(
								"collection role=%s items=%d hidden_nodes=%d",
								role, len(indices), hidden,
							)},
							Parent: parent,
						}
					}
				}
				if len(hide) == 0 {
					continue
				}
				compacted := make([]*AXNode, 0, len(parent.Children))
				for index, child := range parent.Children {
					if summary := summaryAt[index]; summary != nil {
						compacted = append(compacted, summary)
					}
					if hide[index] {
						continue
					}
					compacted = append(compacted, child)
				}
				parent.Children = compacted
				for _, child := range compacted {
					child.Parent = parent
				}
			}
		}
		visit(roots)
		return roots, removed, 0, nil
	}}
}

func outputBudgetAuditPlugin() DistillationPlugin {
	return distillationPlugin{
		name:                "apply_output_budget",
		allowTargetOmission: true,
		fn: func(
			context *DistillationContext, roots []*AXNode,
		) ([]*AXNode, int, int, error) {
			roots = applyAXStructuralBudget(context, roots)
			return roots, 0, 0, nil
		}}
}

func defaultDistillationPlugins() []DistillationPlugin {
	return []DistillationPlugin{
		normalizeWhitespacePlugin(),
		removeDecorativeLeavesPlugin(),
		removeUnaddressableControlsPlugin(),
		coalesceTextPlugin(),
		unwrapNeutralPlugin(),
		relationDuplicatePlugin(),
		suppressCohortPropertiesPlugin(),
		summarizeCollectionsPlugin(),
		shortenNamedDescriptionsPlugin(),
		outputBudgetAuditPlugin(),
	}
}

// maxNamedDescriptionRunes bounds help text on nodes that already have a
// name. In recorded agent traces, no description the model quoted in its
// reasoning or arguments was longer than 80 runes, while longer ones (checkbox
// and spin-button help, toolbar usage hints) were about 1% of AX output.
const maxNamedDescriptionRunes = 80

// boilerplateDescriptions carry no information about the node. LibreOffice
// puts the first one on every toolbar group.
var boilerplateDescriptions = map[string]bool{
	"Please press enter to go into child control for more operations": true,
}

// shortenNamedDescriptionsPlugin shortens help text on named nodes. The name
// already identifies such a node, and the description repeats generic help.
// The full description is kept when it may be the only label (unnamed node),
// when it carries document content (content and item roles such as paragraph,
// heading and cell), on links (whose description is often the URL), on find
// matches, and on the node the current action targeted.
func shortenNamedDescriptionsPlugin() DistillationPlugin {
	return distillationPlugin{name: "shorten_named_descriptions", fn: func(
		context *DistillationContext, roots []*AXNode,
	) ([]*AXNode, int, int, error) {
		actedRef := ""
		if context != nil && context.Snapshot != nil {
			actedRef = context.Snapshot.ActedRef
		}
		var visit func([]*AXNode)
		visit = func(nodes []*AXNode) {
			for _, node := range nodes {
				visit(node.Children)
				description := node.Properties.Description
				if description == "" || node.FindMatch ||
					(actedRef != "" && node.Ref == actedRef) {
					continue
				}
				if boilerplateDescriptions[description] {
					node.Properties.Description = ""
					context.StageSuppressedProperties++
					continue
				}
				if node.Name == "" ||
					node.SemanticClass == "content" ||
					node.SemanticClass == "item" ||
					node.Role == "link" {
					continue
				}
				if shortened, cut := shortenAtWord(
					description, maxNamedDescriptionRunes,
				); cut {
					node.Properties.Description = shortened
					context.StageSuppressedProperties++
				}
			}
		}
		visit(roots)
		return roots, 0, 0, nil
	}}
}

// shortenAtWord cuts text to at most limit runes, preferring the last word
// boundary in the second half, and marks the cut with an ellipsis.
func shortenAtWord(text string, limit int) (string, bool) {
	runes := []rune(text)
	if len(runes) <= limit {
		return text, false
	}
	head := string(runes[:limit])
	if space := strings.LastIndex(head, " "); space >= 0 &&
		utf8.RuneCountInString(head[:space]) >= limit/2 {
		head = head[:space]
	}
	return strings.TrimRight(head, " ,;:.-") + "…", true
}

func distillAXSnapshot(snapshot *AXSnapshot) error {
	if snapshot == nil {
		return errors.New("cannot distill a nil AXSnapshot")
	}
	markAXDistinguishingAncestors(snapshot.Roots)
	context := &DistillationContext{
		Snapshot:       snapshot,
		CanonicalTruth: collectAXTargetIDs(snapshot.CanonicalRoots),
	}
	roots := snapshot.Roots
	for _, plugin := range defaultDistillationPlugins() {
		context.StageSuppressedProperties = 0
		context.StageBudgetApplied = false
		context.StageBudgetCharsBefore = 0
		context.StageBudgetCharsAfter = 0
		context.StageBudgetOmittedNodes = 0
		context.StageBudgetOmittedTargets = 0
		context.StageBudgetExhausted = false
		beforeNodes := countAXNodes(roots)
		beforeAmbiguity := ambiguityCount(roots)
		updated, removed, unwrapped, err := plugin.Apply(context, roots)
		report := DistillationStageReport{
			Name:                 plugin.Name(),
			InputNodes:           beforeNodes,
			RemovedNodes:         removed,
			UnwrappedNodes:       unwrapped,
			SuppressedProperties: context.StageSuppressedProperties,
			BudgetApplied:        context.StageBudgetApplied,
			BudgetCharsBefore:    context.StageBudgetCharsBefore,
			BudgetCharsAfter:     context.StageBudgetCharsAfter,
			BudgetOmittedNodes:   context.StageBudgetOmittedNodes,
			BudgetOmittedTargets: context.StageBudgetOmittedTargets,
			BudgetExhausted:      context.StageBudgetExhausted,
			TargetCount:          len(context.CanonicalTruth),
			AmbiguityBefore:      beforeAmbiguity,
		}
		if err == nil {
			report.OutputNodes = countAXNodes(updated)
			report.TargetRecall = targetRecall(context.CanonicalTruth, updated)
			report.AmbiguityAfter = ambiguityCount(updated)
			if report.TargetRecall != report.TargetCount &&
				!plugin.AllowsTargetOmission() {
				err = fmt.Errorf(
					"target recall %d/%d",
					report.TargetRecall, report.TargetCount,
				)
			} else if report.AmbiguityAfter > report.AmbiguityBefore {
				err = fmt.Errorf(
					"ambiguity increased %d -> %d",
					report.AmbiguityBefore, report.AmbiguityAfter,
				)
			}
		}
		if err != nil {
			report.InvariantFailure = err.Error()
			snapshot.Distillation = append(snapshot.Distillation, report)
			return fmt.Errorf("distillation stage %s: %w", plugin.Name(), err)
		}
		snapshot.Distillation = append(snapshot.Distillation, report)
		roots = updated
	}
	snapshot.Roots = roots
	return snapshot.Validate()
}
