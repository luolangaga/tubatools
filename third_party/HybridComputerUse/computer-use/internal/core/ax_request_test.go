package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestDecodeAXRequestAcceptsEachMode(t *testing.T) {
	for name, args := range map[string]map[string]any{
		"apps":                 {"mode": "apps"},
		"observe app":          {"mode": "observe", "app": "a1"},
		"observe pid":          {"mode": "observe", "app": "1234"},
		"observe window":       {"mode": "observe", "window_ref": "w1", "root": "r5", "observe": "full"},
		"observe focused root": {"mode": "observe", "window_ref": "w1", "root": "focused"},
		"wait only": {"mode": "act", "actions": []any{
			map[string]any{"type": "wait", "window": "Saved", "gone": true}}},
		"wait ref value": {"mode": "act", "actions": []any{
			map[string]any{"type": "wait", "ref": "r3", "value": "5", "timeout_ms": 900.0}}},
		"find": {"mode": "find", "window_ref": "w1", "limit": 50.0,
			"query": map[string]any{"role": "button", "name_contains": "Save"}},
		"find cursor": {"mode": "find", "cursor": "f2", "limit": 10.0},
		"act": {"mode": "act", "observe": "tree", "window_ref": "w1",
			"actions": []any{map[string]any{"type": "click", "ref": "r1"},
				map[string]any{"type": "wait", "query": map[string]any{"role": "dialog"}},
				map[string]any{"type": "click", "ref": "r2"},
				map[string]any{"type": "wait", "text": "Saved", "gone": true}}},
	} {
		if _, err := DecodeAXRequest(args); err != nil {
			t.Fatalf("%s rejected: %#v", name, err)
		}
	}
}

func TestDecodeAXRequestRejectsWithPathAndHint(t *testing.T) {
	for name, test := range map[string]struct {
		args    map[string]any
		path    string
		message string
	}{
		"missing mode": {map[string]any{"app": "Editor"}, "mode", "mode is required"},
		"mode case":    {map[string]any{"mode": "OBSERVE"}, "mode", "mode must be"},
		"assert_ax removed": {map[string]any{"mode": "assert_ax"}, "mode",
			"only wait steps"},
		"observation_target removed": {map[string]any{"mode": "observe", "observation_target": "apps"},
			"observation_target", `mode "apps"`},
		"app_ref renamed": {map[string]any{"mode": "observe", "app_ref": "a1"},
			"app_ref", `use "app"`},
		"postcondition removed": {map[string]any{"mode": "act", "postcondition": map[string]any{},
			"actions": []any{map[string]any{"type": "click", "ref": "r1"}}},
			"postcondition", `of type "wait"`},
		"raw acquisition controls": {map[string]any{"mode": "observe", "app": "x", "text_limit": 100.0},
			"text_limit", "does not accept"},
		"apps takes nothing": {map[string]any{"mode": "apps", "app": "x"},
			"app", "does not accept"},
		"app and window_ref": {map[string]any{"mode": "observe", "app": "x", "window_ref": "w1"},
			"window_ref", "not both"},
		"observe without identity": {map[string]any{"mode": "observe"}, "app",
			"observe needs app or window_ref"},
		"observe none": {map[string]any{"mode": "observe", "app": "x", "observe": "none"},
			"observe", "only wait steps"},
		"observe value case": {map[string]any{"mode": "observe", "app": "x", "observe": "Diff"},
			"observe", "observe must be one of"},
		"notes alias": {map[string]any{"mode": "act", "observe": "notes",
			"actions": []any{map[string]any{"type": "click", "ref": "r1"}}},
			"observe", "observe must be one of"},
		"selector root removed": {map[string]any{"mode": "observe", "app": "x",
			"root": map[string]any{"selector": map[string]any{"role": "list"}}},
			"root", `root must be "focused" or rN`},
		"wN root": {map[string]any{"mode": "observe", "app": "x", "root": "w1"},
			"root", `root must be "focused" or rN`},
		"invented ref": {map[string]any{"mode": "act",
			"actions": []any{map[string]any{"type": "click", "ref": "r??"}}},
			"actions[0].ref", "copied exactly"},
		"pid as number":      {map[string]any{"mode": "observe", "app": 1234.0}, "app", "non-blank string"},
		"find without query": {map[string]any{"mode": "find", "app": "x"}, "query", "find requires query"},
		"find empty query": {map[string]any{"mode": "find", "app": "x", "query": map[string]any{}},
			"query", "at least one filter"},
		"find unknown filter": {map[string]any{"mode": "find", "app": "x", "query": map[string]any{"name": "Save"}},
			"query.name", "query does not accept"},
		"find both name forms": {map[string]any{"mode": "find", "app": "x",
			"query": map[string]any{"name_equals": "Save", "name_contains": "Sa"}},
			"query.name_contains", "not both"},
		"find blank filter": {map[string]any{"mode": "find", "app": "x", "query": map[string]any{"role": " "}},
			"query.role", "non-blank"},
		"limit too large": {map[string]any{"mode": "find", "app": "x", "limit": 51.0,
			"query": map[string]any{"role": "button"}}, "limit", "from 1 to 50"},
		"limit inside query": {map[string]any{"mode": "find", "app": "x",
			"query": map[string]any{"role": "button", "limit": 5.0}}, "query.limit", "query does not accept"},
		"cursor with query": {map[string]any{"mode": "find", "cursor": "f1",
			"query": map[string]any{"role": "button"}}, "query", "send only mode, cursor, and limit"},
		"cursor on observe": {map[string]any{"mode": "observe", "app": "x", "cursor": "f1"},
			"cursor", "does not accept"},
		"act without actions": {map[string]any{"mode": "act"}, "actions", "act requires actions"},
		"act with app": {map[string]any{"mode": "act", "app": "x",
			"actions": []any{map[string]any{"type": "click", "ref": "r1"}}}, "app", "does not accept"},
	} {
		_, err := DecodeAXRequest(test.args)
		if err == nil || err.Code != contract.AXCodeInvalidArgument || err.path != test.path ||
			!strings.Contains(err.Message, test.message) || err.Hint == "" {
			t.Fatalf("%s: got %#v", name, err)
		}
	}
}

