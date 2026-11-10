package core

import (
	"crypto/sha256"
	"encoding/json"
	"errors"
	"fmt"
	"slices"
	"strconv"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func normalizedTreeView(value string) string {
	switch strings.ToLower(strings.TrimSpace(value)) {
	case contract.TreeViewFull:
		return contract.TreeViewFull
	default:
		return contract.TreeViewWorking
	}
}

func normalizedAXScope(value string) string {
	switch strings.ToLower(strings.TrimSpace(value)) {
	case contract.AXScopeContent:
		return contract.AXScopeContent
	default:
		return contract.AXScopeWindow
	}
}

func observationRootKey(root map[string]any) string {
	if len(root) == 0 {
		return ""
	}
	payload, err := json.Marshal(root)
	if err != nil {
		return ""
	}
	return string(payload)
}

func projectionFromRequest(request AXServiceRequest) snapshotProjection {
	return snapshotProjection{
		TextLimit:    request.TextLimit,
		MaxTreeNodes: request.MaxTreeNodes,
		MaxTreeDepth: request.MaxTreeDepth,
		Prune:        request.Prune,
		Boxes:        request.Boxes,
		View:         normalizedTreeView(request.TreeView),
		Scope:        normalizedAXScope(request.Scope),
		Root:         observationRootKey(request.ObservationRoot),
	}
}

func applyProjection(request *AXServiceRequest, projection snapshotProjection) {
	if request == nil {
		return
	}
	request.TextLimit = projection.TextLimit
	request.MaxTreeNodes = projection.MaxTreeNodes
	request.MaxTreeDepth = projection.MaxTreeDepth
	request.Prune = projection.Prune
	request.Boxes = projection.Boxes
	request.TreeView = normalizedTreeView(projection.View)
	// Scope is request-local. Persisting content scope into later action reads
	// would make a newly opened menu or dialog disappear from the final
	// observation. Explicit observe/find calls must request it again.
	// ObservationRoot is request-local for the same reason: action readback
	// must not inherit a previous diagnostic subtree.
}

func runtimePathContains(ancestor, descendant []int) bool {
	// The provider uses [] for the accessibility root path. JSON omits that
	// empty slice, but it still semantically contains every non-root
	// descendant.
	if len(ancestor) == 0 {
		return len(descendant) > 0
	}
	if len(descendant) < len(ancestor) {
		return false
	}
	for index := range ancestor {
		if ancestor[index] != descendant[index] {
			return false
		}
	}
	return true
}

var contentPlaneRoles = map[string]bool{
	"canvas":       true,
	"drawing area": true,
	"image map":    true,
	"page":         true,
	"terminal":     true,
	"tree table":   true,
	"web view":     true,
}

func isContentPlaneRole(role string) bool {
	role = strings.ToLower(strings.TrimSpace(role))
	return strings.HasPrefix(role, "document") || contentPlaneRoles[role]
}

func isContentPlaneRecord(record *ElementRecord) bool {
	if record == nil {
		return false
	}
	return isContentPlaneRole(record.ControlType) ||
		isContentPlaneRole(record.LocalizedControlType)
}

func topLevelContentRoots(snapshot *AppSnapshot) []*ElementRecord {
	if snapshot == nil {
		return nil
	}
	candidates := make([]*ElementRecord, 0)
	for index := range snapshot.Elements {
		record := &snapshot.Elements[index]
		if isContentPlaneRecord(record) {
			candidates = append(candidates, record)
		}
	}
	roots := make([]*ElementRecord, 0, len(candidates))
	for _, candidate := range candidates {
		nested := false
		for _, possibleAncestor := range candidates {
			if candidate == possibleAncestor ||
				slices.Equal(candidate.RuntimeID, possibleAncestor.RuntimeID) {
				continue
			}
			if runtimePathContains(possibleAncestor.RuntimeID, candidate.RuntimeID) {
				nested = true
				break
			}
		}
		if !nested {
			roots = append(roots, candidate)
		}
	}
	return roots
}

func contentRootCandidatesDescription(roots []*ElementRecord) string {
	parts := make([]string, 0, len(roots))
	for _, root := range roots {
		parts = append(parts, fmt.Sprintf(
			"%d %s %q",
			root.Index,
			canonicalRole(root),
			root.Name,
		))
	}
	return strings.Join(parts, "; ")
}

// contentScopeWindowRoles are top-level roles whose whole window is
// content: a dialog has no menu bar, toolbars, or side panels to strip, and
// a preview or canvas inside it is one control among its fields, not its
// content area.
var contentScopeWindowRoles = map[string]bool{
	"dialog": true, "alert": true, "file chooser": true,
	"color chooser": true, "font chooser": true,
}

// selectContentScopeRoot returns the one content area of the window, or an
// error that says in one clause why the window has none.
func selectContentScopeRoot(snapshot *AppSnapshot) (*ElementRecord, error) {
	if root := windowRootRecord(snapshot); root != nil &&
		contentScopeWindowRoles[strings.ToLower(strings.TrimSpace(root.ControlType))] {
		return nil, fmt.Errorf("a %s has no separate content area", strings.ToLower(root.ControlType))
	}
	roots := topLevelContentRoots(snapshot)
	switch len(roots) {
	case 0:
		return nil, errors.New("this window has no document, page, or canvas area")
	case 1:
		return roots[0], nil
	}

	focused := focusedRecord(snapshot)
	if focused != nil {
		matches := make([]*ElementRecord, 0, 1)
		for _, root := range roots {
			if runtimePathContains(root.RuntimeID, focused.RuntimeID) {
				matches = append(matches, root)
			}
		}
		if len(matches) == 1 {
			return matches[0], nil
		}
	}
	return nil, fmt.Errorf("%d content areas and focus is in none of them: %s",
		len(roots), contentRootCandidatesDescription(roots))
}

// windowRootRecord returns the record of the window itself: the record
// with the shortest runtime path, which every other record descends from.
func windowRootRecord(snapshot *AppSnapshot) *ElementRecord {
	if snapshot == nil || len(snapshot.Elements) == 0 {
		return nil
	}
	root := &snapshot.Elements[0]
	for index := range snapshot.Elements {
		if len(snapshot.Elements[index].RuntimeID) < len(root.RuntimeID) {
			root = &snapshot.Elements[index]
		}
	}
	for index := range snapshot.Elements {
		record := &snapshot.Elements[index]
		if record != root && !runtimePathContains(root.RuntimeID, record.RuntimeID) {
			return nil
		}
	}
	return root
}

func recordInAXScope(snapshot *AppSnapshot, record *ElementRecord) bool {
	if snapshot == nil || record == nil ||
		normalizedAXScope(snapshot.Scope) == contract.AXScopeWindow {
		return true
	}
	return snapshot.scopeRoot != nil &&
		runtimePathContains(snapshot.scopeRoot.RuntimeID, record.RuntimeID)
}

func scopedCanonicalElementCount(snapshot *AppSnapshot) int {
	if snapshot == nil {
		return 0
	}
	if normalizedAXScope(snapshot.Scope) == contract.AXScopeContent {
		return snapshot.scopedCanonicalCount
	}
	return len(snapshot.Elements)
}

func recordsUnderRoot(
	snapshot *AppSnapshot,
	root *ElementRecord,
) []*ElementRecord {
	if snapshot == nil || root == nil {
		return nil
	}
	records := make([]*ElementRecord, 0)
	for index := range snapshot.Elements {
		record := &snapshot.Elements[index]
		if runtimePathContains(root.RuntimeID, record.RuntimeID) {
			records = append(records, record)
		}
	}
	return records
}

func contentRevisionForRecords(
	snapshot *AppSnapshot,
	root *ElementRecord,
) string {
	records := recordsUnderRoot(snapshot, root)
	if len(records) == 0 {
		return ""
	}
	var canonical strings.Builder
	for _, record := range records {
		depth := len(record.RuntimeID) - len(root.RuntimeID)
		if depth < 0 {
			depth = 0
		}
		canonical.WriteString(strings.Repeat("\t", depth))
		canonical.WriteString(canonicalRole(record))
		canonical.WriteByte('\x00')
		canonical.WriteString(normalizedTreeText(record.Name))
		canonical.WriteByte('\x00')
		canonical.WriteString(normalizedTreeText(record.Value))
		canonical.WriteByte('\x00')
		canonical.WriteString(normalizedTreeText(record.Description))
		canonical.WriteByte('\x00')
		canonical.WriteString(normalizedTreeText(record.Placeholder))
		canonical.WriteByte('\x00')
		canonical.WriteString(strings.TrimSpace(record.States))
		canonical.WriteByte('\x00')
		canonical.WriteString(strings.Join(record.Actions, "\x1f"))
		canonical.WriteByte('\n')
	}
	canonical.WriteString("selected_text=")
	canonical.WriteString(snapshot.SelectedText)
	sum := sha256.Sum256([]byte(canonical.String()))
	return fmt.Sprintf("c1:%x", sum[:12])
}

func validContentRevision(value string) bool {
	value = strings.TrimSpace(value)
	if len(value) != len("c1:")+24 ||
		!strings.HasPrefix(value, "c1:") {
		return false
	}
	for _, char := range value[len("c1:"):] {
		if !((char >= '0' && char <= '9') ||
			(char >= 'a' && char <= 'f')) {
			return false
		}
	}
	return true
}

// applyAXScope selects the records a read covers. scope "content" narrows
// it to the window's one content area. A window without exactly one, such
// as a dialog or a settings page, is read whole: the content the caller
// wanted is then somewhere in the window, and scopeFallback says why the
// read is wider than asked.
func applyAXScope(snapshot *AppSnapshot, scope string) error {
	if snapshot == nil {
		return errors.New("cannot apply accessibility scope to a nil snapshot")
	}
	scope = normalizedAXScope(scope)
	snapshot.Scope = scope
	snapshot.scopeFallback = ""
	snapshot.scopeRoot = nil
	snapshot.scopedCanonicalCount = len(snapshot.Elements)
	snapshot.contentRoot = nil
	snapshot.contentCanonicalCount = 0
	snapshot.contentRevision = ""
	root, err := selectContentScopeRoot(snapshot)
	if err != nil {
		if scope == contract.AXScopeContent {
			snapshot.Scope = contract.AXScopeWindow
			snapshot.scopeFallback = err.Error()
		}
		return nil
	}
	count := len(recordsUnderRoot(snapshot, root))
	snapshot.contentRoot = root
	snapshot.contentCanonicalCount = count
	snapshot.contentRevision = contentRevisionForRecords(snapshot, root)
	if scope == contract.AXScopeWindow {
		return nil
	}
	snapshot.scopeRoot = root
	snapshot.scopedCanonicalCount = count
	return nil
}

// scopeFallbackLine is the one line a content read that covers the whole
// window starts with.
func (s *AppSnapshot) scopeFallbackLine() string {
	if s == nil || s.scopeFallback == "" {
		return ""
	}
	return "scope: window (" + s.scopeFallback + ")"
}

func recordByIndex(snapshot *AppSnapshot, index int) *ElementRecord {
	if snapshot == nil {
		return nil
	}
	for recordIndex := range snapshot.Elements {
		record := &snapshot.Elements[recordIndex]
		if record.Index == index {
			return record
		}
	}
	return nil
}

func focusedObservationRoot(snapshot *AppSnapshot) *ElementRecord {
	focused := focusedRecord(snapshot)
	if focused == nil {
		return nil
	}
	// Prefer a small nearby ancestor so labels and sibling controls remain
	// visible. Do not turn one focused leaf into a whole-document replay.
	minDepth := len(focused.RuntimeID) - 3
	if minDepth < 1 {
		minDepth = 1
	}
	for depth := len(focused.RuntimeID) - 1; depth >= minDepth; depth-- {
		var ancestor *ElementRecord
		for index := range snapshot.Elements {
			record := &snapshot.Elements[index]
			if len(record.RuntimeID) == depth &&
				runtimePathContains(record.RuntimeID, focused.RuntimeID) &&
				recordInAXScope(snapshot, record) {
				ancestor = record
				break
			}
		}
		if ancestor == nil {
			continue
		}
		descendants := 0
		for index := range snapshot.Elements {
			record := &snapshot.Elements[index]
			if recordInAXScope(snapshot, record) &&
				runtimePathContains(ancestor.RuntimeID, record.RuntimeID) {
				descendants++
			}
		}
		if descendants >= 2 && descendants <= 80 {
			return ancestor
		}
	}
	return focused
}

func resolveObservationRoot(
	snapshot *AppSnapshot,
	root map[string]any,
) (*ElementRecord, string, error) {
	if len(root) == 0 {
		return nil, "", nil
	}
	if root["focused"] == true {
		record := focusedObservationRoot(snapshot)
		if record == nil {
			return nil, "", errors.New(
				"root focused requested, but no canonical element reports focus",
			)
		}
		return record, "focused", nil
	}
	if rawEntry, present := root["_resolved_ref_entry"]; present {
		entry, ok := rawEntry.(*AXRefEntry)
		if !ok || entry == nil {
			return nil, "", errors.New(
				"root ref could not be resolved from the server registry",
			)
		}
		record, err := resolveProviderBoundObservationRoot(snapshot, entry)
		if err != nil {
			return nil, "", err
		}
		if !recordInAXScope(snapshot, record) {
			return nil, "", fmt.Errorf(
				"root %d %s %q is outside scope=%q",
				record.Index,
				canonicalRole(record),
				record.Name,
				normalizedAXScope(snapshot.Scope),
			)
		}
		return record, "ref", nil
	}
	address := ""
	if rawRef, present := root["ref"]; present {
		ref, ok := rawRef.(string)
		if ok && isShortAXWindowRef(strings.TrimSpace(ref)) {
			return nil, "", errors.New(
				"root.ref must identify an element with rN; wN is only for window activate/close",
			)
		}
		if ok && isSemanticRef(strings.TrimSpace(ref)) {
			// Public roots only accept rN refs. The service expands a
			// validated rN to this trusted, window-bound representation before
			// resolving it against the fresh canonical tree.
			address = strings.TrimSpace(ref)
		}
	}
	if address == "" {
		var err error
		address, _, err = axTargetAddress(map[string]any{"target": root})
		if err != nil {
			return nil, "", err
		}
	}
	var record *ElementRecord
	mode := "target"
	if numericElementAddress(address) {
		index, _ := strconv.Atoi(address)
		record = recordByIndex(snapshot, index)
		if record == nil {
			return nil, "", fmt.Errorf(
				"root index %d is not present in the fresh canonical snapshot",
				index,
			)
		}
	} else {
		target, err := semanticTargetFromAddress(address)
		if err != nil {
			return nil, "", err
		}
		if target.WindowHandle != "" &&
			target.WindowHandle != snapshot.WindowHandle {
			return nil, "", fmt.Errorf(
				"stale_window: root ref belongs to a different window; " +
					"observe or find the target again",
			)
		}
		record, err = lookupBySelector(snapshot, target.Selector, "")
		if err != nil {
			return nil, "", err
		}
	}
	if !recordInAXScope(snapshot, record) {
		return nil, "", fmt.Errorf(
			"root %d %s %q is outside scope=%q",
			record.Index,
			canonicalRole(record),
			record.Name,
			normalizedAXScope(snapshot.Scope),
		)
	}
	return record, mode, nil
}

func sameRuntimePath(left, right []int) bool {
	if len(left) != len(right) {
		return false
	}
	for index := range left {
		if left[index] != right[index] {
			return false
		}
	}
	return true
}

func entryMatchesRuntimePath(entry *AXRefEntry, runtimeID []int) bool {
	if entry == nil {
		return false
	}
	if sameRuntimePath(entry.RuntimeID, runtimeID) {
		return true
	}
	for _, alias := range entry.RuntimeIDAliases {
		if sameRuntimePath(alias, runtimeID) {
			return true
		}
	}
	return false
}

func resolveProviderBoundObservationRoot(
	snapshot *AppSnapshot,
	entry *AXRefEntry,
) (*ElementRecord, error) {
	if snapshot == nil || entry == nil {
		return nil, errors.New("root ref cannot be resolved without a snapshot")
	}
	if strings.TrimSpace(entry.WindowHandle) == "" ||
		entry.WindowHandle != snapshot.WindowHandle {
		return nil, errors.New(
			"stale_window: root ref belongs to a different window; " +
				"observe or find the target again",
		)
	}
	matches := make([]*ElementRecord, 0, 2)
	for index := range snapshot.Elements {
		record := &snapshot.Elements[index]
		if record.ProviderBusName == entry.ProviderBusName &&
			record.ProviderObjectPath == entry.ProviderObjectPath &&
			record.ProcessGeneration == entry.ProcessGeneration {
			matches = append(matches, record)
		}
	}
	if len(matches) > 1 &&
		(len(entry.RuntimeID) > 0 || len(entry.RuntimeIDAliases) > 0) {
		narrowed := make([]*ElementRecord, 0, len(matches))
		for _, record := range matches {
			if entryMatchesRuntimePath(entry, record.RuntimeID) {
				narrowed = append(narrowed, record)
			}
		}
		matches = narrowed
	}
	switch len(matches) {
	case 0:
		return nil, errors.New(
			"stale_element_ref: the provider-bound root is no longer present; " +
				"use find to obtain a current ref",
		)
	case 1:
		return matches[0], nil
	default:
		return nil, errors.New(
			"ambiguous_target: the provider-bound root does not identify one " +
				"current canonical element; use find to obtain a current ref",
		)
	}
}

func applyObservationRoot(
	snapshot *AppSnapshot,
	root map[string]any,
) error {
	if snapshot == nil {
		return errors.New("cannot apply observation root to a nil snapshot")
	}
	snapshot.observationRoot = nil
	snapshot.observationRootEntry = nil
	snapshot.observationRootMode = ""
	snapshot.rootedCanonicalCount = 0
	record, mode, err := resolveObservationRoot(snapshot, root)
	if err != nil || record == nil {
		return err
	}
	snapshot.observationRoot = record
	snapshot.observationRootEntry, _ = root["_resolved_ref_entry"].(*AXRefEntry)
	snapshot.observationRootMode = mode
	snapshot.rootedCanonicalCount = len(recordsUnderRoot(snapshot, record))
	return nil
}

func projectedSubtreeLines(
	snapshot *AppSnapshot,
	root *ElementRecord,
) ([]string, error) {
	if snapshot == nil || root == nil {
		return nil, errors.New("subtree projection requires a snapshot and root")
	}
	if snapshot.AXSnapshot == nil {
		return nil, errors.New("subtree projection requires AXSnapshot")
	}
	canonical := snapshot.AXSnapshot.ByIndex[root.Index]
	if canonical == nil {
		return nil, fmt.Errorf(
			"subtree root %d is absent from AXSnapshot", root.Index,
		)
	}
	canonicalRoots := cloneAXForest([]*AXNode{canonical}, nil)
	typed := &AXSnapshot{
		SchemaVersion:  snapshot.AXSnapshot.SchemaVersion,
		AppRef:         snapshot.AXSnapshot.AppRef,
		AppIdentity:    snapshot.AXSnapshot.AppIdentity,
		WindowRef:      snapshot.AXSnapshot.WindowRef,
		WindowHandle:   snapshot.AXSnapshot.WindowHandle,
		Revision:       snapshot.AXSnapshot.Revision,
		ActedRef:       snapshot.AXSnapshot.ActedRef,
		CanonicalRoots: canonicalRoots,
		Roots:          cloneAXForest(canonicalRoots, nil),
		ByID:           snapshot.AXSnapshot.ByID,
		ByIndex:        snapshot.AXSnapshot.ByIndex,
	}
	if err := distillAXSnapshot(typed); err != nil {
		return nil, err
	}
	return RenderAXSnapshotYAML(typed, AXRenderOptions{RootKey: "ax"})
}
