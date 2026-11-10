package core

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestCompactAXActionContentDropsRoutineRouteNotes(t *testing.T) {
	result := compactAXActionContent(ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: strings.Join([]string{
			"Note: [a11y] Resolved element 12 to button Save",
			"Note: [a11y][semantic] Invoked the element's AT-SPI accessibility action.",
			"Note: MODAL DIALOG: Preferences",
			"ax:",
			` - "dialog \"Preferences\""`,
		}, "\n")}},
	})
	text := ResultText(result)
	if strings.Contains(text, "Resolved element") ||
		strings.Contains(text, "Invoked the element") {
		t.Fatalf("routine action narration leaked: %s", text)
	}
	if !strings.Contains(text, "MODAL DIALOG") ||
		!strings.Contains(text, `dialog \"Preferences\"`) {
		t.Fatalf("decision-relevant action evidence was removed: %s", text)
	}
}

func TestAXBatchSourceKeepsExecutionContract(t *testing.T) {
	source := mainGoSource(t)
	for _, fragment := range []string{
		`axBatchActionObservation(`,
		`if index == count-1 && !waitsFollow`,
		`index < len(actions)-1 && index == lastInput`,
		`s.runAXWait(context, action.wait)`,
		`s.executeAXBatchAction(`,
		`"observation_required"] = true`,
	} {
		if !strings.Contains(source, fragment) {
			t.Fatalf("a11y batch execution contract missing %q", fragment)
		}
	}
}

func TestDecodeAXActionsAcceptsFlatShapes(t *testing.T) {
	actions, err := decodeAXActions([]any{
		map[string]any{"type": "click", "ref": "r7"},
		map[string]any{"type": "keypress", "keys": []any{"CTRL", "L"}},
		map[string]any{"type": "type", "ref": "r8", "text": "Lovelace"},
		map[string]any{"type": "set_value", "ref": "r9", "value": ""},
		map[string]any{"type": "scroll", "direction": "down", "pages": 2.5},
		map[string]any{"type": "activate", "title": "Report"},
		map[string]any{"type": "close", "ref": "w3"},
	})
	if err != nil || len(actions) != 7 {
		t.Fatalf("valid batch rejected: %#v %v", actions, err)
	}
	if actions[1].key != "CTRL+L" || actions[2].targetRef != "r8" ||
		actions[4].pages != 2.5 || actions[5].title != "Report" ||
		actions[6].targetRef != "w3" {
		t.Fatalf("decoded actions: %#v", actions)
	}
}

