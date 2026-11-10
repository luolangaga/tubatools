package core

import (
	"encoding/json"
	"errors"
	"fmt"
	"math"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

const ProviderProtocolVersion = 2

type ProviderFrame = frame
type ProviderElement = ElementRecord
type ProviderElementRef = elementRef
type ProviderSnapshot = AppSnapshot
type ProviderActionEvidence = RuntimeActionEvidence
type ProviderAppDescriptor = AppDescriptor

type ProviderAppIdentity struct {
	ProviderBusName   string `json:"providerBusName"`
	ProcessGeneration string `json:"processGeneration"`
}

type ProviderWindowExpectation struct {
	WindowHandle       string `json:"windowHandle"`
	ProviderBusName    string `json:"providerBusName"`
	ProviderObjectPath string `json:"providerObjectPath"`
	ProcessGeneration  string `json:"processGeneration"`
	Role               string `json:"role"`
	Name               string `json:"name,omitempty"`
}

type ProviderReplacementWindow struct {
	WindowHandle       string `json:"windowHandle,omitempty"`
	Title              string `json:"title,omitempty"`
	Role               string `json:"role,omitempty"`
	ProviderBusName    string `json:"providerBusName,omitempty"`
	ProviderObjectPath string `json:"providerObjectPath,omitempty"`
	ProcessGeneration  string `json:"processGeneration,omitempty"`
}

type ProviderRecoveryContext struct {
	SourceAppStatus    string                     `json:"sourceAppStatus,omitempty"`
	SourceWindowStatus string                     `json:"sourceWindowStatus,omitempty"`
	InventoryComplete  bool                       `json:"inventoryComplete,omitempty"`
	CurrentWindow      *ProviderReplacementWindow `json:"currentWindow,omitempty"`
}

type ProviderTargetCandidate struct {
	Role               string `json:"role,omitempty"`
	Name               string `json:"name,omitempty"`
	Description        string `json:"description,omitempty"`
	Path               string `json:"path,omitempty"`
	WindowHandle       string `json:"windowHandle,omitempty"`
	ProviderBusName    string `json:"providerBusName,omitempty"`
	ProviderObjectPath string `json:"providerObjectPath,omitempty"`
	ProcessGeneration  string `json:"processGeneration,omitempty"`
}

type ProviderSemanticTarget struct {
	EditTarget                      *axRelationTarget         `json:"editTarget,omitempty"`
	Selector                        string                    `json:"selector"`
	LocatorKind                     AXLocatorKind             `json:"locatorKind,omitempty"`
	AllowProviderRebind             bool                      `json:"allowProviderRebind,omitempty"`
	WindowHandle                    string                    `json:"windowHandle,omitempty"`
	ProviderBusName                 string                    `json:"providerBusName,omitempty"`
	ProviderObjectPath              string                    `json:"providerObjectPath,omitempty"`
	ProcessGeneration               string                    `json:"processGeneration,omitempty"`
	ProviderWindowBusName           string                    `json:"providerWindowBusName,omitempty"`
	ProviderWindowObjectPath        string                    `json:"providerWindowObjectPath,omitempty"`
	ProviderWindowProcessGeneration string                    `json:"providerWindowProcessGeneration,omitempty"`
	ProviderWindowRole              string                    `json:"providerWindowRole,omitempty"`
	ProviderWindowName              string                    `json:"providerWindowName,omitempty"`
	ProviderRoleAliases             []string                  `json:"providerRoleAliases,omitempty"`
	ProviderIdentityScope           string                    `json:"providerIdentityScope,omitempty"`
	AutomationID                    string                    `json:"automationId,omitempty"`
	RuntimeID                       []int                     `json:"runtimeId,omitempty"`
	RuntimeIDAliases                [][]int                   `json:"runtimeAliases,omitempty"`
	Role                            string                    `json:"role,omitempty"`
	Name                            string                    `json:"name,omitempty"`
	HasName                         bool                      `json:"hasName"`
	Predicates                      []semanticTargetPredicate `json:"predicates,omitempty"`
	SourceIndex                     *int                      `json:"-"`
}

type ProviderObservationOptions struct {
	TextLimit         *ProviderTextLimit `json:"text_limit,omitempty"`
	MaxTreeNodes      int                `json:"max_tree_nodes,omitempty"`
	MaxTreeDepth      int                `json:"max_tree_depth,omitempty"`
	Prune             *bool              `json:"prune,omitempty"`
	Boxes             *bool              `json:"boxes,omitempty"`
	IncludeScreenshot *bool              `json:"include_screenshot,omitempty"`
	CaptureSnapshot   *bool              `json:"capture_snapshot,omitempty"`
	// FindWalk asks the Linux provider to walk with its AX find caps
	// instead of the render budget. Other providers reject the field, so
	// only the Linux backend receives it.
	FindWalk bool `json:"find_walk,omitempty"`
	// FindQuery goes with FindWalk: the provider builds full records only
	// for nodes that can match it and for their ancestors.
	FindQuery *ProviderFindQuery `json:"find_query,omitempty"`
	// FindResumeAfter continues a walk of the same query and window from
	// the AppSnapshot.FindResume it reported.
	FindResumeAfter []int `json:"find_resume_after,omitempty"`
	// RecheckAfterAction marks the read 250 ms after an action whose read
	// showed no change. The Windows provider then reads a LibreOffice Calc
	// sheet in part, as right after the action. Other providers reject the
	// field, so only the Windows backend receives it.
	RecheckAfterAction bool `json:"recheck_after_action,omitempty"`
}

// ProviderFindQuery is the name and text part of an AX find query. The
// provider prefilters with it and never rejects a node that the core's
// query match would accept; the core still matches every returned record.
type ProviderFindQuery struct {
	Name      string `json:"name,omitempty"`
	NameExact bool   `json:"name_exact,omitempty"`
	Text      string `json:"text,omitempty"`
	TextExact bool   `json:"text_exact,omitempty"`
}

type ProviderTextLimit struct {
	Max   bool
	Value int
}

func (limit ProviderTextLimit) MarshalJSON() ([]byte, error) {
	if limit.Max {
		return json.Marshal("max")
	}
	return json.Marshal(limit.Value)
}

func (limit *ProviderTextLimit) UnmarshalJSON(data []byte) error {
	var keyword string
	if err := json.Unmarshal(data, &keyword); err == nil {
		if keyword != "max" {
			return fmt.Errorf("provider text_limit string must be %q", "max")
		}
		*limit = ProviderTextLimit{Max: true}
		return nil
	}
	var value int
	if err := json.Unmarshal(data, &value); err != nil {
		return errors.New("provider text_limit must be a positive integer or \"max\"")
	}
	if value <= 0 {
		return errors.New("provider text_limit must be greater than zero")
	}
	*limit = ProviderTextLimit{Value: value}
	return nil
}

type ProviderClickInput struct {
	X           *float64 `json:"x,omitempty"`
	Y           *float64 `json:"y,omitempty"`
	ClickCount  int      `json:"click_count,omitempty"`
	MouseButton string   `json:"mouse_button,omitempty"`
	ClickMethod string   `json:"click_method,omitempty"`
}

type ProviderScrollInput struct {
	Direction    string  `json:"direction,omitempty"`
	Pages        float64 `json:"pages,omitempty"`
	WindowCentre bool    `json:"window_centre,omitempty"`
}

type ProviderTypeInput struct {
	Text string `json:"text"`
}

type ProviderKeypressInput struct {
	Key string `json:"key"`
}

type ProviderSetValueInput struct {
	Value                *string `json:"value"`
	RequireSemanticWrite bool    `json:"require_semantic_write,omitempty"`
}

type ProviderSelectInput struct {
	Text     string                  `json:"text,omitempty"`
	ToTarget *ProviderSemanticTarget `json:"to_target,omitempty"`
}

type ProviderObserveAppsRequest struct {
	ProtocolVersion int `json:"protocol_version"`
}

type ProviderObserveRequest struct {
	ProtocolVersion int                           `json:"protocol_version"`
	App             string                        `json:"app"`
	AppIdentity     *ProviderAppIdentity          `json:"app_identity,omitempty"`
	WindowHandle    string                        `json:"window_handle,omitempty"`
	Observation     ProviderObservationOptions    `json:"observation,omitempty"`
	KnownRefs       map[string]ProviderElementRef `json:"-"`
}

type ProviderActionRequest struct {
	ProtocolVersion int                           `json:"protocol_version"`
	Intent          AccessibilityOperation        `json:"intent"`
	App             string                        `json:"app"`
	AppIdentity     *ProviderAppIdentity          `json:"app_identity,omitempty"`
	WindowHandle    string                        `json:"window_handle,omitempty"`
	ExpectedWindow  *ProviderWindowExpectation    `json:"expected_window,omitempty"`
	Element         *ProviderElement              `json:"element,omitempty"`
	Target          *ProviderSemanticTarget       `json:"target,omitempty"`
	Click           *ProviderClickInput           `json:"-"`
	Scroll          *ProviderScrollInput          `json:"-"`
	Type            *ProviderTypeInput            `json:"-"`
	Keypress        *ProviderKeypressInput        `json:"-"`
	SetValue        *ProviderSetValueInput        `json:"-"`
	Select          *ProviderSelectInput          `json:"-"`
	WindowBounds    *ProviderFrame                `json:"window_bounds,omitempty"`
	Observation     ProviderObservationOptions    `json:"observation,omitempty"`
	Diagnostics     *ProviderDiagnosticContext    `json:"diagnostics,omitempty"`
	KnownRefs       map[string]ProviderElementRef `json:"-"`
}

type providerActionRequestWire struct {
	ProtocolVersion int                        `json:"protocol_version"`
	Intent          AccessibilityOperation     `json:"intent"`
	App             string                     `json:"app"`
	AppIdentity     *ProviderAppIdentity       `json:"app_identity,omitempty"`
	WindowHandle    string                     `json:"window_handle,omitempty"`
	ExpectedWindow  *ProviderWindowExpectation `json:"expected_window,omitempty"`
	Element         *ProviderElement           `json:"element,omitempty"`
	Target          *ProviderSemanticTarget    `json:"target,omitempty"`
	Input           json.RawMessage            `json:"input,omitempty"`
	WindowBounds    *ProviderFrame             `json:"window_bounds,omitempty"`
	Observation     ProviderObservationOptions `json:"observation,omitempty"`
	Diagnostics     *ProviderDiagnosticContext `json:"diagnostics,omitempty"`
}

func (request ProviderActionRequest) MarshalJSON() ([]byte, error) {
	var input any
	switch request.Intent {
	case AccessibilityClick:
		input = request.Click
	case AccessibilityScroll:
		input = request.Scroll
	case AccessibilityType:
		input = request.Type
	case AccessibilityKeypress:
		input = request.Keypress
	case AccessibilitySetValue:
		input = request.SetValue
	case AccessibilitySelect:
		input = request.Select
	}
	var raw json.RawMessage
	if input != nil {
		encoded, err := json.Marshal(input)
		if err != nil {
			return nil, err
		}
		raw = encoded
	}
	return json.Marshal(providerActionRequestWire{
		ProtocolVersion: request.ProtocolVersion,
		Intent:          request.Intent,
		App:             request.App,
		AppIdentity:     request.AppIdentity,
		WindowHandle:    request.WindowHandle,
		ExpectedWindow:  request.ExpectedWindow,
		Element:         request.Element,
		Target:          request.Target,
		Input:           raw,
		WindowBounds:    request.WindowBounds,
		Observation:     request.Observation,
		Diagnostics:     request.Diagnostics,
	})
}

func (request *ProviderActionRequest) UnmarshalJSON(data []byte) error {
	var wire providerActionRequestWire
	if err := DecodeProviderWireJSON(data, &wire); err != nil {
		return err
	}
	*request = ProviderActionRequest{
		ProtocolVersion: wire.ProtocolVersion,
		Intent:          wire.Intent,
		App:             wire.App,
		AppIdentity:     wire.AppIdentity,
		WindowHandle:    wire.WindowHandle,
		ExpectedWindow:  wire.ExpectedWindow,
		Element:         wire.Element,
		Target:          wire.Target,
		WindowBounds:    wire.WindowBounds,
		Observation:     wire.Observation,
		Diagnostics:     wire.Diagnostics,
	}
	decodeInput := func(target any) error {
		if len(wire.Input) == 0 {
			return errors.New("provider action input is required")
		}
		return DecodeProviderWireJSON(wire.Input, target)
	}
	switch wire.Intent {
	case AccessibilityResolveTarget:
		if len(wire.Input) != 0 {
			return errors.New("provider resolve_target does not accept input")
		}
	case AccessibilityClick:
		request.Click = &ProviderClickInput{}
		return decodeInput(request.Click)
	case AccessibilityScroll:
		request.Scroll = &ProviderScrollInput{}
		return decodeInput(request.Scroll)
	case AccessibilityType:
		request.Type = &ProviderTypeInput{}
		return decodeInput(request.Type)
	case AccessibilityKeypress:
		request.Keypress = &ProviderKeypressInput{}
		return decodeInput(request.Keypress)
	case AccessibilitySetValue:
		request.SetValue = &ProviderSetValueInput{}
		return decodeInput(request.SetValue)
	case AccessibilitySelect:
		request.Select = &ProviderSelectInput{}
		return decodeInput(request.Select)
	default:
		// Validate reports the unsupported intent with the stable public error.
	}
	return nil
}

type ProviderPoint struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
}

