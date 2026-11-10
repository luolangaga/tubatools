package core

import (
	"errors"
	"fmt"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"strings"
)

func prependAXBatchSummary(
	result ToolCallResult,
	summary string,
	facts map[string]any,
) ToolCallResult {
	if strings.TrimSpace(summary) != "" {
		result.Content = append(
			[]ContentItem{{Type: "text", Text: summary}},
			result.Content...,
		)
	}
	if result.StructuredContent == nil {
		result.StructuredContent = map[string]any{}
	}
	result.StructuredContent["a11y_batch_result"] = facts
	if actions, ok := facts["actions"].([]map[string]any); ok {
		emitted, partial, unknown := batchMutationSummary(actions)
		// A successful tail cannot erase uncertainty from an earlier action.
		for key, value := range map[string]bool{
			"input_events_emitted":   emitted,
			"partial_input_possible": partial,
			"mutation_unknown":       unknown,
		} {
			if value {
				result.StructuredContent[key] = true
			}
		}
	}
	return result
}

func compactAXActionContent(result ToolCallResult) ToolCallResult {
	routine := []string{
		"Invoked the element's AT-SPI accessibility action",
		"Synthesized ",
		"Resolved element",
		"Resolved semantic target",
		"The element MOVED",
	}
	for itemIndex := range result.Content {
		if result.Content[itemIndex].Type != "text" {
			continue
		}
		lines := strings.Split(result.Content[itemIndex].Text, "\n")
		kept := make([]string, 0, len(lines))
		for _, line := range lines {
			trimmed := strings.TrimSpace(line)
			if strings.HasPrefix(trimmed, "Note:") {
				drop := false
				for _, marker := range routine {
					if strings.Contains(trimmed, marker) {
						drop = true
						break
					}
				}
				if drop {
					continue
				}
			}
			if strings.HasPrefix(trimmed, `(observe="none":`) {
				kept = append(kept, "observe: none")
				continue
			}
			if strings.HasPrefix(trimmed, `(observe="screenshot":`) {
				kept = append(kept, "observe: screenshot")
				continue
			}
			kept = append(kept, line)
		}
		result.Content[itemIndex].Text = strings.TrimSpace(
			strings.Join(kept, "\n"),
		)
	}
	return result
}

func axBatchValidationError(message string, requested int) ToolCallResult {
	result := TextResult(
		message+"\nNo action was sent.",
		true,
	)
	result.StructuredContent = map[string]any{
		"action_attempted":     false,
		"input_events_emitted": false,
		"a11y_batch_result": map[string]any{
			"actions_requested":                    requested,
			"actions_completed_without_call_error": 0,
			"failed_action_index":                  nil,
			"rejected_before_first_action":         true,
			"partial_sequence":                     false,
		},
	}
	return result
}

func axBatchPreflightError(err error, requested int) ToolCallResult {
	var argument *axArgError
	if errors.As(err, &argument) {
		return axArgErrorResult(argument, requested)
	}
	return withArgumentRule(
		withAXLifecycleFailure(axBatchValidationError(err.Error(), requested), err),
		err,
	)
}

func withAXLifecycleFailure(result ToolCallResult, err error) ToolCallResult {
	var lifecycle *axRefLifecycleError
	if !errors.As(err, &lifecycle) {
		if strings.Contains(err.Error(), "was never issued in this session") {
			// A ref this session never returned: copy refs from results.
			if result.StructuredContent == nil {
				result.StructuredContent = map[string]any{}
			}
			result.StructuredContent["failure_code"] = "unknown_ref"
			result.StructuredContent[contract.AXPublicErrorHintKey] = `{"mode":"find","window_ref":"wN","query":{"name_contains":"..."}}`
		}
		return result
	}
	code := ""
	switch lifecycle.kind {
	case "app":
		code = "stale_app_ref"
	case "window":
		code = "stale_window_ref"
	case "element":
		code = "stale_element_ref"
	default:
		return result
	}
	complete := lifecycle.lifecycle != AXRefNeedsRevalidation
	if !complete {
		code = "observation_incomplete"
	}
	if result.StructuredContent == nil {
		result.StructuredContent = map[string]any{}
	}
	result.StructuredContent["failure_code"] = code
	result.StructuredContent["failure_subject"] = lifecycle.kind
	result.StructuredContent["failure_phase"] = "precondition"
	result.StructuredContent["failure_facts_complete"] = complete
	result.StructuredContent["observation_required"] = true
	result.StructuredContent["final_observation_returned"] = false
	result.StructuredContent["partial_input_possible"] = false
	result.StructuredContent["input_events_emitted"] = false
	result.StructuredContent["retry_same_request"] = false
	return result
}

func batchActionEvidenceEntry(
	index int,
	actionType string,
	result ToolCallResult,
) map[string]any {
	entry := map[string]any{
		"action_index":  index,
		"action_number": index + 1,
		"action_type":   actionType,
		"call_error":    result.IsError,
	}
	if result.StructuredContent != nil {
		if evidence, present := result.StructuredContent["actionability"]; present {
			entry["actionability"] = compactBatchActionability(evidence)
		}
		for _, key := range []string{
			"failure_code",
			"failure_phase",
			"focus_preparation",
			"pointer_preparation",
			"input_events_emitted",
			"partial_input_possible",
			"mutation_unknown",
			"runtime_action_evidence",
		} {
			if value, present := result.StructuredContent[key]; present {
				entry[key] = value
			}
		}
	}
	return entry
}

func batchMutationSummary(actions []map[string]any) (bool, bool, bool) {
	emitted := false
	partial := false
	unknown := false
	for _, action := range actions {
		if value, ok := action["input_events_emitted"].(bool); ok && value {
			emitted = true
		}
		if value, ok := action["partial_input_possible"].(bool); ok && value {
			partial = true
		}
		if value, ok := action["mutation_unknown"].(bool); ok && value {
			unknown = true
		}
		actionability, _ := action["actionability"].(map[string]any)
		if actionability == nil {
			continue
		}
		switch strings.ToLower(strings.TrimSpace(
			fmt.Sprint(actionability["input_emitted"]),
		)) {
		case "true":
			emitted = true
		case "unknown":
			partial = true
			unknown = true
		}
	}
	return emitted, partial, unknown
}

func compactBatchActionability(value any) any {
	evidence, ok := value.(ActionabilityEvidence)
	if !ok {
		if pointer, pointerOK := value.(*ActionabilityEvidence); pointerOK &&
			pointer != nil {
			evidence = *pointer
			ok = true
		}
	}
	if !ok {
		return value
	}
	return map[string]any{
		"resolution":             evidence.Resolution,
		"window_verified":        evidence.WindowVerified,
		"route":                  evidence.Route,
		"stable":                 evidence.Stable,
		"focus_verified":         evidence.FocusVerified,
		"editable_verified":      evidence.EditableVerified,
		"input_emitted":          evidence.InputEmitted,
		"delivery_verified":      evidence.DeliveryVerified,
		"readback_sampled":       evidence.ReadbackSampled,
		"postcondition_verified": evidence.PostconditionVerified,
	}
}
