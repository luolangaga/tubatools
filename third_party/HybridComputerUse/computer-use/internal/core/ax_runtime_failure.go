package core

import (
	"errors"
	"fmt"
	"regexp"
	"strings"
)

func (s *Service) providerIdentityConflictResult(
	request AXServiceRequest,
	response *ProviderAccessibilityResult,
	conflict *axProviderIdentityConflict,
) ToolCallResult {
	snapshot := response.Snapshot
	s.rememberFailedObservation(request.App, snapshot)
	s.pendingFailedObservations[len(s.pendingFailedObservations)-1].continuityNote =
		"[continuity] The previous accessibility acquisition contained contradictory " +
			"provider identity evidence. This independent observation does not establish " +
			"whether earlier input was accepted or persisted."
	if window := snapshotLogicalWindowRef(snapshot); window != "" {
		s.RefRegistry.MarkByWindow(window, AXRefNeedsRevalidation, "provider_identity_conflict")
		s.WindowRefRegistry.MarkLifecycle(window, AXRefNeedsRevalidation, "provider_identity_conflict")
	}
	s.invalidateAccessibilitySnapshots()
	failure := *response
	failure.PrepareForService()
	emitted := failure.InputEventsEmitted ||
		(failure.ActionEvidence != nil && failure.ActionEvidence.InputEmitted)
	unknown := emitted || failure.PartialInputPossible || failure.DesktopStateMutated || failure.MutationUnknown
	failure.OK = false
	failure.Error = conflict.Error() + "; no trustworthy final accessibility observation is available. " +
		"Observe current state before further input; do not replay the action."
	failure.Snapshot = nil
	failure.Failure = &ProviderFailure{
		Code: "observation_incomplete", UnderlyingCode: "provider_identity_conflict",
		Phase: "observation", Subject: "element", FactsComplete: false,
	}
	failure.Mutation = &ProviderMutationFacts{
		InputEventsEmitted: emitted, PartialInputPossible: failure.PartialInputPossible,
		DesktopStateMutated: failure.DesktopStateMutated, MutationUnknown: unknown,
	}
	result := s.runtimeReportedFailure(request.App, request, failure).withEvidenceScope("error_only")
	result.StructuredContent["observation_required"] = true
	result.StructuredContent["final_observation_returned"] = false
	return result
}

func (s *Service) missingRuntimeSnapshotResult(
	app string,
	request AXServiceRequest,
	response *ProviderAccessibilityResult,
) ToolCallResult {
	if response == nil {
		return TextResult("The native provider did not return an app snapshot.", true).
			withEvidenceScope("error_only")
	}
	emitted := response.InputEventsEmitted ||
		response.PartialInputPossible ||
		response.DesktopStateMutated ||
		response.MutationUnknown
	if !emitted {
		return TextResult("The native provider did not return an app snapshot.", true).
			withEvidenceScope("error_only")
	}
	response.OK = false
	response.Error = "post_action_observation_failed; the action outcome is unknown " +
		"because the native provider returned no final app snapshot; inspect fresh state " +
		"before retrying."
	response.Failure = &ProviderFailure{
		Code:           "effect_unknown",
		UnderlyingCode: "missing_post_action_snapshot",
		Phase:          "post_action_observation",
		FactsComplete:  false,
		Subject:        "element",
	}
	if isShortAXWindowRef(request.SourceRef) {
		response.Failure.Subject = "window"
	}
	if response.Mutation == nil {
		response.Mutation = &ProviderMutationFacts{}
	}
	response.Mutation.InputEventsEmitted = response.InputEventsEmitted
	response.Mutation.PartialInputPossible = response.PartialInputPossible
	response.Mutation.DesktopStateMutated = response.DesktopStateMutated
	response.Mutation.MutationUnknown = true
	response.PrepareForService()
	return s.runtimeReportedFailure(app, request, *response)
}

func (s *Service) runtimeReportedFailure(
	app string,
	request AXServiceRequest,
	response ProviderAccessibilityResult,
) ToolCallResult {
	response.PrepareForService()
	subject := strings.TrimSpace(strings.ToLower(response.FailureSubject))
	var sourceEntry *AXRefEntry
	if s != nil && isShortAXRef(request.SourceRef) {
		sourceEntry, _ = s.RefRegistry.Resolve(request.SourceRef, "")
	}
	var sourceWindowEntry *AXWindowRefEntry
	if s != nil && isShortAXWindowRef(request.SourceRef) {
		sourceWindowEntry, _ = s.WindowRefRegistry.Resolve(request.SourceRef, "")
	}
	s.syncRuntimeFailureLifecycle(request, response)
	return s.renderRuntimeReportedFailure(app, request, response, subject, sourceEntry, sourceWindowEntry)
}

