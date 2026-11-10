package core

import "strings"

type ActionabilityStage string

const (
	ActionResolve          ActionabilityStage = "resolve"
	ActionVerifyWindow     ActionabilityStage = "verify_window"
	ActionClassify         ActionabilityStage = "classify_capability"
	ActionSampleGeometry   ActionabilityStage = "sample_live_geometry"
	ActionWaitStable       ActionabilityStage = "wait_stable"
	ActionVerifyFocus      ActionabilityStage = "verify_focus_editability"
	ActionEmit             ActionabilityStage = "emit"
	ActionReadBack         ActionabilityStage = "read_back"
	ActionPostcondition    ActionabilityStage = "check_postcondition"
	ActionStageNotRequired string             = "not_required"
)

type ActionabilityStageEvidence struct {
	Stage  ActionabilityStage `json:"stage"`
	Status string             `json:"status"`
	Detail string             `json:"detail,omitempty"`
}

type ActionabilityEvidence struct {
	Resolution            string                       `json:"resolution"`
	WindowVerified        TriState                     `json:"window_verified"`
	Route                 string                       `json:"route"`
	Stable                TriState                     `json:"stable"`
	WindowFocusVerified   TriState                     `json:"window_focus_verified"`
	TargetFocusVerified   TriState                     `json:"target_focus_verified"`
	PointOwnerVerified    TriState                     `json:"point_owner_verified"`
	Enabled               TriState                     `json:"enabled"`
	FocusVerified         TriState                     `json:"focus_verified"`
	EditableVerified      TriState                     `json:"editable_verified"`
	InputEmitted          TriState                     `json:"input_emitted"`
	DeliveryVerified      TriState                     `json:"delivery_verified"`
	ReadbackSampled       TriState                     `json:"readback_sampled"`
	PostconditionVerified TriState                     `json:"postcondition_verified"`
	Stages                []ActionabilityStageEvidence `json:"stages"`
}

func initialActionabilityEvidence(
	tool string,
	record *ElementRecord,
	semantic *semanticTarget,
) ActionabilityEvidence {
	evidence := ActionabilityEvidence{
		Resolution:            "unique",
		WindowVerified:        TriUnknown,
		Route:                 string(ActivationUnknown),
		Stable:                TriUnknown,
		WindowFocusVerified:   TriUnknown,
		TargetFocusVerified:   TriUnknown,
		PointOwnerVerified:    TriUnknown,
		Enabled:               TriUnknown,
		FocusVerified:         TriUnknown,
		EditableVerified:      TriUnknown,
		InputEmitted:          TriUnknown,
		DeliveryVerified:      TriUnknown,
		ReadbackSampled:       TriUnknown,
		PostconditionVerified: TriUnknown,
	}
	evidence.Stages = append(evidence.Stages,
		ActionabilityStageEvidence{Stage: ActionResolve, Status: "passed"},
		ActionabilityStageEvidence{Stage: ActionVerifyWindow, Status: "pending"},
	)
	evidence.Stages = append(evidence.Stages,
		ActionabilityStageEvidence{
			Stage: ActionClassify, Status: "pending",
		},
	)
	evidence.Stages = append(evidence.Stages,
		ActionabilityStageEvidence{
			Stage: ActionSampleGeometry, Status: "pending",
		},
	)
	return evidence
}

func updateActionabilityStage(
	evidence *ActionabilityEvidence,
	stage ActionabilityStage,
	status,
	detail string,
) {
	if evidence == nil {
		return
	}
	for index := range evidence.Stages {
		if evidence.Stages[index].Stage == stage {
			evidence.Stages[index].Status = status
			evidence.Stages[index].Detail = detail
			return
		}
	}
	evidence.Stages = append(evidence.Stages, ActionabilityStageEvidence{
		Stage: stage, Status: status, Detail: detail,
	})
}

func boolEvidence(value bool) TriState {
	if value {
		return TriTrue
	}
	return TriFalse
}

