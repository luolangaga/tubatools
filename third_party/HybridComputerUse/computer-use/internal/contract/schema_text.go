package contract

import (
	"fmt"
	"strconv"
	"strings"
)

func jsonType(value any) string {
	switch typed := value.(type) {
	case nil:
		return "null"
	case bool:
		return "boolean"
	case string:
		return "string"
	case []any:
		return "array"
	case map[string]any:
		return "object"
	default:
		if number, ok := jsonNumberRat(typed); ok && number.IsInt() {
			return "integer"
		}
		return "number"
	}
}

func jsonTypeArticle(value any) string {
	switch jsonType(value) {
	case "null":
		return "null"
	case "integer", "number":
		return "a number"
	case "array", "object":
		return "an " + jsonType(value)
	default:
		return "a " + jsonType(value)
	}
}

func typeNames(types []string) string {
	names := make([]string, 0, len(types))
	for _, name := range types {
		switch name {
		case "array", "object", "integer":
			names = append(names, "an "+name)
		case "null":
			names = append(names, "null")
		default:
			names = append(names, "a "+name)
		}
	}
	return joinWords(names, "or")
}

func patternDescription(pattern string) string {
	switch pattern {
	case "^r[1-9][0-9]*$":
		return "an element ref rN copied from observe/find"
	case "^w[1-9][0-9]*$":
		return "a window ref wN copied from observe/find"
	case "^a[1-9][0-9]*$":
		return "an app ref aN copied from observe"
	case "^f[1-9][0-9]*$":
		return "a find cursor fN copied from find"
	case "^(?:r|w)[1-9][0-9]*$", "^(r[1-9][0-9]*|w[1-9][0-9]*)$":
		return "an rN or wN ref copied from observe/find"
	case `\S`:
		return "non-blank"
	}
	return "a string matching " + pattern
}

func countRange(minimum, maximum int, noun string) string {
	plural := func(count int) string {
		if count == 1 {
			return noun
		}
		return noun + "s"
	}
	switch {
	case minimum >= 0 && maximum >= 0 && minimum == maximum:
		return fmt.Sprintf("exactly %d %s", minimum, plural(minimum))
	case minimum > 0 && maximum >= 0:
		return fmt.Sprintf("%d to %d %s", minimum, maximum, plural(maximum))
	case maximum >= 0:
		return fmt.Sprintf("at most %d %s", maximum, plural(maximum))
	default:
		return fmt.Sprintf("at least %d %s", minimum, plural(minimum))
	}
}

// pathLabel renders an instance location as actions[0].target.ref; the
// top level is named after the tool.
func pathLabel(path []string, tool string) string {
	if len(path) == 0 {
		if tool == "" {
			return "arguments"
		}
		return tool
	}
	var label strings.Builder
	for index, segment := range path {
		if _, err := strconv.Atoi(segment); err == nil {
			label.WriteString("[" + segment + "]")
			continue
		}
		if index > 0 {
			label.WriteByte('.')
		}
		label.WriteString(segment)
	}
	return label.String()
}

func jsonPointer(path []string) string {
	if len(path) == 0 {
		return ""
	}
	escaped := make([]string, len(path))
	for index, segment := range path {
		segment = strings.ReplaceAll(segment, "~", "~0")
		escaped[index] = strings.ReplaceAll(segment, "/", "~1")
	}
	return "/" + strings.Join(escaped, "/")
}

func AppendPath(path []string, segment string) []string {
	next := make([]string, len(path), len(path)+1)
	copy(next, path)
	return append(next, segment)
}

func joinWords(words []string, conjunction string) string {
	switch len(words) {
	case 0:
		return ""
	case 1:
		return words[0]
	case 2:
		return words[0] + " " + conjunction + " " + words[1]
	default:
		return strings.Join(words[:len(words)-1], ", ") + ", " + conjunction + " " + words[len(words)-1]
	}
}

func stringValues(values []any) []string {
	out := make([]string, len(values))
	for index, value := range values {
		if text, ok := value.(string); ok {
			out[index] = text
		} else {
			out[index] = CompactJSON(value)
		}
	}
	return out
}
