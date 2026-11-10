package core

import (
	"errors"
	"fmt"
	"strconv"
	"strings"
	"time"
)

func (s *Service) currentSnapshot(app string) *AppSnapshot {
	if snapshot, ok := s.snapshots[strings.ToLower(app)]; ok {
		return snapshot
	}
	// When the exact key misses, search again with the same lenient rule that
	// app-name resolution uses. Snapshots are cached under the resolved name
	// (for example "Google Chrome") while the next call may say "chrome"; a
	// strict lookup would report no app state right after a successful
	// observation.
	needle := looseKey(app)
	if needle == "" {
		return nil
	}
	var matched *AppSnapshot
	for key, snapshot := range s.snapshots {
		tight := looseKey(key)
		if tight == "" {
			continue
		}
		if tight == needle || strings.Contains(tight, needle) || strings.Contains(needle, tight) {
			if matched == nil {
				matched = snapshot
				continue
			}
			if matched != snapshot {
				// Go map iteration order is deliberately unstable. Returning the
				// first of multiple snapshots would route an action nondeterministically.
				return nil
			}
		}
	}
	return matched
}

// readSnapshot captures a snapshot without writing the snapshot cache, so that
// read-only tools stay read-only. The cached snapshot is the address space for
// later actions, and replacing it is a side effect.
func (s *Service) readSnapshot(app string, request AXServiceRequest) (*AppSnapshot, []string, ToolCallResult) {
	if err := s.bindRuntimeAppIdentity(&request); err != nil {
		return nil, nil, TextResult(
			"AX application target: "+err.Error(),
			true,
		).withEvidenceScope("error_only")
	}
	previous := s.currentSnapshot(app)
	diagnosticSnapshot := previous
	if diagnosticSnapshot == nil && strings.TrimSpace(app) == "" {
		diagnosticSnapshot = s.latestSnapshot()
	}
	if previous != nil && len(previous.Refs) > 0 {
		request.KnownRefs = previous.Refs
	}
	if expanded, err := s.expandObservationRootShortRef(
		app,
		request.ObservationRoot,
	); err != nil {
		return nil, nil, TextResult(
			"AX observation root: "+err.Error(),
			true,
		).withEvidenceScope("error_only")
	} else {
		request.ObservationRoot = expanded
	}
	response, err := s.callProviderAccessibilityOperation(request)
	if err != nil {
		return nil, nil, s.RuntimeFailureResult(request, diagnosticSnapshot, err)
	}
	if !response.OK {
		return nil, nil, s.runtimeReportedFailure(app, request, *response)
	}
	if isShortAXRef(request.SourceRef) && response.ResolvedElement != nil {
		allowProviderRebind := request.SemanticTarget != nil &&
			request.SemanticTarget.AllowProviderRebind
		s.RefRegistry.RefreshProviderBinding(
			request.SourceRef,
			response.ResolvedElement,
			allowProviderRebind,
		)
	}
	if response.Snapshot == nil && response.ObservationSkipped &&
		accessibilityOperationCanMutate(request) {
		return &AppSnapshot{
			sourceApp:                 app,
			actionExecution:           response.ActionEvidence,
			externalDesktopTransition: response.ExternalDesktopTransition,
			deliveryOnly:              true,
		}, response.Notes, ToolCallResult{}
	}
	if response.Snapshot == nil {
		return nil, nil, s.missingRuntimeSnapshotResult(app, request, response)
	}
	// Tree paths are only stable inside one top-level window instance. If the
	// selected root changed, rerun once without old refs so a structurally
	// similar control in the new window cannot inherit an index from the old one.
	if !accessibilityOperationCanMutate(request) &&
		previous != nil && len(request.KnownRefs) > 0 &&
		previous.WindowHandle != "" && response.Snapshot.WindowHandle != "" &&
		previous.WindowHandle != response.Snapshot.WindowHandle {
		request.KnownRefs = nil
		response, err = s.callProviderAccessibilityOperation(request)
		if err != nil {
			return nil, nil, s.RuntimeFailureResult(request, diagnosticSnapshot, err)
		}
		if !response.OK {
			return nil, nil, s.runtimeReportedFailure(app, request, *response)
		}
		if response.Snapshot == nil {
			return nil, nil, s.missingRuntimeSnapshotResult(app, request, response)
		}
	}
	snapshot, result := s.prepareProviderSnapshot(request, response)
	if result.IsError {
		return nil, nil, result
	}
	notes := response.Notes
	if request.IncludeScreenshot != nil && *request.IncludeScreenshot {
		notes = append(notes, s.attachDesktopFrame(snapshot)...)
	}
	return snapshot, notes, ToolCallResult{}
}