func TestDecodeAXActionsRejectsWithPathAndHint(t *testing.T) {
	tooMany := make([]any, contract.AXBatchMaxActions+1)
	for index := range tooMany {
		tooMany[index] = map[string]any{"type": "keypress", "keys": []any{"TAB"}}
	}
	for name, test := range map[string]struct {
		raw     any
		path    string
		message string
	}{
		"empty":        {[]any{}, "actions", "at least one action"},
		"too many":     {tooMany, "actions", "at most"},
		"not an array": {"click r1", "actions", "array"},
		"click needs ref": {[]any{map[string]any{"type": "click"}},
			"actions[0].ref", "click needs ref"},
		"wN on click": {[]any{map[string]any{"type": "click", "ref": "w1"}},
			"actions[0].ref", "wN is only for activate and close"},
		"rN on close": {[]any{map[string]any{"type": "close", "ref": "r1"}},
			"actions[0].ref", "rN is an element"},
		"input after close": {[]any{
			map[string]any{"type": "close", "ref": "w1"},
			map[string]any{"type": "click", "ref": "r1"},
		}, "actions[1]", "a closed window cannot take later input"},
		"activate both": {[]any{map[string]any{"type": "activate", "ref": "w1", "title": "x"}},
			"actions[0]", "exactly one of ref (wN) or title"},
		"activate neither": {[]any{map[string]any{"type": "activate"}},
			"actions[0]", "exactly one of ref (wN) or title"},
		"blank title": {[]any{map[string]any{"type": "activate", "title": " "}},
			"actions[0].title", "non-blank"},
		"field of another type": {[]any{map[string]any{"type": "click", "ref": "r1", "text": "x"}},
			"actions[0].text", "click does not accept"},
		"legacy key": {[]any{map[string]any{"type": "keypress", "key": "Tab"}},
			"actions[0].key", `use "keys"`},
		"blank key": {[]any{map[string]any{"type": "keypress", "keys": []any{" "}}},
			"actions[0].keys[0]", "key name"},
		"intent": {[]any{map[string]any{"type": "click", "ref": "r1", "intent": "Save"}},
			"actions[0].intent", "click does not accept"},
		"index target": {[]any{map[string]any{"type": "click", "index": 7.0}},
			"actions[0].index", "click does not accept"},
		"wait without subject": {[]any{map[string]any{"type": "wait", "gone": true}},
			"actions[0]", "exactly one of window, query, ref, text, or settle"},
		"wait two subjects": {[]any{map[string]any{"type": "wait", "window": "x", "ref": "r1"}},
			"actions[0]", "exactly one of window, query, ref, text, or settle"},
		"settle first": {[]any{map[string]any{"type": "wait", "settle": true}},
			"actions[0]", "put it after an action"},
		"settle false": {[]any{map[string]any{"type": "keypress", "keys": []any{"TAB"}}, map[string]any{"type": "wait", "settle": false}},
			"actions[1].settle", "settle must be true"},
		"settle gone": {[]any{map[string]any{"type": "keypress", "keys": []any{"TAB"}}, map[string]any{"type": "wait", "settle": true, "gone": true}},
			"actions[1].gone", "no gone form"},
		"settle with text": {[]any{map[string]any{"type": "keypress", "keys": []any{"TAB"}}, map[string]any{"type": "wait", "settle": true, "text": "x"}},
			"actions[1]", "exactly one of"},
		"wait value without ref": {[]any{map[string]any{"type": "wait", "window": "x", "value": "1"}},
			"actions[0].value", "add ref"},
		"wait two qualifiers": {[]any{map[string]any{"type": "wait", "ref": "r1", "value": "1", "state": "checked"}},
			"actions[0]", "one of value, state, or text"},
		"wait wN ref": {[]any{map[string]any{"type": "wait", "ref": "w1"}},
			"actions[0].ref", `"window":"title"`},
		"wait query field": {[]any{map[string]any{"type": "wait", "query": map[string]any{"name": "Save"}}},
			"actions[0].query.name", "query does not accept"},
		"wait timeout": {[]any{map[string]any{"type": "wait", "window": "x", "timeout_ms": 0.0}},
			"actions[0].timeout_ms", "from 1 to 60000"},
		"wait gone string": {[]any{map[string]any{"type": "wait", "window": "x", "gone": "yes"}},
			"actions[0].gone", "true or false"},
		"wait value number": {[]any{map[string]any{"type": "wait", "ref": "r1", "value": 14.0}},
			"actions[0].value", "non-blank string"},
		"per-action observe": {[]any{map[string]any{"type": "keypress", "keys": []any{"TAB"}, "observe": "none"}},
			"actions[0].observe", "keypress does not accept"},
		"type case": {[]any{map[string]any{"type": "Click", "ref": "r1"}},
			"actions[0].type", "type must be one of"},
		"direction case": {[]any{map[string]any{"type": "scroll", "direction": "UP"}},
			"actions[0].direction", "scroll needs direction"},
		"text budget": {[]any{
			map[string]any{"type": "type", "text": strings.Repeat("a", contract.MaxActionText)},
			map[string]any{"type": "type", "text": strings.Repeat("b", contract.MaxActionText)},
			map[string]any{"type": "type", "text": "c"},
		}, "actions[2]", "limited to"},
	} {
		_, err := decodeAXActions(test.raw)
		if err == nil || err.Code != contract.AXCodeInvalidArgument ||
			err.path != test.path || !strings.Contains(err.Message, test.message) {
			t.Fatalf("%s: got %#v", name, err)
		}
	}
}

// TestModelWrittenActionShapesGetUsableAnswers covers shapes models wrote in
// a real panel. The first is valid now; the others name the field and show
// the accepted shape.
func TestModelWrittenActionShapesGetUsableAnswers(t *testing.T) {
	if _, err := decodeAXActions([]any{map[string]any{"ref": "r103", "type": "click"}}); err != nil {
		t.Fatalf("flat ref click must be valid: %v", err)
	}
	for _, test := range []struct {
		action map[string]any
		path   string
		hint   string
	}{
		{map[string]any{"ref": "w1", "action": "close"}, "actions[0].action", `{"type":"close","ref":"w1"}`},
		{map[string]any{"ref": "r1", "action": "press"}, "actions[0].action", `{"type":"click","ref":"r1"}`},
		{map[string]any{"type": "press", "ref": "r1"}, "actions[0].type", `{"type":"click","ref":"r1"}`},
		{map[string]any{"type": "click", "target": map[string]any{"ref": "r4"}}, "actions[0].target", `{"type":"click","ref":"r4"}`},
		{map[string]any{"type": "keypress", "key": "CTRL+S"}, "actions[0].key", `{"type":"keypress","keys":["CTRL","S"]}`},
	} {
		_, err := decodeAXActions([]any{test.action})
		if err == nil || err.path != test.path || err.Hint != test.hint {
			t.Fatalf("%v: got %#v", test.action, err)
		}
		if _, hintErr := decodeAXActions([]any{decodeJSONForTest(t, err.Hint)}); hintErr != nil {
			t.Fatalf("hint %s is not a valid action: %v", err.Hint, hintErr)
		}
	}
}