func (s *Service) syncRuntimeFailureLifecycle(request AXServiceRequest, response ProviderAccessibilityResult) {
	response.PrepareForService()
	subject := strings.TrimSpace(strings.ToLower(response.FailureSubject))
	if s != nil && isShortAXAppRef(request.App) && subject == "app" {
		switch {
		case response.FailureCode == "observation_incomplete" ||
			response.FailureCode == "ax_app_inventory_incomplete":
			s.markAppIdentityLifecycle(
				request.App, AXRefNeedsRevalidation, response.FailureCode,
			)
		case response.FailureCode == "stale_app_ref":
			lifecycle := AXRefStale
			if !response.FailureFactsComplete {
				lifecycle = AXRefNeedsRevalidation
			}
			s.markAppIdentityLifecycle(request.App, lifecycle, response.FailureCode)
		}
	}
	if s != nil && request.SourceRef != "" {
		switch {
		case isShortAXRef(request.SourceRef):
			if subject == "element" &&
				(response.FailureCode == "observation_incomplete" ||
					response.FailureCode == "semantic_target_scan_incomplete") {
				s.RefRegistry.MarkNeedsRevalidation(
					request.SourceRef, response.FailureCode,
				)
			} else if subject == "element" &&
				request.SemanticTarget != nil &&
				request.SemanticTarget.LocatorKind == AXLocatorSemantic &&
				(response.FailureCode == "stale_element_ref" ||
					response.FailureCode == "stale_target") {
				s.RefRegistry.MarkNeedsRevalidation(
					request.SourceRef, response.FailureCode,
				)
			} else if request.SemanticTarget != nil &&
				request.SemanticTarget.LocatorKind == AXLocatorProviderBound &&
				(response.FailureCode == "stale_element_ref" ||
					response.FailureCode == "stale_target") {
				if response.FailureFactsComplete {
					s.RefRegistry.MarkStale(request.SourceRef, response.FailureCode)
				} else {
					s.RefRegistry.MarkNeedsRevalidation(
						request.SourceRef, response.FailureCode,
					)
				}
			}
		case isShortAXWindowRef(request.SourceRef):
			if subject == "window" &&
				(response.FailureCode == "observation_incomplete" ||
					response.FailureCode == "semantic_target_scan_incomplete") {
				s.WindowRefRegistry.MarkLifecycle(
					request.SourceRef, AXRefNeedsRevalidation,
					response.FailureCode,
				)
			} else if response.FailureCode == "stale_window_ref" ||
				response.FailureCode == "stale_window" ||
				(response.FailureCode == "stale_target" && subject == "window") {
				lifecycle := AXRefStale
				if !response.FailureFactsComplete {
					lifecycle = AXRefNeedsRevalidation
				}
				s.WindowRefRegistry.MarkLifecycle(
					request.SourceRef, lifecycle, response.FailureCode,
				)
				if subject == "window" && response.FailureFactsComplete {
					s.RefRegistry.MarkByWindow(request.SourceRef, lifecycle, response.FailureCode)
				}
			}
		}
	}
}

