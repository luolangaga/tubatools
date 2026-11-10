package core

import (
	"sort"
	"strconv"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// The act report is the first text of every act result after the evidence
// line. It states what the tool already knows from its own reads, so the
// model does not have to ask:
//
//	effect: r58 checkbox "Interface" checked=false→true
//	windows: opened w5 "Insert Sheet" (dialog, focused); closed w3 "Print"; focus w2
//
// One effect line per action that named an element ref, compared between
// the read before that action and the next read after it. The windows line
// compares the application's top-level windows before the first action with
// those after the last one. Both come from reads the batch makes anyway; the
// report adds no provider call.

const (
	axReportEffectPrefix  = "effect: "
	axReportWindowsPrefix = "windows: "
)

// actReportTrace records the cached snapshots an act batch passes through.
type actReportTrace struct {
	appToken    string
	windowRef   string
	windowTitle string
	observe     string
	// start is the snapshot before any input; before[i] the snapshot just
	// before action i ran.
	start  *AppSnapshot
	before map[int]*AppSnapshot
}

func newActReportTrace(obs observation) *actReportTrace {
	return &actReportTrace{before: map[int]*AppSnapshot{}, observe: obs.resolvedMode()}
}

func (t *actReportTrace) bind(s *Service, context *AXBatchContext) {
	if t == nil || context == nil {
		return
	}
	t.appToken = context.AppToken
	t.windowRef = context.WindowRef
	t.start = s.currentSnapshot(context.AppToken)
	if entry, err := s.WindowRefRegistry.Resolve(context.WindowRef, ""); err == nil {
		t.windowTitle = entry.ProviderWindowName
	}
}

func (t *actReportTrace) recordBefore(s *Service, index int) {
	if t == nil || t.appToken == "" {
		return
	}
	t.before[index] = s.currentSnapshot(t.appToken)
}

// after returns the first snapshot read after action index, or nil when no
// read happened after it.
func (t *actReportTrace) after(s *Service, index int) *AppSnapshot {
	next := -1
	for candidate := range t.before {
		if candidate > index && (next < 0 || candidate < next) {
			next = candidate
		}
	}
	after := s.currentSnapshot(t.appToken)
	if next >= 0 {
		after = t.before[next]
	}
	if after == nil || after == t.before[index] {
		return nil
	}
	return after
}

// withActReport puts the report first in an act result. A call rejected
// before any input gets no report: nothing happened.
func (s *Service) withActReport(
	result ToolCallResult,
	actions []axBatchAction,
	trace *actReportTrace,
) ToolCallResult {
	if trace == nil || trace.appToken == "" || result.StructuredContent == nil ||
		!axEvidenceReportsInputEmission(result.StructuredContent) {
		return result
	}
	completed := len(actions)
	if batch, ok := result.StructuredContent["a11y_batch_result"].(map[string]any); ok {
		if value, ok := IntEvidence(batch["actions_completed_without_call_error"]); ok {
			completed = value
		}
	}
	lines := []string{}
	// The effect counts as observed only when every action named an element
	// and each of those elements visibly changed; a keypress or a no-change
	// action keeps the call's uncertainty.
	verified := completed == len(actions)
	for index, action := range actions {
		if action.actionType == "wait" {
			continue
		}
		if !isShortAXRef(action.targetRef) {
			verified = false
			continue
		}
		if _, ran := trace.before[index]; !ran {
			continue
		}
		if index > completed {
			break
		}
		line, changed := s.actEffectLine(trace, index, action.targetRef)
		lines = append(lines, line)
		verified = verified && changed
	}
	lines = append(lines, s.actWindowsLine(trace, result))
	if verified && len(lines) > 1 {
		result.StructuredContent["target_effect_observed"] = true
	}
	result.Content = append(
		[]ContentItem{{Type: "text", Text: strings.Join(lines, "\n")}},
		result.Content...,
	)
	return result
}

// actEffectLine compares one element between the reads around its action.
// changed reports a visible change of the element.
func (s *Service) actEffectLine(trace *actReportTrace, index int, ref string) (string, bool) {
	before := axSnapshotNodeByRef(trace.before[index], ref)
	afterSnapshot := trace.after(s, index)
	head := axReportEffectPrefix + ref
	if before != nil {
		head += " " + axReportNodeLabel(before)
	}
	if afterSnapshot == nil {
		reason := "no read after the action"
		if trace.observe == contract.ObserveNone {
			reason = `observe "none" skipped the read after the action`
		}
		return head + " unknown (" + reason + ")", false
	}
	after := axSnapshotNodeByRef(afterSnapshot, ref)
	if after == nil {
		beforeHandle := ""
		if trace.before[index] != nil {
			beforeHandle = trace.before[index].WindowHandle
		}
		switch {
		case afterSnapshot.WindowHandle != beforeHandle && beforeHandle != "":
			return head + " unknown (the read after the action is of " +
				axReportSnapshotWindow(afterSnapshot) + ")", false
		case before != nil && afterSnapshot.AXSnapshot != nil && afterSnapshot.AXSnapshot.RefInventoryComplete:
			return head + " gone", true
		default:
			return head + " unknown (the read after the action did not include it)", false
		}
	}
	if before == nil {
		return axReportEffectPrefix + ref + " " + axReportNodeLabel(after) + " " +
			strings.Join(axReportNodeFields(after), " ") +
			" (not read before the action)", false
	}
	changes := axReportNodeChanges(before, after)
	window, compared := axReportWindowResponse(trace.before[index], afterSnapshot, ref)
	if len(changes) == 0 {
		if compared && window.any() {
			// Nodes appearing or disappearing under the element (a menu that
			// opened or closed) are its own response; changes elsewhere
			// cannot be attributed to it.
			return head + " " + window.besideUnchangedElement(),
				window.underAdded+window.underRemoved > 0
		}
		return head + " no visible change", false
	}
	line := head + " " + strings.Join(changes, " ")
	if compared && window.onlyStates() {
		// A toolkit action can flip a checked or selected flag without the
		// application doing what a user's click makes it do: a Qt category
		// button turned checked while its settings page stayed put. For a
		// plain checkbox this is the whole effect; for a control that should
		// switch a page or run a command it means the application did not
		// respond. Stating it lets the caller tell the two apart.
		line += " (only state flags changed in this window)"
	}
	return line, true
}

// axReportWindowChange counts the nodes that changed in the acted window,
// split into those under the acted element and those elsewhere.
type axReportWindowChange struct {
	underAdded, underRemoved, underChanged int
	otherAdded, otherRemoved, otherChanged int
	// nonState counts changes other than a state flag of an existing node.
	nonState int
}

func (c axReportWindowChange) any() bool {
	return c.underAdded+c.underRemoved+c.underChanged+
		c.otherAdded+c.otherRemoved+c.otherChanged > 0
}

func (c axReportWindowChange) onlyStates() bool {
	return c.nonState == 0
}

func (c axReportWindowChange) besideUnchangedElement() string {
	parts := []string{}
	count := func(n int, verb, where string) {
		if n == 0 {
			return
		}
		noun := "nodes"
		if n == 1 {
			noun = "node"
		}
		parts = append(parts, strconv.Itoa(n)+" "+noun+" "+verb+where)
	}
	count(c.underAdded, "appeared", " under it")
	count(c.underRemoved, "disappeared", " under it")
	count(c.underChanged, "changed", " under it")
	count(c.otherAdded, "appeared", " elsewhere")
	count(c.otherRemoved, "disappeared", " elsewhere")
	count(c.otherChanged, "changed", " elsewhere")
	return "itself unchanged; " + strings.Join(parts, ", ")
}

// axReportWindowResponse diffs one window between the reads around an
// action. compared is false when the reads are of different windows or the
// earlier read was incomplete, so no claim about the rest of the window can
// be made.
func axReportWindowResponse(
	before, after *AppSnapshot, ref string,
) (axReportWindowChange, bool) {
	change := axReportWindowChange{}
	if before == nil || after == nil || before.AXSnapshot == nil ||
		after.AXSnapshot == nil || before.WindowHandle == "" ||
		before.WindowHandle != after.WindowHandle ||
		providerCoverageSummary(before.Annotations).AcquisitionIncomplete > 0 ||
		providerCoverageSummary(after.Annotations).AcquisitionIncomplete > 0 {
		return change, false
	}
	beforeWindows, afterWindows := axReportWindows(before), axReportWindows(after)
	if len(beforeWindows) != len(afterWindows) {
		change.nonState++
	}
	for _, diff := range diffAXSnapshots(before.AXSnapshot, after.AXSnapshot) {
		node := diff.After
		if node == nil {
			node = diff.Before
		}
		under := axReportNodeUnderRef(node, ref)
		switch diff.Kind {
		case AXDiffAdded:
			size := axReportSubtreeSize(diff.After)
			if under {
				change.underAdded += size
			} else {
				change.otherAdded += size
			}
			change.nonState++
		case AXDiffRemoved:
			size := axReportSubtreeSize(diff.Before)
			if under {
				change.underRemoved += size
			} else {
				change.otherRemoved += size
			}
			change.nonState++
		default:
			if node != nil && node.Ref == ref {
				// The element's own change is on the effect line already.
				if diff.Kind != AXDiffStateChanged {
					change.nonState++
				}
				continue
			}
			if under {
				change.underChanged++
			} else {
				change.otherChanged++
			}
			if diff.Kind != AXDiffStateChanged {
				change.nonState++
			}
		}
	}
	return change, true
}

// axReportNodeUnderRef reports whether node is a strict descendant of the
// element with the given ref.
func axReportNodeUnderRef(node *AXNode, ref string) bool {
	if node == nil || ref == "" {
		return false
	}
	for parent := node.Parent; parent != nil; parent = parent.Parent {
		if parent.Ref == ref {
			return true
		}
	}
	return false
}

func axReportSubtreeSize(node *AXNode) int {
	if node == nil {
		return 0
	}
	size := 1
	for _, child := range node.Children {
		size += axReportSubtreeSize(child)
	}
	return size
}

func axSnapshotNodeByRef(snapshot *AppSnapshot, ref string) *AXNode {
	if snapshot == nil || snapshot.AXSnapshot == nil || ref == "" {
		return nil
	}
	var found *AXNode
	var visit func([]*AXNode)
	visit = func(nodes []*AXNode) {
		for _, node := range nodes {
			if node == nil || found != nil {
				continue
			}
			if node.Ref == ref {
				found = node
				return
			}
			visit(node.Children)
		}
	}
	visit(snapshot.AXSnapshot.CanonicalRoots)
	if found == nil {
		visit(snapshot.AXSnapshot.Roots)
	}
	return found
}

func axReportNodeLabel(node *AXNode) string {
	label := string(node.Role)
	if label == "" {
		label = string(RoleGeneric)
	}
	if node.Name != "" {
		label += " " + strconv.Quote(axReportClip(node.Name))
	}
	return label
}

// axReportNodeFields returns the element's state and value fields as
// name→value pairs. AT-SPI reports these states only when set, so an
// unreported boolean state reads as false.
func axReportNodeFieldMap(node *AXNode) map[string]string {
	fields := map[string]string{}
	states := node.States
	boolean := func(name string, value string) {
		switch value {
		case "true", "mixed":
			fields[name] = value
		default:
			fields[name] = "false"
		}
	}
	boolean("checked", string(states.Checked))
	boolean("expanded", string(states.Expanded))
	boolean("selected", string(states.Selected))
	boolean("pressed", string(states.Pressed))
	boolean("active", string(states.Active))
	boolean("disabled", string(states.Disabled))
	boolean("invalid", string(states.Invalid))
	if states.Level != nil {
		fields["level"] = strconv.Itoa(*states.Level)
	}
	fields["value"] = strconv.Quote(axReportClip(node.Properties.Value))
	fields["name"] = strconv.Quote(axReportClip(node.Name))
	return fields
}

func axReportNodeFields(node *AXNode) []string {
	fields := axReportNodeFieldMap(node)
	out := []string{}
	for _, key := range sortedStringKeys(fields) {
		value := fields[key]
		if key == "name" || value == "false" || value == `""` {
			continue
		}
		out = append(out, key+"="+value)
	}
	return out
}

func axReportNodeChanges(before, after *AXNode) []string {
	old, now := axReportNodeFieldMap(before), axReportNodeFieldMap(after)
	keys := map[string]string{}
	for key := range old {
		keys[key] = ""
	}
	for key := range now {
		keys[key] = ""
	}
	changes := []string{}
	for _, key := range sortedStringKeys(keys) {
		from, to := old[key], now[key]
		if from == to {
			continue
		}
		if from == "" {
			from = "none"
		}
		if to == "" {
			to = "none"
		}
		changes = append(changes, key+"="+from+"→"+to)
	}
	return changes
}

func sortedStringKeys(values map[string]string) []string {
	keys := make([]string, 0, len(values))
	for key := range values {
		keys = append(keys, key)
	}
	sort.Strings(keys)
	return keys
}

func axReportClip(text string) string {
	text = strings.Join(strings.Fields(text), " ")
	return truncateRunes(text, 60)
}

// axReportWindow is one top-level window as the report names it.
type axReportWindow struct {
	key, ref, title, role string
	active, modal         bool
}

func (w axReportWindow) name() string {
	if w.ref != "" {
		return w.ref + " " + strconv.Quote(axReportClip(w.title))
	}
	return strconv.Quote(axReportClip(w.title)) + " (no ref)"
}

func axReportSnapshotWindow(snapshot *AppSnapshot) string {
	title := axReportClip(snapshot.WindowTitle)
	if snapshot.publicWindowRef != "" {
		return snapshot.publicWindowRef + " " + strconv.Quote(title)
	}
	return strconv.Quote(title)
}

// axReportWindows lists the application's visible top-level windows in a
// snapshot, keyed by native handle.
func axReportWindows(snapshot *AppSnapshot) []axReportWindow {
	if snapshot == nil {
		return nil
	}
	windows := []axReportWindow{}
	seen := map[string]bool{}
	for _, window := range snapshot.Windows {
		if !(window.Showing || window.Modal || window.Active || window.Selected) ||
			(window.Handle != "" && window.Handle == snapshot.ClosedWindowHandle) {
			continue
		}
		key := FirstNonEmpty(window.Handle, window.publicRef, window.Title)
		if seen[key] {
			continue
		}
		seen[key] = true
		ref := window.publicRef
		if window.Selected && ref == "" {
			ref = snapshot.publicWindowRef
		}
		windows = append(windows, axReportWindow{
			key: key, ref: ref, title: window.Title,
			role: strings.TrimSpace(window.Role), active: window.Active, modal: window.Modal,
		})
	}
	if len(windows) == 0 && !snapshot.WindowAbsent && snapshot.WindowHandle != "" {
		// Providers without a window inventory still name the read window.
		windows = append(windows, axReportWindow{
			key: snapshot.WindowHandle, ref: snapshot.publicWindowRef, title: snapshot.WindowTitle,
		})
	}
	return windows
}

// actWindowsLine compares the application's windows before the first action
// with those after the last one.
func (s *Service) actWindowsLine(trace *actReportTrace, result ToolCallResult) string {
	closedBound := result.StructuredContent["target_window_absent"] == true ||
		result.StructuredContent["close_effect_verified"] == true
	final := s.currentSnapshot(trace.appToken)
	before := axReportWindows(trace.start)
	if closedBound && trace.windowRef != "" && (final == nil || final == trace.start || trace.start == nil) {
		title := trace.windowTitle
		if trace.start != nil && trace.start.WindowHandle != "" {
			title = trace.start.WindowTitle
		}
		return axReportWindowsPrefix + "closed " + trace.windowRef + " " + strconv.Quote(axReportClip(title))
	}
	if final == nil || final == trace.start {
		reason := "no read after the actions"
		if trace.observe == contract.ObserveNone {
			reason = `observe "none" skipped the read`
		}
		return axReportWindowsPrefix + "unknown (" + reason + ")"
	}
	if trace.start == nil {
		return axReportWindowsPrefix + "unknown (no current read of this application before the actions)"
	}
	after := axReportWindows(final)
	beforeByKey := map[string]axReportWindow{}
	for _, window := range before {
		beforeByKey[window.key] = window
	}
	afterByKey := map[string]axReportWindow{}
	for _, window := range after {
		afterByKey[window.key] = window
	}
	parts := []string{}
	for _, window := range after {
		previous, existed := beforeByKey[window.key]
		if !existed {
			detail := []string{FirstNonEmpty(window.role, "window")}
			if window.active || window.modal {
				detail = append(detail, "focused")
			}
			parts = append(parts, "opened "+window.name()+" ("+strings.Join(detail, ", ")+")")
			continue
		}
		if comparableTreeText(previous.title) != comparableTreeText(window.title) {
			parts = append(parts, "renamed "+window.name())
		}
	}
	for _, window := range before {
		if _, still := afterByKey[window.key]; !still {
			parts = append(parts, "closed "+window.name())
		}
	}
	focusBefore, focusAfter := axReportFocused(before), axReportFocused(after)
	if focusAfter != nil && (focusBefore == nil || focusBefore.key != focusAfter.key) {
		if _, opened := beforeByKey[focusAfter.key]; opened {
			parts = append(parts, "focus "+FirstNonEmpty(focusAfter.ref, focusAfter.name()))
		}
	} else if focusAfter == nil && focusBefore != nil && len(after) > 0 {
		parts = append(parts, "focus left these windows")
	}
	if final.externalDesktopTransition {
		parts = append(parts, "a window of another application took focus")
	}
	if len(parts) == 0 {
		return axReportWindowsPrefix + "no change"
	}
	return axReportWindowsPrefix + strings.Join(parts, "; ")
}

func axReportFocused(windows []axReportWindow) *axReportWindow {
	for index := range windows {
		if windows[index].active {
			return &windows[index]
		}
	}
	return nil
}

// isActReportText reports whether a text item is the act report, which is
// never the error message of a failed call.
func isActReportText(text string) bool {
	return strings.HasPrefix(text, axReportEffectPrefix) ||
		strings.HasPrefix(text, axReportWindowsPrefix)
}
