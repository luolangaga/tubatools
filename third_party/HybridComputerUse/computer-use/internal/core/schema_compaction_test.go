package core

import (
	"bytes"
	"encoding/json"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// Codex (openai/codex codex-rs/tools/src/json_schema.rs and
// json_schema/compaction.rs, revision ce0323f2f2d3b25e82806cd555237da3db4e1897)
// rewrites a tool input schema before the model sees it and,
// above 5,000 bytes, compacts it in up to four stages until it fits: strip
// descriptions, inline-drop $defs, collapse nesting at depth 3, prune
// oneOf/anyOf/allOf. This test reproduces those rules and reports each
// public tool's size after every stage. It only reports; it does not fail.

const codexSchemaBudget = 5000

var codexCompositions = []string{"anyOf", "oneOf", "allOf"}

var codexFieldOrder = []string{
	"$ref", "type", "description", "encrypted", "enum", "items", "minItems",
	"properties", "required", "additionalProperties", "anyOf", "oneOf", "allOf",
	"$defs", "definitions",
}

var codexPrimitiveTypes = map[string]bool{
	"string": true, "number": true, "boolean": true, "integer": true,
	"object": true, "array": true, "null": true,
}

type codexStage struct {
	name  string
	bytes int
}

func codexCompact(schema map[string]any) []codexStage {
	value := cloneJSON(jsonRoundTrip(schema)).(map[string]any)
	if _, ok := value["properties"]; !ok {
		value["properties"] = map[string]any{}
	}
	value = codexSanitize(value).(map[string]any)
	codexPruneDefs(value)
	stages := []codexStage{{"normalized", codexSize(value)}}
	for _, stage := range []struct {
		name string
		run  func(any) any
	}{
		{"strip_descriptions", func(v any) any { codexStripDescriptions(v); return v }},
		{"drop_defs", codexDropDefs},
		{"collapse_depth3", func(v any) any { return codexCollapse(v, 0) }},
		{"prune_compositions", codexPruneCompositions},
	} {
		if codexSize(value) <= codexSchemaBudget {
			break
		}
		value = stage.run(value).(map[string]any)
		stages = append(stages, codexStage{stage.name, codexSize(value)})
	}
	return stages
}

func jsonRoundTrip(value any) any {
	encoded, _ := json.Marshal(value)
	var decoded any
	_ = json.Unmarshal(encoded, &decoded)
	return decoded
}

func codexSanitize(value any) any {
	switch typed := value.(type) {
	case bool:
		return map[string]any{"type": "string"}
	case []any:
		for index, item := range typed {
			typed[index] = codexSanitize(item)
		}
		return typed
	case map[string]any:
		if properties, ok := typed["properties"].(map[string]any); ok {
			for key, item := range properties {
				properties[key] = codexSanitize(item)
			}
		}
		for _, key := range []string{"items", "prefixItems"} {
			if item, ok := typed[key]; ok {
				typed[key] = codexSanitize(item)
			}
		}
		if item, ok := typed["additionalProperties"]; ok {
			if _, isBool := item.(bool); !isBool {
				typed["additionalProperties"] = codexSanitize(item)
			}
		}
		for _, key := range codexCompositions {
			if item, ok := typed[key]; ok {
				typed[key] = codexSanitize(item)
			}
		}
		for _, key := range []string{"$defs", "definitions"} {
			if defs, ok := typed[key].(map[string]any); ok {
				for name, item := range defs {
					defs[name] = codexSanitize(item)
				}
			} else if _, present := typed[key]; present {
				delete(typed, key)
			}
		}
		if constant, ok := typed["const"]; ok {
			delete(typed, "const")
			typed["enum"] = []any{constant}
		}
		types := []string{}
		switch kind := typed["type"].(type) {
		case string:
			if codexPrimitiveTypes[kind] {
				types = []string{kind}
			}
		case []any:
			for _, item := range kind {
				if name, ok := item.(string); ok && codexPrimitiveTypes[name] {
					types = append(types, name)
				}
			}
		}
		hasComposition := false
		for _, key := range codexCompositions {
			if _, ok := typed[key]; ok {
				hasComposition = true
			}
		}
		if _, hasRef := typed["$ref"]; len(types) == 0 && (hasRef || hasComposition) {
			return typed
		}
		if len(types) == 0 {
			switch {
			case hasAny(typed, "properties", "required", "additionalProperties"):
				types = []string{"object"}
			case hasAny(typed, "items", "prefixItems"):
				types = []string{"array"}
			case hasAny(typed, "enum", "format"):
				types = []string{"string"}
			case hasAny(typed, "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf"):
				types = []string{"number"}
			default:
				for key := range typed {
					delete(typed, key)
				}
				return typed
			}
		}
		if len(types) == 1 {
			typed["type"] = types[0]
		} else {
			list := make([]any, len(types))
			for index, name := range types {
				list[index] = name
			}
			typed["type"] = list
		}
		if contract.ContainsString(types, "object") {
			if _, ok := typed["properties"]; !ok {
				typed["properties"] = map[string]any{}
			}
		}
		if contract.ContainsString(types, "array") {
			if _, ok := typed["items"]; !ok {
				typed["items"] = map[string]any{"type": "string"}
			}
		}
		return typed
	default:
		return value
	}
}

func hasAny(object map[string]any, keys ...string) bool {
	for _, key := range keys {
		if _, ok := object[key]; ok {
			return true
		}
	}
	return false
}

// codexChildren lists the subschema locations Codex walks.
func codexChildren(object map[string]any, includeDefs bool) [][2]string {
	var children [][2]string
	if properties, ok := object["properties"].(map[string]any); ok {
		for _, key := range contract.SortedKeys(properties) {
			children = append(children, [2]string{"properties", key})
		}
	}
	for _, key := range []string{"items", "anyOf", "oneOf", "allOf"} {
		if _, ok := object[key]; ok {
			children = append(children, [2]string{key, ""})
		}
	}
	if item, ok := object["additionalProperties"]; ok {
		if _, isBool := item.(bool); !isBool {
			children = append(children, [2]string{"additionalProperties", ""})
		}
	}
	if includeDefs {
		for _, key := range []string{"$defs", "definitions"} {
			if defs, ok := object[key].(map[string]any); ok {
				for _, name := range contract.SortedKeys(defs) {
					children = append(children, [2]string{key, name})
				}
			}
		}
	}
	return children
}

func codexGet(object map[string]any, child [2]string) any {
	if child[1] != "" {
		return object[child[0]].(map[string]any)[child[1]]
	}
	return object[child[0]]
}

func codexSet(object map[string]any, child [2]string, value any) {
	if child[1] != "" {
		object[child[0]].(map[string]any)[child[1]] = value
		return
	}
	object[child[0]] = value
}

func codexRefs(value any, refs *[]string, skipDefs bool) {
	switch typed := value.(type) {
	case []any:
		for _, item := range typed {
			codexRefs(item, refs, skipDefs)
		}
	case map[string]any:
		if ref, ok := typed["$ref"].(string); ok && strings.HasPrefix(ref, "#/$defs/") {
			*refs = append(*refs, ref[strings.LastIndex(ref, "/")+1:])
		}
		for _, child := range codexChildren(typed, !skipDefs) {
			codexRefs(codexGet(typed, child), refs, skipDefs)
		}
	}
}

func codexPruneDefs(value map[string]any) {
	var pending []string
	codexRefs(value, &pending, true)
	reachable := map[string]bool{}
	defs, _ := value["$defs"].(map[string]any)
	for len(pending) > 0 {
		name := pending[len(pending)-1]
		pending = pending[:len(pending)-1]
		if reachable[name] {
			continue
		}
		reachable[name] = true
		if def, ok := defs[name]; ok {
			codexRefs(def, &pending, false)
		}
	}
	if defs != nil {
		for name := range defs {
			if !reachable[name] {
				delete(defs, name)
			}
		}
		if len(defs) == 0 {
			delete(value, "$defs")
		}
	}
}

func codexNormalize(value any) any {
	switch typed := value.(type) {
	case []any:
		out := make([]any, len(typed))
		for index, item := range typed {
			out[index] = codexNormalize(item)
		}
		return out
	case map[string]any:
		out := orderedObject{}
		for _, key := range codexFieldOrder {
			item, ok := typed[key]
			if !ok {
				continue
			}
			switch key {
			case "items":
				item = codexNormalize(item)
			case "properties", "$defs", "definitions":
				if nested, ok := item.(map[string]any); ok {
					inner := orderedObject{}
					for _, name := range contract.SortedKeys(nested) {
						inner = append(inner, orderedField{name, codexNormalize(nested[name])})
					}
					item = inner
				}
			case "additionalProperties":
				if _, isBool := item.(bool); !isBool {
					item = codexNormalize(item)
				}
			case "anyOf", "oneOf", "allOf":
				item = codexNormalize(item)
			}
			out = append(out, orderedField{key, item})
		}
		return out
	default:
		return value
	}
}

type orderedField struct {
	key   string
	value any
}

type orderedObject []orderedField

func (object orderedObject) MarshalJSON() ([]byte, error) {
	var out bytes.Buffer
	out.WriteByte('{')
	for index, field := range object {
		if index > 0 {
			out.WriteByte(',')
		}
		key, _ := json.Marshal(field.key)
		value, err := marshalNoEscape(field.value)
		if err != nil {
			return nil, err
		}
		out.Write(key)
		out.WriteByte(':')
		out.Write(value)
	}
	out.WriteByte('}')
	return out.Bytes(), nil
}

func marshalNoEscape(value any) ([]byte, error) {
	var out bytes.Buffer
	encoder := json.NewEncoder(&out)
	encoder.SetEscapeHTML(false)
	if err := encoder.Encode(value); err != nil {
		return nil, err
	}
	return bytes.TrimRight(out.Bytes(), "\n"), nil
}

func codexSize(value any) int {
	encoded, _ := marshalNoEscape(codexNormalize(value))
	return len(encoded)
}

func codexStripDescriptions(value any) {
	switch typed := value.(type) {
	case []any:
		for _, item := range typed {
			codexStripDescriptions(item)
		}
	case map[string]any:
		delete(typed, "description")
		for _, child := range codexChildren(typed, true) {
			codexStripDescriptions(codexGet(typed, child))
		}
	}
}

func codexRewriteRefs(value any) any {
	switch typed := value.(type) {
	case []any:
		for index, item := range typed {
			typed[index] = codexRewriteRefs(item)
		}
		return typed
	case map[string]any:
		if ref, ok := typed["$ref"].(string); ok && strings.HasPrefix(ref, "#/$defs/") {
			return map[string]any{}
		}
		for _, child := range codexChildren(typed, false) {
			codexSet(typed, child, codexRewriteRefs(codexGet(typed, child)))
		}
		return typed
	default:
		return value
	}
}

func codexDropDefs(value any) any {
	value = codexRewriteRefs(value)
	object := value.(map[string]any)
	delete(object, "$defs")
	delete(object, "definitions")
	return object
}

func codexCollapse(value any, depth int) any {
	switch typed := value.(type) {
	case []any:
		for index, item := range typed {
			typed[index] = codexCollapse(item, depth)
		}
		return typed
	case map[string]any:
		if depth >= 3 && hasAny(typed, "items", "anyOf", "oneOf", "allOf", "properties", "additionalProperties", "$ref") {
			return map[string]any{}
		}
		for _, child := range codexChildren(typed, false) {
			codexSet(typed, child, codexCollapse(codexGet(typed, child), depth+1))
		}
		return typed
	default:
		return value
	}
}

func codexPruneCompositions(value any) any {
	switch typed := value.(type) {
	case []any:
		for index, item := range typed {
			typed[index] = codexPruneCompositions(item)
		}
		return typed
	case map[string]any:
		if hasAny(typed, codexCompositions...) {
			return map[string]any{}
		}
		for _, child := range codexChildren(typed, false) {
			codexSet(typed, child, codexPruneCompositions(codexGet(typed, child)))
		}
		return typed
	default:
		return value
	}
}

func itoa(value int) string {
	encoded, _ := json.Marshal(value)
	return string(encoded)
}

// TestAXSchemaSurvivesClientCompaction keeps the AX schema under the Codex
// compaction budget. Above it, Codex strips descriptions and then replaces
// nested objects with {}, so the model no longer sees the action fields and
// writes shapes the runtime rejects.
func TestAXSchemaSurvivesClientCompaction(t *testing.T) {
	ax := findToolDefinition(t, "AX")
	stages := codexCompact(ax.InputSchema)
	if len(stages) != 1 || stages[0].bytes > codexSchemaBudget {
		t.Fatalf("AX schema triggers client compaction: %+v", stages)
	}
	normalized := codexNormalize(codexSanitize(cloneJSON(jsonRoundTrip(ax.InputSchema)))).(orderedObject)
	encoded, err := marshalNoEscape(normalized)
	if err != nil {
		t.Fatal(err)
	}
	var visible map[string]any
	if err := json.Unmarshal(encoded, &visible); err != nil {
		t.Fatal(err)
	}
	properties := visible["properties"].(map[string]any)
	actionFields := properties["actions"].(map[string]any)["items"].(map[string]any)["properties"].(map[string]any)
	for _, field := range []string{"type", "ref", "title", "text", "keys", "value", "direction", "pages",
		"window", "query", "state", "gone", "settle", "timeout_ms"} {
		if _, ok := actionFields[field]; !ok {
			t.Fatalf("action field %q is not visible after normalization: %v", field, contract.SortedKeys(actionFields))
		}
	}
	types := actionFields["type"].(map[string]any)["enum"].([]any)
	if len(types) != len(contract.AXActionTypes) {
		t.Fatalf("action type enum lost values: %v", types)
	}
	query := visible["$defs"].(map[string]any)["query"].(map[string]any)["properties"].(map[string]any)
	for _, field := range contract.AXFindFilters {
		if _, ok := query[field]; !ok {
			t.Fatalf("query field %q is not visible: %v", field, contract.SortedKeys(query))
		}
	}
	if _, present := properties["until"]; present {
		t.Fatal("the removed until field is still published")
	}
	t.Logf("AX schema: raw=%d normalized=%d budget=%d", len(mustJSON(t, ax.InputSchema)), stages[0].bytes, codexSchemaBudget)
}

func mustJSON(t *testing.T, value any) []byte {
	t.Helper()
	encoded, err := json.Marshal(value)
	if err != nil {
		t.Fatal(err)
	}
	return encoded
}
