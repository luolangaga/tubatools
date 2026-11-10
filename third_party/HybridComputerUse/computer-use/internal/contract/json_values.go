package contract

import (
	"encoding/json"
	"fmt"
	"math"
	"math/big"
	"sort"
	"strconv"
	"strings"
)

func SortedKeys[V any](object map[string]V) []string {
	keys := make([]string, 0, len(object))
	for key := range object {
		keys = append(keys, key)
	}
	sort.Strings(keys)
	return keys
}

func ContainsString(values []string, target string) bool {
	for _, value := range values {
		if value == target {
			return true
		}
	}
	return false
}

func containsJSON(values []any, target any) bool {
	for _, value := range values {
		if jsonEqual(value, target) {
			return true
		}
	}
	return false
}

func jsonEqual(left, right any) bool {
	leftNumber, leftIsNumber := jsonNumberRat(left)
	rightNumber, rightIsNumber := jsonNumberRat(right)
	if leftIsNumber || rightIsNumber {
		return leftIsNumber && rightIsNumber && leftNumber.Cmp(rightNumber) == 0
	}
	return CompactJSON(left) == CompactJSON(right)
}

func isContainer(value any) bool {
	switch value.(type) {
	case map[string]any, []any:
		return true
	default:
		return false
	}
}

func jsonNumberRat(value any) (*big.Rat, bool) {
	switch typed := value.(type) {
	case json.Number:
		return new(big.Rat).SetString(typed.String())
	case float64:
		if math.IsNaN(typed) || math.IsInf(typed, 0) {
			return nil, false
		}
		return new(big.Rat).SetFloat64(typed), true
	case int:
		return new(big.Rat).SetInt64(int64(typed)), true
	case int64:
		return new(big.Rat).SetInt64(typed), true
	default:
		return nil, false
	}
}

func CloneObject(object map[string]any) map[string]any {
	clone := make(map[string]any, len(object))
	for key, value := range object {
		clone[key] = value
	}
	return clone
}

func CompactJSON(value any) string {
	encoded, err := json.Marshal(value)
	if err != nil {
		return fmt.Sprint(value)
	}
	return string(encoded)
}

// OrderedJSON encodes an object with its branch-selecting fields first, so a
// hint reads {"type":"click","target":{"ref":"r1"}}.
func OrderedJSON(object map[string]any, first []string) string {
	var out strings.Builder
	out.WriteByte('{')
	written := 0
	write := func(key string) {
		if written > 0 {
			out.WriteByte(',')
		}
		out.WriteString(strconv.Quote(key))
		out.WriteByte(':')
		if nested, ok := object[key].(map[string]any); ok {
			out.WriteString(OrderedJSON(nested, first))
		} else {
			out.WriteString(CompactJSON(object[key]))
		}
		written++
	}
	for _, key := range first {
		if _, present := object[key]; present {
			write(key)
		}
	}
	for _, key := range SortedKeys(object) {
		if !ContainsString(first, key) {
			write(key)
		}
	}
	out.WriteByte('}')
	return out.String()
}
