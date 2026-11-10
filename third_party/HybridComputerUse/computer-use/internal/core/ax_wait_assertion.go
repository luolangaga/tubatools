package core

import (
	"errors"
	"fmt"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// axRefPredicate is what a ref wait step checks: the element ref and the
// value, state, text, or presence it must reach within timeoutMS.
type axRefPredicate struct {
	ref       string
	goal      verifyGoal
	timeoutMS int
}

// axRefWaitPredicate builds the predicate of a ref wait step. The decoder
// has already checked the ref, value, state, and timeout; a text of only
// spaces passes the published schema and is rejected here.
func axRefWaitPredicate(wait *axWait) (axRefPredicate, error) {
	predicate := axRefPredicate{ref: wait.ref, timeoutMS: wait.timeoutMS}
	goal := &predicate.goal
	switch {
	case wait.value != "":
		goal.valueEquals, goal.negated = wait.value, wait.gone
	case wait.state != "":
		goal.state = wait.state
		if wait.gone {
			goal.state = "!" + wait.state
		}
	case wait.text != "":
		if strings.TrimSpace(wait.text) == "" {
			return predicate, errors.New("text must be a non-blank string")
		}
		goal.textContains, goal.negated = wait.text, wait.gone
	default:
		exists := !wait.gone
		goal.exists = &exists
	}
	return predicate, nil
}

func axPostconditionPassed(result ToolCallResult) bool {
	if result.IsError || result.StructuredContent == nil {
		return false
	}
	return strings.TrimSpace(fmt.Sprint(
		result.StructuredContent["assertion_status"],
	)) == "pass"
}

func axPostconditionNeedsCanonicalReconciliation(
	assertionResult ToolCallResult,
) bool {
	if !assertionResult.IsError || assertionResult.StructuredContent == nil {
		return false
	}
	_, underlyingCode := classifyAXPostconditionFailure(assertionResult)
	return underlyingCode == "observation_incomplete"
}

func (s *Service) reconcileAXActionPostcondition(
	context *AXBatchContext,
	predicate axRefPredicate,
	assertionResult ToolCallResult,
) ToolCallResult {
	if !axPostconditionNeedsCanonicalReconciliation(assertionResult) {
		return assertionResult
	}
	expected, err := s.RefRegistry.Resolve(predicate.ref, "")
	if err != nil {
		return assertionResult
	}
	expectedIdentity := *expected
	noScreenshot := false
	snapshot, notes, observationResult := s.refreshSnapshot(
		context.AppToken,
		AXServiceRequest{
			Operation:          axServiceObserve,
			App:                context.AppToken,
			IncludeScreenshot:  &noScreenshot,
			TargetWindowHandle: context.WindowHandle,
			SourceRef:          context.WindowRef,
			Scope:              contract.AXScopeWindow,
			TreeView:           contract.TreeViewWorking,
		},
	)
	if observationResult.IsError || snapshot == nil {
		return assertionResult
	}
	return s.reconcileAXPostconditionFromCanonicalSnapshot(
		predicate,
		assertionResult,
		snapshot,
		notes,
		&expectedIdentity,
	)
}

func (s *Service) reconcileAXPostconditionFromCanonicalSnapshot(
	predicate axRefPredicate,
	assertionResult ToolCallResult,
	snapshot *AppSnapshot,
	notes []string,
	expected *AXRefEntry,
) ToolCallResult {
	record, recordErr := s.exactRefRecordFromSnapshot(
		snapshot,
		predicate.ref,
		expected,
	)
	if recordErr != nil {
		if !strings.HasPrefix(recordErr.Error(), "observation_incomplete:") ||
			snapshot.AXSnapshot == nil ||
			!snapshot.AXSnapshot.RefInventoryComplete ||
			snapshot.WindowHandle != expected.WindowHandle ||
			snapshot.publicWindowRef != expected.LogicalWindowRef ||
			snapshot.publicAppRef != expected.AppRef ||
			axSnapshotRefCount(snapshot.AXSnapshot.CanonicalRoots, predicate.ref) != 0 {
			return assertionResult
		}
		record = nil
	}
	goal := predicate.goal
	ok, observed := goal.check(record, nil)
	reconciled := ToolCallResult{}
	if ok {
		reconciled = axAssertionResult(
			fmt.Sprintf(
				"PASS: %s %s in the action's fresh observation. %s",
				predicate.ref,
				goal.describe(),
				observed,
			),
			false,
			"pass",
			1,
			0,
		)
	} else {
		reconciled = assertionNegativeResult(
			fmt.Sprintf(
				"wait false: %s did not satisfy %s in the "+
					"action's fresh observation. Last observation: %s",
				predicate.ref,
				goal.describe(),
				observed,
			),
			"predicate_false",
			1,
			0,
		)
	}
	reconciled.StructuredContent["postcondition_reconciled_by_canonical_observation"] = true
	reconciled.StructuredContent["cause_unattributed"] = true
	reconciled.axProofSnapshot = snapshot
	reconciled.axProofNotes = append([]string(nil), notes...)
	return reconciled
}

func axSnapshotRefCount(roots []*AXNode, ref string) int {
	count := 0
	var visit func([]*AXNode)
	visit = func(nodes []*AXNode) {
		for _, node := range nodes {
			if node == nil {
				continue
			}
			if node.Ref == ref {
				count++
			}
			visit(node.Children)
		}
	}
	visit(roots)
	return count
}

func (s *Service) preflightAXPostcondition(
	context *AXBatchContext,
	ref string,
) error {
	if context == nil || strings.TrimSpace(context.WindowHandle) == "" {
		return errors.New("batch window identity is unavailable")
	}
	entry, err := s.resolveShortAXRefOwned(ref)
	if err != nil {
		return err
	}
	if entry.WindowHandle != context.WindowHandle {
		return fmt.Errorf(
			"target %s belongs to a different logical window",
			ref,
		)
	}
	return nil
}

// runAXPostcondition polls a ref predicate after a batch's earlier steps.
// It reads that element in the application that owns it and never re-binds
// input.
func (s *Service) runAXPostcondition(
	context *AXBatchContext,
	predicate axRefPredicate,
) ToolCallResult {
	appToken := context.AppToken
	if entry, err := s.resolveShortAXRefOwned(predicate.ref); err == nil &&
		entry.AppRef != "" {
		appToken = entry.AppRef
	}
	entry, err := s.resolveShortAXRef(appToken, predicate.ref)
	if err != nil {
		return axStaleRefResult(predicate.ref, err)
	}
	liveTarget, err := semanticTargetFromRefEntry(entry)
	if err != nil {
		return axStaleRefResult(predicate.ref, err)
	}
	result := s.verifyResolvedAXRef(
		appToken,
		predicate.ref,
		liveTarget,
		predicate.goal,
		predicate.timeoutMS,
		0,
	)
	// The live resolver can fail to read some provider-bound elements
	// completely. One fresh canonical read of the window then decides
	// the predicate.
	result = s.reconcileAXActionPostcondition(&AXBatchContext{
		AppToken:     FirstNonEmpty(entry.AppRef, appToken),
		WindowRef:    entry.LogicalWindowRef,
		WindowHandle: entry.WindowHandle,
	}, predicate, result)
	result = result.withEvidenceScope("independent_ui_read")
	return WithAXModeEvidence(result, contract.AXModeAssert)
}

func classifyAXPostconditionFailure(
	result ToolCallResult,
) (classification, underlying string) {
	if result.StructuredContent == nil {
		return "postcondition_unknown", "missing_assertion_evidence"
	}
	underlying, _ = result.StructuredContent["failure_code"].(string)
	underlying = strings.TrimSpace(underlying)
	if underlying == "" {
		underlying, _ = result.StructuredContent["assertion_status"].(string)
		underlying = strings.TrimSpace(underlying)
	}
	if underlying == "" {
		underlying = "unknown_assertion_failure"
	}
	sampled, _ := result.StructuredContent["assertion_sampled_live_ui"].(bool)
	switch underlying {
	case "predicate_false", "predicate_unstable", "locator_missing":
		if sampled {
			return "postcondition_not_satisfied", underlying
		}
	}
	return "postcondition_unknown", underlying
}