func (s *Service) prepareProviderSnapshot(
	request AXServiceRequest,
	response *ProviderAccessibilityResult,
) (*AppSnapshot, ToolCallResult) {
	if response == nil || response.Snapshot == nil {
		return nil, TextResult("provider snapshot preparation requires a canonical snapshot", true).
			withEvidenceScope("error_only")
	}
	if accessibilityOperationCanMutate(request) && isShortAXRef(request.SourceRef) {
		response.Snapshot.actedRef = request.SourceRef
	}
	if err := applyAXScope(response.Snapshot, request.Scope); err != nil {
		return nil, TextResult(err.Error(), true).
			withEvidenceScope("error_only")
	}
	if err := applyObservationRoot(
		response.Snapshot,
		request.ObservationRoot,
	); err != nil {
		return nil, TextResult(err.Error(), true).
			withEvidenceScope("error_only")
	}
	if err := s.BuildAndRenderAXSnapshot(
		response.Snapshot,
		request.TreeView,
	); err != nil {
		var conflict *axProviderIdentityConflict
		if errors.As(err, &conflict) {
			return nil, s.providerIdentityConflictResult(request, response, conflict)
		}
		return nil, TextResult(
			"AXSnapshot pipeline failed: "+err.Error(),
			true,
		).withEvidenceScope("error_only")
	}
	response.Snapshot.actionExecution = response.ActionEvidence
	response.Snapshot.externalDesktopTransition =
		response.ExternalDesktopTransition
	return response.Snapshot, ToolCallResult{}
}

func (s *Service) expandObservationRootShortRef(
	app string,
	root map[string]any,
) (map[string]any, error) {
	if len(root) == 0 {
		return root, nil
	}
	raw, present := root["ref"]
	if !present {
		return root, nil
	}
	ref, ok := raw.(string)
	if !ok || !isShortAXRef(ref) {
		return root, nil
	}
	entry, err := s.resolveShortAXRef(app, ref)
	if err != nil {
		return nil, err
	}
	if entry.LocatorKind == AXLocatorProviderBound &&
		strings.TrimSpace(entry.ProviderBusName) != "" &&
		strings.TrimSpace(entry.ProviderObjectPath) != "" &&
		strings.TrimSpace(entry.ProcessGeneration) != "" {
		// Locator kind is authoritative. Provider-bound refs may retain a
		// semantic label for diagnostics, but it must never replace their exact
		// identity during rooted observation.
		return map[string]any{"_resolved_ref_entry": entry}, nil
	}
	expanded := assertionTargetFromRefEntry(entry)
	if strings.TrimSpace(expanded) != "" {
		return map[string]any{"ref": expanded}, nil
	}
	return nil, fmt.Errorf(
		"short AX ref %q has no stable observation target",
		ref,
	)
}

func (s *Service) latestSnapshot() *AppSnapshot {
	var latest *AppSnapshot
	for _, snapshot := range s.snapshots {
		if snapshot == nil {
			continue
		}
		if latest == nil || snapshot.capturedAt.After(latest.capturedAt) {
			latest = snapshot
		}
	}
	return latest
}

// readProjectedSnapshot samples live state without replacing the action cache.
//
// Assertions are read-only, but their queries still need the same projection
// as the cached action snapshot. Otherwise a default pruned read can silently
// replace or disagree with a preceding prune=false address space.
func (s *Service) readProjectedSnapshot(app string, request AXServiceRequest) (*AppSnapshot, []string, ToolCallResult) {
	if previous := s.currentSnapshot(app); previous != nil {
		applyProjection(&request, previous.projection)
	}
	return s.readSnapshot(app, request)
}

// refreshProjectedSnapshot samples live state in the existing address-space
// projection, then updates the action cache. find uses this path because every
// target it returns must be directly usable by the next action.
func (s *Service) refreshProjectedSnapshot(app string, request AXServiceRequest) (*AppSnapshot, []string, ToolCallResult) {
	if previous := s.currentSnapshot(app); previous != nil {
		applyProjection(&request, previous.projection)
	}
	return s.refreshSnapshot(app, request)
}