type ProviderDesktopAction struct {
	Type    string          `json:"type"`
	X       *float64        `json:"x,omitempty"`
	Y       *float64        `json:"y,omitempty"`
	Button  string          `json:"button,omitempty"`
	Text    string          `json:"text,omitempty"`
	Keys    []string        `json:"keys,omitempty"`
	Path    []ProviderPoint `json:"path,omitempty"`
	ScrollX *float64        `json:"scroll_x,omitempty"`
	ScrollY *float64        `json:"scroll_y,omitempty"`
	// Region is a zoom rectangle [x0, y0, x1, y1] in pixels of the frame
	// that the request's frame_id names.
	Region []float64 `json:"region,omitempty"`
}

type ProviderDesktopRequest struct {
	ProtocolVersion int                     `json:"protocol_version"`
	Actions         []ProviderDesktopAction `json:"actions"`
	SettleMS        int                     `json:"settle_ms"`
	FrameID         string                  `json:"frame_id,omitempty"`
}

type ProviderWindowListRequest struct {
	ProtocolVersion int                  `json:"protocol_version"`
	App             string               `json:"app"`
	AppIdentity     *ProviderAppIdentity `json:"app_identity,omitempty"`
	WindowHandle    string               `json:"window_handle,omitempty"`
}

type ProviderWindowActionRequest struct {
	ProtocolVersion int                           `json:"protocol_version"`
	Action          WindowOperation               `json:"action"`
	App             string                        `json:"app"`
	AppIdentity     *ProviderAppIdentity          `json:"app_identity,omitempty"`
	WindowHandle    string                        `json:"window_handle,omitempty"`
	Target          *ProviderSemanticTarget       `json:"target,omitempty"`
	Observation     ProviderObservationOptions    `json:"observation,omitempty"`
	KnownRefs       map[string]ProviderElementRef `json:"-"`
}

