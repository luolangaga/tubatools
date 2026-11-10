package core

import (
	"fmt"
	"strconv"
	"strings"
	"time"
)

// axElementLookup finds the one element a predicate tests in a snapshot.
type axElementLookup func(*AppSnapshot) (*ElementRecord, error)

// verifyLookup polls the application that owns the read window until goal
// holds for the element lookup finds, or timeoutMS passes. Query and text
// wait steps share it; only the lookup differs. hint names what to list as
// the closest elements on a miss.
func (s *Service) verifyLookup(
	app string,
	lookup axElementLookup,
	displaySelector, hint string,
	goal verifyGoal,
	timeoutMS,
	stableMS int,
	scope string,
	targetWindowHandle ...string,
) ToolCallResult {
	if timeoutMS <= 0 {
		timeoutMS = 5000
	}

	noScreenshot := false
	deadline := time.Now().Add(time.Duration(timeoutMS) * time.Millisecond)
	attempts := []string{}
	// Whether the UI changed during the wait is the most useful fact on
	// failure. Reporting only that the requested condition never held leaves
	// the agent unable to tell whether to keep waiting or whether its last
	// action never took effect, so it keeps retrying with longer timeouts.
	var first verifyLookupBaseline
	var trueSince time.Time
	stableObserved := ""
	for attempt := 1; ; attempt++ {
		request := AXServiceRequest{
			Operation:         axServiceObserve,
			App:               app,
			IncludeScreenshot: &noScreenshot,
			Scope:             normalizedAXScope(scope),
		}
		if len(targetWindowHandle) > 0 {
			request.TargetWindowHandle = strings.TrimSpace(targetWindowHandle[0])
		}
		read, result := s.readUntilApplication(request)
		if result.IsError {
			if isAXAppUnavailableResult(result) {
				elapsed := timeoutMS - int(time.Until(deadline).Milliseconds())
				attempts = append(attempts, fmt.Sprintf(
					"attempt %d at +%dms — application is not yet visible on the AX bus",
					attempt,
					elapsed,
				))
				if !time.Now().Before(deadline) {
					return appUnavailableWaitResult(
						app, timeoutMS, attempts, result, attempt, stableMS,
					)
				}
				time.Sleep(400 * time.Millisecond)
				continue
			}
			return result
		}
		snapshot := read.primary
		if attempt == 1 {
			first = verifyLookupBaseline{
				window: snapshot.WindowTitle,
				handle: snapshot.WindowHandle,
				count:  scopedCanonicalElementCount(snapshot),
			}
		}
		record, lookupErr, incomplete := s.lookupAcrossWindows(read, lookup)
		ok, observed := goal.check(record, lookupErr)
		if incomplete {
			// No match proves nothing while a visible window was unread.
			ok = false
			observed += "; a visible window of the application could not be read"
		}
		selectorAmbiguous := lookupErr != nil &&
			strings.Contains(strings.ToLower(lookupErr.Error()), "ambiguous")
		attempts = append(attempts, fmt.Sprintf("attempt %d at +%dms — %s",
			attempt, timeoutMS-int(time.Until(deadline).Milliseconds()), observed))
		if attempt == 1 && lookupErr != nil && selectorAmbiguous {
			return locatorFailureWaitResult(
				observed, selectorAmbiguous, snapshot, attempt, stableMS,
			)
		}
		if ok {
			now := time.Now()
			if stableMS == 0 {
				return axAssertionResult(fmt.Sprintf(
					"PASS: %s %s.\n%s\nAssertions do not change the UI; this is a read of the live tree, not a replay of an earlier snapshot.",
					displaySelector, goal.describe(), observed),
					false, "pass", attempt, stableMS)
			}
			if trueSince.IsZero() || stableObserved != observed {
				trueSince = now
				stableObserved = observed
			}
			stableFor := int(now.Sub(trueSince) / time.Millisecond)
			if stableFor >= stableMS {
				return axAssertionResult(fmt.Sprintf(
					"PASS: %s %s and remained continuously true for %dms.\n%s\nAssertions do not change the UI; this is a read of the live tree, not a replay of an earlier snapshot.",
					displaySelector, goal.describe(), stableFor, observed),
					false, "pass", attempt, stableMS)
			}
			observed = fmt.Sprintf(
				"%s; predicate true for %dms/%dms required stability",
				observed,
				stableFor,
				stableMS,
			)
		} else {
			trueSince = time.Time{}
			stableObserved = ""
		}
		// Sample before checking the deadline so even the smallest budget gets
		// one complete live-state evaluation.
		if !time.Now().Before(deadline) {
			return verifyLookupTimeoutResult(verifyLookupTimeout{
				displaySelector:   displaySelector,
				hint:              hint,
				goal:              goal,
				timeoutMS:         timeoutMS,
				stableMS:          stableMS,
				attempt:           attempt,
				attempts:          attempts,
				first:             first,
				snapshot:          snapshot,
				windowRef:         read.windowRef,
				lookupErr:         lookupErr,
				ok:                ok,
				incomplete:        incomplete,
				selectorAmbiguous: selectorAmbiguous,
			})
		}
		time.Sleep(400 * time.Millisecond)
	}
}

