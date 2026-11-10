package core

import (
	"fmt"
	"sort"
	"strconv"
	"strings"
	"unicode"
	"unicode/utf8"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

type AXRenderOptions struct {
	IncludeAudit bool
	RootKey      string
}

func yamlScalar(value string) string {
	value = strings.ToValidUTF8(value, "\uFFFD")
	if value == "" {
		return `""`
	}
	safe := true
	switch strings.ToLower(value) {
	case "null", "true", "false", "yes", "no", "on", "off", "~":
		safe = false
	}
	for index, char := range value {
		if unicode.IsControl(char) || char == ':' || char == '#' ||
			char == '[' || char == ']' || char == '{' || char == '}' ||
			char == ',' || char == '&' || char == '*' || char == '!' ||
			char == '|' || char == '>' || char == '\'' || char == '"' ||
			char == '%' || char == '@' || char == '`' ||
			(index == 0 && (char == '-' || char == '?' || char == ' ')) {
			safe = false
			break
		}
	}
	if safe && utf8.ValidString(value) && strings.TrimSpace(value) == value {
		return value
	}
	return strconv.Quote(value)
}

func renderTriState(name string, value TriState, out *[]string) {
	if value == TriUnknown || value == "" {
		return
	}
	*out = append(*out, name+"="+string(value))
}

func renderAXStateTokens(states AXStates) []string {
	out := []string{}
	renderTriState("active", states.Active, &out)
	if states.Checked != CheckedUnknown && states.Checked != "" {
		out = append(out, "checked="+string(states.Checked))
	}
	renderTriState("expanded", states.Expanded, &out)
	renderTriState("selected", states.Selected, &out)
	renderTriState("disabled", states.Disabled, &out)
	renderTriState("pressed", states.Pressed, &out)
	renderTriState("invalid", states.Invalid, &out)
	if states.Level != nil {
		out = append(out, "level="+strconv.Itoa(*states.Level))
	}
	sort.Strings(out)
	return out
}

func axNodeNeedsAddressabilityMarker(node *AXNode) bool {
	if node == nil {
		return false
	}
	if axNodeHasInteractionSemantics(node) {
		return true
	}
	if node.SemanticClass == "control" || node.SemanticClass == "item" {
		return true
	}
	// Menus are semantic boundaries, but remain visible interaction affordances.
	return node.Role == "menu"
}

func renderAXNodeLabel(node *AXNode) string {
	if node == nil {
		return "generic"
	}
	label := renderAXNodeIdentityLabel(node)
	if node.Ref != "" {
		label += " [ref=" + node.Ref + "]"
	} else if node.AddressabilityEvaluated &&
		axNodeNeedsAddressabilityMarker(node) {
		label += " [addressable=false]"
	} else if axNodeNeedsAddressabilityMarker(node) {
		label += " [addressable=unknown]"
	}
	return label
}

// renderAXNodeIdentityLabel renders role, name, states and visibility,
// without the ref or the addressability marker.
func renderAXNodeIdentityLabel(node *AXNode) string {
	if node == nil {
		return "generic"
	}
	label := string(node.Role)
	if label == "" {
		label = string(RoleGeneric)
	}
	if node.Name != "" {
		label += " " + strconv.Quote(node.Name)
	}
	if value, _ := axInlineCellValue(node); value != "" {
		label += " = " + strconv.Quote(value)
	}
	states := renderAXStateTokens(node.States)
	if len(states) > 0 {
		label += " [" + strings.Join(states, " ") + "]"
	}
	if evidence := node.VisibilityEvidence; evidence != nil {
		switch {
		case evidence.State == "unknown" &&
			(evidence.Reason == "unsupported" || evidence.Reason == "read_error"):
			label += " [visibility=unknown reason=" + evidence.Reason + "]"
		case evidence.State == "offscreen" && evidence.Reason == "focus_geometry_override":
			label += " [visibility=offscreen reason=focus_geometry_override]"
		}
	}
	return label
}

func renderAXTreeNodeLine(node *AXNode, depth int, hasChildren bool) string {
	line := strings.Repeat(" ", depth) + "- " + renderAXNodeLabel(node)
	if hasChildren {
		line += ":"
	}
	return line
}

func renderAXTreePropertyLine(property [2]string, depth int) string {
	return strings.Repeat(" ", depth) + "- " + property[0] + ": " +
		yamlScalar(property[1])
}

func roleExposesWorkingValue(role CanonicalRole) bool {
	switch role {
	case "textbox", "combobox", "slider", "spinbutton", "progressbar",
		"scrollbar":
		return true
	default:
		return false
	}
}

// axInlineCellValueRunes bounds a cell value shown on the cell's line; find
// shows up to maxFindTextRunes of a longer one.
const axInlineCellValueRunes = 40

// axInlineCellValue returns the value shown after a cell's name and whether
// it was shortened. A spreadsheet names a cell by its address (A1), so its
// content is only in the value; item views name a cell by its text, which
// the name already shows.
func axInlineCellValue(node *AXNode) (string, bool) {
	if node == nil || (node.Role != "cell" && node.Role != "gridcell") {
		return "", false
	}
	value := strings.TrimSpace(strings.ToValidUTF8(node.Properties.Value, "\uFFFD"))
	if comparableTreeText(value) == "" || semanticTextEqual(value, node.Name) ||
		pureNonSemanticUnicode(value, node) {
		return "", false
	}
	return shortenAtWord(strings.Join(strings.Fields(value), " "), axInlineCellValueRunes)
}

func renderAXProperties(node *AXNode, options AXRenderOptions) [][2]string {
	properties := make([][2]string, 0, 8)
	if node.Properties.Value != "" &&
		(roleExposesWorkingValue(node.Role) &&
			!semanticTextEqual(node.Properties.Value, node.Name) &&
			!pureNonSemanticUnicode(node.Properties.Value, node)) {
		properties = append(properties, [2]string{"/value", node.Properties.Value})
	}
	if node.Properties.Placeholder != "" &&
		!semanticTextEqual(node.Properties.Placeholder, node.Properties.Value) {
		properties = append(properties, [2]string{"/placeholder", node.Properties.Placeholder})
	}
	if node.Properties.Description != "" &&
		(!semanticTextEqual(node.Properties.Description, node.Name) &&
			!semanticTextEqual(node.Properties.Description, node.Properties.Value)) {
		properties = append(properties, [2]string{"/description", node.Properties.Description})
	}
	if node.Capabilities.SetValue {
		properties = append(properties, [2]string{"/set-value", "true"})
	}
	if node.Capabilities.EditText {
		properties = append(properties, [2]string{"/edit-text", "true"})
	}
	if node.Capabilities.Scroll {
		properties = append(properties, [2]string{"/scroll", "true"})
	}
	if node.Properties.Omitted != "" {
		properties = append(properties, [2]string{"/omitted", node.Properties.Omitted})
	}
	for _, annotation := range node.Annotations {
		if annotation.Kind == axCellSummaryAnnotation && annotation.Detail != "" {
			properties = append(properties, [2]string{"/empty-cells", annotation.Detail})
		}
		if annotation.Reason == axNullReferenceReason && annotation.Detail != "" {
			properties = append(properties, [2]string{"/null-children", annotation.Detail})
		}
	}
	if node.FindMatch {
		properties = append(properties, [2]string{"/match", "true"})
	}
	return properties
}

func semanticTextEqual(left, right string) bool {
	return comparableTreeText(left) != "" &&
		comparableTreeText(left) == comparableTreeText(right)
}

func pureNonSemanticUnicode(value string, node *AXNode) bool {
	if node == nil || node.Name != "" ||
		node.Properties.Description != "" ||
		node.Properties.Placeholder != "" {
		return false
	}
	sawRune := false
	for _, char := range strings.TrimSpace(value) {
		sawRune = true
		if !(unicode.IsControl(char) ||
			unicode.In(char, unicode.Co, unicode.Cf) ||
			char == '\u200b' || char == '\u200c' ||
			char == '\u200d' || char == '\ufeff') {
			return false
		}
	}
	return sawRune
}

func RenderAXSnapshotYAML(
	snapshot *AXSnapshot,
	options AXRenderOptions,
) ([]string, error) {
	if snapshot == nil {
		return nil, fmt.Errorf("cannot render nil AXSnapshot")
	}
	if err := snapshot.Validate(); err != nil {
		return nil, err
	}
	rootKey := strings.TrimSpace(options.RootKey)
	if rootKey == "" {
		rootKey = "ax"
	}
	lines := []string{rootKey + ":"}
	var render func([]*AXNode, int)
	render = func(nodes []*AXNode, depth int) {
		for _, node := range nodes {
			properties := renderAXProperties(node, options)
			hasChildren := len(properties) > 0 || len(node.Children) > 0
			lines = append(lines, renderAXTreeNodeLine(node, depth, hasChildren))
			for _, property := range properties {
				lines = append(lines, renderAXTreePropertyLine(property, depth+1))
			}
			render(node.Children, depth+1)
		}
	}
	render(snapshot.Roots, 1)
	if options.IncludeAudit && len(snapshot.Distillation) > 0 {
		lines = append(lines, "distillation:")
		for _, report := range snapshot.Distillation {
			lines = append(lines,
				" - "+yamlScalar(report.Name)+":",
				"  - /input-nodes: "+strconv.Itoa(report.InputNodes),
				"  - /output-nodes: "+strconv.Itoa(report.OutputNodes),
				"  - /target-recall: "+
					strconv.Itoa(report.TargetRecall)+"/"+strconv.Itoa(report.TargetCount),
			)
		}
	}
	return lines, nil
}

func AXProjectedIndices(nodes []*AXNode) map[int]bool {
	out := map[int]bool{}
	var visit func([]*AXNode)
	visit = func(current []*AXNode) {
		for _, node := range current {
			if !node.Synthetic {
				out[node.ProviderIndex] = true
			}
			visit(node.Children)
		}
	}
	visit(nodes)
	return out
}

func (s *Service) BuildAndRenderAXSnapshot(
	snapshot *AppSnapshot,
	view string,
) error {
	// App and window refs are identity inputs for the canonical AST. Assign
	// them before construction so provider identity is scoped by stable wN
	// rather than by a replaceable provider top-level object.
	s.assignWindowRefs(snapshot)
	axSnapshot, err := buildAXSnapshot(snapshot)
	if err != nil {
		return err
	}
	axSnapshot.AppRef = snapshot.publicAppRef
	axSnapshot.ActedRef = snapshot.actedRef
	// Addressability is an identity property of the canonical snapshot.
	// Evaluate it before deriving the working projection so distillation can
	// remove or unwrap controls that the ref-only action surface cannot target.
	s.RefRegistry.AssignSnapshotRefs(axSnapshot)
	// Preserve the requested root even when it has no label or is currently
	// unaddressable. Set this only after visibility and identity checks so a
	// read-only root cannot make a hidden control actionable.
	for _, root := range axSnapshot.Roots {
		if root.ExplicitRoot {
			root.ForceKeep = true
		}
	}
	if normalizedTreeView(view) == contract.TreeViewFull {
		axSnapshot.Roots = cloneAXForest(axSnapshot.CanonicalRoots, nil)
	} else if err := distillAXSnapshot(axSnapshot); err != nil {
		return err
	}
	lines, err := RenderAXSnapshotYAML(axSnapshot, AXRenderOptions{})
	if err != nil {
		return err
	}
	snapshot.AXSnapshot = axSnapshot
	snapshot.TreeLines = lines
	return nil
}
