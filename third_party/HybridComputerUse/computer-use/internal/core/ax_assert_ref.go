package core

import (
	"errors"
	"fmt"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"strings"
	"time"
)

func (s *Service) verifyResolvedAXRef(
	app,
	displayRef string,
	target *semanticTarget,
	goal verifyGoal,
	timeoutMS,
	stableMS int,
) ToolCallResult {
	var entry *AXRefEntry
	if resolved, err := s.RefRegistry.Resolve(displayRef, ""); err == nil {
		entry = resolved
		s.authorizeProviderRebind(app, entry, target)
	}
	var snapshotProbe axAssertionSnapshotProbe
	if entry != nil &&
		strings.TrimSpace(entry.ProviderBusName) != "" &&
		strings.TrimSpace(entry.ProviderObjectPath) != "" &&
		strings.TrimSpace(entry.ProcessGeneration) != "" {
		snapshotProbe = func() (*axAssertionSnapshotProof, ToolCallResult) {
			noScreenshot := false
			snapshot, notes, result := s.readProjectedSnapshot(
				app,
				AXServiceRequest{
					Operation:          axServiceObserve,
					App:                app,
					IncludeScreenshot:  &noScreenshot,
					TargetWindowHandle: entry.WindowHandle,
					Scope:              contract.AXScopeWindow,
					TreeView:           contract.TreeViewWorking,
				},
			)
			if result.IsError {
				return nil, result
			}
			record, err := s.exactRefRecordFromSnapshot(
				snapshot,
				displayRef,
				entry,
			)
			if err != nil {
				return nil, TextResult(err.Error(), true).
					withEvidenceScope("error_only")
			}
			return &axAssertionSnapshotProof{
				snapshot: snapshot,
				notes:    notes,
				record:   record,
			}, ToolCallResult{}
		}
	}
	return s.verifyResolvedAXRefWithProbes(
		app,
		displayRef,
		target,
		goal,
		timeoutMS,
		stableMS,
		s.callProviderAction,
		snapshotProbe,
	)
}

func (s *Service) exactRefRecordFromSnapshot(
	snapshot *AppSnapshot,
	displayRef string,
	expected *AXRefEntry,
) (*ElementRecord, error) {
	if s == nil || snapshot == nil || snapshot.AXSnapshot == nil ||
		expected == nil {
		return nil, errors.New(
			"canonical assertion fallback has incomplete identity evidence",
		)
	}
	current, err := s.RefRegistry.Resolve(displayRef, "")
	if err != nil {
		return nil, err
	}
	if current.Ref != expected.Ref ||
		current.IdentityKey != expected.IdentityKey ||
		current.LocatorKind != expected.LocatorKind ||
		current.AppRef != expected.AppRef ||
		current.AppIdentity != expected.AppIdentity ||
		current.LogicalWindowRef != expected.LogicalWindowRef ||
		current.WindowHandle != expected.WindowHandle {
		return nil, errors.New(
			"stale_element_ref: the canonical snapshot did not preserve the exact ref identity",
		)
	}
	if snapshot.WindowHandle != expected.WindowHandle ||
		snapshot.publicWindowRef != expected.LogicalWindowRef ||
		snapshot.publicAppRef != expected.AppRef {
		return nil, errors.New(
			"stale_window_ref: the canonical snapshot belongs to a different owner",
		)
	}
	var matches []*AXNode
	var visit func([]*AXNode)
	visit = func(nodes []*AXNode) {
		for _, node := range nodes {
			if node != nil && node.Ref == displayRef {
				matches = append(matches, node)
			}
			if node != nil {
				visit(node.Children)
			}
		}
	}
	visit(snapshot.AXSnapshot.CanonicalRoots)
	if len(matches) != 1 || matches[0].SourceRecord == nil {
		return nil, errors.New(
			"observation_incomplete: the canonical snapshot did not contain exactly one current ref target",
		)
	}
	node := matches[0]
	if node.LocatorKind != expected.LocatorKind {
		return nil, errors.New(
			"stale_element_ref: the canonical ref target changed locator identity",
		)
	}
	record := node.SourceRecord
	if expected.LocatorKind == AXLocatorProviderBound {
		if current.ProviderBusName != expected.ProviderBusName ||
			current.ProviderObjectPath != expected.ProviderObjectPath ||
			current.ProcessGeneration != expected.ProcessGeneration ||
			current.ProviderIdentityScope != expected.ProviderIdentityScope ||
			record.ProviderBusName != expected.ProviderBusName ||
			record.ProviderObjectPath != expected.ProviderObjectPath ||
			record.ProcessGeneration != expected.ProcessGeneration ||
			record.ProviderIdentityScope != expected.ProviderIdentityScope {
			return nil, errors.New(
				"stale_element_ref: the canonical ref target changed provider identity",
			)
		}
	} else if strings.TrimSpace(current.SemanticTarget) !=
		strings.TrimSpace(expected.SemanticTarget) ||
		current.Role != expected.Role ||
		comparableTreeText(current.Name) != comparableTreeText(expected.Name) ||
		strings.TrimSpace(node.SemanticTarget) != strings.TrimSpace(expected.SemanticTarget) ||
		node.Role != expected.Role ||
		comparableTreeText(node.Name) != comparableTreeText(expected.Name) ||
		!runtimePathMatchesEntry(expected, node.RuntimeID) {
		return nil, errors.New(
			"stale_element_ref: the canonical ref target changed semantic lineage",
		)
	}
	return record, nil
}

