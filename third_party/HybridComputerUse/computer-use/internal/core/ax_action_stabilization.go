package core

import (
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"strings"
	"time"
)

type providerTopLevelIdentity struct {
	busName           string
	objectPath        string
	processGeneration string
	roleFamily        string
}

func snapshotProviderTopLevelIdentity(
	snapshot *AppSnapshot,
) (providerTopLevelIdentity, bool) {
	if snapshot == nil {
		return providerTopLevelIdentity{}, false
	}
	var selected providerTopLevelIdentity
	found := false
	for index := range snapshot.Elements {
		record := &snapshot.Elements[index]
		identity := providerTopLevelIdentity{
			busName:           strings.TrimSpace(record.ProviderWindowBusName),
			objectPath:        strings.TrimSpace(record.ProviderWindowObjectPath),
			processGeneration: strings.TrimSpace(record.ProviderWindowProcessGeneration),
			roleFamily:        windowRoleFamily(record.ProviderWindowRole),
		}
		if identity.busName != "" &&
			identity.objectPath != "" &&
			identity.processGeneration != "" &&
			identity.roleFamily != "" {
			if found && selected != identity {
				return providerTopLevelIdentity{}, false
			}
			selected = identity
			found = true
		}
	}
	return selected, found
}

func transientTopLevelSnapshot(snapshot *AppSnapshot) bool {
	if snapshot == nil {
		return false
	}
	if snapshot.WindowSelection != nil {
		for _, candidate := range snapshot.WindowSelection.Candidates {
			if candidate.Selected && candidate.Modal {
				return true
			}
		}
	}
	for _, window := range snapshot.Windows {
		if window.Selected && window.Modal {
			return true
		}
	}
	identity, ok := snapshotProviderTopLevelIdentity(snapshot)
	if !ok {
		return false
	}
	switch identity.roleFamily {
	case "dialog", "file chooser", "color chooser":
		return true
	default:
		return false
	}
}

func commitLikeTransientAction(request AXServiceRequest) bool {
	action, ok := request.Action.(axKeypressCommand)
	if !ok {
		return false
	}
	switch strings.ToLower(strings.TrimSpace(action.Key)) {
	case "enter", "return", "escape", "esc":
		return true
	default:
		return false
	}
}

func provenGoneSourceReplacement(
	service *Service,
	result ToolCallResult,
) (*AXWindowRefEntry, bool) {
	if service == nil || !result.IsError || result.StructuredContent == nil {
		return nil, false
	}
	sourceStatus, _ :=
		result.StructuredContent["source_window_status"].(string)
	inventoryComplete, _ :=
		result.StructuredContent["recovery_inventory_complete"].(bool)
	currentRef, _ :=
		result.StructuredContent["current_window_ref"].(string)
	if sourceStatus != "gone" ||
		!inventoryComplete ||
		!isShortAXWindowRef(currentRef) {
		return nil, false
	}
	entry, err := service.WindowRefRegistry.Resolve(currentRef, "")
	if err != nil || entry == nil ||
		!validInternalWindowHandle(entry.WindowHandle) {
		return nil, false
	}
	return entry, true
}

var transientCommitProbeDelays = []time.Duration{
	250 * time.Millisecond,
	750 * time.Millisecond,
	2 * time.Second,
}