type ProviderWindowActionObservation struct {
	Type               string `json:"type,omitempty"`
	WindowID           string `json:"window_id,omitempty"`
	Observed           *bool  `json:"observed,omitempty"`
	StillObservedAtEnd *bool  `json:"still_observed_at_end,omitempty"`
}

type ProviderGUIResult struct {
	Dispatched               *int                              `json:"dispatched,omitempty"`
	InputEventsEmitted       *bool                             `json:"input_events_emitted,omitempty"`
	InputActionsDispatched   *int                              `json:"input_actions_dispatched,omitempty"`
	WindowCommandsDispatched *int                              `json:"window_commands_dispatched,omitempty"`
	DesktopStateMutated      *bool                             `json:"desktop_state_mutated,omitempty"`
	MutationUnknown          *bool                             `json:"mutation_unknown,omitempty"`
	PartialInputPossible     *bool                             `json:"partial_input_possible,omitempty"`
	EffectUnknown            *bool                             `json:"effect_unknown,omitempty"`
	Quiescent                *bool                             `json:"quiescent,omitempty"`
	ChangedSinceBefore       *bool                             `json:"changed_since_before,omitempty"`
	VisualChangePercent      *float64                          `json:"visual_change_percent,omitempty"`
	VisualChangeBox          any                               `json:"visual_change_box,omitempty"`
	VisualChangeMethod       string                            `json:"visual_change_method,omitempty"`
	VisualChangeObserved     *bool                             `json:"visual_change_observed,omitempty"`
	WindowActionObservations []ProviderWindowActionObservation `json:"window_action_observations,omitempty"`
	WaitActionsRequested     *int                              `json:"wait_actions_requested,omitempty"`
	WaitSecondsRequested     *float64                          `json:"wait_seconds_requested,omitempty"`
	KeyboardMicroWaitActions *int                              `json:"keyboard_micro_wait_actions,omitempty"`
	Zoom                     *ProviderGUIZoom                  `json:"zoom,omitempty"`
}

