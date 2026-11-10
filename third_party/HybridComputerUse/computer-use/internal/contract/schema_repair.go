package contract

import (
	"github.com/santhosh-tekuri/jsonschema/v6"
	"strconv"
	"strings"
)

// repairObject applies the corrections a caller most often needs: a
// discriminator written under another key ({"action":"close"}), a field that
// belongs in a nested object ({"ref":"r1"} instead of {"target":{"ref":"r1"}}),
// a string where a one-field object is required, and case or surrounding
// whitespace in a const value. It reports the corrected object only when the
// result validates.
func repairObject(schema *jsonschema.Schema, object map[string]any, synonyms map[string]string) (map[string]any, bool) {
	branches := schema.OneOf
	discriminators := ConstProperties(branches)
	candidate := CloneObject(object)
	changed := false
	for round := 0; round < 3; round++ {
		if schema.Validate(candidate) == nil {
			return candidate, changed
		}
		progress := false
		for _, key := range discriminators {
			values := constValues(branches, key)
			if current, present := candidate[key]; present {
				if text, ok := current.(string); ok {
					if mapped := synonyms[text]; mapped != "" && containsJSON(values, mapped) {
						candidate[key] = mapped
						progress = true
						continue
					}
					for _, value := range values {
						exact, _ := value.(string)
						if exact != text && strings.EqualFold(strings.TrimSpace(text), exact) {
							candidate[key] = exact
							progress = true
						}
					}
				}
				continue
			}
			for _, other := range SortedKeys(candidate) {
				if declaredInAnyBranch(branches, other) {
					continue
				}
				text, ok := candidate[other].(string)
				if !ok {
					continue
				}
				if !containsJSON(values, text) && containsJSON(values, synonyms[text]) {
					text = synonyms[text]
				}
				if containsJSON(values, text) {
					candidate[key] = text
					delete(candidate, other)
					progress = true
					break
				}
			}
		}
		branch := SelectByDiscriminator(branches, discriminators, candidate)
		if branch == nil {
			if !progress {
				break
			}
			changed = true
			continue
		}
		properties := resolved(branch).Properties
		for _, key := range SortedKeys(candidate) {
			if _, declared := properties[key]; declared {
				if text, ok := candidate[key].(string); ok {
					if wrapped, ok := wrapStringInObject(properties[key], text); ok {
						candidate[key] = wrapped
						progress = true
					}
				}
				continue
			}
			for _, parent := range SortedKeys(properties) {
				if !objectDeclares(properties[parent], key) {
					continue
				}
				nested, _ := candidate[parent].(map[string]any)
				if nested == nil {
					if _, present := candidate[parent]; present {
						continue
					}
					nested = map[string]any{}
				}
				if _, taken := nested[key]; taken {
					continue
				}
				nested = CloneObject(nested)
				nested[key] = candidate[key]
				candidate[parent] = nested
				delete(candidate, key)
				progress = true
				break
			}
		}
		if !progress {
			break
		}
		changed = true
	}
	return candidate, changed && schema.Validate(candidate) == nil
}

// wrapStringInObject turns "r1" into {"ref":"r1"} when the expected object
// has exactly one required field and the string is valid for it.
func wrapStringInObject(schema *jsonschema.Schema, text string) (map[string]any, bool) {
	schema = resolved(schema)
	candidates := []*jsonschema.Schema{schema}
	if len(schema.OneOf) > 0 {
		candidates = schema.OneOf
	}
	for _, candidate := range candidates {
		candidate = resolved(candidate)
		if len(candidate.Required) != 1 {
			continue
		}
		wrapped := map[string]any{candidate.Required[0]: text}
		if candidate.Validate(wrapped) == nil && schema.Validate(wrapped) == nil {
			return wrapped, true
		}
	}
	return nil, false
}

// objectExample renders a one-field object schema as {"ref":"rN"}, using the
// field's pattern for the placeholder.
func objectExample(schema *jsonschema.Schema) string {
	schema = resolved(schema)
	if schema.Types == nil || !ContainsString(schema.Types.ToStrings(), "object") ||
		len(schema.Required) != 1 {
		return ""
	}
	field := schema.Required[0]
	placeholder := "..."
	if property := schema.Properties[field]; property != nil && resolved(property).Pattern != nil {
		switch pattern := resolved(property).Pattern.String(); {
		case strings.HasPrefix(pattern, "^r"), strings.HasPrefix(pattern, "^(r"),
			strings.HasPrefix(pattern, "^(?:r"):
			placeholder = "rN"
		case strings.HasPrefix(pattern, "^w"):
			placeholder = "wN"
		case strings.HasPrefix(pattern, "^a"):
			placeholder = "aN"
		}
	}
	return "{" + strconv.Quote(field) + ":" + strconv.Quote(placeholder) + "}"
}

func objectDeclares(schema *jsonschema.Schema, key string) bool {
	schema = resolved(schema)
	if _, declared := schema.Properties[key]; declared {
		return true
	}
	for _, branch := range schema.OneOf {
		if _, declared := resolved(branch).Properties[key]; declared {
			return true
		}
	}
	return false
}

func declaredInAnyBranch(branches []*jsonschema.Schema, key string) bool {
	for _, branch := range branches {
		if _, declared := resolved(branch).Properties[key]; declared {
			return true
		}
	}
	return false
}