type axAssertionSnapshotProof struct {
	snapshot *AppSnapshot
	notes    []string
	record   *ElementRecord
}

type axAssertionSnapshotProbe func() (
	*axAssertionSnapshotProof,
	ToolCallResult,
)

func (s *Service) verifyResolvedAXRefWithProbes(
	app,
	displayRef string,
	target *semanticTarget,
	goal verifyGoal,
	timeoutMS,
	stableMS int,
	probe func(ProviderActionRequest) (*ProviderAccessibilityResult, error),
	snapshotProbe axAssertionSnapshotProbe,
) ToolCallResult {
	if target == nil {
		return axAssertionResult(
			"wait: the ref has no live target identity.",
			true, "invalid_target", 0, stableMS,
		)
	}
	if timeoutMS <= 0 {
		timeoutMS = 5000
	}
	deadline := time.Now().Add(time.Duration(timeoutMS) * time.Millisecond)
	var trueSince time.Time
	stableObserved := ""
	lastObserved := ""
	poll := refAssertionPoll{lastStatus: "predicate_false"}
	for attempt := 1; ; attempt++ {
		operation := AXServiceRequest{
			Action:             axResolveTargetCommand{},
			App:                app,
			TargetWindowHandle: target.WindowHandle,
			SemanticTarget:     target,
			SourceRef:          displayRef,
		}
		if err := s.bindRuntimeAppIdentity(&operation); err != nil {
			return axAssertionResult(
				"wait: "+err.Error(),
				true, "invalid_target", attempt, stableMS,
			)
		}
		response, err := probe(
			providerActionRequestFromOperation(operation),
		)
		if response != nil {
			response.PrepareForService()
		}
		sample := poll.classify(response, err, goal, snapshotProbe)
		if sample.locatorFailed {
			result := axAssertionResult(
				fmt.Sprintf(
					"wait %s: %s",
					poll.lastStatus,
					firstLine(sample.lookupErr.Error()),
				),
				true, poll.lastStatus, attempt, stableMS,
			)
			result.StructuredContent["assertion_sampled_live_ui"] =
				poll.sampledLiveUI
			return result
		}
		if !sample.factsComplete && sample.record == nil {
			lastObserved = "the live AX target could not be read completely"
			trueSince = time.Time{}
			stableObserved = ""
		} else {
			ok, observed := goal.check(sample.record, sample.lookupErr)
			lastObserved = observed
			if ok {
				now := time.Now()
				if stableMS == 0 {
					result := axAssertionResult(
						fmt.Sprintf(
							"PASS: %s %s. %s",
							displayRef, goal.describe(), observed,
						),
						false, "pass", attempt, stableMS,
					)
					return withCanonicalFallbackProof(result, sample.proof)
				}
				if trueSince.IsZero() || stableObserved != observed {
					trueSince = now
					stableObserved = observed
				}
				if int(now.Sub(trueSince)/time.Millisecond) >= stableMS {
					result := axAssertionResult(
						fmt.Sprintf(
							"PASS: %s %s and remained continuously true for %dms. %s",
							displayRef,
							goal.describe(),
							int(now.Sub(trueSince)/time.Millisecond),
							observed,
						),
						false, "pass", attempt, stableMS,
					)
					return withCanonicalFallbackProof(result, sample.proof)
				}
			} else {
				trueSince = time.Time{}
				stableObserved = ""
			}
		}
		if !time.Now().Before(deadline) {
			text := fmt.Sprintf(
				"wait %s: %s did not satisfy %s within %dms. Last observation: %s",
				poll.lastStatus,
				displayRef,
				goal.describe(),
				timeoutMS,
				lastObserved,
			)
			result := ToolCallResult{}
			if poll.sampledLiveUI && poll.lastStatus == "predicate_false" {
				result = assertionNegativeResult(
					text, poll.lastStatus, attempt, stableMS,
				)
			} else {
				result = axAssertionResult(
					text, true, poll.lastStatus, attempt, stableMS,
				)
			}
			result.StructuredContent["assertion_sampled_live_ui"] =
				poll.sampledLiveUI
			return result
		}
		time.Sleep(400 * time.Millisecond)
	}
}

// refAssertionPoll carries the status of the latest classified sample and
// whether any sample read the live UI. A successful resolve leaves the
// status of the previous sample in place.
type refAssertionPoll struct {
	lastStatus    string
	sampledLiveUI bool
}