func decodeJSONForTest(t *testing.T, text string) map[string]any {
	t.Helper()
	var value map[string]any
	if err := json.Unmarshal([]byte(text), &value); err != nil {
		t.Fatalf("decode %s: %v", text, err)
	}
	return value
}

func TestBatchMutationSummaryPreservesActionUnknown(t *testing.T) {
	emitted, partial, unknown := batchMutationSummary([]map[string]any{
		{
			"input_events_emitted": false,
			"mutation_unknown":     true,
		},
	})
	if emitted || partial || !unknown {
		t.Fatalf(
			"batch summary lost mutation uncertainty: emitted=%t partial=%t unknown=%t",
			emitted, partial, unknown,
		)
	}
}

func TestRefOwnedBatchDerivesOneWindowAndKeyboardInheritance(t *testing.T) {
	service, appRef, windowRef, elementRef := sheetBatchService(t)
	actions := []axBatchAction{
		{actionType: "click", targetRef: elementRef},
		{actionType: "keypress", key: "ENTER"},
	}
	context, err := service.deriveAXBatchContext("", actions, nil)
	if err != nil {
		t.Fatal(err)
	}
	if context.AppRef != appRef || context.AppToken != appRef ||
		context.WindowRef != windowRef || context.WindowHandle == "" {
		t.Fatalf("unexpected ref-owned context: %#v", context)
	}
}

func TestWaitRefsArePreflightedBeforeFirstInput(t *testing.T) {
	service, _, _, elementRef := sheetBatchService(t)
	backend := &recordingPlatformBackend{}
	service.Backend = backend

	result := service.axBatch(
		[]any{
			map[string]any{"type": "click", "ref": elementRef},
			map[string]any{"type": "keypress", "keys": []any{"ENTER"}},
			map[string]any{"type": "wait", "ref": "r999999", "state": "selected"},
		},
		observation{},
		"",
	)
	if !result.IsError ||
		result.StructuredContent["action_attempted"] != false {
		t.Fatalf("an unknown wait ref must fail before input: %#v", result)
	}
	if len(backend.calls) != 0 {
		t.Fatalf("preflight failure reached provider: %#v", backend.calls)
	}
	if !strings.Contains(
		ResultText(result),
		"actions[2].ref",
	) {
		t.Fatalf("preflight error did not name the wait step: %q",
			ResultText(result))
	}
}

func TestRefLessBatchWithoutFocusOrWindowHasNoWindow(t *testing.T) {
	service, _, _, _ := sheetBatchService(t)
	_, err := service.deriveAXBatchContext("", []axBatchAction{{
		actionType: "keypress",
		key:        "CTRL+S",
	}}, nil)
	if err == nil || !strings.Contains(err.Error(), "the batch has no window") {
		t.Fatalf("expected no-window rejection, got %v", err)
	}
}

func TestCrossWindowBatchFailsBeforeInput(t *testing.T) {
	service, appRef, _, firstRef := sheetBatchService(t)
	secondHandle := "w2:fedcba9876543210fedcba9876543210"
	secondRef := service.RefRegistry.Acquire(
		"libreoffice", secondHandle, `button "OK"`, CanonicalRole("button"),
		"OK", "c1",
		":1.84", "/window/2/ok", "pid:10:start:100",
		":1.84", "/window/2", "pid:10:start:100", "dialog", "Question",
		appRef,
	)
	_, err := service.deriveAXBatchContext(
		"", []axBatchAction{
			{actionType: "click", targetRef: firstRef},
			{actionType: "click", targetRef: secondRef},
		}, nil,
	)
	if err == nil || !strings.Contains(err.Error(), "batch spans two windows") {
		t.Fatalf("expected cross-window rejection, got %v", err)
	}
}

func TestBatchAcceptsMatchingWindowRefAndRejectsAnother(t *testing.T) {
	service, appRef, windowRef, elementRef := sheetBatchService(t)
	context, err := service.deriveAXBatchContext(windowRef, []axBatchAction{{
		actionType: "click", targetRef: elementRef,
	}}, nil)
	if err != nil || context.WindowRef != windowRef {
		t.Fatalf("matching window_ref was rejected: %#v %v", context, err)
	}
	other := service.WindowRefRegistry.Acquire(
		"libreoffice", "w2:fedcba9876543210fedcba9876543210", `dialog "Question"`, "c1",
		":1.84", "/window/2", "pid:10:start:100", "dialog", "Question", appRef,
	)
	_, err = service.deriveAXBatchContext(other, []axBatchAction{{
		actionType: "click", targetRef: elementRef,
	}}, nil)
	if err == nil || !strings.Contains(err.Error(), "omit window_ref") {
		t.Fatalf("conflicting window_ref was accepted: %v", err)
	}
}