func (s *Service) stabilizePostActionTransientCommit(
	app string,
	after *AppSnapshot,
	request AXServiceRequest,
) (*AppSnapshot, []string, bool, bool) {
	if s == nil ||
		(observation{mode: request.Observe}).resolvedMode() == contract.ObserveNone ||
		after == nil ||
		after.externalDesktopTransition ||
		!transientTopLevelSnapshot(after) ||
		!commitLikeTransientAction(request) {
		return after, nil, false, false
	}

	const transitionBudget = 3 * time.Second
	started := time.Now()
	sourceRef := snapshotLogicalWindowRef(after)
	if !isShortAXWindowRef(sourceRef) ||
		!validInternalWindowHandle(after.WindowHandle) {
		return after, nil, false, false
	}
	for _, delay := range transientCommitProbeDelays {
		remaining := transitionBudget - time.Since(started)
		if remaining <= 0 {
			break
		}
		if delay > remaining {
			delay = remaining
		}
		time.Sleep(delay)

		withoutScreenshot := false
		sourceProbeRequest := AXServiceRequest{
			Operation:          axServiceObserve,
			App:                app,
			IncludeScreenshot:  &withoutScreenshot,
			TargetWindowHandle: after.WindowHandle,
			SourceRef:          sourceRef,
			Scope:              contract.AXScopeWindow,
			TreeView:           contract.TreeViewWorking,
		}
		applyProjection(&sourceProbeRequest, after.projection)
		probe := s.isolatedProbe()
		_, _, sourceResult := probe.readSnapshot(app, sourceProbeRequest)
		replacement, proven :=
			provenGoneSourceReplacement(probe, sourceResult)
		if !proven {
			continue
		}
		finalRequest := AXServiceRequest{
			Operation:          axServiceObserve,
			App:                app,
			IncludeScreenshot:  request.IncludeScreenshot,
			TargetWindowHandle: replacement.WindowHandle,
			SourceRef:          replacement.Ref,
			Scope:              contract.AXScopeWindow,
			TreeView:           contract.TreeViewWorking,
		}
		applyProjection(&finalRequest, after.projection)
		refreshed, notes, finalResult :=
			probe.readSnapshot(app, finalRequest)
		if finalResult.IsError || refreshed == nil ||
			snapshotLogicalWindowRef(refreshed) != replacement.Ref {
			return after, nil, true, false
		}
		refreshed.projection = projectionFromRequest(finalRequest)
		refreshed.actionExecution = after.actionExecution
		refreshed.externalDesktopTransition = after.externalDesktopTransition

		s.RefRegistry = probe.RefRegistry
		s.WindowRefRegistry = probe.WindowRefRegistry
		s.AppRefRegistry = probe.AppRefRegistry
		s.FindCursorRegistry.Reset()
		s.rememberSnapshot(app, refreshed)
		notes = append([]string{
			"[transition] A bounded no-input probe observed the transient " +
				"window finish and returned the current application window.",
		}, notes...)
		return refreshed, notes, true, true
	}
	return after, nil, true, false
}

func postActionWindowIdentityStabilizationTrigger(
	before,
	after *AppSnapshot,
	obs observation,
) string {
	if obs.resolvedMode() != contract.ObserveDiff ||
		before == nil ||
		after == nil {
		return ""
	}
	beforeProvider, beforeProviderOK :=
		snapshotProviderTopLevelIdentity(before)
	afterProvider, afterProviderOK :=
		snapshotProviderTopLevelIdentity(after)
	providerChanged := beforeProviderOK && afterProviderOK &&
		beforeProvider != afterProvider
	if isShortAXWindowRef(after.publicWindowRef) &&
		snapshotHasActionableElementRef(after) &&
		!providerChanged {
		return ""
	}
	if validInternalWindowHandle(before.WindowHandle) &&
		validInternalWindowHandle(after.WindowHandle) &&
		before.WindowHandle != after.WindowHandle {
		return "exact_handle_change"
	}
	if providerChanged {
		return "provider_window_change"
	}
	beforeTitle := normalizedTreeText(before.WindowTitle)
	afterTitle := normalizedTreeText(after.WindowTitle)
	if beforeTitle != "" && afterTitle != "" && beforeTitle != afterTitle {
		return "semantic_window_change"
	}
	return ""
}

