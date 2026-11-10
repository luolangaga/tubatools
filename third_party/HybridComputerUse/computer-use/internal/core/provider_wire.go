package core

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

const ProviderProtocolName = "computer-use-provider"

type ProviderWireOperation string

const (
	ProviderWireObserveApps   ProviderWireOperation = "observe_apps"
	ProviderWireObserve       ProviderWireOperation = "observe"
	ProviderWireResolveAndAct ProviderWireOperation = "resolve_and_act"
	ProviderWireOrderedInput  ProviderWireOperation = "ordered_input"
	ProviderWireDesktopBatch  ProviderWireOperation = "desktop_execute_batch"
	ProviderWireWindowList    ProviderWireOperation = "window_list"
	ProviderWireWindowAction  ProviderWireOperation = "window_action"
)

type ProviderWireRequest struct {
	Protocol      string                           `json:"protocol"`
	SchemaVersion int                              `json:"schema_version"`
	RequestID     string                           `json:"request_id"`
	Operation     ProviderWireOperation            `json:"operation"`
	ObserveApps   *ProviderObserveAppsRequest      `json:"observe_apps,omitempty"`
	Observe       *ProviderObserveRequest          `json:"observe,omitempty"`
	Action        *ProviderActionRequest           `json:"action,omitempty"`
	OrderedInput  *ProviderOrderedInputWireRequest `json:"ordered_input,omitempty"`
	Desktop       *ProviderDesktopRequest          `json:"desktop,omitempty"`
	WindowList    *ProviderWindowListRequest       `json:"window_list,omitempty"`
	WindowAction  *ProviderWindowActionRequest     `json:"window_action,omitempty"`
}

type ProviderWireResponse struct {
	Protocol              string                       `json:"protocol"`
	SchemaVersion         int                          `json:"schema_version"`
	RequestID             string                       `json:"request_id"`
	Operation             ProviderWireOperation        `json:"operation"`
	ProviderResetRequired bool                         `json:"provider_reset_required,omitempty"`
	Accessibility         *ProviderAccessibilityResult `json:"accessibility,omitempty"`
	OrderedInput          *ProviderOrderedInputResult  `json:"ordered_input,omitempty"`
	Desktop               *ProviderDesktopResult       `json:"desktop,omitempty"`
	Window                *ProviderWindowResult        `json:"window,omitempty"`
	RequestRejection      *ProviderRequestRejection    `json:"request_rejection,omitempty"`
}

func NewProviderWireRequest(
	requestID string,
	operation ProviderWireOperation,
) ProviderWireRequest {
	return ProviderWireRequest{
		Protocol:      ProviderProtocolName,
		SchemaVersion: ProviderProtocolVersion,
		RequestID:     requestID,
		Operation:     operation,
	}
}

func DecodeProviderWireJSON(data []byte, target any) error {
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(target); err != nil {
		return err
	}
	var trailing any
	if err := decoder.Decode(&trailing); err != io.EOF {
		if err == nil {
			return errors.New("provider wire contains trailing JSON")
		}
		return err
	}
	return nil
}

func DecodeProviderWireResponse(data []byte) (ProviderWireResponse, error) {
	var response ProviderWireResponse
	if err := DecodeProviderWireJSON(data, &response); err != nil {
		return ProviderWireResponse{}, err
	}
	if err := response.Validate(); err != nil {
		return ProviderWireResponse{}, err
	}
	if err := validateProviderPreparationWire(data); err != nil {
		return ProviderWireResponse{}, err
	}
	return response, nil
}