// ProviderGUIZoom is the geometry of a zoom image: the captured region in
// frame pixels, snapped to whole native pixels, and the image size. Each
// axis maps back by its own scale, width/(x1-x0) and height/(y1-y0).
type ProviderGUIZoom struct {
	Region []float64 `json:"region"`
	Width  int       `json:"width"`
	Height int       `json:"height"`
}

type ProviderFailure struct {
	Code              string                     `json:"code,omitempty"`
	UnderlyingCode    string                     `json:"underlying_code,omitempty"`
	Phase             string                     `json:"phase,omitempty"`
	FactsComplete     bool                       `json:"facts_complete,omitempty"`
	Subject           string                     `json:"subject,omitempty"`
	ReplacementWindow *ProviderReplacementWindow `json:"replacement_window,omitempty"`
}

type ProviderMutationFacts struct {
	FocusPreparation     string `json:"focus_preparation,omitempty"`
	PointerPreparation   string `json:"pointer_preparation,omitempty"`
	InputEventsEmitted   bool   `json:"input_events_emitted,omitempty"`
	PartialInputPossible bool   `json:"partial_input_possible,omitempty"`
	DesktopStateMutated  bool   `json:"desktop_state_mutated,omitempty"`
	MutationUnknown      bool   `json:"mutation_unknown,omitempty"`
}