func (s *Service) stabilizePostActionWindowIdentity(
	app string,
	before,
	after *AppSnapshot,
	request AXServiceRequest,
) (*AppSnapshot, []string, bool, bool, string) {
	obs := observation{mode: request.Observe}
	trigger := postActionWindowIdentityStabilizationTrigger(before, after, obs)
	if s == nil || trigger == "" {
		return after, nil, false, false, ""
	}
	expectedProvider, expectedProviderOK :=
		snapshotProviderTopLevelIdentity(after)
	if !expectedProviderOK {
		return after, nil, true, false, trigger
	}

	includeScreenshot := false
	targetWindowHandle := ""
	if trigger == "exact_handle_change" {
		targetWindowHandle = after.WindowHandle
	}
	refresh := AXServiceRequest{
		Operation:          axServiceObserve,
		App:                app,
		IncludeScreenshot:  &includeScreenshot,
		TargetWindowHandle: targetWindowHandle,
		Scope:              contract.AXScopeWindow,
		TreeView:           contract.TreeViewWorking,
	}
	applyProjection(&refresh, after.projection)
	probe := s.isolatedProbe()
	if trigger == "provider_window_change" &&
		isShortAXWindowRef(before.publicWindowRef) {
		probe.RefRegistry.MarkByWindow(
			before.publicWindowRef,
			AXRefSuperseded,
			"provider top-level window identity changed",
		)
	}
	refreshed, notes, result := probe.readSnapshot(app, refresh)
	refreshedProvider, refreshedProviderOK :=
		snapshotProviderTopLevelIdentity(refreshed)
	if result.IsError ||
		refreshed == nil ||
		!refreshedProviderOK ||
		refreshedProvider != expectedProvider ||
		!isShortAXWindowRef(refreshed.publicWindowRef) ||
		!snapshotHasActionableElementRef(refreshed) {
		return after, nil, true, false, trigger
	}
	if trigger == "exact_handle_change" &&
		refreshed.WindowHandle != after.WindowHandle {
		return after, nil, true, false, trigger
	}
	if trigger == "semantic_window_change" &&
		(!validInternalWindowHandle(refreshed.WindowHandle) ||
			refreshed.WindowHandle == before.WindowHandle) {
		return after, nil, true, false, trigger
	}
	if trigger == "provider_window_change" &&
		!validInternalWindowHandle(refreshed.WindowHandle) {
		return after, nil, true, false, trigger
	}

	refreshed.projection = projectionFromRequest(refresh)
	refreshed.actionExecution = after.actionExecution
	refreshed.externalDesktopTransition = after.externalDesktopTransition
	s.RefRegistry = probe.RefRegistry
	s.WindowRefRegistry = probe.WindowRefRegistry
	s.AppRefRegistry = probe.AppRefRegistry
	if trigger == "provider_window_change" {
		s.FindCursorRegistry.Reset()
	}
	s.rememberSnapshot(app, refreshed)
	notes = append([]string{
		"[identity] The new top-level window was refreshed once without input; " +
			"the current wN/rN refs below are actionable.",
	}, notes...)
	return refreshed, notes, true, true, trigger
}

func shouldStabilizePostActionContent(
	service *Service,
	after *AppSnapshot,
	request AXServiceRequest,
	identityStabilized bool,
) bool {
	if service == nil ||
		identityStabilized ||
		request.WindowAction != nil ||
		(observation{mode: request.Observe}).resolvedMode() != contract.ObserveDiff ||
		after == nil ||
		after.externalDesktopTransition {
		return false
	}
	baseline := service.deliveredSnapshotFor(after)
	if baseline == nil {
		return false
	}
	if diff, ok := incrementalTree(baseline, after); ok {
		return !incrementalObservationHasChanges(diff)
	}
	if contentRevisionChanged(baseline, after) {
		return false
	}
	return true
}

func incrementalObservationHasChanges(lines []string) bool {
	if len(lines) == 0 {
		return false
	}
	sawNoChangeMarker := false
	for _, line := range lines {
		trimmed := strings.TrimSpace(line)
		switch {
		case strings.HasPrefix(trimmed, "Incremental view: 0 changes"):
			sawNoChangeMarker = true
		case trimmed == "effect: no_ax_change":
			sawNoChangeMarker = true
		case strings.Contains(trimmed, "<changed>"),
			strings.Contains(trimmed, "<added>"),
			strings.Contains(trimmed, "<removed>"),
			strings.HasPrefix(trimmed, "added:"),
			strings.HasPrefix(trimmed, "removed:"),
			strings.HasPrefix(trimmed, "changed:"),
			strings.HasPrefix(trimmed, "effect: target_window_absent"):
			return true
		case strings.HasPrefix(trimmed, "Incremental view:"):
			return true
		}
	}
	return !sawNoChangeMarker
}