func (request ProviderWireRequest) Validate() error {
	if request.Protocol != ProviderProtocolName {
		return fmt.Errorf("provider protocol = %q", request.Protocol)
	}
	if request.SchemaVersion != ProviderProtocolVersion {
		return fmt.Errorf(
			"provider schema version = %d, want %d",
			request.SchemaVersion,
			ProviderProtocolVersion,
		)
	}
	if request.RequestID == "" {
		return errors.New("provider request_id is required")
	}
	payloads := map[ProviderWireOperation]bool{
		ProviderWireObserveApps:   request.ObserveApps != nil,
		ProviderWireObserve:       request.Observe != nil,
		ProviderWireResolveAndAct: request.Action != nil,
		ProviderWireOrderedInput:  request.OrderedInput != nil,
		ProviderWireDesktopBatch:  request.Desktop != nil,
		ProviderWireWindowList:    request.WindowList != nil,
		ProviderWireWindowAction:  request.WindowAction != nil,
	}
	active := 0
	for _, present := range payloads {
		if present {
			active++
		}
	}
	if active != 1 {
		return fmt.Errorf(
			"provider request must contain exactly one payload, got %d",
			active,
		)
	}
	if !payloads[request.Operation] {
		return fmt.Errorf(
			"provider operation %q does not match its payload",
			request.Operation,
		)
	}
	var payloadVersion int
	switch request.Operation {
	case ProviderWireObserveApps:
		payloadVersion = request.ObserveApps.ProtocolVersion
	case ProviderWireObserve:
		payloadVersion = request.Observe.ProtocolVersion
	case ProviderWireResolveAndAct:
		payloadVersion = request.Action.ProtocolVersion
	case ProviderWireOrderedInput:
		payloadVersion = request.OrderedInput.ProtocolVersion
	case ProviderWireDesktopBatch:
		payloadVersion = request.Desktop.ProtocolVersion
	case ProviderWireWindowList:
		payloadVersion = request.WindowList.ProtocolVersion
	case ProviderWireWindowAction:
		payloadVersion = request.WindowAction.ProtocolVersion
	default:
		return fmt.Errorf("unknown provider request operation %q", request.Operation)
	}
	if payloadVersion != ProviderProtocolVersion {
		return fmt.Errorf(
			"provider payload protocol version = %d, want %d",
			payloadVersion,
			ProviderProtocolVersion,
		)
	}
	switch request.Operation {
	case ProviderWireObserveApps:
		return ValidateProviderObserveAppsRequest(*request.ObserveApps)
	case ProviderWireObserve:
		return ValidateProviderObserveRequest(*request.Observe)
	case ProviderWireResolveAndAct:
		return ValidateProviderActionRequest(*request.Action)
	case ProviderWireOrderedInput:
		return request.OrderedInput.ProviderOrderedInputRequest.Validate()
	case ProviderWireDesktopBatch:
		return validateProviderDesktopRequest(*request.Desktop)
	case ProviderWireWindowList:
		return ValidateProviderWindowListRequest(*request.WindowList)
	case ProviderWireWindowAction:
		return ValidateProviderWindowActionRequest(*request.WindowAction)
	default:
		return fmt.Errorf("unknown provider request operation %q", request.Operation)
	}
}

func (response ProviderWireResponse) Validate() error {
	if response.Protocol != ProviderProtocolName {
		return fmt.Errorf("provider protocol = %q", response.Protocol)
	}
	if response.SchemaVersion != ProviderProtocolVersion {
		return fmt.Errorf(
			"provider schema version = %d, want %d",
			response.SchemaVersion,
			ProviderProtocolVersion,
		)
	}
	if response.RequestID == "" {
		return errors.New("provider response request_id is required")
	}
	if err := validateProviderPreparationEnvelope(response); err != nil {
		return err
	}
	active := 0
	if response.Accessibility != nil {
		active++
	}
	if response.OrderedInput != nil {
		active++
	}
	if response.Desktop != nil {
		active++
	}
	if response.Window != nil {
		active++
	}
	if response.RequestRejection != nil {
		if active != 0 || response.ProviderResetRequired {
			return errors.New("provider request rejection cannot accompany domain results or reset")
		}
		switch response.Operation {
		case ProviderWireObserveApps, ProviderWireObserve, ProviderWireResolveAndAct,
			ProviderWireOrderedInput, ProviderWireDesktopBatch,
			ProviderWireWindowList, ProviderWireWindowAction:
			return response.RequestRejection.Validate()
		default:
			return fmt.Errorf("unknown provider rejection operation %q", response.Operation)
		}
	}
	if active != 1 {
		return fmt.Errorf(
			"provider response must contain exactly one payload, got %d",
			active,
		)
	}
	switch response.Operation {
	case ProviderWireObserveApps, ProviderWireObserve, ProviderWireResolveAndAct:
		if response.Accessibility == nil {
			return errors.New("provider accessibility response payload is required")
		}
		if err := validateProviderRecoveryEnvelope(
			response.Operation,
			response.Accessibility.OK,
			response.Accessibility.Failure,
			response.Accessibility.Mutation,
			response.Accessibility.RecoveryContext,
		); err != nil {
			return err
		}
	case ProviderWireOrderedInput:
		if response.OrderedInput == nil {
			return errors.New("provider ordered-input response payload is required")
		}
	case ProviderWireDesktopBatch:
		if response.Desktop == nil {
			return errors.New("provider desktop response payload is required")
		}
	case ProviderWireWindowList, ProviderWireWindowAction:
		if response.Window == nil {
			return errors.New("provider window response payload is required")
		}
		if err := validateProviderRecoveryEnvelope(
			response.Operation,
			response.Window.OK,
			response.Window.Failure,
			response.Window.Mutation,
			response.Window.RecoveryContext,
		); err != nil {
			return err
		}
	default:
		return fmt.Errorf("unknown provider response operation %q", response.Operation)
	}
	return nil
}