// ProviderInventoryIssue marks an unobserved part of an applications listing.
// An empty App means the provider could not enumerate every application.
type ProviderInventoryIssue struct {
	App    string `json:"app,omitempty"`
	Reason string `json:"reason"`
}

type ProviderAccessibilityResult struct {
	OK                        bool                      `json:"ok"`
	Text                      string                    `json:"text,omitempty"`
	Error                     string                    `json:"error,omitempty"`
	Notes                     []string                  `json:"notes,omitempty"`
	Snapshot                  *ProviderSnapshot         `json:"snapshot,omitempty"`
	Failure                   *ProviderFailure          `json:"failure,omitempty"`
	RecoveryContext           *ProviderRecoveryContext  `json:"recovery_context,omitempty"`
	Mutation                  *ProviderMutationFacts    `json:"mutation,omitempty"`
	ActionEvidence            *ProviderActionEvidence   `json:"action_evidence,omitempty"`
	Apps                      []ProviderAppDescriptor   `json:"apps,omitempty"`
	InventoryIssues           []ProviderInventoryIssue  `json:"inventory_issues,omitempty"`
	ResolvedElement           *ProviderElement          `json:"resolved_element,omitempty"`
	Candidates                []ProviderTargetCandidate `json:"candidates,omitempty"`
	CandidateCount            int                       `json:"candidate_count,omitempty"`
	CandidatesTruncated       bool                      `json:"candidates_truncated,omitempty"`
	ExternalDesktopTransition bool                      `json:"external_desktop_transition,omitempty"`
	ObservationSkipped        bool                      `json:"observation_skipped,omitempty"`

	// Common-service projections of the typed failure and mutation objects.
	// These fields never cross the provider wire and do not define a second
	// protocol. They keep failure recovery code readable while the wire remains
	// operation-specific and nested.
	FailureCode           string                     `json:"-"`
	UnderlyingFailureCode string                     `json:"-"`
	FailurePhase          string                     `json:"-"`
	FailureFactsComplete  bool                       `json:"-"`
	FailureSubject        string                     `json:"-"`
	ReplacementWindow     *ProviderReplacementWindow `json:"-"`
	Recovery              *ProviderRecoveryContext   `json:"-"`
	InputEventsEmitted    bool                       `json:"-"`
	PartialInputPossible  bool                       `json:"-"`
	DesktopStateMutated   bool                       `json:"-"`
	MutationUnknown       bool                       `json:"-"`
	FocusPreparation      string                     `json:"-"`
	PointerPreparation    string                     `json:"-"`
}

