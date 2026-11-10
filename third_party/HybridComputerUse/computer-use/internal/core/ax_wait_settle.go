package core

import (
	"crypto/sha256"
	"fmt"
	"sort"
	"strconv"
	"strings"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// A settle wait, {"type":"wait","settle":true}, needs no prediction of the
// next state. It holds once the UI has changed since the input step before
// it and then stayed the same for axSettleQuiet. A change is a difference in
// the desktop's top-level windows (opened, closed, retitled), in the window
// that has focus, or in the tree of the batch window (of the newly focused
// window once focus moved), or a pixel change the step's own readback
// reported. Each poll reads the window list and one window tree; on Linux
// the provider reuses an unchanged tree when the application sent no
// accessibility event, so the polls are cheap while the UI is quiet.
//
// The wait fails only when nothing changed by its timeout: the steps after
// it assume a change. A UI that never stops changing (a clock, a spinner)
// passes at the timeout with a note, because the change it waited for did
// happen.

const (
	axSettleQuiet = 400 * time.Millisecond
	axSettlePoll  = 150 * time.Millisecond
)

// axSettleState is one read of the signals a settle wait compares.
type axSettleState struct {
	inventoryRead bool
	// windows maps the ref of each addressable top-level window to its title.
	windows map[string]string
	// titles lists every input-target window title, sorted, including
	// windows without a ref.
	titles string
	active string
	// The tree of the focused window, or of the batch window.
	treeRef      string
	treeHandle   string
	treeRead     bool
	treeSum      string
	tree         *AppSnapshot
	pixelChanged bool
}

// axSettleWaitFollows reports whether a settle wait comes after the input
// step at index, before the next input step.
func axSettleWaitFollows(actions []axBatchAction, index int) bool {
	for next := index + 1; next < len(actions) && actions[next].actionType == "wait"; next++ {
		if actions[next].wait != nil && actions[next].wait.settle {
			return true
		}
	}
	return false
}

// notePixelChange records the pixel verdict of the input step's own
// readback, which compares the window before and after the step. It is the
// only change signal for content that has no accessibility tree.
func (state *axSettleState) notePixelChange(result ToolCallResult) {
	if state != nil && readPixelVerdict(strings.Split(ResultText(result), "\n")) == pixelsChanged {
		state.pixelChanged = true
	}
}

// readAXSettleState reads the window list and the tree of the batch window.
// When focus has moved since the read before the step (baseline) to another
// window with a tree, it reads that window instead: a dialog the step
// opened is where the next change shows.
func (s *Service) readAXSettleState(context *AXBatchContext, baseline *axSettleState) *axSettleState {
	state := &axSettleState{windows: map[string]string{}}
	app := FirstNonEmpty(context.AppRef, context.AppToken)
	state.treeRef, state.treeHandle = context.WindowRef, context.WindowHandle
	if inventory, _ := s.listDesktopWindows(true); inventory != nil {
		state.inventoryRead = true
		titles := append([]string{}, inventory.titles...)
		sort.Strings(titles)
		state.titles = strings.Join(titles, "\x00")
		for _, window := range inventory.windows {
			state.windows[window.ref] = window.title
			if !window.active {
				continue
			}
			state.active = window.ref
			if baseline != nil && baseline.inventoryRead && baseline.active != window.ref &&
				!window.noTree && validInternalWindowHandle(window.handle) {
				app, state.treeRef, state.treeHandle = window.appRef, window.ref, window.handle
			}
		}
	}
	if state.treeHandle == "" {
		return state
	}
	noScreenshot := false
	snapshot, _, result := s.readSnapshot(app, AXServiceRequest{
		Operation:          axServiceObserve,
		App:                app,
		IncludeScreenshot:  &noScreenshot,
		TargetWindowHandle: state.treeHandle,
		Scope:              contract.AXScopeWindow,
		TreeView:           contract.TreeViewWorking,
	})
	if !result.IsError && snapshot != nil {
		state.treeRead, state.tree, state.treeSum = true, snapshot, axSettleTreeSum(snapshot)
	}
	return state
}

// axSettleTreeSum digests the parts of a window read that the model sees:
// title, selection, and every element's role, text, and states. The
// transient flags are left out: a click focuses its target, and takes focus
// from another element, even when the application does nothing, and that
// alone is not the change the wait is for. Focus moving to another window
// still counts, through the window list.
func axSettleTreeSum(snapshot *AppSnapshot) string {
	var canonical strings.Builder
	for _, field := range []string{snapshot.WindowTitle, snapshot.SelectedText} {
		canonical.WriteString(field)
		canonical.WriteByte('\x00')
	}
	for index := range snapshot.Elements {
		record := &snapshot.Elements[index]
		canonical.WriteString(strconv.Itoa(len(record.RuntimeID)))
		for _, field := range []string{canonicalRole(record), record.Name, record.Value,
			record.Description, record.Placeholder, axSettleStates(record)} {
			canonical.WriteByte('\x00')
			canonical.WriteString(field)
		}
		canonical.WriteByte('\n')
	}
	sum := sha256.Sum256([]byte(canonical.String()))
	return fmt.Sprintf("%x", sum[:12])
}

// axSettleTransientStates are the flags an input step sets on its target,
// or moves from one element to another, even when the application does
// nothing: keyboard focus and a press or hover in progress. They do not count
// as a change; checked, selected, and expanded do.
var axSettleTransientStates = map[string]bool{
	"focused": true, "active": true, "armed": true, "pressed": true, "hover": true, "hovered": true,
}

// axSettleStates drops the transient flags from a state list such as
// " [focused selected]".
func axSettleStates(record *ElementRecord) string {
	kept := []string{}
	for _, state := range recordStateTokens(record) {
		if !axSettleTransientStates[strings.ToLower(state)] {
			kept = append(kept, state)
		}
	}
	return strings.Join(kept, " ")
}

// same reports whether two reads show the same UI. A signal that one of the
// reads could not take is compared only by whether it was read.
func (state *axSettleState) same(other *axSettleState) bool {
	if state.inventoryRead && other.inventoryRead &&
		(state.titles != other.titles || state.active != other.active ||
			!sameStringMap(state.windows, other.windows)) {
		return false
	}
	if state.treeHandle != other.treeHandle || state.treeRead != other.treeRead {
		return false
	}
	return state.treeSum == other.treeSum
}

func sameStringMap(first, second map[string]string) bool {
	if len(first) != len(second) {
		return false
	}
	for key, value := range first {
		if other, ok := second[key]; !ok || other != value {
			return false
		}
	}
	return true
}

// waitForSettle polls until the UI changed since the input step before the
// wait and then stayed the same for axSettleQuiet, or the timeout passes.
func (s *Service) waitForSettle(context *AXBatchContext, wait *axWait) ToolCallResult {
	start := time.Now()
	deadline := start.Add(time.Duration(wait.timeoutMS) * time.Millisecond)
	baseline := context.settleBaseline
	var changedAt, lastChangeAt time.Time
	if baseline != nil && baseline.pixelChanged {
		changedAt, lastChangeAt = start, start
	}
	previous := baseline
	for attempts := 1; ; attempts++ {
		current := s.readAXSettleState(context, baseline)
		now := time.Now()
		switch {
		case baseline == nil:
			// Without a read before the step, the first read is the
			// reference, and only later changes count.
			baseline, previous = current, current
		case !previous.same(current):
			if changedAt.IsZero() {
				changedAt = now
			}
			lastChangeAt = now
		}
		previous = current
		quiet := now.Sub(lastChangeAt)
		if !changedAt.IsZero() && quiet >= axSettleQuiet {
			text := fmt.Sprintf("wait: settled after %s: %s",
				axSettleSeconds(now.Sub(start)), axSettleSummary(baseline, current))
			return s.axSettlePassed(context, current, text, attempts)
		}
		if !now.Before(deadline) {
			if changedAt.IsZero() {
				return axAssertionResult(fmt.Sprintf(
					"wait_timeout: no change within %d ms after the step before it: %s. "+
						"The step may have had no effect; read the state before repeating it.",
					wait.timeoutMS, axSettleUnchanged(current)), false, "timeout", attempts, 0)
			}
			text := fmt.Sprintf("wait: the UI changed (%s) and was still changing at the %d ms timeout, "+
				"%s after its last change; read it again before relying on details",
				axSettleSummary(baseline, current), wait.timeoutMS, axSettleSeconds(quiet))
			return s.axSettlePassed(context, current, text, attempts)
		}
		time.Sleep(min(axSettlePoll, time.Until(deadline)))
	}
}

// axSettlePassed returns a passed settle wait. When focus moved to another
// window with a tree, the batch continues in that window.
func (s *Service) axSettlePassed(context *AXBatchContext, current *axSettleState, text string, attempts int) ToolCallResult {
	result := axAssertionResult(text, false, "pass", attempts, 0)
	if current.active != "" && current.active != context.WindowRef && current.treeRef == current.active {
		result.StructuredContent["wait_window_ref"] = current.active
	}
	return result
}

func axSettleSeconds(duration time.Duration) string {
	return strconv.FormatFloat(duration.Seconds(), 'f', 1, 64) + " s"
}

// axSettleSummary names what changed between the read before the step and
// the last read, so the next step can use it without another read.
func axSettleSummary(before, after *axSettleState) string {
	parts := []string{}
	if before.inventoryRead && after.inventoryRead {
		for _, ref := range axSettleSortedRefs(after.windows) {
			title, existed := before.windows[ref]
			switch {
			case !existed:
				parts = append(parts, "window "+ref+" "+strconv.Quote(after.windows[ref])+" opened")
			case title != after.windows[ref]:
				parts = append(parts, ref+" title "+strconv.Quote(title)+" -> "+strconv.Quote(after.windows[ref]))
			}
		}
		for _, ref := range axSettleSortedRefs(before.windows) {
			if _, remains := after.windows[ref]; !remains {
				parts = append(parts, "window "+ref+" "+strconv.Quote(before.windows[ref])+" closed")
			}
		}
		if before.active != after.active && after.active != "" {
			parts = append(parts, "focus "+FirstNonEmpty(before.active, "none")+" -> "+after.active)
		}
	}
	if before.treeHandle == after.treeHandle && before.treeSum != after.treeSum && after.treeRead {
		parts = append(parts, after.treeRef+" "+axSettleTreeChange(before.tree, after.tree))
	}
	if len(parts) == 0 && before.pixelChanged {
		parts = append(parts, "window pixels changed; windows and tree did not")
	}
	if len(parts) == 0 {
		parts = append(parts, "the UI changed and returned to its earlier state")
	}
	return strings.Join(parts, "; ")
}

// axSettleTreeChange counts the nodes that appeared, disappeared, or
// changed between two reads of one window.
func axSettleTreeChange(before, after *AppSnapshot) string {
	change, compared := axReportWindowResponse(before, after, "")
	if !compared || !change.any() {
		return "tree changed"
	}
	parts := []string{}
	for _, count := range []struct {
		n    int
		verb string
	}{{change.otherAdded, "appeared"}, {change.otherRemoved, "disappeared"}, {change.otherChanged, "changed"}} {
		if count.n > 0 {
			parts = append(parts, strconv.Itoa(count.n)+" "+count.verb)
		}
	}
	return "nodes: " + strings.Join(parts, ", ")
}

func axSettleUnchanged(state *axSettleState) string {
	parts := []string{}
	if state.inventoryRead {
		parts = append(parts, "same windows", "same focus")
	}
	if state.treeRead {
		parts = append(parts, "same "+FirstNonEmpty(state.treeRef, "window")+" tree")
	}
	if len(parts) == 0 {
		return "the windows and the tree could not be read"
	}
	return strings.Join(parts, ", ")
}

func axSettleSortedRefs(values map[string]string) []string {
	keys := make([]string, 0, len(values))
	for key := range values {
		keys = append(keys, key)
	}
	sort.Strings(keys)
	return keys
}
