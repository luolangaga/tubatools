package core

import (
	"strings"
	"unicode"
)

func normalizedTreeText(value string) string {
	return strings.Join(strings.Fields(value), " ")
}

func comparableTreeText(value string) string {
	return strings.ToLower(normalizedTreeText(value))
}

func normalizedSemanticWhitespace(value string) string {
	return strings.Join(strings.Fields(value), " ")
}

func equalSemanticIdentity(left, right string) bool {
	return normalizedSemanticWhitespace(left) ==
		normalizedSemanticWhitespace(right)
}

func canonicalRole(record *ElementRecord) string {
	if record == nil {
		return ""
	}
	role := strings.TrimSpace(record.LocalizedControlType)
	if role == "" {
		role = strings.TrimSpace(record.ControlType)
	}
	return strings.ToLower(role)
}

func normalizedActionName(action string) string {
	var out strings.Builder
	for _, char := range strings.ToLower(action) {
		if unicode.IsLetter(char) || unicode.IsDigit(char) {
			out.WriteRune(char)
		}
	}
	return out.String()
}

func recordStateTokens(record *ElementRecord) []string {
	if record == nil {
		return nil
	}
	return strings.Fields(strings.Trim(
		strings.TrimSpace(record.States),
		"[]",
	))
}

func hasMeaningfulWorkingClick(record *ElementRecord) bool {
	if record == nil ||
		!roleHasMeaningfulClickCapability(canonicalRole(record)) {
		return false
	}
	for _, state := range recordStateTokens(record) {
		if state == "has-click-action" {
			return true
		}
	}
	return false
}

func roleHasMeaningfulClickCapability(role string) bool {
	for _, fragment := range []string{
		"button",
		"check box",
		"combo box",
		"entry",
		"icon",
		"link",
		"list box",
		"list item",
		"menu item",
		"page tab",
		"radio",
		"scroll bar",
		"searchbox",
		"slider",
		"spin button",
		"textarea",
		"textfield",
		"toggle",
		"tree item",
	} {
		if strings.Contains(role, fragment) {
			return true
		}
	}
	return false
}