type ProviderDesktopResult struct {
	ActionEvidence   *ProviderActionEvidence `json:"action_evidence,omitempty"`
	OK               bool                    `json:"ok"`
	Text             string                  `json:"text,omitempty"`
	Error            string                  `json:"error,omitempty"`
	Notes            []string                `json:"notes,omitempty"`
	Snapshot         *ProviderSnapshot       `json:"snapshot,omitempty"`
	GUIResult        ProviderGUIResult       `json:"gui_result,omitempty"`
	ValidationErrors []map[string]any        `json:"validation_errors,omitempty"`
	Failure          *ProviderFailure        `json:"failure,omitempty"`
	Mutation         *ProviderMutationFacts  `json:"mutation,omitempty"`
}

type ProviderWindowResult struct {
	OK                        bool                     `json:"ok"`
	Text                      string                   `json:"text,omitempty"`
	Error                     string                   `json:"error,omitempty"`
	Notes                     []string                 `json:"notes,omitempty"`
	Snapshot                  *ProviderSnapshot        `json:"snapshot,omitempty"`
	AppName                   string                   `json:"app_name,omitempty"`
	AppPID                    int                      `json:"app_pid,omitempty"`
	WindowTitles              []string                 `json:"window_titles,omitempty"`
	Failure                   *ProviderFailure         `json:"failure,omitempty"`
	RecoveryContext           *ProviderRecoveryContext `json:"recovery_context,omitempty"`
	Mutation                  *ProviderMutationFacts   `json:"mutation,omitempty"`
	ActionEvidence            *ProviderActionEvidence  `json:"action_evidence,omitempty"`
	ExternalDesktopTransition bool                     `json:"external_desktop_transition,omitempty"`
}

func (result *ProviderAccessibilityResult) PrepareForService() {
	if result == nil {
		return
	}
	if result.Failure != nil {
		result.FailureCode = result.Failure.Code
		result.UnderlyingFailureCode = result.Failure.UnderlyingCode
		result.FailurePhase = result.Failure.Phase
		result.FailureFactsComplete = result.Failure.FactsComplete
		result.FailureSubject = result.Failure.Subject
		result.ReplacementWindow = result.Failure.ReplacementWindow
	}
	if result.RecoveryContext != nil {
		result.Recovery = result.RecoveryContext
	}
	if result.Mutation != nil {
		result.InputEventsEmitted = result.Mutation.InputEventsEmitted
		result.PartialInputPossible = result.Mutation.PartialInputPossible
		result.DesktopStateMutated = result.Mutation.DesktopStateMutated
		result.MutationUnknown = result.Mutation.MutationUnknown
		result.FocusPreparation = result.Mutation.FocusPreparation
		result.PointerPreparation = result.Mutation.PointerPreparation
	}
	if result.ActionEvidence != nil && result.ActionEvidence.InputEmitted {
		result.InputEventsEmitted = true
		if result.Mutation == nil {
			result.Mutation = &ProviderMutationFacts{}
		}
		result.Mutation.InputEventsEmitted = true
		if (result.Snapshot == nil && !result.ObservationSkipped) ||
			(result.Failure != nil && result.Failure.Code == "effect_unknown") {
			result.MutationUnknown = true
			result.Mutation.MutationUnknown = true
		}
	}
}

// decodeProviderDesktopActions converts the public GUI actions into the
// provider protocol type directly, without a JSON round trip. It is strict:
// a field the protocol does not carry would otherwise vanish here, before the
// provider's own field check could see it, and {"type":"wait","ms":5} would
// run as a plain one-second wait.
func decodeProviderDesktopActions(
	value any,
) ([]ProviderDesktopAction, error) {
	if value == nil {
		return nil, nil
	}
	items, ok := value.([]any)
	if !ok {
		return nil, errors.New("GUI actions must be an array of action objects")
	}
	actions := make([]ProviderDesktopAction, len(items))
	for index, item := range items {
		if err := decodeProviderDesktopAction(item, &actions[index]); err != nil {
			return nil, fmt.Errorf("GUI actions[%d]: %w", index, err)
		}
	}
	return actions, nil
}