// refAssertionSample is one resolver read of the asserted ref.
type refAssertionSample struct {
	record        *ElementRecord
	lookupErr     error
	factsComplete bool
	// proof is set when a canonical snapshot completed the sample.
	proof *axAssertionSnapshotProof
	// locatorFailed means the ref is ambiguous, stale or invalid, so
	// further polls cannot establish the predicate.
	locatorFailed bool
}

// classify turns one resolver response into the element the goal is
// checked against and updates the poll status.
func (poll *refAssertionPoll) classify(
	response *ProviderAccessibilityResult,
	err error,
	goal verifyGoal,
	snapshotProbe axAssertionSnapshotProbe,
) refAssertionSample {
	sample := refAssertionSample{factsComplete: true}
	switch {
	case err != nil:
		sample.lookupErr = err
		sample.factsComplete = false
		poll.lastStatus = "observation_incomplete"
	case response == nil:
		sample.lookupErr = errors.New("empty accessibility resolver response")
		sample.factsComplete = false
		poll.lastStatus = "observation_incomplete"
	case response.OK:
		sample.record = response.ResolvedElement
		poll.sampledLiveUI = true
		if sample.record == nil {
			sample.lookupErr = errors.New("live resolver returned no element")
			sample.factsComplete = false
			poll.lastStatus = "observation_incomplete"
		}
	default:
		poll.classifyFailure(&sample, *response, goal, snapshotProbe)
	}
	return sample
}

func (poll *refAssertionPoll) classifyFailure(
	sample *refAssertionSample,
	response ProviderAccessibilityResult,
	goal verifyGoal,
	snapshotProbe axAssertionSnapshotProbe,
) {
	sample.lookupErr = errors.New(FirstNonEmpty(
		publicRuntimeFailureText(response),
		response.Error,
		response.FailureCode,
	))
	sample.factsComplete = response.FailureFactsComplete
	switch response.FailureCode {
	case "semantic_target_not_found":
		poll.lastStatus = "locator_missing"
	case "semantic_target_ambiguous":
		poll.lastStatus = "locator_ambiguous"
	case "stale_element_ref":
		poll.lastStatus = "locator_stale"
	case "observation_incomplete":
		poll.lastStatus = "observation_incomplete"
	default:
		poll.lastStatus = FirstNonEmpty(
			response.FailureCode, "observation_incomplete",
		)
	}
	// A ref-scoped negative existence assertion asks whether the exact
	// previously issued object still exists, not whether another
	// role/name-equivalent object is present. A complete live inventory
	// may prove that the application generation, logical window, or
	// provider-bound element has disappeared. Treat that as a successful
	// absence sample. Incomplete reads remain unknown, and ambiguity
	// remains an error.
	if goal.isNegativeExistenceOnly() &&
		sample.factsComplete &&
		completeRefAbsenceFailure(response) {
		sample.record = nil
		poll.sampledLiveUI = true
		poll.lastStatus = "predicate_false"
		return
	}
	if response.FailureCode == "observation_incomplete" &&
		snapshotProbe != nil {
		// The resolver probe began before the assertion deadline, but a
		// provider tree scan may consume that entire interval before it
		// reports incomplete evidence. Complete that one compound sample
		// with a canonical read instead of returning unknown immediately
		// before the final observation proves the same ref. The fallback
		// remains bounded to one call for this polling attempt.
		if proof, fallbackResult := snapshotProbe(); proof != nil &&
			!fallbackResult.IsError &&
			proof.snapshot != nil &&
			proof.record != nil {
			sample.record = proof.record
			sample.lookupErr = nil
			sample.factsComplete = true
			poll.sampledLiveUI = true
			poll.lastStatus = "predicate_false"
			sample.proof = proof
		}
	}
	sample.locatorFailed = response.FailureCode == "semantic_target_ambiguous" ||
		response.FailureCode == "stale_element_ref" ||
		response.FailureCode == "semantic_target_invalid"
}

// withCanonicalFallbackProof attaches the canonical snapshot that completed
// a passing sample, so the caller can show the tree the pass was read from.
func withCanonicalFallbackProof(
	result ToolCallResult,
	proof *axAssertionSnapshotProof,
) ToolCallResult {
	if proof != nil {
		result.axProofSnapshot = proof.snapshot
		result.axProofNotes = append(
			[]string(nil),
			proof.notes...,
		)
		result.StructuredContent["assertion_canonical_fallback"] =
			true
	}
	return result
}

func (g verifyGoal) isNegativeExistenceOnly() bool {
	return g.exists != nil && !*g.exists &&
		g.state == "" &&
		g.valueEquals == "" &&
		g.valueContains == "" &&
		g.textContains == ""
}

func completeRefAbsenceFailure(response ProviderAccessibilityResult) bool {
	if !response.FailureFactsComplete {
		return false
	}
	switch response.FailureCode {
	case "semantic_target_not_found", "stale_element_ref":
		return true
	case "stale_target", "stale_app_ref", "stale_window":
		switch strings.ToLower(strings.TrimSpace(response.FailureSubject)) {
		case "app", "window", "element":
			return true
		}
	}
	return false
}