func validateProviderRecoveryEnvelope(
	operation ProviderWireOperation,
	ok bool,
	failure *ProviderFailure,
	mutation *ProviderMutationFacts,
	context *ProviderRecoveryContext,
) error {
	if context == nil {
		return nil
	}
	if ok {
		return errors.New(
			"provider recovery context cannot accompany a successful result",
		)
	}
	if operation == ProviderWireObserve {
		// A provider that cannot read the bound window's lifecycle reports
		// its status as unknown, with incomplete facts. Present and gone
		// still require complete facts.
		unknownWindowFacts := failure != nil && failure.Subject == "window" &&
			context.SourceWindowStatus == "unknown"
		if failure == nil ||
			failure.Code != "stale_window_ref" ||
			failure.Phase != "precondition" ||
			(!failure.FactsComplete && !unknownWindowFacts) {
			return errors.New(
				"observe recovery context requires stale_window_ref precondition facts that are complete or explicitly unknown for the window",
			)
		}
		if mutation != nil &&
			(mutation.InputEventsEmitted ||
				mutation.PartialInputPossible ||
				mutation.DesktopStateMutated ||
				mutation.MutationUnknown) {
			return errors.New(
				"observe recovery context cannot accompany mutation evidence",
			)
		}
		return validateProviderRecoveryContext(context)
	}
	if operation != ProviderWireResolveAndAct &&
		operation != ProviderWireWindowAction {
		return errors.New(
			"provider recovery context is invalid for this operation",
		)
	}
	if failure == nil || failure.Code != "effect_unknown" ||
		failure.Phase != "post_action_observation" {
		return errors.New(
			"provider recovery context requires a post-action effect_unknown failure",
		)
	}
	if mutation == nil || !mutation.InputEventsEmitted ||
		!mutation.MutationUnknown {
		return errors.New(
			"provider recovery context requires emitted input with unknown mutation",
		)
	}
	return validateProviderRecoveryContext(context)
}

func validateProviderRecoveryContext(context *ProviderRecoveryContext) error {
	if context == nil {
		return nil
	}
	validAppStatus := func(status string) bool {
		switch status {
		case "", "alive", "exited", "unknown":
			return true
		default:
			return false
		}
	}
	validWindowStatus := func(status string) bool {
		switch status {
		case "", "present", "gone", "unknown":
			return true
		default:
			return false
		}
	}
	if !validAppStatus(context.SourceAppStatus) {
		return fmt.Errorf(
			"unsupported provider sourceAppStatus %q",
			context.SourceAppStatus,
		)
	}
	if !validWindowStatus(context.SourceWindowStatus) {
		return fmt.Errorf(
			"unsupported provider sourceWindowStatus %q",
			context.SourceWindowStatus,
		)
	}
	if context.CurrentWindow != nil {
		if !context.InventoryComplete {
			return errors.New(
				"provider recovery currentWindow requires a complete inventory",
			)
		}
		if context.SourceAppStatus != "alive" {
			return errors.New(
				"provider recovery currentWindow requires a live source app",
			)
		}
		if !validInternalWindowHandle(context.CurrentWindow.WindowHandle) {
			return errors.New(
				"provider recovery currentWindow requires a valid private window handle",
			)
		}
		if strings.TrimSpace(context.CurrentWindow.ProviderBusName) == "" ||
			strings.TrimSpace(context.CurrentWindow.ProviderObjectPath) == "" ||
			strings.TrimSpace(context.CurrentWindow.ProcessGeneration) == "" {
			return errors.New(
				"provider recovery currentWindow requires complete private identity",
			)
		}
	}
	if context.InventoryComplete && context.SourceAppStatus == "exited" &&
		context.SourceWindowStatus != "gone" {
		return errors.New(
			"provider recovery exited app requires source window status gone",
		)
	}
	return nil
}