// finalizeActionabilityEvidence completes the stage record of one AX action
// from the runtime's own evidence. Stage order matters: a stage absent from
// the initial record is appended where it is first updated.
func finalizeActionabilityEvidence(
	evidence ActionabilityEvidence,
	request AXServiceRequest,
	response ToolCallResult,
	success bool,
	actual *RuntimeActionEvidence,
) ActionabilityEvidence {
	if !success {
		return finalizeFailedActionabilityEvidence(evidence, response, actual)
	}
	recordWindowFocusStage(&evidence, actual)
	if actual != nil {
		recordRuntimeRoute(&evidence, actual)
		if actual.GeometrySampled {
			updateActionabilityStage(
				&evidence, ActionSampleGeometry, "passed", "",
			)
		} else {
			updateActionabilityStage(
				&evidence, ActionSampleGeometry, ActionStageNotRequired, "",
			)
		}
		copyRuntimeVerificationFlags(&evidence, actual)
	} else {
		updateActionabilityStage(
			&evidence, ActionClassify, "unknown", "runtime did not report route",
		)
		updateActionabilityStage(
			&evidence, ActionSampleGeometry, "unknown", "",
		)
	}
	recordFocusStage(&evidence, request)
	recordEmitStage(&evidence, response, actual)
	recordReadbackStage(&evidence, actual)
	// Single AX actions carry no settle predicate; batch postconditions are
	// reported by the batch, not by this per-action evidence.
	updateActionabilityStage(
		&evidence, ActionWaitStable, ActionStageNotRequired, "",
	)
	updateActionabilityStage(
		&evidence, ActionPostcondition, ActionStageNotRequired, "",
	)

	return evidence
}

// finalizeFailedActionabilityEvidence keeps whatever partial evidence the
// runtime reported before the failure and classifies the emit stage from the
// failure response, so a failed action never claims stages it did not reach.
func finalizeFailedActionabilityEvidence(
	evidence ActionabilityEvidence,
	response ToolCallResult,
	actual *RuntimeActionEvidence,
) ActionabilityEvidence {
	if actual != nil {
		recordRuntimeRoute(&evidence, actual)
		if actual.GeometrySampled {
			updateActionabilityStage(
				&evidence, ActionSampleGeometry, "passed", "",
			)
		}
		if actual.WindowFocusVerified != nil {
			evidence.WindowFocusVerified =
				boolEvidence(*actual.WindowFocusVerified)
			evidence.WindowVerified = evidence.WindowFocusVerified
		}
		copyRuntimeVerificationFlags(&evidence, actual)
		evidence.InputEmitted = boolEvidence(actual.InputEmitted)
		if actual.ReadbackSampled {
			evidence.ReadbackSampled = TriTrue
			updateActionabilityStage(
				&evidence, ActionReadBack, "passed", "",
			)
		}
	}
	code := ""
	if response.StructuredContent != nil {
		code, _ = response.StructuredContent["failure_code"].(string)
	}
	switch code {
	case "semantic_target_not_found", "semantic_target_ambiguous",
		"stale_element_ref", "semantic_target_invalid":
		evidence.Resolution = "failed"
		updateActionabilityStage(&evidence, ActionResolve, "failed", code)
	}
	if response.StructuredContent != nil {
		if emitted, ok := response.StructuredContent["input_events_emitted"].(bool); ok {
			evidence.InputEmitted = boolEvidence(emitted)
		}
	}
	if evidence.InputEmitted == TriUnknown {
		updateActionabilityStage(&evidence, ActionEmit, "unknown", code)
	} else if evidence.InputEmitted == TriFalse {
		updateActionabilityStage(&evidence, ActionEmit, "not_executed", code)
	} else {
		updateActionabilityStage(&evidence, ActionEmit, "partial_or_emitted", code)
	}
	return evidence
}

// recordRuntimeRoute records the activation route the runtime chose; an
// empty route stays unknown rather than being guessed from the action type.
func recordRuntimeRoute(
	evidence *ActionabilityEvidence,
	actual *RuntimeActionEvidence,
) {
	evidence.Route = strings.TrimSpace(actual.Route)
	if evidence.Route == "" {
		evidence.Route = string(ActivationUnknown)
	}
	updateActionabilityStage(
		evidence, ActionClassify, "passed", evidence.Route,
	)
}