func (s *Service) postActionContentWindowHandle(
	snapshot *AppSnapshot,
) string {
	if snapshot == nil {
		return ""
	}
	if validInternalWindowHandle(snapshot.WindowHandle) {
		return snapshot.WindowHandle
	}
	windowRef := snapshotLogicalWindowRef(snapshot)
	if !isShortAXWindowRef(windowRef) {
		return ""
	}
	entry, err := s.WindowRefRegistry.Resolve(windowRef, "")
	if err != nil || !validInternalWindowHandle(entry.WindowHandle) {
		return ""
	}
	return entry.WindowHandle
}

func (s *Service) stabilizePostActionContent(
	app string,
	after *AppSnapshot,
	request AXServiceRequest,
	identityStabilized bool,
) (*AppSnapshot, []string, bool, bool) {
	if !shouldStabilizePostActionContent(
		s,
		after,
		request,
		identityStabilized,
	) {
		return after, nil, false, false
	}
	windowHandle := s.postActionContentWindowHandle(after)
	if windowHandle == "" {
		return after, nil, false, false
	}

	const retryDelay = 250 * time.Millisecond
	time.Sleep(retryDelay)
	includeScreenshot := false
	refresh := AXServiceRequest{
		Operation:          axServiceObserve,
		App:                app,
		IncludeScreenshot:  &includeScreenshot,
		TargetWindowHandle: windowHandle,
		SourceRef:          snapshotLogicalWindowRef(after),
		Scope:              contract.AXScopeWindow,
		TreeView:           contract.TreeViewWorking,
		RecheckAfterAction: s.platformBackend().HostInfo().Platform == "windows",
	}
	applyProjection(&refresh, after.projection)
	probe := s.isolatedProbe()
	refreshed, notes, result := probe.readSnapshot(app, refresh)
	if result.IsError ||
		refreshed == nil ||
		snapshotLogicalWindowRef(refreshed) != snapshotLogicalWindowRef(after) ||
		refreshed.SnapshotID == after.SnapshotID {
		return after, nil, true, false
	}
	semanticChange := contentRevisionChanged(after, refreshed)
	if diff, ok := incrementalTree(after, refreshed); ok {
		semanticChange = semanticChange ||
			incrementalObservationHasChanges(diff)
	}
	if !semanticChange {
		return after, nil, true, false
	}
	refreshed.projection = projectionFromRequest(refresh)
	refreshed.actionExecution = after.actionExecution
	refreshed.externalDesktopTransition = after.externalDesktopTransition
	s.RefRegistry = probe.RefRegistry
	s.WindowRefRegistry = probe.WindowRefRegistry
	s.AppRefRegistry = probe.AppRefRegistry
	s.rememberSnapshot(app, refreshed)
	notes = append([]string{
		"[content] The window changed after the first post-action read; " +
			"the result below is from one re-read 250 ms later.",
	}, notes...)
	return refreshed, notes, true, true
}

// renderAXContentStabilizedObservation returns the re-read window as a diff
// against the last state the model received for it, and falls back to the
// working tree only when no diff is available or the diff is not shorter.
func (s *Service) renderAXContentStabilizedObservation(
	snapshot *AppSnapshot,
	notes []string,
) (ToolCallResult, []string, bool, bool, bool) {
	if diff, ok := incrementalTree(s.deliveredSnapshotFor(snapshot), snapshot); ok {
		result := snapshot.resultWithTreeOverride(notes, diff)
		if result.StructuredContent == nil {
			result.StructuredContent = map[string]any{}
		}
		result.StructuredContent["final_observation_kind"] = "diff"
		return result, diff, true, false, false
	}
	return s.renderAXWorkingObservation(snapshot, notes)
}
