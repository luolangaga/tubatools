package core

import (
	"fmt"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"strconv"
	"strings"
	"time"
)

// runAXWait polls one wait step. It sends no input. The result is a
// predicate result: assertion_status "pass", a sampled false, or an error
// when the state could not be read.
func (s *Service) runAXWait(context *AXBatchContext, wait *axWait) ToolCallResult {
	switch {
	case wait.settle:
		return s.waitForSettle(context, wait)
	case wait.window != "":
		return s.waitForDesktopWindow(context, wait)
	case wait.query != nil:
		return s.waitForQuery(context, wait)
	case wait.ref != "":
		return s.waitForRef(context, wait)
	default:
		return s.waitForText(context, wait)
	}
}

// waitForDesktopWindow polls the top-level windows of the whole desktop:
// a window may belong to another process, and an application that exits and
// starts again has a new one.
func (s *Service) waitForDesktopWindow(context *AXBatchContext, wait *axWait) ToolCallResult {
	deadline := time.Now().Add(time.Duration(wait.timeoutMS) * time.Millisecond)
	attempts := 0
	lastError := ""
	for {
		attempts++
		inventory, failure := s.listDesktopWindows(false)
		matched := []axDesktopWindow{}
		titleMatches := 0
		if inventory == nil {
			lastError = firstLine(ResultText(failure))
		} else {
			lastError = ""
			for _, title := range inventory.titles {
				if containsFold(title, wait.window) {
					titleMatches++
				}
			}
			for _, window := range inventory.windows {
				if containsFold(window.title, wait.window) {
					matched = append(matched, window)
				}
			}
		}
		holds := titleMatches > 0
		if wait.gone {
			holds = !holds && inventory != nil
		}
		if holds {
			text := "wait: no window title contains " + strconv.Quote(wait.window)
			if !wait.gone {
				names := []string{}
				for _, window := range matched {
					names = append(names, window.ref+" "+strconv.Quote(window.title))
				}
				if len(names) == 0 {
					names = append(names, "titled "+strconv.Quote(wait.window)+" (no ref)")
				}
				text = "wait: window " + strings.Join(names, ", ")
			}
			result := axAssertionResult(text, false, "pass", attempts, 0)
			if len(matched) == 1 {
				result.StructuredContent["wait_window_ref"] = matched[0].ref
			}
			return result
		}
		if !time.Now().Before(deadline) {
			return s.windowWaitTimeout(context, wait, inventory, lastError, attempts)
		}
		time.Sleep(axWaitPollInterval)
	}
}

// waitForQuery polls every visible window of the batch application with
// find's matching rules.
func (s *Service) waitForQuery(context *AXBatchContext, wait *axWait) ToolCallResult {
	query := wait.query
	name := OptionalString(query, "name_contains")
	_, nameExact := query["name_equals"]
	if nameExact {
		name = OptionalString(query, "name_equals")
	}
	text := OptionalString(query, "text_contains")
	_, textExact := query["text_equals"]
	if textExact {
		text = OptionalString(query, "text_equals")
	}
	role, state := OptionalString(query, "role"), OptionalString(query, "state")
	seen := &axWaitSeen{}
	lookup := func(snapshot *AppSnapshot) (*ElementRecord, error) {
		if snapshot == nil {
			return nil, nil
		}
		seen.add(snapshot)
		for index := range snapshot.Elements {
			record := snapshot.Elements[index]
			if queryRecordMatches(snapshot, &record, role, name, text, state, nameExact, textExact) {
				// The first match answers "appeared"; a wait does not act,
				// so several matches are not ambiguous.
				return &record, nil
			}
		}
		return nil, nil
	}
	exists := !wait.gone
	result := s.waitOnApplication(context, lookup, axWaitQueryText(query), FirstNonEmpty(name, text),
		verifyGoal{exists: &exists}, wait.timeoutMS)
	return seen.explainTimeout(result, wait, newAXNearQuery(role, name, text, state), context)
}

// waitForText polls the batch window for an element whose name,
// description, value, or placeholder contains the text.
func (s *Service) waitForText(context *AXBatchContext, wait *axWait) ToolCallResult {
	seen := &axWaitSeen{}
	lookup := func(snapshot *AppSnapshot) (*ElementRecord, error) {
		if snapshot == nil {
			return nil, nil
		}
		seen.add(snapshot)
		for index := range snapshot.Elements {
			record := snapshot.Elements[index]
			if recordMatchesFind(&record, "", "", wait.text, "", false, false) {
				return &record, nil
			}
		}
		return nil, nil
	}
	exists := !wait.gone
	result := s.waitOnApplication(context, lookup, "text "+strconv.Quote(wait.text), wait.text,
		verifyGoal{exists: &exists}, wait.timeoutMS)
	return seen.explainTimeout(result, wait, newAXNearQuery("", "", wait.text, ""), context)
}

func (s *Service) waitOnApplication(
	context *AXBatchContext,
	lookup axElementLookup,
	display, hint string,
	goal verifyGoal,
	timeoutMS int,
) ToolCallResult {
	app := FirstNonEmpty(context.AppRef, context.AppToken)
	result := s.verifyLookup(app, lookup, display, hint, goal, timeoutMS, 0,
		contract.AXScopeWindow, context.WindowHandle)
	return axWaitResultText(result)
}

// waitForRef polls one element ref for a value, state, or text, or for its
// absence.
func (s *Service) waitForRef(context *AXBatchContext, wait *axWait) ToolCallResult {
	predicate, err := axRefWaitPredicate(wait)
	if err != nil {
		return TextResult(err.Error(), true)
	}
	if err := s.preflightAXPostcondition(context, wait.ref); err != nil {
		return axStaleRefResult(wait.ref, err)
	}
	if held, ok := s.waitHoldsInLatestRead(context, wait.ref, predicate.goal); ok {
		return held
	}
	return axWaitResultText(s.runAXPostcondition(context, predicate))
}

// waitHoldsInLatestRead checks a ref wait against the read the previous
// step already made, the same read the effect line reports. When it holds
// there, the wait is answered without polling; the live resolver cannot
// read some elements completely and would otherwise poll to its deadline
// before a canonical read decides.
func (s *Service) waitHoldsInLatestRead(
	context *AXBatchContext,
	ref string,
	goal verifyGoal,
) (ToolCallResult, bool) {
	snapshot := s.currentSnapshot(context.AppToken)
	expected, err := s.RefRegistry.Resolve(ref, "")
	if snapshot == nil || err != nil {
		return ToolCallResult{}, false
	}
	record, err := s.exactRefRecordFromSnapshot(snapshot, ref, expected)
	if err != nil || record == nil {
		return ToolCallResult{}, false
	}
	held, observed := goal.check(record, nil)
	if !held {
		return ToolCallResult{}, false
	}
	return axAssertionResult(fmt.Sprintf("wait: %s %s. %s", ref, goal.describe(), observed),
		false, "pass", 1, 0), true
}