// copyRuntimeVerificationFlags copies the tri-state checks the runtime
// reported. A nil flag leaves the prior value, so an unreported check stays
// unknown. Delivery counts as verified only when a readback was sampled.
func copyRuntimeVerificationFlags(
	evidence *ActionabilityEvidence,
	actual *RuntimeActionEvidence,
) {
	if actual.GeometryStable != nil {
		evidence.Stable = boolEvidence(*actual.GeometryStable)
	}
	if actual.TargetFocusVerified != nil {
		evidence.TargetFocusVerified =
			boolEvidence(*actual.TargetFocusVerified)
	}
	if actual.PointOwnerVerified != nil {
		evidence.PointOwnerVerified =
			boolEvidence(*actual.PointOwnerVerified)
	}
	if actual.Enabled != nil {
		evidence.Enabled = boolEvidence(*actual.Enabled)
	}
	if actual.Editable != nil {
		evidence.EditableVerified = boolEvidence(*actual.Editable)
	}
	if actual.FocusVerified != nil {
		evidence.FocusVerified = boolEvidence(*actual.FocusVerified)
	}
	if actual.EditableVerified != nil {
		evidence.EditableVerified = boolEvidence(*actual.EditableVerified)
	}
	if actual.ReadbackSampled && actual.ReadbackVerified != nil {
		evidence.DeliveryVerified = boolEvidence(*actual.ReadbackVerified)
	}
}

func recordWindowFocusStage(
	evidence *ActionabilityEvidence,
	actual *RuntimeActionEvidence,
) {
	if actual != nil && actual.WindowFocusVerified != nil {
		evidence.WindowFocusVerified =
			boolEvidence(*actual.WindowFocusVerified)
		evidence.WindowVerified = evidence.WindowFocusVerified
	} else {
		evidence.WindowVerified = TriUnknown
	}
	updateActionabilityStage(
		evidence,
		ActionVerifyWindow,
		map[TriState]string{
			TriTrue: "passed", TriFalse: "failed", TriUnknown: "unknown",
		}[evidence.WindowVerified],
		"",
	)
}

// recordFocusStage requires verified focus only for text and key input,
// which go to whatever element holds focus.
func recordFocusStage(
	evidence *ActionabilityEvidence,
	request AXServiceRequest,
) {
	intent := request.operationName()
	if intent == "type" || intent == "keypress" {
		if evidence.FocusVerified == TriTrue {
			updateActionabilityStage(evidence, ActionVerifyFocus, "passed", "")
		} else if evidence.FocusVerified == TriFalse {
			updateActionabilityStage(evidence, ActionVerifyFocus, "failed", "")
		} else {
			updateActionabilityStage(
				evidence, ActionVerifyFocus, "unknown", "",
			)
		}
	} else {
		updateActionabilityStage(
			evidence, ActionVerifyFocus, ActionStageNotRequired, "",
		)
	}
}

// recordEmitStage prefers the runtime's own emission flag and falls back to
// the response's input_events_emitted only when the runtime reported nothing.
func recordEmitStage(
	evidence *ActionabilityEvidence,
	response ToolCallResult,
	actual *RuntimeActionEvidence,
) {
	emitted := actual != nil && actual.InputEmitted
	if actual == nil && response.StructuredContent != nil {
		if value, ok := response.StructuredContent["input_events_emitted"].(bool); ok {
			emitted = value
		}
	}
	evidence.InputEmitted = boolEvidence(emitted)
	emitStatus := "unknown"
	if evidence.InputEmitted == TriTrue {
		emitStatus = "passed"
	} else if evidence.InputEmitted == TriFalse {
		emitStatus = "not_executed"
	}
	updateActionabilityStage(
		evidence, ActionEmit, emitStatus,
		"input_emitted="+string(evidence.InputEmitted),
	)
}

func recordReadbackStage(
	evidence *ActionabilityEvidence,
	actual *RuntimeActionEvidence,
) {
	if actual != nil && actual.ReadbackSampled {
		evidence.ReadbackSampled = TriTrue
		updateActionabilityStage(evidence, ActionReadBack, "passed", "")
	} else {
		evidence.ReadbackSampled = TriFalse
		updateActionabilityStage(
			evidence, ActionReadBack, "not_performed", "",
		)
	}
}

func attachActionabilityEvidence(
	result ToolCallResult,
	evidence ActionabilityEvidence,
) ToolCallResult {
	if result.StructuredContent == nil {
		result.StructuredContent = map[string]any{}
	}
	result.StructuredContent["actionability"] = evidence
	return result
}
