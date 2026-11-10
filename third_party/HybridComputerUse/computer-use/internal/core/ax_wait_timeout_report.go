package core

import (
	"fmt"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"sort"
	"strconv"
	"strings"
)

// windowWaitTimeout reports a window wait that did not hold. The message
// leads with the windows that are there now, each with its wN, focus
// first; when a title is close to the awaited one, the hint is the same
// wait for that title. The batch application's windows come before other
// applications', since the wait most often follows input to that
// application.
func (s *Service) windowWaitTimeout(
	context *AXBatchContext, wait *axWait, inventory *axWindowInventory, readError string, attempts int,
) ToolCallResult {
	head := fmt.Sprintf("wait_timeout: after %dms ", wait.timeoutMS)
	if inventory == nil || readError != "" {
		result := axAssertionResult(head+"the window list could not be read: "+readError, true,
			"observation_incomplete", attempts, 0)
		return result
	}
	if wait.gone {
		still := []string{}
		for _, window := range inventory.windows {
			if containsFold(window.title, wait.window) {
				still = append(still, window.ref+" "+strconv.Quote(window.title))
			}
		}
		if len(still) == 0 {
			// Only windows without a wN are still there.
			still = append(still, strconv.Quote(wait.window)+" (no wN)")
		}
		head += "still open: " + strings.Join(still, ", ")
	} else {
		head += "no window title contains " + strconv.Quote(wait.window)
	}
	appRef := ""
	if context != nil {
		appRef = context.AppRef
	}
	listed := axWaitWindowList(inventory, appRef, wait.window,
		axPublicMessageRunes-len([]rune(head))-len("; windows: "))
	result := axAssertionResult(head+"; windows: "+listed, false, "predicate_false", attempts, 0)
	for _, window := range inventory.windows {
		if window.active {
			result.StructuredContent["focused_window_ref"] = window.ref
		}
	}
	if !wait.gone {
		if near := axNearestWindowTitle(inventory, wait.window); near != "" {
			corrected := map[string]any{"type": "wait", "window": near}
			if wait.timeoutMS != contract.AXDefaultWaitTimeout {
				corrected["timeout_ms"] = wait.timeoutMS
			}
			result.StructuredContent["wait_hint"] = axHintJSON(corrected)
		}
	}
	return result
}

// axWaitWindowList names the windows of a listing as wN "title", the
// focused one first and marked, then the windows of appRef, then the rest;
// untitled windows are left out. It stops before budget runes.
func axWaitWindowList(inventory *axWindowInventory, appRef, wanted string, budget int) string {
	windows := append([]axDesktopWindow(nil), inventory.windows...)
	rank := func(window axDesktopWindow) int {
		switch {
		case window.active:
			return 0
		case appRef != "" && window.appRef == appRef:
			return 1
		case axWordOverlap(axSignificantWords(wanted), window.title) > 0:
			return 2
		}
		return 3
	}
	sort.SliceStable(windows, func(i, j int) bool { return rank(windows[i]) < rank(windows[j]) })
	parts := []string{}
	for _, window := range windows {
		if strings.TrimSpace(window.title) == "" {
			continue
		}
		entry := window.ref + " " + strconv.Quote(axReportClip(window.title))
		if window.active {
			entry += " (focused)"
		}
		if len(parts) == axWaitListedWindows ||
			len([]rune(strings.Join(append(parts, entry, "..."), ", "))) > budget {
			parts = append(parts, "...")
			break
		}
		parts = append(parts, entry)
	}
	if len(parts) == 0 {
		return "none with a title"
	}
	return strings.Join(parts, ", ")
}

const axWaitListedWindows = 4

// axNearestWindowTitle returns the title of the one window whose title is
// close to wanted but does not contain it: "Find and Replace" for "Find &
// Replace", "Move/Copy Sheet" for "Move or Copy Sheet". It returns "" when
// no title or several titles are close.
func axNearestWindowTitle(inventory *axWindowInventory, wanted string) string {
	words := axSignificantWords(wanted)
	if len(words) == 0 {
		return ""
	}
	best, bestScore, ties := "", 0.0, 0
	for _, window := range inventory.windows {
		title := strings.TrimSpace(window.title)
		if title == "" {
			continue
		}
		score := axWordOverlap(words, title)
		// A title that only shares one generic word ("Settings") is not
		// close to a three-word title.
		if score < 0.5 || (len(words) > 1 && score*float64(len(words)) < 2) {
			continue
		}
		switch {
		case score > bestScore:
			best, bestScore, ties = title, score, 1
		case score == bestScore && title != best:
			ties++
		}
	}
	if ties != 1 {
		return ""
	}
	return best
}

// axWaitSeen keeps the reads of the last poll of an element wait: the
// batch window, then the application's other windows when the batch
// window had no match.
type axWaitSeen struct {
	reads []*AppSnapshot
}

func (seen *axWaitSeen) add(snapshot *AppSnapshot) {
	if snapshot.AXSnapshot != nil {
		// The batch window is read first in every poll.
		seen.reads = seen.reads[:0]
	}
	seen.reads = append(seen.reads, snapshot)
}