func validateProviderProtocolVersion(version int) error {
	if version != ProviderProtocolVersion {
		return fmt.Errorf(
			"provider payload protocol version = %d, want %d",
			version,
			ProviderProtocolVersion,
		)
	}
	return nil
}

func ValidateProviderObserveAppsRequest(request ProviderObserveAppsRequest) error {
	return validateProviderProtocolVersion(request.ProtocolVersion)
}

func ValidateProviderObserveRequest(request ProviderObserveRequest) error {
	if err := validateProviderProtocolVersion(request.ProtocolVersion); err != nil {
		return err
	}
	if strings.TrimSpace(request.App) == "" {
		return errors.New("provider observe app is required")
	}
	return validateProviderObservationOptions(request.Observation)
}

func validateProviderObservationOptions(
	observation ProviderObservationOptions,
) error {
	if observation.TextLimit != nil &&
		!observation.TextLimit.Max &&
		observation.TextLimit.Value <= 0 {
		return errors.New(
			"provider observation text_limit must be a positive integer or \"max\"",
		)
	}
	if observation.MaxTreeNodes < 0 {
		return errors.New("provider observation max_tree_nodes must be non-negative")
	}
	if observation.MaxTreeDepth < 0 {
		return errors.New("provider observation max_tree_depth must be non-negative")
	}
	return nil
}

func validAccessibilityOperation(operation AccessibilityOperation) bool {
	switch operation {
	case AccessibilityResolveTarget,
		AccessibilityClick,
		AccessibilityScroll,
		AccessibilityType,
		AccessibilityKeypress,
		AccessibilitySetValue,
		AccessibilitySelect:
		return true
	default:
		return false
	}
}

