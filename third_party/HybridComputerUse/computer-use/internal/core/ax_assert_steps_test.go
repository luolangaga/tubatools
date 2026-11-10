package core

import (
	"strings"
	"testing"
)

func TestRefAssertionPollKeepsStatusOnSuccessfulResolve(t *testing.T) {
	poll := refAssertionPoll{lastStatus: "locator_missing"}
	sample := poll.classify(&ProviderAccessibilityResult{
		OK: true, ResolvedElement: &ElementRecord{Name: "Save"},
	}, nil, verifyGoal{}, nil)
	if sample.record == nil || !sample.factsComplete ||
		poll.lastStatus != "locator_missing" || !poll.sampledLiveUI {
		t.Fatalf("sample=%#v poll=%#v", sample, poll)
	}
}

func TestRefAssertionPollUsesCanonicalProofOnlyForIncompleteReads(t *testing.T) {
	calls := 0
	proof := &axAssertionSnapshotProof{
		snapshot: &AppSnapshot{}, record: &ElementRecord{Name: "Save"},
	}
	probe := func() (*axAssertionSnapshotProof, ToolCallResult) {
		calls++
		return proof, ToolCallResult{}
	}
	incomplete := ProviderAccessibilityResult{
		Error:   "partial tree",
		Failure: &ProviderFailure{Code: "observation_incomplete"},
	}
	incomplete.PrepareForService()
	poll := refAssertionPoll{lastStatus: "predicate_false"}
	sample := poll.classify(&incomplete, nil, verifyGoal{}, probe)
	if sample.proof != proof || sample.lookupErr != nil ||
		poll.lastStatus != "predicate_false" || sample.locatorFailed {
		t.Fatalf("canonical proof did not complete the sample: %#v %#v", sample, poll)
	}
	ambiguous := ProviderAccessibilityResult{
		Error: "two matches",
		Failure: &ProviderFailure{
			Code: "semantic_target_ambiguous", FactsComplete: true,
		},
	}
	ambiguous.PrepareForService()
	sample = poll.classify(&ambiguous, nil, verifyGoal{}, probe)
	if !sample.locatorFailed || sample.proof != nil ||
		poll.lastStatus != "locator_ambiguous" || calls != 1 {
		t.Fatalf("ambiguity must stop the wait without a fallback read: %#v %#v calls=%d",
			sample, poll, calls)
	}
}

func TestWaitChangeReportComparesWindowsByHandle(t *testing.T) {
	first := verifyLookupBaseline{window: "Doc", handle: "h1", count: 0}
	e := &toolError{}
	addWaitChangeReport(e, first,
		&AppSnapshot{WindowTitle: "Doc", WindowHandle: "h2"}, false)
	if len(e.fields) != 1 || e.fields[0][0] != "Window changed" {
		t.Fatalf("a same-titled replacement window was not reported: %#v", e.fields)
	}
	e = &toolError{}
	addWaitChangeReport(e, first,
		&AppSnapshot{WindowTitle: "Doc", WindowHandle: "h1"}, true)
	if len(e.fields) != 1 || e.fields[0][0] != "Nothing changed" ||
		!strings.Contains(e.fields[0][1], "ambiguous") {
		t.Fatalf("unchanged ambiguous wait = %#v", e.fields)
	}
}
