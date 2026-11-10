package core

import (
	"fmt"
	"strconv"
	"strings"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// A wait step is one ordered step of an act batch. It sends no input and
// stops the batch, like a failed action, when its condition does not hold
// within timeout_ms. Forms:
//
//	{"type":"wait","window":"Export"}               a top-level window whose title contains the text appears
//	{"type":"wait","window":"Print","gone":true}    no such window remains (an exited app has none)
//	{"type":"wait","query":{...find query...}}      an element appears in the batch app's windows
//	{"type":"wait","ref":"r31","value":"14"}        an element condition: value, state, or text
//	{"type":"wait","text":"Saved"}                  text appears in the batch window
//	{"type":"wait","settle":true}                   the UI changes after the step before it, then stops changing
//
// gone inverts every form but settle. The evaluation reuses the polling the earlier until
// predicate used: window titles, application-wide element search with find's
// matching, and live ref resolution.

const axWaitPollInterval = 400 * time.Millisecond

// axWait is one decoded wait step.
type axWait struct {
	window string
	query  map[string]any
	ref    string
	value  string
	state  string
	text   string
	gone   bool
	// settle waits for any change after the input step before it; see
	// waitForSettle.
	settle    bool
	timeoutMS int
}

func axWaitQueryText(query map[string]any) string {
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
	return describeQuery(OptionalString(query, "role"), name, text,
		OptionalString(query, "state"), nameExact, textExact)
}

// decodeAXWait checks one wait step. It needs exactly one of window, query,
// ref, a bare text, or settle; value, state, and text qualify a ref.
func decodeAXWait(object map[string]any, path string) (*axWait, *axArgError) {
	wait := &axWait{timeoutMS: contract.AXDefaultWaitTimeout}
	example := `{"type":"wait","window":"Export"}`
	var err *axArgError
	if wait.window, err = axOptionalString(object, "window", path, true); err != nil {
		return nil, err
	}
	if wait.ref, err = axOptionalPattern(object, "ref", path, AXElementRefPattern, "rN"); err != nil {
		err.rule = axRuleRefKind
		if AXWindowRefPattern.MatchString(OptionalString(object, "ref")) {
			err.Message = `a wait ref is an element rN; for a window use {"type":"wait","window":"title"}`
			err.Hint = example
		}
		return nil, err
	}
	if wait.state, err = axOptionalString(object, "state", path, true); err != nil {
		return nil, err
	}
	// text is shared with type, whose text may be only spaces; a wait reads
	// it as a literal substring in the same way.
	if wait.text, err = axOptionalString(object, "text", path, false); err != nil {
		return nil, err
	}
	if _, present := object["text"]; present && wait.text == "" {
		return nil, axInvalid(path+".text", "text must not be empty", `{"type":"wait","text":"Saved"}`)
	}
	if raw, present := object["value"]; present {
		value, ok := raw.(string)
		if !ok || strings.TrimSpace(value) == "" {
			return nil, axInvalid(path+".value", "value needs a non-blank string, a number, or a boolean",
				`{"type":"wait","ref":"r1","value":"14"}`).withRule(axRuleRequiredForType)
		}
		wait.value = value
	}
	if raw, present := object["query"]; present {
		if wait.query, err = decodeAXQuery(raw, path+".query"); err != nil {
			return nil, err
		}
	}
	if raw, present := object["gone"]; present {
		gone, ok := raw.(bool)
		if !ok {
			return nil, axInvalid(path+".gone", "gone must be true or false", "")
		}
		wait.gone = gone
	}
	if raw, present := object["settle"]; present {
		settle, ok := raw.(bool)
		if !ok || !settle {
			return nil, axInvalid(path+".settle", "settle must be true; to wait for a known state use window, query, ref, or text",
				`{"type":"wait","settle":true}`)
		}
		wait.settle = true
	}
	if raw, present := object["timeout_ms"]; present {
		timeout, ok := axInteger(raw)
		if !ok || timeout < 1 || timeout > contract.MaxVerifyTimeout {
			return nil, axInvalid(path+".timeout_ms",
				fmt.Sprintf("timeout_ms must be an integer from 1 to %d", contract.MaxVerifyTimeout), "").
				withFix(axNumberEdit(path+".timeout_ms", raw, 1, contract.MaxVerifyTimeout, true))
		}
		wait.timeoutMS = timeout
	}
	subjects := 0
	for _, present := range []bool{wait.window != "", wait.query != nil, wait.ref != "", wait.settle} {
		if present {
			subjects++
		}
	}
	if subjects == 0 && wait.text != "" {
		subjects = 1
	}
	if wait.settle && wait.text != "" {
		subjects++
	}
	if subjects != 1 {
		return nil, axInvalid(path, "wait needs exactly one of window, query, ref, text, or settle", example).withRule(axRuleOneOf)
	}
	if wait.settle && wait.gone {
		return nil, axInvalid(path+".gone", "settle waits for any change and has no gone form; gone needs window, query, ref, or text",
			`{"type":"wait","settle":true}`).withRule(axRuleOneOf)
	}
	qualifiers := 0
	for _, value := range []string{wait.value, wait.state} {
		if value != "" {
			qualifiers++
		}
	}
	if wait.ref != "" && wait.text != "" {
		qualifiers++
	}
	if qualifiers > 0 && wait.ref == "" {
		field := "value"
		if wait.state != "" {
			field = "state"
		}
		return nil, axInvalid(path+"."+field, field+" qualifies an element; add ref, or use query to wait for an element",
			`{"type":"wait","ref":"r1","`+field+`":"..."}`).withRule(axRuleRequiredForType)
	}
	if qualifiers > 1 {
		return nil, axInvalid(path, "a ref wait checks one of value, state, or text",
			`{"type":"wait","ref":"`+wait.ref+`","value":"14"}`).withRule(axRuleOneOf)
	}
	return wait, nil
}

// axRejectRemovedUntil rejects a call that uses the removed until field (or
// an older spelling of it) and answers with the equivalent call written
// with wait steps.
func axRejectRemovedUntil(args map[string]any) *axArgError {
	path := ""
	for _, field := range axRemovedWaitFields {
		if _, present := args[field]; present {
			path = field
			break
		}
	}
	if actions, ok := args["actions"].([]any); ok && path == "" {
		for index, item := range actions {
			action, _ := item.(map[string]any)
			for _, field := range axRemovedWaitFields {
				if _, present := action[field]; present && path == "" {
					path = fmt.Sprintf("actions[%d].%s", index, field)
				}
			}
		}
	}
	if path == "" {
		return nil
	}
	field := path[strings.LastIndex(path, ".")+1:]
	hint, ok := axUntilCallAsWait(args)
	if ok {
		hint, ok = axRepairWaitHint(hint)
	}
	message := strconv.Quote(field) + " was removed; wait with a step of type \"wait\" in act actions, at the point in the order where the batch should wait"
	if !ok {
		hint = `{"mode":"act","actions":[{"type":"click","ref":"r1"},{"type":"wait","window":"title"}]}`
	}
	return axInvalid(path, message, hint)
}

// axRepairWaitHint returns hint unchanged when it decodes. A converted call
// keeps the values of the rejected predicate, so an out-of-range timeout or
// a blank name stays wrong after the conversion; the decoder's repairs fix
// such values as they do for any other call, so the hint always decodes.
func axRepairWaitHint(hint string) (string, bool) {
	decoded, ok := axDecodeHintJSON(hint)
	call, isObject := decoded.(map[string]any)
	if !ok || !isObject {
		return "", false
	}
	_, err := decodeAXCall(call)
	if err == nil {
		return hint, true
	}
	for step := 0; step < axMaxRepairSteps; step++ {
		if !axApplyRepair(&call, err) {
			return "", false
		}
		if _, err = decodeAXCall(call); err == nil {
			return axOrderedCall(call), true
		}
	}
	return "", false
}

// axUntilCallAsWait rewrites a call that used until into the same call with
// wait steps: an action's until follows that action, an act-level until
// follows the last action, and an observe until becomes an act call with
// only that wait step. It reports false when a predicate has no wait form.
func axUntilCallAsWait(args map[string]any) (string, bool) {
	mode, _ := args["mode"].(string)
	out := map[string]any{"mode": "act"}
	if ref, ok := args["window_ref"].(string); ok && mode == contract.AXModeAct {
		out["window_ref"] = ref
	}
	if observe, ok := args["observe"].(string); ok && mode == contract.AXModeAct {
		out["observe"] = observe
	}
	steps := []any{}
	if mode == contract.AXModeAct {
		items, _ := args["actions"].([]any)
		for _, item := range items {
			action, ok := item.(map[string]any)
			if !ok {
				return "", false
			}
			copied := map[string]any{}
			var predicate any
			for key, value := range action {
				if contract.ContainsString(axRemovedWaitFields, key) {
					predicate = value
					continue
				}
				copied[key] = value
			}
			steps = append(steps, copied)
			if predicate != nil {
				wait, ok := axUntilAsWait(predicate)
				if !ok {
					return "", false
				}
				steps = append(steps, wait)
			}
		}
	}
	for _, field := range axRemovedWaitFields {
		if predicate, present := args[field]; present {
			wait, ok := axUntilAsWait(predicate)
			if !ok {
				return "", false
			}
			steps = append(steps, wait)
		}
	}
	if len(steps) == 0 {
		return "", false
	}
	out["actions"] = steps
	return axOrderedCall(out), true
}

// axUntilAsWait converts one until predicate into a wait step.
func axUntilAsWait(raw any) (map[string]any, bool) {
	predicate, ok := raw.(map[string]any)
	if !ok {
		return nil, false
	}
	wait := map[string]any{"type": "wait"}
	negate, _ := predicate["negate"].(bool)
	if negate {
		wait["gone"] = true
	}
	if timeout, present := predicate["timeout_ms"]; present {
		wait["timeout_ms"] = timeout
	}
	kind, _ := predicate["type"].(string)
	ref, hasRef := predicate["ref"].(string)
	selector, hasSelector := predicate["selector"].(map[string]any)
	query := map[string]any{}
	if hasSelector {
		for field, value := range selector {
			switch field {
			case "role", "state":
				query[field] = value
			case "name":
				query["name_contains"] = value
			case "description", "placeholder":
				query["text_contains"] = value
			default:
				return nil, false
			}
		}
		if len(query) == 0 {
			return nil, false
		}
	}
	switch kind {
	case "title":
		text, _ := predicate["text"].(string)
		if text == "" {
			return nil, false
		}
		wait["window"] = text
	case "exists":
		switch {
		case hasRef:
			// A ref that should appear is already present; waiting for it
			// only makes sense as gone.
			wait["ref"] = ref
			if !negate {
				return nil, false
			}
		case hasSelector:
			wait["query"] = query
		default:
			return nil, false
		}
	case "state", "value", "text":
		field := kind
		value := predicate[field]
		if value == nil {
			return nil, false
		}
		if kind == "value" {
			if text, isText := value.(string); !isText || predicate["match"] == "contains" {
				if !isText {
					value = fmt.Sprint(value)
				}
				// A value wait compares the whole value; contains has no
				// element form, so it becomes a text wait on that element.
				if predicate["match"] == "contains" {
					field = "text"
				}
			} else {
				value = text
			}
		}
		switch {
		case hasRef:
			wait["ref"] = ref
			wait[field] = value
		case hasSelector && kind == "state":
			query["state"] = value
			wait["query"] = query
		case hasSelector && kind == "text":
			query["text_contains"] = value
			wait["query"] = query
		case !hasRef && !hasSelector && kind == "text":
			wait["text"] = value
		default:
			return nil, false
		}
	default:
		return nil, false
	}
	return wait, true
}

// axOrderedCall renders a call with mode, window_ref, and actions first, and
// each action with type first, so a hint reads like the documentation.
func axOrderedCall(call map[string]any) string {
	var out strings.Builder
	out.WriteByte('{')
	keys := []string{"mode", "window_ref", "actions", "observe"}
	written := 0
	for _, key := range keys {
		value, present := call[key]
		if !present {
			continue
		}
		if written > 0 {
			out.WriteByte(',')
		}
		written++
		out.WriteString(strconv.Quote(key) + ":")
		if key != "actions" {
			out.WriteString(contract.CompactJSON(value))
			continue
		}
		out.WriteByte('[')
		for index, item := range value.([]any) {
			if index > 0 {
				out.WriteByte(',')
			}
			action, _ := item.(map[string]any)
			out.WriteString(contract.OrderedJSON(action, []string{"type", "ref", "window", "query", "title"}))
		}
		out.WriteByte(']')
	}
	out.WriteByte('}')
	return out.String()
}