func ValidateProviderActionRequest(request ProviderActionRequest) error {
	if request.Target != nil && request.Target.EditTarget != nil {
		identity := request.Target.EditTarget
		if strings.TrimSpace(identity.ProviderBusName) == "" ||
			strings.TrimSpace(identity.ProviderObjectPath) == "" ||
			strings.TrimSpace(identity.ProcessGeneration) == "" || identity.Role != "" || identity.Name != "" {
			return errors.New("provider editTarget requires exactly one complete provider identity")
		}
	}
	if err := validateProviderProtocolVersion(request.ProtocolVersion); err != nil {
		return err
	}
	if !validAccessibilityOperation(request.Intent) {
		return fmt.Errorf("unsupported provider accessibility intent %q", request.Intent)
	}
	if strings.TrimSpace(request.App) == "" {
		return errors.New("provider accessibility app is required")
	}
	if err := validateProviderWindowExpectation(
		request.WindowHandle,
		request.ExpectedWindow,
	); err != nil {
		return err
	}
	if err := validateProviderObservationOptions(request.Observation); err != nil {
		return err
	}
	hasTarget := request.Target != nil || request.Element != nil
	inputCount := 0
	for _, present := range []bool{
		request.Click != nil,
		request.Scroll != nil,
		request.Type != nil,
		request.Keypress != nil,
		request.SetValue != nil,
		request.Select != nil,
	} {
		if present {
			inputCount++
		}
	}
	switch request.Intent {
	case AccessibilityResolveTarget:
		if !hasTarget {
			return errors.New("provider resolve_target requires an exact target")
		}
		if inputCount != 0 {
			return errors.New("provider resolve_target does not accept action input")
		}
	case AccessibilityClick:
		if !hasTarget {
			return errors.New("provider click requires an exact target")
		}
		if request.Click == nil || inputCount != 1 {
			return errors.New("provider click requires click input only")
		}
	case AccessibilityScroll:
		if request.Scroll == nil || inputCount != 1 {
			return errors.New("provider scroll requires scroll input only")
		}
		if strings.TrimSpace(request.Scroll.Direction) == "" {
			return errors.New("provider scroll direction is required")
		}
		if request.Scroll.Pages <= 0 {
			return errors.New("provider scroll pages must be greater than zero")
		}
		if !hasTarget && strings.TrimSpace(request.WindowHandle) == "" {
			return errors.New(
				"provider untargeted scroll requires a logical window",
			)
		}
	case AccessibilityType:
		if request.Type == nil || inputCount != 1 {
			return errors.New("provider type requires type input only")
		}
		if request.Type.Text == "" {
			return errors.New("provider type text is required")
		}
		if !hasTarget && strings.TrimSpace(request.WindowHandle) == "" {
			return errors.New(
				"provider untargeted type requires a logical window",
			)
		}
	case AccessibilityKeypress:
		if request.Keypress == nil || inputCount != 1 {
			return errors.New(
				"provider keypress requires keypress input only",
			)
		}
		if strings.TrimSpace(request.Keypress.Key) == "" {
			return errors.New("provider keypress key is required")
		}
		if !hasTarget && strings.TrimSpace(request.WindowHandle) == "" {
			return errors.New(
				"provider untargeted keypress requires a logical window",
			)
		}
	case AccessibilitySetValue:
		if !hasTarget {
			return errors.New("provider set_value requires an exact target")
		}
		if request.SetValue == nil || inputCount != 1 {
			return errors.New(
				"provider set_value requires set_value input only",
			)
		}
		if request.SetValue.Value == nil {
			return errors.New("provider set_value value is required")
		}
	case AccessibilitySelect:
		if !hasTarget {
			return errors.New("provider select requires an exact target")
		}
		if request.Select == nil || inputCount != 1 {
			return errors.New("provider select requires select input only")
		}
		if request.Select.Text != "" && request.Select.ToTarget != nil {
			return errors.New("provider select accepts text or to_target, not both")
		}
	}
	return nil
}

func validateProviderWindowExpectation(
	windowHandle string,
	expectation *ProviderWindowExpectation,
) error {
	if expectation == nil {
		return nil
	}
	if !validInternalWindowHandle(windowHandle) ||
		expectation.WindowHandle != windowHandle {
		return errors.New(
			"provider expected_window must match the private window_handle",
		)
	}
	if strings.TrimSpace(expectation.ProviderBusName) == "" ||
		strings.TrimSpace(expectation.ProcessGeneration) == "" ||
		strings.TrimSpace(expectation.Role) == "" {
		return errors.New(
			"provider expected_window requires complete private identity",
		)
	}
	if modalWindowRole(expectation.Role) &&
		strings.TrimSpace(expectation.ProviderObjectPath) == "" {
		return errors.New(
			"provider transient expected_window requires a provider object path",
		)
	}
	return nil
}

func validateProviderDesktopRequest(request ProviderDesktopRequest) error {
	if err := validateProviderProtocolVersion(request.ProtocolVersion); err != nil {
		return err
	}
	if len(request.Actions) == 0 {
		return errors.New("provider desktop actions are required")
	}
	if request.SettleMS < 0 || request.SettleMS > contract.GUIMaxSettleMS {
		return fmt.Errorf(
			"provider desktop settle_ms must be between 0 and %d",
			contract.GUIMaxSettleMS,
		)
	}
	for index, action := range request.Actions {
		if err := validateProviderDesktopAction(action); err != nil {
			return fmt.Errorf("provider desktop action[%d]: %w", index, err)
		}
	}
	return nil
}

