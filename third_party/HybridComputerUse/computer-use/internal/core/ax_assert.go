package core

import (
	"fmt"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// Post-action readback is passive: it reports what changed after an action.
// Confirming a condition actively (has the save dialog closed?) otherwise
// means pulling another tree and comparing once, which gives the wrong answer
// when it runs a moment too early.
//
// An assertion is reliable because it re-checks within a timeout window. That matters more on the
// desktop, where UI changes raise no load event and a window can appear after
// the post-action settle wait has ended.
//
// Every poll is a full AT-SPI traversal, so the default timeout is 5 seconds
// with a 400ms interval, and the first sample always runs: one snapshot of a
// large app can take longer than the whole budget, and checking the deadline
// first would fail without sampling once.

type verifyGoal struct {
	state         string
	valueEquals   string
	valueContains string
	textContains  string
	exists        *bool
	// negated inverts a value or text predicate: wait until it is false.
	negated bool
}

func (g verifyGoal) describe() string {
	parts := []string{}
	if g.exists != nil {
		if *g.exists {
			parts = append(parts, "to exist")
		} else {
			parts = append(parts, "to be gone")
		}
	}
	if g.state != "" {
		if bare, negated := strings.CutPrefix(g.state, "!"); negated {
			parts = append(parts, fmt.Sprintf("not to be %s", bare))
		} else {
			parts = append(parts, fmt.Sprintf("to be %s", g.state))
		}
	}
	if g.valueContains != "" {
		parts = append(parts, fmt.Sprintf("value to contain %q", g.valueContains))
	}
	if g.valueEquals != "" {
		parts = append(parts, fmt.Sprintf("value to equal %q", g.valueEquals))
	}
	if g.textContains != "" {
		parts = append(parts, fmt.Sprintf("text to contain %q", g.textContains))
	}
	if g.negated {
		return "not (" + strings.Join(parts, " and ") + ")"
	}
	return strings.Join(parts, " and ")
}

// check reports whether the goal holds and what was actually observed. The
// observation matters most: "assertion failed" carries no information, while
// "expected checked, observed enabled focused" does.
func (g verifyGoal) check(record *ElementRecord, lookupErr error) (bool, string) {
	if g.negated {
		positive := g
		positive.negated = false
		if record == nil {
			return false, "no element matches the selector"
		}
		ok, observed := positive.check(record, lookupErr)
		return !ok, observed
	}
	if record == nil {
		if g.exists != nil && !*g.exists {
			return true, "no element matches the selector"
		}
		reason := "no element matches the selector"
		if lookupErr != nil {
			reason = firstLine(lookupErr.Error())
		}
		return false, reason
	}
	observed := []string{fmt.Sprintf("resolved to %d %s", record.Index, describeRecord(record))}
	ok := true
	if g.exists != nil && !*g.exists {
		ok = false
		observed = append(observed, "but it is still present")
	}
	if g.state != "" {
		want := true
		bare := g.state
		if trimmed, negated := strings.CutPrefix(g.state, "!"); negated {
			want, bare = false, trimmed
		}
		has := containsFold(record.States, bare)
		if has != want {
			ok = false
		}
		// States already renders with brackets (`[enabled focused]` in the
		// tree); wrapping it again would print `[ [enabled focused]]`, so it
		// is shown exactly as the tree shows it.
		shown := strings.TrimSpace(record.States)
		if shown == "" {
			shown = "(no states reported)"
		}
		observed = append(observed, "states are "+shown)
	}
	if g.valueContains != "" {
		if !containsFold(record.Value, g.valueContains) {
			ok = false
		}
		observed = append(observed, fmt.Sprintf("value is %q", record.Value))
	}
	if g.valueEquals != "" {
		if strings.TrimSpace(record.Value) != strings.TrimSpace(g.valueEquals) {
			ok = false
		}
		observed = append(observed, fmt.Sprintf("value is %q", record.Value))
	}
	if g.textContains != "" {
		hit := containsFold(record.Name, g.textContains) ||
			containsFold(record.Description, g.textContains) ||
			containsFold(record.Value, g.textContains) ||
			containsFold(record.Placeholder, g.textContains)
		if !hit {
			ok = false
		}
		observed = append(observed, fmt.Sprintf("name=%q value=%q", record.Name, record.Value))
	}
	return ok, strings.Join(observed, ", ")
}

func firstLine(text string) string {
	if index := strings.IndexByte(text, '\n'); index >= 0 {
		return text[:index]
	}
	return text
}

func axAssertionResult(
	text string,
	isError bool,
	status string,
	attempts int,
	stableMS int,
) ToolCallResult {
	result := TextResult(text, isError)
	result.StructuredContent = map[string]any{
		"assertion_sampled_live_ui": true,
		"assertion_status":          status,
		"assertion_passed":          status == "pass",
		"assertion_attempts":        attempts,
		"assertion_stable_ms":       stableMS,
		"poll_attempted":            attempts > 0,
		"task_effect_verified":      false,
	}
	return result
}

func assertionNegativeResult(
	text string,
	status string,
	attempts int,
	stableMS int,
) ToolCallResult {
	text = strings.TrimSpace(text) +
		"\nassertion_passed=false retry_same_request=false"
	result := axAssertionResult(
		text,
		false,
		status,
		attempts,
		stableMS,
	)
	result.StructuredContent["retry_same_request"] = false
	result.StructuredContent["continuation"] = map[string]any{
		"state":              "predicate_false",
		"retry_same_request": false,
		"route":              "inspect_or_continue",
	}
	return result
}

func isAXAppUnavailableResult(result ToolCallResult) bool {
	if !result.IsError || result.StructuredContent == nil {
		return false
	}
	code, _ := result.StructuredContent["failure_code"].(string)
	return code == "ax_app_unavailable"
}

func addAssertionSnapshotMetadata(
	result *ToolCallResult,
	snapshot *AppSnapshot,
) {
	if result == nil || snapshot == nil {
		return
	}
	addTreeProjectionMetadata(result, snapshot)
	if result.StructuredContent == nil {
		result.StructuredContent = map[string]any{}
	}
	result.StructuredContent["current_window_title"] = snapshot.WindowTitle
}

func snapshotForScopedLookup(snapshot *AppSnapshot) *AppSnapshot {
	if snapshot == nil ||
		normalizedAXScope(snapshot.Scope) != contract.AXScopeContent {
		return snapshot
	}
	clone := *snapshot
	clone.Elements = make([]ElementRecord, 0, snapshot.scopedCanonicalCount)
	for _, record := range snapshot.Elements {
		if recordInAXScope(snapshot, &record) {
			clone.Elements = append(clone.Elements, record)
		}
	}
	return &clone
}