func TestBatchContextCarriesPrivateWindowExpectation(t *testing.T) {
	service, _, windowRef, elementRef := sheetBatchService(t)
	context, err := service.deriveAXBatchContext(
		"", []axBatchAction{
			{actionType: "click", targetRef: elementRef},
		}, nil,
	)
	if err != nil {
		t.Fatal(err)
	}
	if context.WindowRef != windowRef || context.expectedWindow == nil {
		t.Fatalf("batch context lacks expected window: %#v", context)
	}
	if context.expectedWindow.WindowHandle != context.WindowHandle ||
		context.expectedWindow.ProviderBusName == "" ||
		context.expectedWindow.ProcessGeneration == "" {
		t.Fatalf("incomplete expected window: %#v", context.expectedWindow)
	}
}

func TestBatchStopsAfterConfirmedWindowTransition(t *testing.T) {
	service, appRef, _, _ := sheetBatchService(t)
	context := &AXBatchContext{
		AppRef:       appRef,
		AppToken:     appRef,
		WindowHandle: "w2:0123456789abcdef0123456789abcdef",
	}
	service.snapshots[appRef] = &AppSnapshot{
		WindowHandle: "w2:fedcba9876543210fedcba9876543210",
	}
	if got := service.axBatchWindowContinuity(context); got != axWindowChanged {
		t.Fatal("confirmed logical-window transition was not detected")
	}
}

func TestBatchAllowsProviderRebindInsideSameNativeWindow(t *testing.T) {
	service, appRef, windowRef, _ := sheetBatchService(t)
	handle := "w2:0123456789abcdef0123456789abcdef"
	context := &AXBatchContext{
		AppRef:       appRef,
		AppToken:     appRef,
		WindowRef:    windowRef,
		WindowHandle: handle,
	}
	service.snapshots[appRef] = &AppSnapshot{
		WindowHandle:    handle,
		publicWindowRef: windowRef,
		App: AppDescriptor{
			ProviderBusName:   ":1.84",
			ProcessGeneration: "pid:10:start:100",
		},
	}
	if got := service.axBatchWindowContinuity(context); got != axWindowSame {
		t.Fatal("provider rebind inside the same native window must keep wN valid")
	}
}

func TestBatchStopsWhenWindowContinuityCannotBeSampled(t *testing.T) {
	service, appRef, windowRef, _ := sheetBatchService(t)
	context := &AXBatchContext{
		AppRef:       appRef,
		AppToken:     appRef,
		WindowRef:    windowRef,
		WindowHandle: "w2:0123456789abcdef0123456789abcdef",
	}
	if got := service.axBatchWindowContinuity(context); got != axWindowUnknown {
		t.Fatalf("missing refreshed snapshot was reported as %s", got)
	}
	service.snapshots[appRef] = &AppSnapshot{}
	if got := service.axBatchWindowContinuity(context); got != axWindowUnknown {
		t.Fatalf("missing refreshed window handle was reported as %s", got)
	}
}

func TestBatchStopsWhenLogicalWindowChangesOnSameNativeHandle(t *testing.T) {
	service, appRef, oldWindowRef, _ := sheetBatchService(t)
	handle := "w2:0123456789abcdef0123456789abcdef"
	newWindowRef := service.WindowRefRegistry.Acquire(
		"soffice", handle, `dialog "Question"`, "c2",
		":1.84", "/app/dialog/question", "pid:10:start:100",
		"dialog", "Question", appRef,
	)
	if newWindowRef == "" || newWindowRef == oldWindowRef {
		t.Fatalf(
			"logical replacement did not receive a new ref: old=%q new=%q",
			oldWindowRef, newWindowRef,
		)
	}
	service.snapshots[appRef] = &AppSnapshot{
		WindowHandle:    handle,
		publicAppRef:    appRef,
		publicWindowRef: newWindowRef,
	}
	context := &AXBatchContext{
		AppRef:       appRef,
		AppToken:     appRef,
		WindowRef:    oldWindowRef,
		WindowHandle: handle,
	}
	if got := service.axBatchWindowContinuity(context); got != axWindowChanged {
		t.Fatalf("same-handle logical replacement was reported as %s", got)
	}
}

// axBatch decodes public action objects and runs them, so tests can drive
// a batch from raw JSON-shaped actions. The public act path decodes in
// DecodeAXRequest and calls axBatchDecoded directly.
func (s *Service) axBatch(
	rawActions any,
	obs observation,
	windowRef string,
) ToolCallResult {
	actions, argErr := decodeAXActions(rawActions)
	if argErr != nil {
		requested := 0
		if items, ok := rawActions.([]any); ok {
			requested = len(items)
		}
		return axArgErrorResult(argErr, requested)
	}
	return s.axBatchDecoded(actions, obs, windowRef)
}