func (s *Service) renderRuntimeReportedFailure(
	app string, request AXServiceRequest, response ProviderAccessibilityResult,
	subject string, sourceEntry *AXRefEntry, sourceWindowEntry *AXWindowRefEntry,
) ToolCallResult {
	result := TextResult(publicRuntimeFailureText(response), true)
	if response.FailureCode == "" {
		return result
	}
	result.StructuredContent = map[string]any{
		"failure_code":           response.FailureCode,
		"failure_phase":          response.FailurePhase,
		"input_events_emitted":   response.InputEventsEmitted,
		"partial_input_possible": response.PartialInputPossible,
		"mutation_unknown":       response.MutationUnknown,
		"failure_facts_complete": response.FailureFactsComplete,
	}
	if response.UnderlyingFailureCode != "" {
		result.StructuredContent["underlying_failure_code"] =
			response.UnderlyingFailureCode
	}
	if subject != "" {
		result.StructuredContent["failure_subject"] = subject
	}
	continuation := map[string]any{
		"state":              "observe_required",
		"retry_same_request": false,
		"route":              "AX",
	}
	if response.FailureCode == "effect_unknown" {
		continuation["state"] = "verify_only"
	}
	result.StructuredContent["continuation"] = continuation
	if response.FocusPreparation != "" || response.PointerPreparation != "" {
		return preparationFailureResult(response, result)
	}
	s.attachRuntimeRecoveryContext(
		&result,
		request,
		sourceEntry,
		sourceWindowEntry,
		response.Recovery,
		continuation,
	)
	if s != nil && sourceEntry != nil && response.ReplacementWindow != nil &&
		replacementWindowContinuesSource(sourceEntry, response.ReplacementWindow) {
		replacement := response.ReplacementWindow
		windowRef := s.WindowRefRegistry.Acquire(
			sourceEntry.AppIdentity,
			replacement.WindowHandle,
			"window",
			"",
			replacement.ProviderBusName,
			replacement.ProviderObjectPath,
			replacement.ProcessGeneration,
			replacement.Role,
			replacement.Title,
			sourceEntry.AppRef,
		)
		if windowRef != "" {
			result.StructuredContent["replacement_window_ref"] = windowRef
			result.StructuredContent["recovery"] = "find"
			continuation["state"] = "ready"
			continuation["fresh_window_ref"] = windowRef
			continuation["next_mode"] = "find"
			result.Content[0].Text += fmt.Sprintf(
				"\nreplacement_window_ref=%s recovery=find",
				windowRef,
			)
		}
	}
	if response.ActionEvidence != nil {
		result.StructuredContent["runtime_action_evidence"] =
			*response.ActionEvidence
	}
	if s != nil && response.FailureCode == "ax_app_ambiguous" {
		// Its candidates are application processes, not elements.
		s.describeAmbiguousApp(&result, response.Candidates)
		return result
	}
	if s != nil && len(response.Candidates) > 0 {
		snapshot := s.currentSnapshot(app)
		publicCandidates := make([]map[string]any, 0, len(response.Candidates))
		lines := []string{"Candidates:"}
		for _, candidate := range response.Candidates {
			if snapshot == nil {
				break
			}
			windowHandle := strings.TrimSpace(candidate.WindowHandle)
			if !validInternalWindowHandle(windowHandle) {
				continue
			}
			windowRef := ""
			for _, window := range snapshot.Windows {
				if window.Handle == windowHandle {
					windowRef = window.publicRef
					break
				}
			}
			if windowRef == "" {
				windowRef = s.WindowRefRegistry.Acquire(
					appIdentityForSnapshot(snapshot),
					windowHandle,
					"window",
					snapshot.SnapshotID,
					"", "", "", "window", "",
					snapshot.publicAppRef,
				)
			}
			if windowRef == "" {
				continue
			}
			role := canonicalRoleDescriptor(candidate.Role, nil).ModelRole
			name := FirstNonEmpty(candidate.Name, candidate.Description)
			selector := selectorHead(string(role), name)
			ref := s.RefRegistry.AcquireTyped(
				AXLocatorProviderBound,
				appIdentityForSnapshot(snapshot),
				windowHandle,
				selector,
				role,
				name,
				snapshot.SnapshotID,
				candidate.ProviderBusName,
				candidate.ProviderObjectPath,
				candidate.ProcessGeneration,
				"", "", "", "", "",
				snapshot.publicAppRef,
				windowRef,
			)
			if ref == "" {
				continue
			}
			entry := map[string]any{
				"ref":        ref,
				"window_ref": windowRef,
				"path":       publicCandidatePath(candidate.Path),
			}
			publicCandidates = append(publicCandidates, entry)
			lines = append(lines, fmt.Sprintf(
				"- %s %s [window_ref=%s] path=%s",
				ref,
				selector,
				windowRef,
				publicCandidatePath(candidate.Path),
			))
		}
		if len(publicCandidates) > 0 {
			result.StructuredContent["candidates"] = publicCandidates
			result.StructuredContent["candidate_count"] = response.CandidateCount
			result.StructuredContent["candidates_truncated"] =
				response.CandidatesTruncated
			result.Content[0].Text += "\n" + strings.Join(lines, "\n") +
				"\nrecovery=choose_ref|find"
		}
	}
	return result
}