// verifyLookupBaseline is the bound window as the first poll saw it; the
// timeout report compares the last poll against it.
type verifyLookupBaseline struct {
	window string
	handle string
	count  int
}

func appUnavailableWaitResult(
	app string,
	timeoutMS int,
	attempts []string,
	result ToolCallResult,
	attempt,
	stableMS int,
) ToolCallResult {
	text := fmt.Sprintf(
		"app_unavailable: %q did not become visible on the AX bus within %dms.\n",
		app,
		timeoutMS,
	)
	for _, line := range attempts {
		text += "  - " + line + "\n"
	}
	text += "Last AX availability result: " +
		firstLine(ResultText(result))
	return axAssertionResult(
		strings.TrimSpace(text),
		true,
		"app_unavailable",
		attempt,
		stableMS,
	)
}

// lookupAcrossWindows runs lookup on the bound window and, when that finds
// no unambiguous miss to report, on the application's other visible
// windows. incomplete is true when nothing matched and some window could not
// be read, so the miss proves nothing.
func (s *Service) lookupAcrossWindows(
	read *untilApplicationRead,
	lookup axElementLookup,
) (*ElementRecord, error, bool) {
	record, lookupErr := lookup(read.lookup)
	incomplete := false
	if record == nil &&
		!strings.Contains(strings.ToLower(fmt.Sprint(lookupErr)), "ambiguous") {
		// The bound window has no match: the effect may be a dialog
		// or popup in another window of the application.
		others, complete := s.readOtherWindows(read)
		if otherRecord, otherErr := lookup(others); otherRecord != nil ||
			strings.Contains(strings.ToLower(fmt.Sprint(otherErr)), "ambiguous") {
			record, lookupErr = otherRecord, otherErr
		}
		incomplete = !complete && record == nil
	}
	return record, lookupErr, incomplete
}

// locatorFailureWaitResult stops a wait whose first poll shows the locator
// cannot name one current element: polling it would never answer.
func locatorFailureWaitResult(
	observed string,
	selectorAmbiguous bool,
	snapshot *AppSnapshot,
	attempt,
	stableMS int,
) ToolCallResult {
	status := "locator_stale"
	if selectorAmbiguous {
		status = "locator_ambiguous"
	}
	result := axAssertionResult(
		fmt.Sprintf(
			"wait %s: %s\nThis locator did not identify one "+
				"current element, so polling it would not establish the "+
				"requested predicate. Obtain a fresh ref or use a more "+
				"specific structured selector.",
			status, observed,
		),
		true,
		status,
		attempt,
		stableMS,
	)
	addAssertionSnapshotMetadata(&result, snapshot)
	return result
}

// verifyLookupTimeout is the state of the last poll of a wait that timed
// out.
type verifyLookupTimeout struct {
	displaySelector   string
	hint              string
	goal              verifyGoal
	timeoutMS         int
	stableMS          int
	attempt           int
	attempts          []string
	first             verifyLookupBaseline
	snapshot          *AppSnapshot
	windowRef         string
	lookupErr         error
	ok                bool
	incomplete        bool
	selectorAmbiguous bool
}

func verifyLookupTimeoutResult(t verifyLookupTimeout) ToolCallResult {
	snapshot := t.snapshot
	e := &toolError{tool: "wait", summary: fmt.Sprintf(
		"expected %s %s, and it did not become true within %dms.",
		t.displaySelector, t.goal.describe(), t.timeoutMS)}
	e.add("Selector", strconv.Quote(t.displaySelector))
	e.add("Snapshot", snapshotAge(snapshot))
	addWaitChangeReport(e, t.first, snapshot, t.selectorAmbiguous)
	for _, line := range t.attempts {
		e.log = append(e.log, "  - "+line)
	}
	addWaitNextStep(e, t)
	status := "locator_missing"
	if t.lookupErr == nil {
		status = "predicate_false"
	}
	if t.ok && t.stableMS > 0 {
		status = "predicate_unstable"
		e.next = fmt.Sprintf(
			"the predicate became true but did not remain unchanged for "+
				"the requested %dms stability window before timeout. "+
				"Increase timeout_ms, reduce stable_ms, or assert a more "+
				"specific next-state condition.",
			t.stableMS,
		)
	}
	result := ToolCallResult{}
	lookupErr := t.lookupErr
	// For an exists predicate a target that never appeared is the
	// answer: the predicate is false, not unknown.
	goal := t.goal
	existsOnly := goal.exists != nil && *goal.exists && goal.state == "" &&
		goal.valueEquals == "" && goal.valueContains == "" && goal.textContains == ""
	if status == "locator_missing" && existsOnly {
		status = "predicate_false"
		lookupErr = nil
	}
	if t.incomplete {
		status = "observation_incomplete"
	}
	if !t.incomplete && lookupErr == nil &&
		(status == "predicate_false" || status == "predicate_unstable") {
		result = assertionNegativeResult(
			e.Error(), status, t.attempt, t.stableMS,
		)
	} else {
		result = axAssertionResult(
			e.Error(), true, status, t.attempt, t.stableMS,
		)
	}
	addAssertionSnapshotMetadata(&result, snapshot)
	if t.windowRef != "" {
		result.StructuredContent["until_window_ref"] = t.windowRef
	}
	return result
}

