package core

import (
	"errors"
	"fmt"
	"strings"
)

type axPlanExecutionClass string

const (
	axPlanSingleAction axPlanExecutionClass = "single_action"
	axPlanOrderedInput axPlanExecutionClass = "ordered_window_input"
)

type axPlanConsistency string

const (
	axPlanWindowIdentity  axPlanConsistency = "window_identity"
	axPlanCanonicalWindow axPlanConsistency = "canonical_window"
)

type axPlanTargetRequirement string

const (
	axPlanNoTarget      axPlanTargetRequirement = "none"
	axPlanElementTarget axPlanTargetRequirement = "element_ref"
	axPlanWindowTarget  axPlanTargetRequirement = "window_ref"
)

type axPlanBoundary string

const (
	axPlanNoBoundary          axPlanBoundary = "none"
	axPlanCanonicalBoundary   axPlanBoundary = "canonical_action"
	axPlanTargetedBoundary    axPlanBoundary = "targeted_action"
	axPlanWaitBoundary        axPlanBoundary = "wait"
	axPlanWindowBoundary      axPlanBoundary = "window_transition"
	axPlanNextActionBoundary  axPlanBoundary = "next_action_requires_canonical"
	axPlanTransactionBoundary axPlanBoundary = "transaction_end"
)

type axPlannedStep struct {
	actionIndex     int
	actionType      string
	executionClass  axPlanExecutionClass
	target          axPlanTargetRequirement
	preConsistency  axPlanConsistency
	postConsistency axPlanConsistency
	boundary        axPlanBoundary
	orderedInputRun int
}

type axBatchExecutionPlan struct {
	windowRef             string
	windowHandle          string
	initialConsistency    axPlanConsistency
	requiresMutationLease bool
	finalObservation      string
	steps                 []axPlannedStep
}

func axPlanTargetForAction(action axBatchAction) (
	axPlanTargetRequirement,
	error,
) {
	target := strings.TrimSpace(action.targetRef)
	switch {
	case target == "":
		return axPlanNoTarget, nil
	case isShortAXRef(target):
		return axPlanElementTarget, nil
	case isShortAXWindowRef(target):
		return axPlanWindowTarget, nil
	default:
		return "", fmt.Errorf("target %q is not a current rN or wN ref", target)
	}
}

func axPlanDefaultBoundary(
	action axBatchAction,
	target axPlanTargetRequirement,
) axPlanBoundary {
	if action.actionType == "activate" || action.actionType == "close" {
		return axPlanWindowBoundary
	}
	if action.actionType == "wait" {
		return axPlanWaitBoundary
	}
	if target != axPlanNoTarget {
		return axPlanTargetedBoundary
	}
	return axPlanCanonicalBoundary
}

func axPlanOrderedInputEligible(
	action axBatchAction,
	target axPlanTargetRequirement,
) bool {
	return action.actionType == "keypress" &&
		target == axPlanNoTarget
}

// compileAXBatchExecutionPlan runs only after public normalization, ownership
// binding, and wait preflight. It describes consistency boundaries;
// providers do not consume it until their batch contract can preserve the same
// per-action stop and emission evidence as the current path.
func compileAXBatchExecutionPlan(
	context *AXBatchContext,
	actions []axBatchAction,
	final observation,
) (axBatchExecutionPlan, error) {
	if context == nil ||
		strings.TrimSpace(context.WindowRef) == "" ||
		strings.TrimSpace(context.WindowHandle) == "" {
		return axBatchExecutionPlan{}, errors.New(
			"AX execution plans require one bound logical window",
		)
	}
	if len(actions) == 0 {
		return axBatchExecutionPlan{}, errors.New(
			"AX execution plans require at least one action",
		)
	}

	plan := axBatchExecutionPlan{
		windowRef:             context.WindowRef,
		windowHandle:          context.WindowHandle,
		initialConsistency:    axPlanCanonicalWindow,
		requiresMutationLease: true,
		finalObservation:      final.resolvedMode(),
		steps:                 make([]axPlannedStep, 0, len(actions)),
	}

	nextRun := 0
	activeRun := 0
	for index, action := range actions {
		target, err := axPlanTargetForAction(action)
		if err != nil {
			return axBatchExecutionPlan{}, fmt.Errorf(
				"actions[%d]: %w", index, err,
			)
		}
		step := axPlannedStep{
			actionIndex:     index,
			actionType:      action.actionType,
			executionClass:  axPlanSingleAction,
			target:          target,
			preConsistency:  axPlanCanonicalWindow,
			postConsistency: axPlanCanonicalWindow,
			boundary:        axPlanDefaultBoundary(action, target),
		}
		if axPlanOrderedInputEligible(action, target) {
			if activeRun == 0 {
				nextRun++
				activeRun = nextRun
			}
			step.executionClass = axPlanOrderedInput
			step.preConsistency = axPlanWindowIdentity
			step.postConsistency = axPlanWindowIdentity
			step.boundary = axPlanNoBoundary
			step.orderedInputRun = activeRun
		} else {
			activeRun = 0
		}
		plan.steps = append(plan.steps, step)
	}

	for index := range plan.steps {
		step := &plan.steps[index]
		if step.executionClass != axPlanOrderedInput {
			continue
		}
		runEnds := index == len(plan.steps)-1 ||
			plan.steps[index+1].orderedInputRun != step.orderedInputRun
		if !runEnds {
			continue
		}
		step.postConsistency = axPlanCanonicalWindow
		switch {
		case index < len(plan.steps)-1:
			step.boundary = axPlanNextActionBoundary
		default:
			step.boundary = axPlanTransactionBoundary
		}
	}
	return plan, nil
}
