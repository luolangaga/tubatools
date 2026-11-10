package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// settleDesktop lists the fake application's windows and lets a test change
// what each read after the action shows.
type settleDesktop struct {
	*reportDesktop
	reads int
	// onRead changes the state before the n-th read after the action; n
	// starts at 1. shape changes each read after the action.
	onRead func(b *reportBackend, n int)
	shape  func(snapshot *AppSnapshot)
}

func (d *settleDesktop) Observe(request ProviderObserveRequest) (ProviderAccessibilityResult, error) {
	d.observeRequests = append(d.observeRequests, request)
	acted := len(d.actionRequests) > 0
	if acted {
		d.reads++
	}
	if acted && d.onRead != nil {
		d.onRead(d.reportBackend, d.reads)
	}
	result := d.read(request.WindowHandle)
	if acted && d.shape != nil && result.Snapshot != nil {
		d.shape(result.Snapshot)
	}
	return result, nil
}

func settleService(t *testing.T) (*Service, *settleDesktop, map[string]string) {
	t.Helper()
	s, backend := reportService(t, reportMain())
	_, refs := reportObserve(t, s, "OK", "Size")
	desktop := &settleDesktop{reportDesktop: &reportDesktop{reportBackend: backend}}
	s.Backend = desktop
	return s, desktop, refs
}

func settleCall(s *Service, refs map[string]string, timeoutMS float64, more ...any) ToolCallResult {
	actions := append([]any{
		map[string]any{"type": "click", "ref": refs["OK"]},
		map[string]any{"type": "wait", "settle": true, "timeout_ms": timeoutMS},
	}, more...)
	return s.callTool(contract.PublicToolAX, map[string]any{"mode": "act", "actions": actions})
}

// A dialog that an action opens is named in the wait's line, and keys after
// the wait go to it, as after a window wait.
func TestSettleWaitNamesTheWindowAnActionOpened(t *testing.T) {
	s, desktop, refs := settleService(t)
	desktop.onAction = func(b *reportBackend) {
		if len(b.windows) == 1 {
			b.windows[0].active = false
			b.windows = append(b.windows, reportWindow{
				handle: reportDialogHandle, role: "dialog", title: "Export", path: "/window/2", active: true,
			})
		}
	}
	result := settleCall(s, refs, 3000, map[string]any{"type": "keypress", "keys": []any{"ENTER"}})
	text := ResultText(result)
	if result.IsError || !strings.Contains(text, `"Export" opened`) || !strings.Contains(text, "focus w1 -> w") {
		t.Fatalf("settle after a dialog opened:\n%s", text)
	}
	if len(desktop.actionRequests) != 2 || desktop.actionRequests[1].WindowHandle != reportDialogHandle {
		t.Fatalf("keys after the settle wait did not go to the dialog: %+v", desktop.actionRequests)
	}
}

// An action with no visible effect fails the wait with "no change" and
// stops the batch: the later steps assumed a change.
func TestSettleWaitWithoutChangeStopsTheBatch(t *testing.T) {
	s, desktop, refs := settleService(t)
	// The click only focuses and presses its target and takes focus from
	// another element, which is not a response. The states are written as
	// the Linux provider writes them, with a leading space.
	desktop.shape = func(snapshot *AppSnapshot) {
		for index, flags := range map[int]string{3: "focused armed", 1: "active"} {
			states := strings.Trim(strings.TrimSpace(snapshot.Elements[index].States), "[]")
			snapshot.Elements[index].States = " [" + flags + " " + states + "]"
		}
		snapshot.FocusedSummary = "push button OK"
	}
	result := settleCall(s, refs, 600, map[string]any{"type": "keypress", "keys": []any{"ENTER"}})
	line := publicLine(t, result)
	if !strings.HasPrefix(line, `{"ok":false,"code":"wait_timeout","path":"actions[1]"`) ||
		!strings.Contains(ResultText(result), "no change within 600 ms after the step before it: same windows, same focus, same w1 tree") {
		t.Fatalf("settle without change: %s\n%s", line, ResultText(result))
	}
	if len(desktop.actionRequests) != 1 {
		t.Fatalf("steps after the failed settle wait were sent: %d", len(desktop.actionRequests))
	}
}

// A change that arrives after several reads, such as a list that loads
// late, is waited for, and the wait then waits for it to stop.
func TestSettleWaitWaitsForALateChange(t *testing.T) {
	s, desktop, refs := settleService(t)
	desktop.onRead = func(b *reportBackend, n int) {
		if n == 4 {
			b.value = "14"
		}
	}
	result := settleCall(s, refs, 4000)
	text := ResultText(result)
	if result.IsError || !strings.Contains(text, "wait: settled") || !strings.Contains(text, "w1 nodes: 1 changed") {
		t.Fatalf("late change:\n%s", text)
	}
	if desktop.reads < 6 {
		t.Fatalf("the wait did not keep reading until the UI was quiet: %d reads", desktop.reads)
	}
}

// A UI that keeps changing passes at the timeout with a note: the change
// the wait was for happened.
func TestSettleWaitOnAUIThatKeepsChanging(t *testing.T) {
	s, desktop, refs := settleService(t)
	desktop.onRead = func(b *reportBackend, n int) { b.value = itoa(100 + n) }
	result := settleCall(s, refs, 900)
	text := ResultText(result)
	if result.IsError || !strings.Contains(text, "was still changing at the 900 ms timeout") {
		t.Fatalf("changing UI:\n%s", text)
	}
}

// settle needs an input step before it and takes no other subject.
func TestSettleWaitDecoding(t *testing.T) {
	for _, call := range []map[string]any{
		{"mode": "act", "window_ref": "w1", "actions": []any{map[string]any{"type": "wait", "settle": true}}},
		{"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": "r1"},
			map[string]any{"type": "wait", "settle": true, "window": "Export"}}},
	} {
		if _, err := DecodeAXRequest(call); err == nil || err.Code != contract.AXCodeInvalidArgument {
			t.Fatalf("%v: want invalid_argument, got %#v", call, err)
		}
	}
	request, err := DecodeAXRequest(map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "keypress", "keys": []any{"CTRL", "O"}},
		map[string]any{"type": "wait", "settle": true},
		map[string]any{"type": "wait", "window": "Open"},
	}})
	if err != nil || !request.actions[1].wait.settle || request.actions[1].wait.timeoutMS != contract.AXDefaultWaitTimeout {
		t.Fatalf("settle after an action: %#v %#v", request, err)
	}
}
