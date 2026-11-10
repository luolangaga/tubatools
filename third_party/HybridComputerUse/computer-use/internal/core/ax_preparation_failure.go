package core

func preparationFailureResult(
	response ProviderAccessibilityResult, result ToolCallResult,
) ToolCallResult {
	if response.FocusPreparation != "" {
		result.StructuredContent["focus_preparation"] = response.FocusPreparation
	}
	if response.PointerPreparation != "" {
		result.StructuredContent["pointer_preparation"] = response.PointerPreparation
	}
	result.StructuredContent["failed_action_input_emitted"] = response.InputEventsEmitted
	result.StructuredContent["observation_required"] = false
	result.StructuredContent["final_observation_kind"] = "error_only"
	result.StructuredContent["final_observation_returned"] = false
	result.StructuredContent["recovery_read_attempted"] = false
	result.StructuredContent["continuation"] = map[string]any{
		"state": "stop", "retry_same_request": false,
	}
	if response.ActionEvidence != nil {
		result.StructuredContent["runtime_action_evidence"] = *response.ActionEvidence
	}
	result.Content[0].Text += "\nPreparation may have changed state or may complete later. " +
		"Stop; do not replay or observe. No recovery read or later batch action was attempted."
	return result.withEvidenceScope("error_only")
}
