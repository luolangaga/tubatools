package core

import (
	"encoding/json"
	"fmt"
	"math"
	"regexp"
	"runtime"
	"sort"
	"strconv"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// This file decodes the public AX arguments. The published input schema is
// flat (no oneOf), so it cannot say which fields belong to which mode or
// action type; those rules live here, and every rejection names the field
// and shows a corrected call. Decoding is strict: no case folding, aliases,
// or numeric strings. The services below receive only decoded values.

// axPublicErrorCodes is the complete list of public AX error codes. The
// documentation and the instructions list the same codes.
var axPublicErrorCodes = []string{
	contract.AXCodeInvalidArgument, contract.AXCodeStaleRef, contract.AXCodeWindowChanged, contract.AXCodeWaitTimeout,
	contract.AXCodeEffectUnknown, contract.AXCodeAppUnavailable, contract.AXCodeRuntimeError,
	contract.AXCodeAmbiguousWindow, contract.AXCodeWindowNotFound, contract.AXCodeNoFocusedWindow,
}

// axArgError is a rejected argument. It is returned before any input.
// rule names the cross-field rule a schema-valid call broke; it is empty
// when the published schema rejects the call too.
type axArgError struct {
	Code    string `json:"-"`
	path    string
	Message string `json:"-"`
	Hint    string `json:"-"`
	rule    string
	// fix corrects the rejected value; axDerivedHint turns it into a hint
	// when the site writes none.
	fix []axEdit
}

// Cross-field rules that the flat AX schema cannot express. The consistency
// test accepts a runtime rejection of a schema-valid call only with one of
// these codes.
const (
	axRuleFieldForMode    = "ax_field_for_mode"
	axRuleFieldForType    = "ax_field_for_action_type"
	axRuleRequiredForMode = "ax_required_for_mode"
	axRuleRequiredForType = "ax_required_for_action_type"
	axRuleOneOf           = "ax_exactly_one_of"
	axRuleRefKind         = "ax_ref_kind_for_action_type"
	axRuleObserveNone     = "ax_observe_none"
	axRuleFindFilterPair  = "ax_find_filter_pair"
	axRuleBlankValue      = "ax_blank_value"
)

func (e *axArgError) withRule(rule string) *axArgError {
	if e != nil {
		e.rule = rule
	}
	return e
}

func (e *axArgError) Error() string { return e.Message }

// axInvalidHook, when set by a test, receives the source line of each
// axInvalid call, so the test can prove it reached every rejection site.
var axInvalidHook func(file string, line int)

func axInvalid(path, message, hint string) *axArgError {
	if axInvalidHook != nil {
		_, file, line, _ := runtime.Caller(1)
		axInvalidHook(file, line)
	}
	return &axArgError{
		Code: contract.AXCodeInvalidArgument, path: path, Message: message, Hint: hint,
	}
}

// axRequest is one decoded AX call.
type axRequest struct {
	mode      string
	app       string
	windowRef string
	scope     string
	observe   string
	root      string
	query     map[string]any
	limit     int
	cursor    string
	actions   []axBatchAction
}

var (
	AXElementRefPattern = regexp.MustCompile(`^r[1-9][0-9]*$`)
	AXWindowRefPattern  = regexp.MustCompile(`^w[1-9][0-9]*$`)
	axCursorPattern     = regexp.MustCompile(`^f[1-9][0-9]*$`)
	axRootPattern       = regexp.MustCompile(`^(focused|r[1-9][0-9]*)$`)
)

var axModeFields = map[string][]string{
	contract.AXModeApps:    {"mode"},
	contract.AXModeObserve: {"mode", "app", "window_ref", "scope", "observe", "root"},
	contract.AXModeFind:    {"mode", "app", "window_ref", "scope", "query", "limit", "cursor"},
	contract.AXModeAct:     {"mode", "window_ref", "actions", "observe"},
}

// axActionFields lists the fields each action type accepts besides type.
var axActionFields = map[string][]string{
	"click":     {"ref"},
	"type":      {"ref", "text"},
	"keypress":  {"ref", "keys"},
	"set_value": {"ref", "value"},
	"select":    {"ref", "to_ref", "text"},
	"scroll":    {"ref", "direction", "pages"},
	"activate":  {"ref", "title"},
	"close":     {"ref", "title"},
	"wait":      {"window", "query", "ref", "value", "state", "text", "gone", "settle", "timeout_ms"},
}

// axRenamedFields maps field names from earlier interfaces, and names models
// commonly write, to the current field.
var axRenamedFields = map[string]string{
	"action":  "type",
	"target":  "ref",
	"key":     "keys",
	"app_ref": "app",
}

// axRemovedWaitFields name the predicate fields of earlier interfaces. A call
// that uses one is rejected with the equivalent call written with wait steps.
var axRemovedWaitFields = []string{"until", "postcondition", "assertion"}

// axDecodeHook, when set by a test, is called once per decodeAXRequest.
var axDecodeHook func()

// DecodeAXRequest decodes one public AX call. Every rejection carries a
// hint that decodes (ax_argument_hint.go).
func DecodeAXRequest(args map[string]any) (*axRequest, *axArgError) {
	if axDecodeHook != nil {
		axDecodeHook()
	}
	request, err := decodeAXCall(args)
	if err != nil && err.Hint == "" {
		err.Hint = axDerivedHint(args, err)
	}
	return request, err
}

func decodeAXCall(args map[string]any) (*axRequest, *axArgError) {
	rawMode, present := args["mode"]
	if !present {
		return nil, axInvalid("mode", "mode is required: apps, observe, find, or act",
			`{"mode":"apps"}`)
	}
	mode, ok := rawMode.(string)
	if !ok || !contract.ContainsString([]string{contract.AXModeApps, contract.AXModeObserve, contract.AXModeFind, contract.AXModeAct}, mode) {
		if mode == "assert_ax" {
			return nil, axInvalid("mode",
				`mode "assert_ax" was removed; wait with an act call that has only wait steps`,
				`{"mode":"act","actions":[{"type":"wait","ref":"r1"}]}`)
		}
		err := axInvalid("mode", "mode must be apps, observe, find, or act; got "+
			contract.CompactJSON(rawMode), `{"mode":"apps"}`)
		if lower := strings.ToLower(mode); lower != mode && axModeFields[lower] != nil {
			err.Hint, err.fix = "", []axEdit{axSet("mode", lower)}
		}
		return nil, err
	}
	if err := axRejectRemovedUntil(args); err != nil {
		return nil, err
	}
	if err := axCheckFields(args, axModeFields[mode], "", "mode "+strconv.Quote(mode)); err != nil {
		return nil, err.withRule(axRuleFieldForMode)
	}
	request := &axRequest{mode: mode}
	var err *axArgError
	if request.app, err = axOptionalString(args, "app", "", true); err != nil {
		return nil, err
	}
	if request.windowRef, err = axOptionalPattern(args, "window_ref", "", AXWindowRefPattern, "wN"); err != nil {
		return nil, err
	}
	if request.app != "" && request.windowRef != "" {
		return nil, axInvalid("window_ref", "use app or window_ref, not both; window_ref already identifies its app",
			`{"mode":"`+mode+`","window_ref":"`+request.windowRef+`"}`)
	}
	if request.scope, err = axOptionalEnum(args, "scope", "", contract.AXScopeValues); err != nil {
		return nil, err
	}
	if request.observe, err = axOptionalEnum(args, "observe", "", contract.ObserveValues); err != nil {
		return nil, err
	}
	if request.root, err = axOptionalPattern(args, "root", "", axRootPattern, `"focused" or rN`); err != nil {
		if root, _ := args["root"].(string); AXWindowRefPattern.MatchString(root) {
			// A wN root names the window to read: read it whole.
			err.Message += "; " + root + " is a window: use window_ref"
			if request.windowRef == "" {
				err.fix = []axEdit{axRemove("root"), axRemove("app"), axSet("window_ref", root)}
			}
		}
		return nil, err
	}
	switch mode {
	case contract.AXModeObserve:
		if request.observe == contract.ObserveNone {
			return nil, axInvalid("observe", `observe "none" returns nothing; to wait without input, send act with only wait steps`,
				"").withRule(axRuleObserveNone).withFix(axRemove("observe"))
		}
		if request.app == "" && request.windowRef == "" {
			return nil, axInvalid("app", "observe needs app or window_ref",
				"").withRule(axRuleRequiredForMode).withFix(axSet("window_ref", "w1"))
		}
	case contract.AXModeFind:
		if err := decodeAXFind(args, request); err != nil {
			return nil, err
		}
	case contract.AXModeAct:
		raw, present := args["actions"]
		if !present {
			return nil, axInvalid("actions", "act requires actions",
				"").withRule(axRuleRequiredForMode).withFix(
				axSet("actions", []any{map[string]any{"type": "click", "ref": "r1"}}))
		}
		if request.actions, err = decodeAXActions(raw); err != nil {
			return nil, err
		}
	}
	return request, nil
}

func decodeAXFind(args map[string]any, request *axRequest) *axArgError {
	var err *axArgError
	if request.cursor, err = axOptionalPattern(args, "cursor", "", axCursorPattern, "fN"); err != nil {
		return err
	}
	request.limit = contract.AXDefaultFindPageSize
	if raw, present := args["limit"]; present {
		limit, ok := axInteger(raw)
		if !ok || limit < 1 || limit > contract.MaxAXFindPageSize {
			return axInvalid("limit", fmt.Sprintf("limit must be an integer from 1 to %d", contract.MaxAXFindPageSize), "").
				withFix(axNumberEdit("limit", raw, 1, contract.MaxAXFindPageSize, true))
		}
		request.limit = limit
	}
	if request.cursor != "" {
		for _, field := range []string{"app", "window_ref", "scope", "query"} {
			if _, present := args[field]; present {
				return axInvalid(field, "a cursor continues an earlier find; send only mode, cursor, and limit",
					"").withRule(axRuleFieldForMode).withFix(axRemove(field))
			}
		}
		return nil
	}
	if request.app == "" && request.windowRef == "" {
		return axInvalid("app", "find needs app or window_ref, or a cursor from a truncated result",
			"").withRule(axRuleRequiredForMode).withFix(axSet("window_ref", "w1"))
	}
	raw, present := args["query"]
	if !present {
		return axInvalid("query", "find requires query",
			"").withRule(axRuleRequiredForMode).withFix(axSet("query", map[string]any{"name_contains": "Save"}))
	}
	query, err := decodeAXQuery(raw, "query")
	if err != nil {
		return err
	}
	request.query = query
	return nil
}

// decodeAXQuery checks a find query; a wait step uses the same fields.
func decodeAXQuery(raw any, path string) (map[string]any, *axArgError) {
	query, ok := raw.(map[string]any)
	if !ok {
		return nil, axInvalid(path, "query must be an object", `{"name_contains":"Save"}`)
	}
	if err := axCheckFields(query, contract.AXFindFilters, path, "query"); err != nil {
		for _, field := range []string{"name", "text"} {
			if err.path == path+"."+field {
				err.fix = []axEdit{axSet(path+"."+field+"_contains", query[field]), axRemove(err.path)}
			}
		}
		return nil, err
	}
	if len(query) == 0 {
		return nil, axInvalid(path, "query needs at least one filter: "+strings.Join(contract.AXFindFilters, ", "),
			`{"name_contains":"Save"}`).withRule(axRuleRequiredForMode)
	}
	for _, field := range contract.AXFindFilters {
		if _, err := axOptionalString(query, field, path, true); err != nil {
			return nil, err
		}
	}
	for _, pair := range [][2]string{{"name_equals", "name_contains"}, {"text_equals", "text_contains"}} {
		if _, first := query[pair[0]]; first {
			if _, second := query[pair[1]]; second {
				return nil, axInvalid(path+"."+pair[1], "use "+pair[0]+" or "+pair[1]+", not both", "").
					withRule(axRuleFindFilterPair).withFix(axRemove(path + "." + pair[1]))
			}
		}
	}
	return query, nil
}

func decodeAXActions(raw any) ([]axBatchAction, *axArgError) {
	items, ok := raw.([]any)
	if !ok {
		return nil, axInvalid("actions", "actions must be an array of action objects",
			`[{"type":"click","ref":"r1"}]`)
	}
	if len(items) == 0 {
		return nil, axInvalid("actions", `act needs at least one action; use mode "observe" to read`,
			`[{"type":"click","ref":"r1"}]`)
	}
	if len(items) > contract.AXBatchMaxActions {
		return nil, axInvalid("actions", fmt.Sprintf("act accepts at most %d actions", contract.AXBatchMaxActions), "").
			withFix(axSet("actions", items[:contract.AXBatchMaxActions]))
	}
	actions := make([]axBatchAction, 0, len(items))
	totalText, totalWait := 0, 0
	for index, item := range items {
		path := fmt.Sprintf("actions[%d]", index)
		object, ok := item.(map[string]any)
		if !ok {
			return nil, axInvalid(path, "each action must be an object", `{"type":"click","ref":"r1"}`)
		}
		action, err := decodeAXAction(object, path)
		if err != nil {
			return nil, err
		}
		if index > 0 && actions[index-1].actionType == "close" && action.actionType != "wait" {
			return nil, axInvalid(path, "a closed window cannot take later input; after close, wait for the window to be gone first",
				`{"type":"wait","window":"window title","gone":true}`).withRule(contract.ArgumentRuleCloseNotLast)
		}
		totalText += len([]rune(action.text)) + len([]rune(action.value))
		if totalText > contract.AXBatchMaxText {
			return nil, axInvalid(path, fmt.Sprintf("text and value together are limited to %d characters per call", contract.AXBatchMaxText), "").withRule(contract.ArgumentRuleTextBudget)
		}
		if action.wait != nil && action.wait.settle && !axBatchHasInputBefore(actions) {
			return nil, axInvalid(path, "settle waits for the change after an earlier step of this call; put it after an action",
				`{"mode":"act","actions":[{"type":"click","ref":"r1"},{"type":"wait","settle":true}]}`).
				withRule(axRuleRequiredForType)
		}
		if action.wait != nil {
			totalWait += action.wait.timeoutMS
			if totalWait > contract.MaxVerifyTimeout {
				return nil, axInvalid(path, fmt.Sprintf(
					"wait steps of one act may wait %d ms in total (a wait without timeout_ms counts %d); send the steps from here in a second call",
					contract.MaxVerifyTimeout, contract.AXDefaultWaitTimeout), "").
					withRule(contract.ArgumentRuleWaitBudget).withFix(axSet("actions", items[:index]))
			}
		}
		actions = append(actions, action)
	}
	return actions, nil
}

func decodeAXAction(object map[string]any, path string) (axBatchAction, *axArgError) {
	action := axBatchAction{pages: 1}
	rawType, hasType := object["type"]
	actionType, _ := rawType.(string)
	if !hasType || !contract.ContainsString(contract.AXActionTypes, actionType) {
		if hint := axActionShapeHint(object); hint != "" && !hasType {
			field := "type"
			for name := range object {
				if axRenamedFields[name] == "type" {
					field = name
				}
			}
			return action, axInvalid(path+"."+field, `actions use "type", not "`+field+`"`, hint)
		}
		message := "type must be one of " + strings.Join(contract.AXActionTypes, ", ")
		if hasType {
			message += "; got " + contract.CompactJSON(rawType)
		} else {
			message = "action needs type: " + strings.Join(contract.AXActionTypes, ", ")
		}
		return action, axInvalid(path+".type", message, axActionShapeHint(object))
	}
	action.actionType = actionType
	aliased, aliasErr := axNormalizeTextAlias(object, actionType, path)
	if aliasErr != nil {
		return action, aliasErr
	}
	object = aliased
	allowed := append([]string{"type"}, axActionFields[actionType]...)
	if err := axCheckFields(object, allowed, path, actionType); err != nil {
		_, hasRef := object["ref"]
		if err.path == path+".window_ref" && !hasRef && (actionType == "activate" || actionType == "close") {
			// The action names its window with ref; window_ref is the call's.
			err.fix = []axEdit{axSet(path+".ref", object["window_ref"]), axRemove(err.path)}
		}
		// A renamed type or ref changes the action's shape; a renamed until
		// keeps its predicate, which the attached fix moves.
		if renamed, known := axRenamedFields[strings.TrimPrefix(err.path, path+".")]; known && renamed != "until" {
			err.Hint = axActionShapeHint(object)
		}
		return action, err.withRule(axRuleFieldForType)
	}
	if actionType == "wait" {
		wait, err := decodeAXWait(object, path)
		if err != nil {
			return action, err
		}
		action.wait = wait
		action.targetRef = wait.ref
		return action, nil
	}
	var err *axArgError
	refPattern := AXElementRefPattern
	refShape := "rN"
	if actionType == "activate" || actionType == "close" {
		refPattern, refShape = AXWindowRefPattern, "wN"
	}
	if action.targetRef, err = axOptionalPattern(object, "ref", path, refPattern, refShape); err != nil {
		err.rule = axRuleRefKind
		if actionType != "activate" && actionType != "close" && AXWindowRefPattern.MatchString(OptionalString(object, "ref")) {
			err.Message = actionType + " takes an element ref rN; wN is only for activate and close"
		}
		if (actionType == "activate" || actionType == "close") && AXElementRefPattern.MatchString(OptionalString(object, "ref")) {
			err.Message = actionType + " takes a window ref wN or a title; rN is an element"
			err.Hint = `{"type":"` + actionType + `","title":"window title"}`
		}
		return action, err
	}
	switch actionType {
	case "click", "set_value":
		if action.targetRef == "" {
			return action, axInvalid(path+".ref", actionType+" needs ref: an rN copied from observe or find",
				`{"type":"`+actionType+`","ref":"r1"`+map[bool]string{true: `,"value":"..."`, false: ""}[actionType == "set_value"]+`}`).withRule(axRuleRequiredForType)
		}
		if actionType == "set_value" {
			raw, present := object["value"]
			value, ok := raw.(string)
			if !present || !ok {
				return action, axInvalid(path+".value", "set_value needs value as a string",
					`{"type":"set_value","ref":"`+action.targetRef+`","value":"..."}`).withRule(axRuleRequiredForType)
			}
			if len([]rune(value)) > contract.MaxActionText {
				return action, axInvalid(path+".value", fmt.Sprintf("value is limited to %d characters", contract.MaxActionText), "")
			}
			action.value = value
		}
	case "select":
		if action.targetRef == "" {
			return action, axInvalid(path+".ref", "select needs ref: an rN copied from observe or find",
				`{"type":"select","ref":"r1"}`).withRule(axRuleRequiredForType)
		}
		if action.toRef, err = axOptionalPattern(object, "to_ref", path, AXElementRefPattern, "rN"); err != nil {
			return action, err
		}
		if raw, present := object["text"]; present {
			text, ok := raw.(string)
			if !ok || text == "" {
				return action, axInvalid(path+".text", "select text must be a non-empty substring of the ref element",
					`{"type":"select","ref":"`+action.targetRef+`","text":"..."}`)
			}
			if len([]rune(text)) > contract.MaxActionText {
				return action, axInvalid(path+".text", fmt.Sprintf("text is limited to %d characters", contract.MaxActionText), "")
			}
			action.text = text
		}
		if action.toRef != "" && action.text != "" {
			return action, axInvalid(path, "select takes to_ref or text, not both",
				`{"type":"select","ref":"`+action.targetRef+`","to_ref":"`+action.toRef+`"}`).withRule(axRuleOneOf)
		}
	case "type":
		raw, present := object["text"]
		text, ok := raw.(string)
		if !present || !ok || text == "" {
			return action, axInvalid(path+".text", "type needs non-empty text", `{"type":"type","text":"hello"}`).withRule(axRuleRequiredForType)
		}
		if len([]rune(text)) > contract.MaxActionText {
			return action, axInvalid(path+".text", fmt.Sprintf("text is limited to %d characters", contract.MaxActionText), "")
		}
		action.text = text
	case "keypress":
		key, err := decodeAXKeys(object, path)
		if err != nil {
			return action, err.withRule(axRuleRequiredForType)
		}
		action.key = key
	case "scroll":
		raw, present := object["direction"]
		direction, ok := raw.(string)
		if !present || !ok || !contract.ContainsString([]string{"up", "down", "left", "right"}, direction) {
			return action, axInvalid(path+".direction", "scroll needs direction: up, down, left, or right",
				`{"type":"scroll","direction":"down"}`).withRule(axRuleRequiredForType)
		}
		action.direction = direction
		if raw, present := object["pages"]; present {
			pages, ok := axNumber(raw)
			if !ok || pages <= 0 || pages > contract.MaxScrollPages {
				return action, axInvalid(path+".pages", fmt.Sprintf("pages must be a number greater than 0 and at most %d", contract.MaxScrollPages), "").
					withFix(axNumberEdit(path+".pages", raw, math.SmallestNonzeroFloat64, contract.MaxScrollPages, false))
			}
			action.pages = pages
		}
	case "activate", "close":
		title, err := axOptionalString(object, "title", path, true)
		if err != nil {
			return action, err
		}
		if (title == "") == (action.targetRef == "") {
			return action, axInvalid(path, actionType+" needs exactly one of ref (wN) or title",
				`{"type":"`+actionType+`","title":"window title"}`).withRule(axRuleOneOf)
		}
		action.title = title
	}
	return action, nil
}

func decodeAXKeys(object map[string]any, path string) (string, *axArgError) {
	hint := `{"type":"keypress","keys":["CTRL","S"]}`
	raw, present := object["keys"]
	items, ok := raw.([]any)
	if !present || !ok {
		if text, isText := raw.(string); isText && text != "" {
			hint = `{"type":"keypress","keys":` + contract.CompactJSON(strings.Split(text, "+")) + `}`
		}
		return "", axInvalid(path+".keys", "keypress needs keys: an array naming one chord", hint)
	}
	if len(items) == 0 || len(items) > contract.GUIMaxKeys {
		return "", axInvalid(path+".keys", fmt.Sprintf("keys must name 1 to %d keys", contract.GUIMaxKeys), hint)
	}
	keys := make([]string, 0, len(items))
	for index, item := range items {
		key, ok := item.(string)
		if !ok || strings.TrimSpace(key) == "" || key != strings.TrimSpace(key) {
			return "", axInvalid(fmt.Sprintf("%s.keys[%d]", path, index), "each key must be a key name without surrounding spaces", hint)
		}
		if len([]rune(key)) > contract.GUIMaxKeyLength {
			return "", axInvalid(fmt.Sprintf("%s.keys[%d]", path, index), fmt.Sprintf("a key name is limited to %d characters", contract.GUIMaxKeyLength), "")
		}
		keys = append(keys, key)
	}
	return strings.Join(keys, "+"), nil
}

// axActionShapeHint rewrites an action written with another interface's
// field names into the current shape, e.g. {"ref":"r1","action":"press"}
// becomes {"type":"click","ref":"r1"}.
func axActionShapeHint(object map[string]any) string {
	ref, _ := object["ref"].(string)
	if target, ok := object["target"].(map[string]any); ok && ref == "" {
		ref, _ = target["ref"].(string)
	} else if target, ok := object["target"].(string); ok && ref == "" {
		ref = target
	}
	verb, _ := object["type"].(string)
	if verb == "" {
		verb, _ = object["action"].(string)
	}
	if synonym, known := axActionVerbSynonyms[verb]; known {
		verb = synonym
	}
	if !contract.ContainsString(contract.AXActionTypes, verb) {
		switch {
		case AXWindowRefPattern.MatchString(ref) && strings.Contains(strings.ToLower(verb), "close"):
			verb = "close"
		case AXWindowRefPattern.MatchString(ref):
			verb = "activate"
		case AXElementRefPattern.MatchString(ref):
			verb = "click"
		case object["keys"] != nil || object["key"] != nil:
			verb = "keypress"
		case object["text"] != nil:
			verb = "type"
		default:
			return ""
		}
	}
	hint := map[string]any{"type": verb}
	if ref != "" {
		hint["ref"] = ref
	}
	for _, field := range axActionFields[verb] {
		if field == "ref" {
			continue
		}
		if value, present := object[field]; present {
			hint[field] = value
		}
	}
	if verb == "set_value" && hint["value"] == nil {
		if text, ok := object["text"].(string); ok {
			hint["value"] = text
		}
	}
	if verb == "keypress" && hint["keys"] == nil {
		if key, ok := object["key"].(string); ok && key != "" {
			hint["keys"] = strings.Split(key, "+")
		}
	}
	return contract.OrderedJSON(hint, []string{"type", "ref"})
}

// axActionVerbSynonyms are verbs models wrote for AX actions in recorded
// runs. They only shape the corrected-call hint; they are never accepted.
var axActionVerbSynonyms = map[string]string{
	"press":        "click",
	"invoke":       "click",
	"set_text":     "set_value",
	"close_window": "close",
	"focus":        "activate",
	"key":          "keypress",
	"press_key":    "keypress",
	"type_text":    "type",
}

// axCheckFields rejects fields outside allowed. A field renamed from an
// earlier interface names its replacement.
func axCheckFields(object map[string]any, allowed []string, path, owner string) *axArgError {
	unknown := []string{}
	for field := range object {
		if !contract.ContainsString(allowed, field) {
			unknown = append(unknown, field)
		}
	}
	if len(unknown) == 0 {
		return nil
	}
	sort.Strings(unknown)
	field := unknown[0]
	fieldPath := field
	if path != "" {
		fieldPath = path + "." + field
	}
	if replacement, renamed := axRenamedFields[field]; renamed && contract.ContainsString(allowed, replacement) {
		err := axInvalid(fieldPath, strconv.Quote(field)+" is not a field here; use "+strconv.Quote(replacement), "")
		if _, taken := object[replacement]; taken {
			return err.withFix(axRemove(fieldPath))
		}
		return err.withFix(axSet(axJoinPath(path, replacement), object[field]), axRemove(fieldPath))
	}
	if field == "observation_target" {
		return axInvalid(fieldPath, `observation_target was removed; list windows with mode "apps"`, `{"mode":"apps"}`)
	}
	return axInvalid(fieldPath, owner+" does not accept "+strconv.Quote(field)+
		"; accepted: "+strings.Join(allowed, ", "), "").withFix(axRemove(fieldPath))
}

func axOptionalString(object map[string]any, field, path string, nonBlank bool) (string, *axArgError) {
	raw, present := object[field]
	if !present {
		return "", nil
	}
	text, ok := raw.(string)
	if !ok || (nonBlank && strings.TrimSpace(text) == "") {
		fieldPath := axJoinPath(path, field)
		fix := axRemove(fieldPath)
		if number, isNumber := axNumber(raw); isNumber {
			// A PID sent as a number.
			fix = axSet(fieldPath, strconv.FormatFloat(number, 'f', -1, 64))
		}
		return "", axInvalid(fieldPath, field+" must be a non-blank string", "").withFix(fix)
	}
	return text, nil
}

func axOptionalPattern(object map[string]any, field, path string, pattern *regexp.Regexp, shape string) (string, *axArgError) {
	raw, present := object[field]
	if !present {
		return "", nil
	}
	text, ok := raw.(string)
	if !ok || !pattern.MatchString(text) {
		fix := axRemove(axJoinPath(path, field))
		if lower := strings.ToLower(strings.TrimSpace(text)); pattern.MatchString(lower) {
			fix = axSet(axJoinPath(path, field), lower)
		}
		return "", axInvalid(axJoinPath(path, field), field+" must be "+shape+
			", copied exactly from an earlier result; got "+contract.CompactJSON(raw), "").withFix(fix)
	}
	return text, nil
}

func axOptionalEnum(object map[string]any, field, path string, values []string) (string, *axArgError) {
	raw, present := object[field]
	if !present {
		return "", nil
	}
	text, ok := raw.(string)
	if !ok || !contract.ContainsString(values, text) {
		fix := axRemove(axJoinPath(path, field))
		if contract.ContainsString(values, strings.ToLower(text)) {
			fix = axSet(axJoinPath(path, field), strings.ToLower(text))
		}
		return "", axInvalid(axJoinPath(path, field), field+" must be one of "+strings.Join(values, ", ")+
			"; got "+contract.CompactJSON(raw), "").withFix(fix)
	}
	return text, nil
}

func axJoinPath(path, field string) string {
	if path == "" {
		return field
	}
	return path + "." + field
}

func axInteger(raw any) (int, bool) {
	switch typed := raw.(type) {
	case int:
		return typed, true
	case float64:
		if typed != math.Trunc(typed) || math.Abs(typed) > 1e9 {
			return 0, false
		}
		return int(typed), true
	case json.Number:
		// JSON Schema treats 1.0 as an integer, so the decoder does too.
		value, err := typed.Float64()
		if err != nil || value != math.Trunc(value) || math.Abs(value) > 1e9 {
			return 0, false
		}
		return int(value), true
	}
	return 0, false
}

func axNumber(raw any) (float64, bool) {
	switch typed := raw.(type) {
	case int:
		return float64(typed), true
	case float64:
		return typed, !math.IsNaN(typed) && !math.IsInf(typed, 0)
	case json.Number:
		value, err := typed.Float64()
		return value, err == nil && !math.IsNaN(value) && !math.IsInf(value, 0)
	}
	return 0, false
}

// axArgErrorResult renders a rejected call. Nothing was sent.
func axArgErrorResult(err *axArgError, requested int) ToolCallResult {
	result := TextResult(err.Message, true)
	result.StructuredContent = map[string]any{
		contract.AXPublicErrorCodeKey:   err.Code,
		contract.AXPublicErrorPathKey:   err.path,
		contract.AXPublicErrorHintKey:   err.Hint,
		contract.AXPublicErrorDetailKey: err.Message,
		"argument_error_code":           FirstNonEmpty(err.rule, err.Code),
		"action_attempted":              false,
		"input_events_emitted":          false,
		"preflight_only":                true,
		"evidence_scope":                "error_only",
		"a11y_batch_result": map[string]any{
			"actions_requested":                    requested,
			"actions_completed_without_call_error": 0,
			"rejected_before_first_action":         true,
		},
	}
	return result
}