func validateProviderDesktopAction(action ProviderDesktopAction) error {
	hasPoint := action.X != nil || action.Y != nil
	hasInput := action.Text != "" || len(action.Keys) > 0 ||
		len(action.Path) > 0 || action.ScrollX != nil || action.ScrollY != nil
	if action.Type != "zoom" && len(action.Region) > 0 {
		return fmt.Errorf("%s does not accept region", action.Type)
	}
	switch action.Type {
	case "zoom":
		if len(action.Region) != 4 {
			return errors.New("zoom requires region [x0, y0, x1, y1]")
		}
		if hasPoint || action.Button != "" || hasInput {
			return errors.New("zoom accepts only region")
		}
	case "screenshot":
		if hasPoint || action.Button != "" || hasInput {
			return errors.New("screenshot does not accept action fields")
		}
	case "click":
		if action.X == nil || action.Y == nil {
			return errors.New("click requires x and y")
		}
		if action.Button != "" &&
			action.Button != "left" &&
			action.Button != "right" &&
			action.Button != "wheel" &&
			action.Button != "back" &&
			action.Button != "forward" {
			return fmt.Errorf("click button %q is unsupported", action.Button)
		}
		if action.Text != "" || len(action.Path) > 0 ||
			action.ScrollX != nil || action.ScrollY != nil {
			return errors.New("click contains fields for another action")
		}
	case "double_click", "move":
		if action.X == nil || action.Y == nil {
			return fmt.Errorf("%s requires x and y", action.Type)
		}
		if action.Button != "" || action.Text != "" || len(action.Path) > 0 ||
			action.ScrollX != nil || action.ScrollY != nil {
			return fmt.Errorf("%s contains fields for another action", action.Type)
		}
	case "drag":
		if len(action.Path) < 2 {
			return errors.New("drag requires at least two path points")
		}
		if hasPoint || action.Button != "" || action.Text != "" ||
			action.ScrollX != nil || action.ScrollY != nil {
			return errors.New("drag contains fields for another action")
		}
	case "scroll":
		if action.X == nil || action.Y == nil {
			return errors.New("scroll requires x and y")
		}
		if (action.ScrollX == nil || *action.ScrollX == 0) &&
			(action.ScrollY == nil || *action.ScrollY == 0) {
			return errors.New("scroll requires a non-zero scroll_x or scroll_y")
		}
		if action.Button != "" || action.Text != "" || len(action.Path) > 0 {
			return errors.New("scroll contains fields for another action")
		}
	case "type":
		if action.Text == "" {
			return errors.New("type requires text")
		}
		if hasPoint || action.Button != "" || len(action.Keys) > 0 ||
			len(action.Path) > 0 || action.ScrollX != nil || action.ScrollY != nil {
			return errors.New("type contains fields for another action")
		}
	case "keypress":
		if len(action.Keys) == 0 {
			return errors.New("keypress requires keys")
		}
		for _, key := range action.Keys {
			if strings.TrimSpace(key) == "" {
				return errors.New("keypress keys must be non-empty strings")
			}
		}
		if hasPoint || action.Button != "" || action.Text != "" ||
			len(action.Path) > 0 || action.ScrollX != nil || action.ScrollY != nil {
			return errors.New("keypress contains fields for another action")
		}
	case "wait":
		if hasPoint || action.Button != "" || hasInput {
			return errors.New("wait does not accept action fields")
		}
	default:
		return fmt.Errorf("unsupported action type %q", action.Type)
	}
	return nil
}

func ValidateProviderWindowListRequest(request ProviderWindowListRequest) error {
	if err := validateProviderProtocolVersion(request.ProtocolVersion); err != nil {
		return err
	}
	if strings.TrimSpace(request.App) == "" {
		return errors.New("provider window-list app is required")
	}
	return nil
}

func ValidateProviderWindowActionRequest(
	request ProviderWindowActionRequest,
) error {
	if err := validateProviderProtocolVersion(request.ProtocolVersion); err != nil {
		return err
	}
	if request.Action != WindowActivate && request.Action != WindowClose {
		return fmt.Errorf("unsupported provider window action %q", request.Action)
	}
	if strings.TrimSpace(request.App) == "" {
		return errors.New("provider window-action app is required")
	}
	if request.Target == nil {
		return errors.New("provider window action requires an exact target")
	}
	return validateProviderObservationOptions(request.Observation)
}