// explainTimeout rewrites the first line of an element wait that timed out
// as the elements closest to the query: role, name, and ref when the read
// issued one, in the order of the ranking. A candidate that differs only
// in its role spelling becomes the corrected wait. The rest of the text,
// the poll log, stays.
func (seen *axWaitSeen) explainTimeout(
	result ToolCallResult, wait *axWait, query axNearQuery, context *AXBatchContext,
) ToolCallResult {
	status, _ := result.StructuredContent["assertion_status"].(string)
	if wait.gone || axPostconditionPassed(result) || len(result.Content) == 0 ||
		(status != "predicate_false" && status != "observation_incomplete") {
		return result
	}
	candidates := []axNearCandidate{}
	owner := map[*ElementRecord]*AppSnapshot{}
	for _, snapshot := range seen.reads {
		for _, candidate := range axNearElements(snapshot, query, 3) {
			owner[candidate.record] = snapshot
			candidates = append(candidates, candidate)
		}
	}
	sort.SliceStable(candidates, func(i, j int) bool { return candidates[i].score > candidates[j].score })
	subject := strings.TrimPrefix(axWaitSubject(wait), "element ")
	where := ""
	if context != nil && context.WindowRef != "" {
		where = " in " + context.WindowRef + " or its app's other windows"
	}
	line := fmt.Sprintf("wait_timeout: after %dms nothing matches %s%s", wait.timeoutMS, subject, where)
	if status == "observation_incomplete" {
		line += " (a window of the app could not be read)"
	}
	if len(candidates) == 0 {
		line += "; nothing close either"
	}
	listed := 0
	for _, candidate := range candidates {
		label := axNearLabel(owner[candidate.record], candidate.record)
		separator := ", "
		if listed == 0 {
			separator = "; closest: "
		}
		if len([]rune(line+separator+label)) > axPublicMessageRunes {
			break
		}
		line += separator + label
		listed++
		if listed == 3 {
			break
		}
	}
	// The poll log and the generic advice of the shared timeout text are
	// replaced by the candidates; whether the window changed during the
	// wait stays, in its short form.
	text := line
	for _, detail := range strings.Split(result.Content[0].Text, "\n") {
		for _, prefix := range []string{"Window changed:", "Tree changed:", "Nothing changed:"} {
			if strings.HasPrefix(detail, prefix) {
				detail, _, _ = strings.Cut(detail, " —")
				text += "\n" + strings.Join(strings.Fields(detail), " ")
			}
		}
	}
	result.Content[0].Text = text
	if len(candidates) > 0 && candidates[0].sameWords && wait.query != nil {
		best := candidates[0].record
		if corrected := axQueryFor(owner[best], best, wait.query); corrected != nil {
			hint := map[string]any{"type": "wait", "query": corrected}
			if wait.timeoutMS != contract.AXDefaultWaitTimeout {
				hint["timeout_ms"] = wait.timeoutMS
			}
			result.StructuredContent["wait_hint"] = axHintJSON(hint)
		}
	}
	return result
}

// axQueryFor returns query changed as little as possible to match record:
// the role as the tree shows it, and a name or text filter that no longer
// matches replaced by the record's name. It returns nil when no such query
// matches.
func axQueryFor(snapshot *AppSnapshot, record *ElementRecord, query map[string]any) map[string]any {
	corrected := map[string]any{}
	for key, value := range query {
		corrected[key] = value
	}
	if _, present := corrected["role"]; present {
		role := axRenderedRole(snapshot, record)
		if role == "" {
			role = canonicalRole(record)
		}
		corrected["role"] = role
	}
	for _, field := range []string{"name_contains", "name_equals", "text_contains", "text_equals"} {
		value, present := corrected[field].(string)
		if !present {
			continue
		}
		matches := map[string]bool{
			"name_contains": containsFold(record.Name, value),
			"name_equals":   strings.EqualFold(strings.TrimSpace(record.Name), strings.TrimSpace(value)),
		}
		if strings.HasPrefix(field, "text_") {
			_, _, matched := MatchedFindTextField(record, value, field == "text_equals")
			matches[field] = matched
		}
		if !matches[field] {
			if strings.TrimSpace(record.Name) == "" {
				return nil
			}
			delete(corrected, field)
			corrected["name_contains"] = record.Name
		}
	}
	name, _ := corrected["name_contains"].(string)
	_, nameExact := corrected["name_equals"]
	if nameExact {
		name, _ = corrected["name_equals"].(string)
	}
	text, _ := corrected["text_contains"].(string)
	_, textExact := corrected["text_equals"]
	if textExact {
		text, _ = corrected["text_equals"].(string)
	}
	role, _ := corrected["role"].(string)
	state, _ := corrected["state"].(string)
	if !queryRecordMatches(snapshot, record, role, name, text, state, nameExact, textExact) {
		return nil
	}
	return corrected
}

// axWaitSubject names what an element wait waited for.
func axWaitSubject(wait *axWait) string {
	if wait.query != nil {
		return axWaitQueryText(wait.query)
	}
	return "text " + strconv.Quote(wait.text)
}

// axPublicMessageRunes is the length at which the evidence line cuts an
// error message (axPublicEvidence).
const axPublicMessageRunes = 300

// axWaitResultText names a failed wait wait_timeout in its first line; the
// polling code's text says why it did not hold.
func axWaitResultText(result ToolCallResult) ToolCallResult {
	if len(result.Content) == 0 || result.Content[0].Type != "text" {
		return result
	}
	text := result.Content[0].Text
	for _, prefix := range []string{"wait: ", "wait false: ", "wait ", "PASS: "} {
		text = strings.TrimPrefix(text, prefix)
	}
	if axPostconditionPassed(result) {
		text = "wait: " + text
	} else if !strings.HasPrefix(text, "wait_timeout") {
		text = "wait_timeout: " + text
	}
	result.Content[0].Text = text
	return result
}
