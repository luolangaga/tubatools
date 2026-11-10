package contract

import (
	"github.com/santhosh-tekuri/jsonschema/v6"
	"sort"
)

// SelectByDiscriminator returns the branch whose const properties agree with
// the object, preferring branches whose consts are all present, then the
// most matched consts, then the most present required and declared fields.
func SelectByDiscriminator(branches []*jsonschema.Schema, discriminators []string, object map[string]any) *jsonschema.Schema {
	var best *jsonschema.Schema
	bestScore := -1
	for _, branch := range branches {
		properties := resolved(branch).Properties
		compatible := true
		complete := 1
		matched := 0
		for _, key := range discriminators {
			property := properties[key]
			if property == nil || resolved(property).Const == nil {
				continue
			}
			value, present := object[key]
			if !present {
				if ContainsString(resolved(branch).Required, key) {
					complete = 0
				}
				continue
			}
			if !jsonEqual(*resolved(property).Const, value) {
				compatible = false
				break
			}
			matched++
		}
		if !compatible {
			continue
		}
		required, declared := 0, 0
		for _, key := range resolved(branch).Required {
			if _, present := object[key]; present {
				required++
			}
		}
		for key := range object {
			if _, ok := properties[key]; ok {
				declared++
			}
		}
		score := complete<<24 | matched<<16 | required<<8 | declared
		if score > bestScore {
			best, bestScore = branch, score
		}
	}
	return best
}

// ConstProperties lists the property names that carry a const in at least
// two branches; those are the fields that select a branch.
func ConstProperties(branches []*jsonschema.Schema) []string {
	counts := map[string]int{}
	for _, branch := range branches {
		for key, property := range resolved(branch).Properties {
			if resolved(property).Const != nil {
				counts[key]++
			}
		}
	}
	keys := []string{}
	for key, count := range counts {
		if count >= 2 {
			keys = append(keys, key)
		}
	}
	// Fields present in every branch decide first.
	sort.Slice(keys, func(i, j int) bool {
		if counts[keys[i]] != counts[keys[j]] {
			return counts[keys[i]] > counts[keys[j]]
		}
		return keys[i] < keys[j]
	})
	return keys
}

func constBranchCount(branches []*jsonschema.Schema, key string) int {
	count := 0
	for _, branch := range branches {
		if property := resolved(branch).Properties[key]; property != nil &&
			resolved(property).Const != nil {
			count++
		}
	}
	return count
}

func constPropertyNames(branches []*jsonschema.Schema) []string {
	return ConstProperties(branches)
}

// constValues returns the distinct const values of key in branch order.
func constValues(branches []*jsonschema.Schema, key string) []any {
	values := []any{}
	for _, branch := range branches {
		property := resolved(branch).Properties[key]
		if property == nil || resolved(property).Const == nil {
			continue
		}
		value := *resolved(property).Const
		if !containsJSON(values, value) {
			values = append(values, value)
		}
	}
	return values
}

func requiredAlternatives(branches []*jsonschema.Schema) []string {
	keys := []string{}
	for _, branch := range branches {
		for _, key := range resolved(branch).Required {
			if !ContainsString(keys, key) {
				keys = append(keys, key)
			}
		}
	}
	return keys
}

// ShapeBranches reports whether the branches are alternative object shapes
// rather than constraints over one shape (such as "exactly one identity").
func ShapeBranches(branches []*jsonschema.Schema) bool {
	for _, branch := range branches {
		if resolved(branch).Types == nil {
			return false
		}
	}
	return true
}

func oneOfHolds(branches []*jsonschema.Schema, value any) bool {
	return countValid(branches, value) == 1
}

func countValid(branches []*jsonschema.Schema, value any) int {
	count := 0
	for _, branch := range branches {
		if branch.Validate(value) == nil {
			count++
		}
	}
	return count
}

func resolved(schema *jsonschema.Schema) *jsonschema.Schema {
	for schema != nil && schema.Ref != nil && schema.Types == nil &&
		len(schema.Properties) == 0 && len(schema.OneOf) == 0 {
		schema = schema.Ref
	}
	return schema
}

func typeAllowed(types []string, value any) bool {
	actual := jsonType(value)
	for _, expected := range types {
		if expected == actual || (expected == "number" && actual == "integer") {
			return true
		}
	}
	return false
}