func (s *Service) attachRuntimeRecoveryContext(
	result *ToolCallResult,
	request AXServiceRequest,
	sourceEntry *AXRefEntry,
	sourceWindowEntry *AXWindowRefEntry,
	recovery *ProviderRecoveryContext,
	continuation map[string]any,
) {
	if s == nil || result == nil || result.StructuredContent == nil ||
		recovery == nil {
		return
	}
	appStatus := strings.TrimSpace(strings.ToLower(recovery.SourceAppStatus))
	windowStatus := strings.TrimSpace(strings.ToLower(recovery.SourceWindowStatus))
	if appStatus != "" {
		result.StructuredContent["source_app_status"] = appStatus
	}
	if windowStatus != "" {
		result.StructuredContent["source_window_status"] = windowStatus
	}
	result.StructuredContent["recovery_inventory_complete"] =
		recovery.InventoryComplete

	appRef := ""
	appIdentity := ""
	windowRef := ""
	if sourceEntry != nil {
		appRef = sourceEntry.AppRef
		appIdentity = sourceEntry.AppIdentity
		windowRef = sourceEntry.LogicalWindowRef
	} else if sourceWindowEntry != nil {
		appRef = sourceWindowEntry.AppRef
		appIdentity = sourceWindowEntry.AppIdentity
		windowRef = sourceWindowEntry.Ref
	} else if isShortAXWindowRef(request.SourceRef) {
		if entry, err := s.WindowRefRegistry.Resolve(request.SourceRef, ""); err == nil {
			appRef = entry.AppRef
			appIdentity = entry.AppIdentity
			windowRef = entry.Ref
		}
	}

	if recovery.InventoryComplete && appStatus == "exited" {
		if appRef != "" {
			s.markAppIdentityLifecycle(
				appRef,
				AXRefStale,
				"source_app_exited_after_input",
			)
		}
		result.StructuredContent["recovery"] = "observe_apps"
		continuation["state"] = "source_app_absent"
		continuation["next_mode"] = "observe"
		result.Content[0].Text +=
			"\nsource_app_status=exited source_window_status=gone recovery=observe_apps"
		return
	}

	if recovery.InventoryComplete && windowStatus == "gone" && windowRef != "" {
		s.WindowRefRegistry.MarkLifecycle(
			windowRef,
			AXRefStale,
			"source_window_gone_after_input",
		)
		s.RefRegistry.MarkByWindow(
			windowRef,
			AXRefStale,
			"source_window_gone_after_input",
		)
	}

	currentRef := ""
	currentOwnerMatches := false
	if appEntry, err := s.AppRefRegistry.Resolve(appRef); err == nil &&
		recovery.CurrentWindow != nil {
		currentOwnerMatches =
			strings.TrimSpace(appEntry.ProviderBusName) ==
				strings.TrimSpace(recovery.CurrentWindow.ProviderBusName) &&
				strings.TrimSpace(appEntry.ProcessGeneration) ==
					strings.TrimSpace(recovery.CurrentWindow.ProcessGeneration)
	}
	if current := recovery.CurrentWindow; recovery.InventoryComplete &&
		current != nil && appRef != "" && appIdentity != "" &&
		validInternalWindowHandle(current.WindowHandle) &&
		currentOwnerMatches {
		currentRef = s.WindowRefRegistry.Acquire(
			appIdentity,
			current.WindowHandle,
			selectorHead(
				string(canonicalRoleDescriptor(current.Role, nil).ModelRole),
				current.Title,
			),
			"",
			current.ProviderBusName,
			current.ProviderObjectPath,
			current.ProcessGeneration,
			current.Role,
			current.Title,
			appRef,
		)
	}
	if currentRef == "" && windowStatus == "present" && windowRef != "" {
		if _, err := s.WindowRefRegistry.Resolve(windowRef, ""); err == nil {
			currentRef = windowRef
		}
	}
	if currentRef != "" {
		result.StructuredContent["current_window_ref"] = currentRef
		result.StructuredContent["recovery"] = "find"
		continuation["state"] = "ready"
		continuation["fresh_window_ref"] = currentRef
		continuation["next_mode"] = "find"
		result.Content[0].Text += fmt.Sprintf(
			"\nsource_app_status=%s source_window_status=%s "+
				"current_window_ref=%s recovery=find",
			appStatus,
			windowStatus,
			currentRef,
		)
	}
}

func publicCandidatePath(path string) string {
	path = strings.TrimSpace(path)
	if path == "" {
		return ""
	}
	for _, pattern := range privateProviderIdentityPatterns {
		if pattern.MatchString(path) {
			return "semantic path unavailable"
		}
	}
	return path
}

func (s *Service) markAppIdentityLifecycle(
	appRef string,
	lifecycle AXRefLifecycle,
	reason string,
) {
	if s == nil || strings.TrimSpace(appRef) == "" {
		return
	}
	s.AppRefRegistry.MarkLifecycle(appRef, lifecycle, reason)
	s.WindowRefRegistry.MarkByApp(appRef, lifecycle, reason)
	s.RefRegistry.MarkByApp(appRef, lifecycle, reason)
}