// addWaitChangeReport reports whether the UI changed during the wait. This
// separates three situations: no AX-visible change, a change that is not the
// awaited one (the predicate may be the wrong test), and a different window
// (the target may live elsewhere). Windows are compared by instance handle,
// not title: a second same-titled window of one app can become the root
// without any title change while every element index is renumbered.
func addWaitChangeReport(
	e *toolError,
	first verifyLookupBaseline,
	snapshot *AppSnapshot,
	selectorAmbiguous bool,
) {
	if (first.handle != "" && snapshot.WindowHandle != first.handle) ||
		snapshot.WindowTitle != first.window {
		e.add("Window changed", fmt.Sprintf(
			"%q → %q; current snapshot indices changed",
			first.window, snapshot.WindowTitle,
		))
	} else if scopedCanonicalElementCount(snapshot) != first.count {
		e.add("Tree changed", fmt.Sprintf("%d → %d elements, same window",
			first.count, scopedCanonicalElementCount(snapshot)))
	} else if selectorAmbiguous {
		e.add("Nothing changed", fmt.Sprintf(
			"same window, still %d elements — the selector remained ambiguous, "+
				"so no unique assertion target was identified and this does not "+
				"say whether the prior action took effect",
			first.count))
	} else {
		e.add("Nothing changed", fmt.Sprintf(
			"same window, still %d elements — no AX-visible structural "+
				"change was observed during the wait. This does not establish "+
				"whether a prior input was emitted or whether a visual, file, "+
				"document, or application effect occurred outside A11y",
			first.count))
	}
}

// addWaitNextStep chooses the next step by cause. When the target never
// appeared, it lists the closest current elements so the agent need not
// reread the whole tree to correct a guessed selector. When an exists:false
// wait times out without any UI change, the agent is probably using the
// disappearance of an old element as a proxy for navigation; single-page
// apps often keep the old node, so the condition may never hold, and waiting
// for something new is the better test. When the UI did change, the
// candidate list is more useful than that advice.
func addWaitNextStep(e *toolError, t verifyLookupTimeout) {
	snapshot := t.snapshot
	if t.selectorAmbiguous {
		e.next = "the selector remained ambiguous throughout the wait, so the wait " +
			"never identified one element to test. Use a fresh rN ref or a selector " +
			"copied from the current tree with enough role, name, and predicates to " +
			"match exactly one element. This failure does not prove " +
			"the requested state is false or that the prior action failed."
	} else if t.goal.exists != nil && !*t.goal.exists &&
		snapshot.WindowTitle == t.first.window && len(snapshot.Elements) == t.first.count {
		e.next = "waiting for an element to DISAPPEAR is a poor proxy for " +
			"\"the page moved on\": single-page apps routinely leave the old " +
			"node in the DOM, so this condition may never become true no " +
			"matter how long you wait. Wait for something NEW instead — a " +
			"heading you expect on the next screen, or the address bar's " +
			"value changing. The unchanged AX tree does not attribute the " +
			"cause of this timeout."
	} else if closest := closestElements(snapshot, t.hint, 5); len(closest) > 0 {
		e.log = append(e.log, "  closest elements currently in the tree:")
		for _, line := range closest {
			e.log = append(e.log, "  "+line)
		}
		e.next = "the element never appeared. The closest things actually in the tree are listed above — if one of them is what you meant, retry with its selector. Otherwise the target may be absent or the app may expose it under a different A11y name. This assertion does not attribute the cause."
	} else {
		e.next = "this is a real observation of the current tree, not a stale cache. The declared A11y predicate was not observed. Inspect a fresh tree, the visual channel, or durable state as appropriate; this assertion alone does not prove why the predicate was absent."
	}
}
