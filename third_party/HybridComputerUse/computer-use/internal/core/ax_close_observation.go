package core

import (
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"strings"
)

func closedWindowFinalObservation(
	context *AXBatchContext,
	obs observation,
) ToolCallResult {
	windowRef := ""
	if context != nil {
		windowRef = strings.TrimSpace(context.WindowRef)
	}
	refLine := ""
	if windowRef != "" {
		refLine = "\n absent_refs: [" + windowRef + "]"
	}
	result := TextResult(
		"ax_diff:\n effect: target_window_absent"+refLine,
		false,
	)
	result.StructuredContent = map[string]any{
		"full_tree_returned":                   false,
		"tree_reference_only":                  false,
		"compact_action_observation":           true,
		"delivered_snapshot_baseline_advanced": false,
		"target_window_absent":                 true,
		"final_observation_kind":               "lifecycle",
		"next_identical_observation_required":  false,
		"observation_required":                 false,
	}
	if obs.resolvedMode() == contract.ObserveScreenshot {
		result.StructuredContent["final_observation_returned"] = false
		result.StructuredContent["screenshot_returned"] = false
		result.StructuredContent["observation_required"] = true
		result.Content = append(result.Content, ContentItem{
			Type: "text",
			Text: "Target-window screenshot unavailable because the verified " +
				"closed window no longer exists.",
		})
		return result
	}
	result.StructuredContent["final_observation_returned"] = true
	return result
}

func closedWindowLifecycleProven(
	context *AXBatchContext,
	result ToolCallResult,
) bool {
	if context == nil ||
		!isShortAXWindowRef(context.WindowRef) ||
		!result.IsError ||
		result.StructuredContent == nil {
		return false
	}
	code, _ := result.StructuredContent["failure_code"].(string)
	switch code {
	case "stale_window_ref":
		sourceStatus, _ :=
			result.StructuredContent["source_window_status"].(string)
		inventoryComplete, _ :=
			result.StructuredContent["recovery_inventory_complete"].(bool)
		factsComplete, _ :=
			result.StructuredContent["failure_facts_complete"].(bool)
		return sourceStatus == "gone" && inventoryComplete && factsComplete
	case "stale_app_ref":
		subject, _ := result.StructuredContent["failure_subject"].(string)
		factsComplete, _ :=
			result.StructuredContent["failure_facts_complete"].(bool)
		return subject == "app" && factsComplete
	default:
		return false
	}
}

func finalizeVerifiedCloseLifecycle(result ToolCallResult) ToolCallResult {
	if result.StructuredContent == nil ||
		result.StructuredContent["target_window_absent"] != true {
		return result
	}
	result.Content = stripSupersededFinalObservationUnavailable(result.Content)
	result.StructuredContent["close_effect_verified"] = true
	result.StructuredContent["next_identical_observation_required"] = false
	delete(result.StructuredContent, "task_effect_verified")

	finalReturned, _ :=
		result.StructuredContent["final_observation_returned"].(bool)
	if finalReturned {
		result.StructuredContent["observation_required"] = false
	} else {
		result.StructuredContent["observation_required"] = true
	}

	failureCode, _ :=
		result.StructuredContent["failure_code"].(string)
	observationFailureCode, _ :=
		result.StructuredContent["observation_failure_code"].(string)
	actionFailure := failureCode != ""
	if !actionFailure {
		result.IsError = false
		clearVerifiedCloseUncertainty(result.StructuredContent, nil)
		delete(result.StructuredContent, "observation_failure_code")
		delete(result.StructuredContent, "post_action_observation_status")
	}
	if !result.StructuredContent["observation_required"].(bool) {
		delete(result.StructuredContent, "continuation")
	} else if observationFailureCode != "" {
		result.StructuredContent["continuation"] = map[string]any{
			"state":              "observation_required",
			"route":              "GUI",
			"retry_same_request": false,
		}
	}
	return result
}

func stripSupersededFinalObservationUnavailable(
	content []ContentItem,
) []ContentItem {
	filtered := content[:0]
	for _, item := range content {
		if item.Type != "text" {
			filtered = append(filtered, item)
			continue
		}
		if strings.HasPrefix(item.Text, "final_observation_unavailable: read-only follow-up") {
			continue
		}
		lines := strings.Split(item.Text, "\n")
		kept := lines[:0]
		for _, line := range lines {
			if strings.HasPrefix(
				strings.TrimSpace(line),
				"final_observation_unavailable:",
			) {
				continue
			}
			kept = append(kept, line)
		}
		item.Text = strings.TrimSpace(strings.Join(kept, "\n"))
		if item.Text != "" {
			filtered = append(filtered, item)
		}
	}
	return filtered
}