var privateProviderIdentityPatterns = []*regexp.Regexp{
	regexp.MustCompile(`(?i)(?:provider_identity_ambiguous|provider[^\s]*path)[^\n]*`),
	regexp.MustCompile(`/org/a11y/[^\s'",]+`),
	regexp.MustCompile(`(?i)\b(?:pid|process_generation)\s*[=:]\s*[^\s,;]+`),
	regexp.MustCompile(`\b:[0-9]+\.[0-9]+\b`),
	regexp.MustCompile(`\b(?:s[12]|w2):[A-Za-z0-9+/=_-]+\b`),
}

func publicRuntimeFailureText(response ProviderAccessibilityResult) string {
	text := strings.TrimSpace(response.Error)
	if strings.Contains(strings.ToLower(text), "provider_identity_ambiguous") {
		return "ambiguous_target: multiple accessibility projections matched; " +
			"choose one public candidate ref or observe again"
	}
	for _, pattern := range privateProviderIdentityPatterns {
		text = pattern.ReplaceAllString(
			text,
			"internal accessibility identity unavailable",
		)
	}
	for internal, public := range map[string]string{
		"provider_window_disappeared":   "target_window_changed",
		"provider_identity_disappeared": "target_identity_changed",
		"provider_identity_replaced":    "target_identity_changed",
		"provider_identity_changed":     "target_identity_changed",
	} {
		text = strings.ReplaceAll(text, internal, public)
	}
	return text
}

func replacementWindowContinuesSource(
	source *AXRefEntry,
	replacement *ProviderReplacementWindow,
) bool {
	if source == nil || replacement == nil {
		return false
	}
	return strings.TrimSpace(source.WindowHandle) != "" &&
		source.WindowHandle == strings.TrimSpace(replacement.WindowHandle) &&
		strings.TrimSpace(source.ProviderWindowBusName) != "" &&
		source.ProviderWindowBusName ==
			strings.TrimSpace(replacement.ProviderBusName) &&
		strings.TrimSpace(source.ProviderWindowProcessGeneration) != "" &&
		source.ProviderWindowProcessGeneration ==
			strings.TrimSpace(replacement.ProcessGeneration) &&
		windowRoleFamily(source.ProviderWindowRole) ==
			windowRoleFamily(replacement.Role)
}

func windowRoleFamily(role string) string {
	switch strings.ToLower(strings.TrimSpace(role)) {
	case "frame", "window":
		return "window"
	case "alert", "dialog":
		return "dialog"
	default:
		return strings.ToLower(strings.TrimSpace(role))
	}
}

func (s *Service) RuntimeFailureResult(
	request AXServiceRequest,
	diagnosticSnapshot *AppSnapshot,
	err error,
) ToolCallResult {
	if result, rejected := runtimeRequestRejectionResult(err); rejected {
		return result
	}
	var timeout *RuntimeTimeoutError
	var transport *RuntimeTransportError
	if !errors.As(err, &timeout) && !errors.As(err, &transport) {
		return TextResult(err.Error(), true)
	}
	phase := ""
	if timeout != nil {
		phase = strings.TrimSpace(timeout.Phase)
	} else {
		phase = strings.TrimSpace(transport.Phase)
	}
	if phase == "" {
		phase = "runtime"
	}
	if request.Operation == axServiceObserve {
		failureCode := "runtime_timeout"
		failureMarker := "runtime_timeout=true "
		if transport != nil {
			failureCode = "runtime_transport_failure"
			failureMarker = "runtime_transport_failure=true "
		}
		result := TextResult(
			err.Error()+"\n"+failureMarker+"mutation_unknown=false "+
				s.runtimeTimeoutDiagnostic(diagnosticSnapshot), true)
		result.StructuredContent = map[string]any{
			"failure_code":         failureCode,
			"failure_phase":        phase,
			"input_events_emitted": false,
			"mutation_unknown":     false,
			"observation_required": true,
		}
		return result
	}
	mutationUnknown := accessibilityOperationCanMutate(request) &&
		((timeout != nil && !timeout.PreDispatch) || (transport != nil && transport.RequestSent))
	failureCode := "runtime_timeout"
	failureMarker := "runtime_timeout=true "
	if transport != nil {
		failureCode = "runtime_transport_failure"
		failureMarker = "runtime_transport_failure=true "
	}
	result := TextResult(
		err.Error()+"\n"+failureMarker+
			fmt.Sprintf("mutation_unknown=%t ", mutationUnknown)+
			"a11y_responsive=unknown. This action runtime includes input, window, "+
			"pixel capture, settle, and A11Y work. Inspect fresh GUI state before "+
			"retrying whenever mutation_unknown=true.", true)
	result.StructuredContent = map[string]any{
		"failure_code":         failureCode,
		"failure_phase":        phase,
		"mutation_unknown":     mutationUnknown,
		"observation_required": true,
		"retry_same_request":   false,
	}
	return result
}
