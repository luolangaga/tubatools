package core

import (
	"bytes"
	"encoding/json"
	"math"
	"reflect"
	"regexp"
	"sort"
	"strconv"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// Every invalid_argument from the AX decoder carries a hint that decodes.
// A rejection site either writes the hint itself or attaches edits (axEdit)
// that correct the rejected value: drop a field the mode does not accept,
// clamp a number, fold the case of an enum. axDerivedHint applies those
// edits to a copy of the call, decodes it again, and repeats for the next
// error, so a call with several mistakes still gets one valid hint.
//
// The hint is the smallest object the model can paste back: the corrected
// action, query, predicate, or selector that contains the error path, or the
// whole call when the correction reaches outside that object or adds it. A hint that
// starts with "mode" is a whole call. When no correction can be derived (too
// many actions, text over the budget, an unknown action type), the hint is
// the minimal valid call for the mode (axMinimalCalls).

// axEdit sets or removes the value at a decoder error path.
type axEdit struct {
	path   string
	value  any
	remove bool
}

func axRemove(path string) axEdit         { return axEdit{path: path, remove: true} }
func axSet(path string, value any) axEdit { return axEdit{path: path, value: value} }

func (e *axArgError) withFix(edits ...axEdit) *axArgError {
	if e != nil {
		e.fix = edits
	}
	return e
}

// axMinimalCalls are the documented fallback hints, one valid call per mode.
var axMinimalCalls = map[string]string{
	contract.AXModeApps:    `{"mode":"apps"}`,
	contract.AXModeObserve: `{"mode":"observe","window_ref":"w1"}`,
	contract.AXModeFind:    `{"mode":"find","window_ref":"w1","query":{"name_contains":"Save"}}`,
	contract.AXModeAct:     `{"mode":"act","actions":[{"type":"click","ref":"r1"}]}`,
}

// axMaxRepairSteps bounds the repair loop; each step fixes one error.
const axMaxRepairSteps = 8

func axDerivedHint(args map[string]any, err *axArgError) string {
	fallback := axMinimalCalls[contract.AXModeApps]
	if mode, _ := args["mode"].(string); axMinimalCalls[mode] != "" {
		fallback = axMinimalCalls[mode]
	}
	call, _ := axCloneValue(args).(map[string]any)
	current := err
	for step := 0; ; step++ {
		if step == axMaxRepairSteps || !axApplyRepair(&call, current) {
			return fallback
		}
		if _, current = decodeAXCall(call); current == nil {
			break
		}
	}
	anchor := axHintAnchor(err.path)
	hint := axHintJSON(call)
	if _, existed := axValueAt(args, anchor); anchor != nil && existed {
		// A fragment replaces an object the call already has; a missing
		// object needs the whole call to show where it goes.
		if fragment, ok := axValueAt(call, anchor); ok && axEqualOutside(args, call, anchor) {
			hint = axHintJSON(fragment)
		}
	}
	if len(hint) > contract.MaxArgumentHintBytes {
		return fallback
	}
	return hint
}

// axApplyRepair corrects the value at the error path in call. A written
// hint takes precedence over attached edits. It reports false when the
// error has neither, or the correction changes nothing.
func axApplyRepair(call *map[string]any, err *axArgError) bool {
	before := axCloneValue(*call)
	switch {
	case err.Hint != "":
		spliced, ok := axSpliceHint(*call, err.path, err.Hint)
		if !ok {
			return false
		}
		*call = spliced
	case len(err.fix) > 0:
		for _, edit := range err.fix {
			segments := axParsePath(edit.path)
			if segments == nil {
				return false
			}
			if !axSetAt(*call, segments, axCloneValue(edit.value), edit.remove) {
				return false
			}
		}
	default:
		return false
	}
	return !reflect.DeepEqual(before, *call)
}

// axSpliceHint returns call with hint applied: a hint with "mode" replaces
// the call; any other hint replaces the object at the anchor of path.
func axSpliceHint(call map[string]any, path, hint string) (map[string]any, bool) {
	value, ok := axDecodeHintJSON(hint)
	if !ok {
		return nil, false
	}
	if object, isObject := value.(map[string]any); isObject {
		if _, whole := object["mode"]; whole {
			return object, true
		}
	}
	anchor := axHintAnchor(path)
	if anchor == nil {
		return nil, false
	}
	spliced, _ := axCloneValue(call).(map[string]any)
	if !axSetAt(spliced, anchor, value, false) {
		return nil, false
	}
	return spliced, true
}

func axDecodeHintJSON(hint string) (any, bool) {
	decoder := json.NewDecoder(strings.NewReader(hint))
	decoder.UseNumber()
	var value any
	if decoder.Decode(&value) != nil || decoder.More() {
		return nil, false
	}
	return value, true
}

var axPathSegmentPattern = regexp.MustCompile(`^([^\[\]]+)((?:\[[0-9]+\])*)$`)

// axParsePath splits a decoder path such as actions[0].until.ref into
// field names (string) and array indexes (int).
func axParsePath(path string) []any {
	segments := []any{}
	if path == "" {
		return segments
	}
	for _, part := range strings.Split(path, ".") {
		match := axPathSegmentPattern.FindStringSubmatch(part)
		if match == nil {
			return nil
		}
		segments = append(segments, match[1])
		for _, index := range strings.FieldsFunc(match[2], func(r rune) bool { return r == '[' || r == ']' }) {
			value, _ := strconv.Atoi(index)
			segments = append(segments, value)
		}
	}
	return segments
}

// axHintAnchor returns the deepest object a fragment hint for path
// replaces: actions, one action, query, a predicate, or a selector. It
// returns nil for a top-level field, whose hint is the whole call.
func axHintAnchor(path string) []any {
	segments := axParsePath(path)
	var anchor []any
	for end := 1; end <= len(segments); end++ {
		prefix := segments[:end]
		switch last := prefix[end-1].(type) {
		case int:
			if end == 2 && prefix[0] == "actions" {
				anchor = prefix
			}
		case string:
			if (end == 1 && (last == "actions" || last == "query")) ||
				last == "until" || last == "selector" {
				anchor = prefix
			}
		}
	}
	return anchor
}

func axValueAt(root any, segments []any) (any, bool) {
	value := root
	for _, segment := range segments {
		switch key := segment.(type) {
		case string:
			object, ok := value.(map[string]any)
			if !ok {
				return nil, false
			}
			if value, ok = object[key]; !ok {
				return nil, false
			}
		case int:
			items, ok := value.([]any)
			if !ok || key >= len(items) {
				return nil, false
			}
			value = items[key]
		}
	}
	return value, true
}

// axSetAt sets (or removes) the value at segments; the parent must exist.
func axSetAt(root map[string]any, segments []any, value any, remove bool) bool {
	if len(segments) == 0 {
		return false
	}
	parent, ok := axValueAt(root, segments[:len(segments)-1])
	if !ok {
		return false
	}
	switch key := segments[len(segments)-1].(type) {
	case string:
		object, ok := parent.(map[string]any)
		if !ok {
			return false
		}
		if remove {
			delete(object, key)
		} else {
			object[key] = value
		}
		return true
	case int:
		items, ok := parent.([]any)
		if !ok || key >= len(items) || remove {
			return false
		}
		items[key] = value
		return true
	}
	return false
}

// axEqualOutside reports whether a and b differ only inside anchor.
func axEqualOutside(a, b map[string]any, anchor []any) bool {
	left, _ := axCloneValue(a).(map[string]any)
	right, _ := axCloneValue(b).(map[string]any)
	if !axSetAt(left, anchor, nil, false) || !axSetAt(right, anchor, nil, false) {
		return false
	}
	return reflect.DeepEqual(left, right)
}

func axCloneValue(value any) any {
	switch typed := value.(type) {
	case map[string]any:
		clone := make(map[string]any, len(typed))
		for key, item := range typed {
			clone[key] = axCloneValue(item)
		}
		return clone
	case []any:
		clone := make([]any, len(typed))
		for index, item := range typed {
			clone[index] = axCloneValue(item)
		}
		return clone
	default:
		return value
	}
}

// axHintJSON encodes a hint with mode, type, ref, and identity first, as the
// tool description writes calls, and without HTML escaping ("&" stays "&").
func axHintJSON(value any) string {
	var out strings.Builder
	axWriteHintJSON(&out, value)
	return out.String()
}

func axWriteHintJSON(out *strings.Builder, value any) {
	switch typed := value.(type) {
	case map[string]any:
		keys := make([]string, 0, len(typed))
		for key := range typed {
			keys = append(keys, key)
		}
		rank := map[string]int{"mode": 1, "type": 2, "ref": 3, "app": 4, "window_ref": 5}
		sort.Slice(keys, func(i, j int) bool {
			ri, rj := rank[keys[i]], rank[keys[j]]
			if ri == 0 {
				ri = len(rank) + 1
			}
			if rj == 0 {
				rj = len(rank) + 1
			}
			if ri != rj {
				return ri < rj
			}
			return keys[i] < keys[j]
		})
		out.WriteByte('{')
		for index, key := range keys {
			if index > 0 {
				out.WriteByte(',')
			}
			axWriteHintJSON(out, key)
			out.WriteByte(':')
			axWriteHintJSON(out, typed[key])
		}
		out.WriteByte('}')
	case []any:
		out.WriteByte('[')
		for index, item := range typed {
			if index > 0 {
				out.WriteByte(',')
			}
			axWriteHintJSON(out, item)
		}
		out.WriteByte(']')
	default:
		var buffer bytes.Buffer
		encoder := json.NewEncoder(&buffer)
		encoder.SetEscapeHTML(false)
		if encoder.Encode(typed) != nil {
			out.WriteString("null")
			return
		}
		out.WriteString(strings.TrimSuffix(buffer.String(), "\n"))
	}
}

// axNumberEdit corrects a number field: a value above high becomes high, a
// fraction of an integer field is rounded, and anything else (below low,
// not a number) is removed so the default applies.
func axNumberEdit(path string, raw any, low, high float64, integer bool) axEdit {
	value, ok := axNumber(raw)
	if integer {
		value = math.Round(value)
	}
	if !ok || value < low {
		return axRemove(path)
	}
	return axSet(path, json.Number(strconv.FormatFloat(math.Min(value, high), 'f', -1, 64)))
}