func (s *Service) refreshSnapshot(app string, request AXServiceRequest) (*AppSnapshot, []string, ToolCallResult) {
	// Capture the previous snapshot first: continuity is defined by the
	// relation between this snapshot and the last one. Only the refresh path
	// reports continuity; readSnapshot does not write the cache, and an
	// independent read must not narrate AX history it took no part in.
	internalPrevious := s.currentSnapshot(app)
	if internalPrevious != nil && request.Operation != axServiceObserve {
		applyProjection(&request, internalPrevious.projection)
	}
	snapshot, notes, result := s.readSnapshot(app, request)
	if result.IsError {
		return nil, nil, result
	}
	return s.commitPreparedSnapshot(app, request, snapshot, notes)
}

func (s *Service) commitPreparedSnapshot(
	app string, request AXServiceRequest, snapshot *AppSnapshot, notes []string,
) (*AppSnapshot, []string, ToolCallResult) {
	if snapshot == nil {
		return nil, nil, TextResult("provider snapshot commit requires a canonical snapshot", true).
			withEvidenceScope("error_only")
	}
	snapshot.projection = projectionFromRequest(request)
	if request.LightReadback {
		snapshot.lightReadback = true
		// The root-only read carries one ref. Keep the full ref map of the
		// same window, so that the next full read assigns the indices it
		// would have assigned without this read.
		if previous := s.currentSnapshot(app); previous != nil &&
			previous.WindowHandle == snapshot.WindowHandle &&
			len(previous.Refs) > 0 {
			snapshot.Refs = previous.Refs
		}
	}
	visiblePrevious := s.deliveredSnapshotFor(snapshot)
	notes = append(continuityNotes(visiblePrevious, snapshot), notes...)
	notes = s.dedupeDiagnosticNotes(app, notes)
	if !snapshot.deliveryOnly {
		s.rememberSnapshot(app, snapshot)
	}
	return snapshot, notes, ToolCallResult{}
}

// readFindWalk reads a Linux find walk for findMatches. A walk that holds
// records only for query candidates (FindCandidatesOnly) is not remembered
// as the app's snapshot: an action needs its target's state before it, and
// a diff the whole range the caller observed, which the observe read that
// preceded the walk still holds. The walk's refs are added to that read's,
// so the next read keeps the indices of the elements the walk returned.
func (s *Service) readFindWalk(app string, request AXServiceRequest) (*AppSnapshot, []string, ToolCallResult) {
	previous := s.currentSnapshot(app)
	if previous != nil {
		applyProjection(&request, previous.projection)
	}
	snapshot, notes, result := s.readSnapshot(app, request)
	if result.IsError {
		return nil, nil, result
	}
	if !snapshot.FindCandidatesOnly {
		return s.commitPreparedSnapshot(app, request, snapshot, notes)
	}
	snapshot.projection = projectionFromRequest(request)
	if previous != nil && previous.WindowHandle == snapshot.WindowHandle {
		previous.Refs = mergedElementRefs(previous.Refs, snapshot.Refs)
	}
	return snapshot, s.dedupeDiagnosticNotes(app, notes), ToolCallResult{}
}

// mergedElementRefs returns older with newer's entries on top.
func mergedElementRefs(older, newer map[string]elementRef) map[string]elementRef {
	merged := make(map[string]elementRef, len(older)+len(newer))
	for path, ref := range older {
		merged[path] = ref
	}
	for path, ref := range newer {
		merged[path] = ref
	}
	return merged
}

func (s *Service) rememberSnapshot(query string, snapshot *AppSnapshot) {
	// Replace every alias that pointed at the exact previous address space used
	// for this query. Otherwise `soffice` can refresh while an older title alias
	// still wins an exact lookup and revives stale indices.
	keys := []string{
		query,
		snapshot.publicAppRef,
		snapshot.App.Name,
		snapshot.App.BundleIdentifier,
		strconv.Itoa(snapshot.App.PID),
	}
	var previous *AppSnapshot
	ambiguous := false
	for _, key := range keys {
		key = strings.ToLower(strings.TrimSpace(key))
		if key == "" {
			continue
		}
		cached := s.snapshots[key]
		if cached == nil {
			continue
		}
		if previous == nil {
			previous = cached
		} else if previous != cached {
			ambiguous = true
			break
		}
	}
	if !ambiguous && previous != nil && previous != snapshot {
		for key, cached := range s.snapshots {
			if cached == previous {
				delete(s.snapshots, key)
			}
		}
	}
	snapshot.capturedAt = time.Now()
	snapshot.sourceApp = query
	for _, key := range keys {
		key = strings.ToLower(strings.TrimSpace(key))
		if key != "" {
			s.snapshots[key] = snapshot
		}
	}
}
