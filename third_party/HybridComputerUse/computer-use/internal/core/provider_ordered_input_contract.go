package core

import (
	"errors"
	"fmt"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

const ProviderOrderedInputContractVersion = 1
const providerOrderedInputMaxKeyLength = contract.GUIMaxKeys*contract.GUIMaxKeyLength + contract.GUIMaxKeys - 1

type ProviderOrderedInputStepStatus string

const (
	ProviderOrderedInputCompleted    ProviderOrderedInputStepStatus = "completed"
	ProviderOrderedInputFailed       ProviderOrderedInputStepStatus = "failed"
	ProviderOrderedInputNotAttempted ProviderOrderedInputStepStatus = "not_attempted"
)

type ProviderOrderedInputEmission string

const (
	ProviderOrderedInputEmissionNone     ProviderOrderedInputEmission = "none"
	ProviderOrderedInputEmissionComplete ProviderOrderedInputEmission = "complete"
	ProviderOrderedInputEmissionPartial  ProviderOrderedInputEmission = "partial"
)

type ProviderOrderedInputWindowContinuity string

const (
	ProviderOrderedInputWindowUnchecked ProviderOrderedInputWindowContinuity = ""
	ProviderOrderedInputWindowSame      ProviderOrderedInputWindowContinuity = "same"
	ProviderOrderedInputWindowChanged   ProviderOrderedInputWindowContinuity = "changed"
	ProviderOrderedInputWindowUnknown   ProviderOrderedInputWindowContinuity = "unknown"
)

type ProviderOrderedInputStep struct {
	ActionIndex int    `json:"action_index"`
	Key         string `json:"key"`
}

type ProviderOrderedInputRequest struct {
	ContractVersion  int                        `json:"contract_version"`
	App              string                     `json:"app"`
	AppIdentity      *ProviderAppIdentity       `json:"app_identity,omitempty"`
	WindowHandle     string                     `json:"window_handle"`
	ExpectedWindow   *ProviderWindowExpectation `json:"expected_window"`
	Steps            []ProviderOrderedInputStep `json:"steps"`
	FinalObservation ProviderObservationOptions `json:"final_observation"`
	Diagnostics      *ProviderDiagnosticContext `json:"diagnostics,omitempty"`
}

type ProviderOrderedInputWireRequest struct {
	ProtocolVersion int `json:"protocol_version"`
	ProviderOrderedInputRequest
}

type ProviderOrderedInputStepResult struct {
	ActionIndex       int                                  `json:"action_index"`
	Status            ProviderOrderedInputStepStatus       `json:"status"`
	DispatchAttempted bool                                 `json:"dispatch_attempted"`
	Emission          ProviderOrderedInputEmission         `json:"emission"`
	WindowContinuity  ProviderOrderedInputWindowContinuity `json:"window_continuity,omitempty"`
}

type ProviderOrderedInputResult struct {
	ContractVersion             int                              `json:"contract_version"`
	OK                          bool                             `json:"ok"`
	Steps                       []ProviderOrderedInputStepResult `json:"steps"`
	StoppedActionIndex          *int                             `json:"stopped_action_index,omitempty"`
	FirstUnattemptedActionIndex *int                             `json:"first_unattempted_action_index,omitempty"`
	Snapshot                    *ProviderSnapshot                `json:"snapshot,omitempty"`
	Failure                     *ProviderFailure                 `json:"failure,omitempty"`
	Mutation                    ProviderMutationFacts            `json:"mutation"`
	RetrySameRequest            bool                             `json:"retry_same_request"`
}

// ProviderOrderedInputBackend is optional until a native provider implements
// the complete ordered-input contract. PlatformBackend deliberately does not
// embed it, so existing providers stay on the current single-action path.
type ProviderOrderedInputBackend interface {
	ExecuteOrderedInput(
		ProviderOrderedInputRequest,
	) (ProviderOrderedInputResult, error)
}

func orderedInputBackend(
	backend PlatformBackend,
) (ProviderOrderedInputBackend, bool) {
	ordered, ok := backend.(ProviderOrderedInputBackend)
	return ordered, ok
}

func providerOrderedInputRequestsFromPlan(
	context *AXBatchContext,
	plan axBatchExecutionPlan,
	actions []axBatchAction,
) ([]ProviderOrderedInputRequest, error) {
	if context == nil ||
		strings.TrimSpace(context.AppToken) == "" ||
		strings.TrimSpace(context.WindowHandle) == "" {
		return nil, errors.New(
			"ordered-input request compilation requires a bound batch context",
		)
	}
	if context.expectedWindow == nil {
		// No private provider identity means the backend cannot prove the
		// per-action window invariant. The caller must keep the current path.
		return nil, nil
	}
	if plan.windowHandle != context.WindowHandle ||
		plan.windowRef != context.WindowRef ||
		len(plan.steps) != len(actions) {
		return nil, errors.New(
			"ordered-input plan does not match its bound batch context",
		)
	}

	expectedWindow := *context.expectedWindow
	requests := make([]ProviderOrderedInputRequest, 0)
	activeRun := 0
	for position, step := range plan.steps {
		if step.actionIndex != position ||
			step.actionIndex < 0 ||
			step.actionIndex >= len(actions) {
			return nil, errors.New(
				"ordered-input plan action indices do not match the normalized batch",
			)
		}
		if step.executionClass != axPlanOrderedInput {
			activeRun = 0
			continue
		}
		action := actions[step.actionIndex]
		if !axPlanOrderedInputEligible(action, step.target) ||
			step.orderedInputRun <= 0 {
			return nil, fmt.Errorf(
				"ordered-input plan step %d is not an eligible untargeted keypress",
				step.actionIndex,
			)
		}
		if activeRun != step.orderedInputRun {
			capture := true
			requests = append(requests, ProviderOrderedInputRequest{
				ContractVersion: ProviderOrderedInputContractVersion,
				App:             context.AppToken,
				AppIdentity: &ProviderAppIdentity{
					ProviderBusName:   expectedWindow.ProviderBusName,
					ProcessGeneration: expectedWindow.ProcessGeneration,
				},
				WindowHandle:   context.WindowHandle,
				ExpectedWindow: &expectedWindow,
				FinalObservation: ProviderObservationOptions{
					CaptureSnapshot: &capture,
				},
			})
			activeRun = step.orderedInputRun
		}
		request := &requests[len(requests)-1]
		request.Steps = append(request.Steps, ProviderOrderedInputStep{
			ActionIndex: step.actionIndex,
			Key:         action.key,
		})
	}
	for index, request := range requests {
		if err := request.Validate(); err != nil {
			return nil, fmt.Errorf(
				"ordered-input run %d: %w",
				index,
				err,
			)
		}
	}
	return requests, nil
}

func (request ProviderOrderedInputRequest) Validate() error {
	if request.ContractVersion != ProviderOrderedInputContractVersion {
		return fmt.Errorf(
			"ordered-input contract version = %d, want %d",
			request.ContractVersion,
			ProviderOrderedInputContractVersion,
		)
	}
	if strings.TrimSpace(request.App) == "" {
		return errors.New("ordered-input app is required")
	}
	if request.AppIdentity == nil ||
		strings.TrimSpace(request.AppIdentity.ProviderBusName) == "" ||
		strings.TrimSpace(request.AppIdentity.ProcessGeneration) == "" {
		return errors.New("ordered-input requires exact app_identity")
	}
	if request.ExpectedWindow == nil {
		return errors.New("ordered-input requires an exact expected_window")
	}
	if request.AppIdentity.ProviderBusName !=
		request.ExpectedWindow.ProviderBusName ||
		request.AppIdentity.ProcessGeneration !=
			request.ExpectedWindow.ProcessGeneration {
		return errors.New(
			"ordered-input app_identity must match expected_window ownership",
		)
	}
	if err := validateProviderWindowExpectation(
		request.WindowHandle,
		request.ExpectedWindow,
	); err != nil {
		return err
	}
	if len(request.Steps) == 0 ||
		len(request.Steps) > contract.AXBatchMaxActions {
		return fmt.Errorf(
			"ordered-input steps must contain between 1 and %d actions",
			contract.AXBatchMaxActions,
		)
	}
	for index, step := range request.Steps {
		if step.ActionIndex < 0 {
			return fmt.Errorf(
				"ordered-input steps[%d].action_index must be non-negative",
				index,
			)
		}
		if index > 0 &&
			step.ActionIndex != request.Steps[index-1].ActionIndex+1 {
			return errors.New(
				"ordered-input action indices must be strictly contiguous",
			)
		}
		if strings.TrimSpace(step.Key) == "" {
			return fmt.Errorf(
				"ordered-input steps[%d].key is required",
				index,
			)
		}
		if len([]rune(step.Key)) > providerOrderedInputMaxKeyLength {
			return fmt.Errorf(
				"ordered-input steps[%d].key is too long",
				index,
			)
		}
	}
	if err := validateProviderObservationOptions(
		request.FinalObservation,
	); err != nil {
		return err
	}
	if request.FinalObservation.CaptureSnapshot == nil ||
		!*request.FinalObservation.CaptureSnapshot {
		return errors.New(
			"ordered-input requires a final canonical snapshot boundary",
		)
	}
	return nil
}

func validProviderOrderedInputStatus(
	status ProviderOrderedInputStepStatus,
) bool {
	switch status {
	case ProviderOrderedInputCompleted,
		ProviderOrderedInputFailed,
		ProviderOrderedInputNotAttempted:
		return true
	default:
		return false
	}
}

func validProviderOrderedInputEmission(
	emission ProviderOrderedInputEmission,
) bool {
	switch emission {
	case ProviderOrderedInputEmissionNone,
		ProviderOrderedInputEmissionComplete,
		ProviderOrderedInputEmissionPartial:
		return true
	default:
		return false
	}
}

func validProviderOrderedInputContinuity(
	continuity ProviderOrderedInputWindowContinuity,
) bool {
	switch continuity {
	case ProviderOrderedInputWindowUnchecked,
		ProviderOrderedInputWindowSame,
		ProviderOrderedInputWindowChanged,
		ProviderOrderedInputWindowUnknown:
		return true
	default:
		return false
	}
}

func orderedInputFirstUnattempted(
	steps []ProviderOrderedInputStepResult,
) *int {
	for _, step := range steps {
		if !step.DispatchAttempted {
			index := step.ActionIndex
			return &index
		}
	}
	return nil
}

func equalOptionalActionIndex(left, right *int) bool {
	if left == nil || right == nil {
		return left == nil && right == nil
	}
	return *left == *right
}

func providerOrderedInputRetryableFailure(failure *ProviderFailure) bool {
	if failure == nil ||
		!failure.FactsComplete ||
		failure.Phase != "precondition" {
		return false
	}
	switch failure.Code {
	case "provider_busy", "lease_unavailable":
		return true
	default:
		return false
	}
}

// ValidateFor checks that a provider's ordered-input result is a coherent
// answer to request: the steps mirror the request, at most one step failed
// and nothing ran after it, the aggregate mutation facts match the steps, and
// the terminal fields fit a success, a final observation failure, or a stop.
func (result ProviderOrderedInputResult) ValidateFor(
	request ProviderOrderedInputRequest,
) error {
	if err := result.validateShapeFor(request); err != nil {
		return err
	}
	steps, err := result.validateSteps(request)
	if err != nil {
		return err
	}
	if err := result.validateAggregate(steps); err != nil {
		return err
	}
	if steps.failedPosition < 0 {
		return result.validateUnstoppedTerminal(request, steps.anyEmission)
	}
	return result.validateStoppedTerminal(steps)
}

// orderedInputStepSummary is what the per-step pass learns about the run as
// a whole. failedPosition is -1 when no step failed.
type orderedInputStepSummary struct {
	failedPosition int
	anyEmission    bool
	anyPartial     bool
}

func (result ProviderOrderedInputResult) validateShapeFor(
	request ProviderOrderedInputRequest,
) error {
	if err := request.Validate(); err != nil {
		return fmt.Errorf("ordered-input request: %w", err)
	}
	if result.ContractVersion != request.ContractVersion {
		return fmt.Errorf(
			"ordered-input result contract version = %d, want %d",
			result.ContractVersion,
			request.ContractVersion,
		)
	}
	if len(result.Steps) != len(request.Steps) {
		return fmt.Errorf(
			"ordered-input result has %d steps, want %d",
			len(result.Steps),
			len(request.Steps),
		)
	}
	return nil
}

func (result ProviderOrderedInputResult) validateSteps(
	request ProviderOrderedInputRequest,
) (orderedInputStepSummary, error) {
	summary := orderedInputStepSummary{failedPosition: -1}
	for position, step := range result.Steps {
		if err := validateOrderedInputStepFields(
			position, step, request.Steps[position],
		); err != nil {
			return summary, err
		}
		if summary.failedPosition >= 0 &&
			step.Status != ProviderOrderedInputNotAttempted {
			return summary, errors.New(
				"ordered-input result continued after the first failed action",
			)
		}
		switch step.Status {
		case ProviderOrderedInputCompleted:
			if !step.DispatchAttempted ||
				step.Emission != ProviderOrderedInputEmissionComplete ||
				step.WindowContinuity != ProviderOrderedInputWindowSame {
				return summary, fmt.Errorf(
					"ordered-input completed step %d requires same-window dispatch and complete emission",
					position,
				)
			}
		case ProviderOrderedInputFailed:
			if summary.failedPosition >= 0 {
				return summary, errors.New(
					"ordered-input result contains multiple failed actions",
				)
			}
			summary.failedPosition = position
			if err := validateOrderedInputFailedStep(position, step); err != nil {
				return summary, err
			}
		case ProviderOrderedInputNotAttempted:
			if summary.failedPosition < 0 {
				return summary, errors.New(
					"ordered-input result has an unattempted step before any failure",
				)
			}
			if step.DispatchAttempted ||
				step.Emission != ProviderOrderedInputEmissionNone ||
				step.WindowContinuity != ProviderOrderedInputWindowUnchecked {
				return summary, fmt.Errorf(
					"ordered-input unattempted step %d contains execution evidence",
					position,
				)
			}
		}
		if step.Emission == ProviderOrderedInputEmissionComplete ||
			step.Emission == ProviderOrderedInputEmissionPartial {
			summary.anyEmission = true
		}
		if step.Emission == ProviderOrderedInputEmissionPartial {
			summary.anyPartial = true
		}
	}
	return summary, nil
}

// validateOrderedInputStepFields checks one step on its own: it answers the
// request step at the same position and every enum holds a known value.
func validateOrderedInputStepFields(
	position int,
	step ProviderOrderedInputStepResult,
	requestStep ProviderOrderedInputStep,
) error {
	if step.ActionIndex != requestStep.ActionIndex {
		return fmt.Errorf(
			"ordered-input result step %d has action_index %d, want %d",
			position,
			step.ActionIndex,
			requestStep.ActionIndex,
		)
	}
	if !validProviderOrderedInputStatus(step.Status) {
		return fmt.Errorf(
			"ordered-input result step %d has invalid status %q",
			position,
			step.Status,
		)
	}
	if !validProviderOrderedInputEmission(step.Emission) {
		return fmt.Errorf(
			"ordered-input result step %d has invalid emission %q",
			position,
			step.Emission,
		)
	}
	if !validProviderOrderedInputContinuity(step.WindowContinuity) {
		return fmt.Errorf(
			"ordered-input result step %d has invalid window continuity %q",
			position,
			step.WindowContinuity,
		)
	}
	return nil
}

// validateOrderedInputFailedStep checks that a failed step emits input only
// after it dispatched into the expected window, and that a step stopped
// before dispatch still reports the window check it ran.
func validateOrderedInputFailedStep(
	position int,
	step ProviderOrderedInputStepResult,
) error {
	if !step.DispatchAttempted &&
		step.Emission != ProviderOrderedInputEmissionNone {
		return fmt.Errorf(
			"ordered-input failed step %d claims emission without dispatch",
			position,
		)
	}
	if step.DispatchAttempted &&
		step.WindowContinuity != ProviderOrderedInputWindowSame {
		return fmt.Errorf(
			"ordered-input failed step %d dispatched without proving the expected window",
			position,
		)
	}
	if !step.DispatchAttempted &&
		step.WindowContinuity == ProviderOrderedInputWindowUnchecked {
		return fmt.Errorf(
			"ordered-input failed step %d lacks pre-dispatch window continuity",
			position,
		)
	}
	if (step.WindowContinuity == ProviderOrderedInputWindowChanged ||
		step.WindowContinuity == ProviderOrderedInputWindowUnknown) &&
		step.Emission != ProviderOrderedInputEmissionNone {
		return fmt.Errorf(
			"ordered-input failed step %d emitted after window continuity was lost",
			position,
		)
	}
	return nil
}

func (result ProviderOrderedInputResult) validateAggregate(
	steps orderedInputStepSummary,
) error {
	if result.Mutation.InputEventsEmitted != steps.anyEmission {
		return errors.New(
			"ordered-input aggregate input emission contradicts step evidence",
		)
	}
	if result.Mutation.PartialInputPossible != steps.anyPartial {
		return errors.New(
			"ordered-input aggregate partial emission contradicts step evidence",
		)
	}

	firstUnattempted := orderedInputFirstUnattempted(result.Steps)
	if !equalOptionalActionIndex(
		result.FirstUnattemptedActionIndex,
		firstUnattempted,
	) {
		return errors.New(
			"ordered-input first_unattempted_action_index contradicts step evidence",
		)
	}
	return nil
}

// validateUnstoppedTerminal checks a run in which every step completed. It
// either succeeded with a final snapshot of the expected window, or only its
// final observation failed.
func (result ProviderOrderedInputResult) validateUnstoppedTerminal(
	request ProviderOrderedInputRequest,
	anyEmission bool,
) error {
	if !result.OK {
		if result.Failure == nil ||
			result.Failure.Phase != "observation" ||
			!result.Failure.FactsComplete {
			return errors.New(
				"ordered-input result without a failed step requires a complete observation failure",
			)
		}
		if result.StoppedActionIndex != nil ||
			result.FirstUnattemptedActionIndex != nil ||
			result.Snapshot != nil ||
			result.RetrySameRequest {
			return errors.New(
				"ordered-input final observation failure contains invalid stop, snapshot, or retry evidence",
			)
		}
		if anyEmission && !result.Mutation.MutationUnknown {
			return errors.New(
				"ordered-input final observation failure after emission must preserve mutation_unknown",
			)
		}
		return nil
	}
	if result.Failure != nil ||
		result.StoppedActionIndex != nil ||
		result.FirstUnattemptedActionIndex != nil {
		return errors.New(
			"ordered-input successful result contains failure or stop evidence",
		)
	}
	if result.Snapshot == nil ||
		result.Snapshot.WindowAbsent ||
		result.Snapshot.WindowHandle != request.WindowHandle {
		return errors.New(
			"ordered-input success requires the final canonical snapshot for the expected window",
		)
	}
	if result.RetrySameRequest {
		return errors.New(
			"ordered-input successful result cannot retry the same request",
		)
	}
	return nil
}

// validateStoppedTerminal checks a run that stopped at its failed step. A
// same-request retry is allowed only when nothing was emitted and the stop
// was a transient failure before dispatch into the expected window.
func (result ProviderOrderedInputResult) validateStoppedTerminal(
	steps orderedInputStepSummary,
) error {
	if result.OK {
		return errors.New(
			"ordered-input result with a failed step cannot be successful",
		)
	}
	failedStep := result.Steps[steps.failedPosition]
	if result.Failure == nil {
		return errors.New(
			"ordered-input failed result requires typed failure evidence",
		)
	}
	if strings.TrimSpace(result.Failure.Code) == "" ||
		strings.TrimSpace(result.Failure.Phase) == "" {
		return errors.New(
			"ordered-input failed result requires failure code and phase",
		)
	}
	if result.StoppedActionIndex == nil ||
		*result.StoppedActionIndex != failedStep.ActionIndex {
		return errors.New(
			"ordered-input stopped_action_index must identify the failed action",
		)
	}
	if steps.anyEmission && !result.Mutation.MutationUnknown {
		return errors.New(
			"ordered-input failure after emission must preserve mutation_unknown",
		)
	}
	if result.RetrySameRequest {
		if steps.anyEmission ||
			failedStep.DispatchAttempted ||
			failedStep.WindowContinuity != ProviderOrderedInputWindowSame ||
			!providerOrderedInputRetryableFailure(result.Failure) {
			return errors.New(
				"ordered-input same-request retry requires a complete transient same-window pre-dispatch failure with no emission",
			)
		}
	}
	return nil
}