func TestRefWaitPredicateChecksEachQualifier(t *testing.T) {
	for name, test := range map[string]struct {
		wait   axWait
		record ElementRecord
		holds  bool
	}{
		"exists":        {axWait{ref: "r1"}, ElementRecord{}, true},
		"gone":          {axWait{ref: "r1", gone: true}, ElementRecord{}, false},
		"state":         {axWait{ref: "r1", state: "checked"}, ElementRecord{States: "[checked]"}, true},
		"not state":     {axWait{ref: "r1", state: "checked", gone: true}, ElementRecord{States: "[checked]"}, false},
		"value":         {axWait{ref: "r1", value: "14"}, ElementRecord{Value: " 14 "}, true},
		"value differs": {axWait{ref: "r1", value: "14"}, ElementRecord{Value: "140"}, false},
		"text":          {axWait{ref: "r1", text: "Done"}, ElementRecord{Name: "All done"}, true},
		"text gone":     {axWait{ref: "r1", text: "Done", gone: true}, ElementRecord{Name: "All done"}, false},
	} {
		predicate, err := axRefWaitPredicate(&test.wait)
		if err != nil {
			t.Fatalf("%s: %v", name, err)
		}
		if held, observed := predicate.goal.check(&test.record, nil); held != test.holds {
			t.Fatalf("%s: held=%v want %v (%s)", name, held, test.holds, observed)
		}
	}
}

// The schema accepts a text of only spaces; a ref wait rejects it.
func TestRefWaitPredicateRejectsBlankText(t *testing.T) {
	if _, err := axRefWaitPredicate(&axWait{ref: "r1", text: "  "}); err == nil {
		t.Fatal("a blank text was accepted")
	}
}

func TestReadIdentityDerivesOwnerFromElementRef(t *testing.T) {
	service, appRef, windowRef, elementRef := sheetBatchService(t)
	got, err := service.resolveAXReadIdentity("", "", elementRef)
	if err != nil || got.appToken != appRef || got.windowRef != windowRef {
		t.Fatalf("owned assertion identity=%#v err=%v", got, err)
	}
	got, err = service.resolveAXReadIdentity("", windowRef, elementRef)
	if err != nil || got.appToken != appRef ||
		got.windowRef != windowRef || got.windowHandle == "" {
		t.Fatalf("window-scoped assertion identity=%#v err=%v", got, err)
	}
	got, err = service.resolveAXReadIdentity(appRef, "", "")
	if err != nil || got.appToken != appRef {
		t.Fatalf("app accepts aN: %#v err=%v", got, err)
	}
}

func TestReadIdentityRejectsMismatchedWindowRef(t *testing.T) {
	service, appRef, _, elementRef := sheetBatchService(t)
	other := service.WindowRefRegistry.Acquire(
		"libreoffice", "w2:fedcba9876543210fedcba9876543210", `dialog "Question"`, "c1",
		":1.84", "/window/2", "pid:10:start:100", "dialog", "Question", appRef,
	)
	if _, err := service.resolveAXReadIdentity("", other, elementRef); err == nil ||
		!strings.Contains(err.Error(), "omit window_ref") {
		t.Fatalf("mismatched window_ref accepted: %v", err)
	}
}