func (s *Service) recoverClosedWindowActionObservation(
	context *AXBatchContext,
	action axBatchAction,
	result ToolCallResult,
	obs observation,
) ToolCallResult {
	if s == nil ||
		action.actionType != "close" ||
		result.StructuredContent == nil {
		return result
	}
	omitFinalObservation := obs.resolvedMode() == contract.ObserveNone
	returned, hasReturned :=
		result.StructuredContent["final_observation_returned"].(bool)
	required, _ :=
		result.StructuredContent["observation_required"].(bool)
	if result.StructuredContent["target_window_absent"] == true ||
		(hasReturned && returned && !required) {
		result = finalizeVerifiedCloseLifecycle(result)
		if omitFinalObservation {
			result = omitVerifiedCloseFinalObservation(result)
		}
		return result
	}
	delete(result.StructuredContent, "observation_required")
	delete(result.StructuredContent, "next_identical_observation_required")
	probeObservation := obs
	if omitFinalObservation {
		probeObservation = observation{mode: contract.ObserveDiff}
	}
	finalObservation := s.axBatchFinalObservation(
		context,
		probeObservation,
		true,
	)
	if omitFinalObservation &&
		finalObservation.StructuredContent["target_window_absent"] != true {
		result.StructuredContent["final_observation_kind"] = "none"
		result.StructuredContent["final_observation_returned"] = false
		result.StructuredContent["close_effect_verified"] = false
		result.StructuredContent["observation_required"] = true
		result.StructuredContent["next_identical_observation_required"] = false
		result.StructuredContent["continuation"] = map[string]any{
			"state":              "observation_required",
			"route":              "observe",
			"retry_same_request": false,
		}
		return result
	}
	merged := mergeAXBatchFinalObservation(result, finalObservation)
	merged = finalizeVerifiedCloseLifecycle(merged)
	if omitFinalObservation &&
		merged.StructuredContent["close_effect_verified"] == true {
		merged = omitVerifiedCloseFinalObservation(merged)
	}
	return merged
}

func omitVerifiedCloseFinalObservation(result ToolCallResult) ToolCallResult {
	if result.StructuredContent == nil ||
		result.StructuredContent["close_effect_verified"] != true {
		return result
	}
	failurePhase, _ := result.StructuredContent["failure_phase"].(string)
	if failurePhase == "post_action_observation" ||
		failurePhase == "final_observation" {
		result.IsError = false
		delete(result.StructuredContent, "failure_code")
		delete(result.StructuredContent, "failure_phase")
		delete(result.StructuredContent, "observation_failure_code")
		delete(result.StructuredContent, "post_action_observation_status")
		clearVerifiedCloseUncertainty(result.StructuredContent, nil)
	}
	filtered := result.Content[:0]
	for _, item := range result.Content {
		if item.Type == "text" &&
			strings.HasPrefix(
				strings.TrimSpace(item.Text),
				"ax_diff:\n effect: target_window_absent",
			) {
			continue
		}
		filtered = append(filtered, item)
	}
	result.Content = append(filtered, ContentItem{
		Type: "text",
		Text: "close_effect: verified target_window_absent; " +
			`observe="none" returned no final tree or screenshot.`,
	})
	result.StructuredContent["full_tree_returned"] = false
	result.StructuredContent["tree_reference_only"] = false
	result.StructuredContent["compact_action_observation"] = false
	result.StructuredContent["final_observation_kind"] = "none"
	result.StructuredContent["final_observation_returned"] = false
	result.StructuredContent["observation_required"] = false
	result.StructuredContent["next_identical_observation_required"] = false
	return result
}

func stripSupersededAXObservationPlaceholder(
	content []ContentItem,
) []ContentItem {
	filtered := content[:0]
	for _, item := range content {
		if item.Type != "text" {
			filtered = append(filtered, item)
			continue
		}
		lines := strings.Split(item.Text, "\n")
		kept := lines[:0]
		for _, line := range lines {
			trimmed := strings.TrimSpace(line)
			if trimmed == "observe: none" ||
				strings.HasPrefix(trimmed, `(observe="none":`) {
				continue
			}
			kept = append(kept, line)
		}
		text := strings.TrimSpace(strings.Join(kept, "\n"))
		if text == "" {
			continue
		}
		item.Text = text
		filtered = append(filtered, item)
	}
	return filtered
}

func clearVerifiedCloseUncertainty(
	structuredContent map[string]any,
	actionEvidence map[string]any,
) {
	delete(structuredContent, "effect_unknown")
	delete(structuredContent, "mutation_unknown")
	delete(structuredContent, "cause_unattributed")
	if actionEvidence != nil {
		delete(actionEvidence, "mutation_unknown")
		delete(actionEvidence, "cause_unattributed")
	}
}
