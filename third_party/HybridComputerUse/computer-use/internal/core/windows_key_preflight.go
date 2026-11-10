package core

import (
	"fmt"
	"strconv"
	"strings"
)

// Match WindowsKeyMapping before a batch can emit its first input event.
var windowsKeyNames = func() map[string]bool {
	names := strings.Fields(
		"CTRL CONTROL SHIFT ALT SUPER WIN META ENTER RETURN ESC ESCAPE TAB SPACE SPACEBAR " +
			"BACKSPACE DELETE LEFT ARROWLEFT UP ARROWUP RIGHT ARROWRIGHT DOWN ARROWDOWN " +
			"HOME END PAGE_UP PAGEUP PAGE_DOWN PAGEDOWN SEMICOLON EQUAL EQUALS COMMA MINUS " +
			"PERIOD DOT SLASH BACKTICK GRAVE LEFT_BRACKET BRACKET_LEFT BACKSLASH " +
			"RIGHT_BRACKET BRACKET_RIGHT QUOTE APOSTROPHE")
	result := make(map[string]bool, len(names))
	for _, name := range names {
		result[name] = true
	}
	return result
}()

func validateWindowsKeypress(specification string) error {
	for _, token := range strings.Split(specification, "+") {
		trimmed := strings.TrimSpace(token)
		normalized := strings.ReplaceAll(strings.ToUpper(trimmed), "-", "_")
		if windowsKeyNames[normalized] {
			continue
		}
		// One character maps to a virtual key only for ASCII letters,
		// digits and the OEM punctuation keys the native mapping names.
		if len(trimmed) == 1 {
			key := trimmed[0]
			if key >= 'a' && key <= 'z' || key >= 'A' && key <= 'Z' || key >= '0' && key <= '9' ||
				strings.ContainsRune(";=,-./`[\\]'", rune(key)) {
				continue
			}
		}
		if strings.HasPrefix(normalized, "F") {
			number, err := strconv.Atoi(strings.TrimSpace(normalized[1:]))
			if err == nil && number >= 1 && number <= 24 {
				continue
			}
		}
		return fmt.Errorf("unsupported Windows key %q", token)
	}
	return nil
}

func preflightWindowsAXKeys(actions []axBatchAction) error {
	for index, action := range actions {
		if action.actionType == "keypress" {
			if err := validateWindowsKeypress(action.key); err != nil {
				return fmt.Errorf("actions[%d].keys: %w", index, err)
			}
		}
	}
	return nil
}

func preflightWindowsGUIKeys(actions []ProviderDesktopAction) error {
	for index, action := range actions {
		for _, key := range action.Keys {
			if err := validateWindowsKeypress(key); err != nil {
				return fmt.Errorf("actions[%d].keys: %w", index, err)
			}
		}
	}
	return nil
}