func decodeProviderDesktopAction(item any, action *ProviderDesktopAction) error {
	fields, ok := item.(map[string]any)
	if !ok {
		return errors.New("action must be an object")
	}
	for _, key := range contract.SortedKeys(fields) {
		raw := fields[key]
		var err error
		switch key {
		case "type":
			action.Type, err = desktopString(key, raw)
		case "button":
			action.Button, err = desktopString(key, raw)
		case "text":
			action.Text, err = desktopString(key, raw)
		case "x":
			action.X, err = desktopNumber(key, raw)
		case "y":
			action.Y, err = desktopNumber(key, raw)
		case "scroll_x":
			action.ScrollX, err = desktopNumber(key, raw)
		case "scroll_y":
			action.ScrollY, err = desktopNumber(key, raw)
		case "keys":
			action.Keys, err = desktopStrings(key, raw)
		case "path":
			action.Path, err = desktopPath(raw)
		case "region":
			action.Region, err = desktopNumbers(key, raw)
		default:
			err = fmt.Errorf("unknown field %q", key)
		}
		if err != nil {
			return err
		}
	}
	return nil
}

func desktopNumbers(key string, raw any) ([]float64, error) {
	items, ok := raw.([]any)
	if !ok {
		return nil, fmt.Errorf("%s must be an array of numbers", key)
	}
	values := make([]float64, len(items))
	for index, item := range items {
		number, err := desktopNumber(fmt.Sprintf("%s[%d]", key, index), item)
		if err != nil {
			return nil, err
		}
		values[index] = *number
	}
	return values, nil
}

func desktopString(key string, raw any) (string, error) {
	text, ok := raw.(string)
	if !ok {
		return "", fmt.Errorf("%s must be a string", key)
	}
	return text, nil
}

func desktopNumber(key string, raw any) (*float64, error) {
	var number float64
	switch typed := raw.(type) {
	case json.Number:
		parsed, err := typed.Float64()
		if err != nil {
			return nil, fmt.Errorf("%s must be a number", key)
		}
		number = parsed
	case float64:
		number = typed
	case int:
		number = float64(typed)
	default:
		return nil, fmt.Errorf("%s must be a number", key)
	}
	if math.IsNaN(number) || math.IsInf(number, 0) {
		return nil, fmt.Errorf("%s must be a finite number", key)
	}
	return &number, nil
}

func desktopStrings(key string, raw any) ([]string, error) {
	items, ok := raw.([]any)
	if !ok {
		return nil, fmt.Errorf("%s must be an array of strings", key)
	}
	values := make([]string, len(items))
	for index, item := range items {
		text, ok := item.(string)
		if !ok {
			return nil, fmt.Errorf("%s[%d] must be a string", key, index)
		}
		values[index] = text
	}
	return values, nil
}

func desktopPath(raw any) ([]ProviderPoint, error) {
	items, ok := raw.([]any)
	if !ok {
		return nil, errors.New("path must be an array of points")
	}
	points := make([]ProviderPoint, len(items))
	for index, item := range items {
		point, ok := item.(map[string]any)
		if !ok {
			return nil, fmt.Errorf("path[%d] must be an object", index)
		}
		for _, key := range contract.SortedKeys(point) {
			number, err := desktopNumber(fmt.Sprintf("path[%d].%s", index, key), point[key])
			if err != nil {
				return nil, err
			}
			switch key {
			case "x":
				points[index].X = *number
			case "y":
				points[index].Y = *number
			default:
				return nil, fmt.Errorf("path[%d]: unknown field %q", index, key)
			}
		}
	}
	return points, nil
}

func ProviderMutationCanInvalidate(facts *ProviderMutationFacts) bool {
	return facts != nil && (facts.InputEventsEmitted ||
		facts.PartialInputPossible ||
		facts.DesktopStateMutated ||
		facts.MutationUnknown)
}
