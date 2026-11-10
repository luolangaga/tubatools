package core

import (
	"fmt"
	"strings"
	"unicode"

	"golang.org/x/text/cases"
	"golang.org/x/text/language"
)

// Keep this vocabulary aligned with the immutable native KeyPressParser.
// The parity test reads its complete map; no live provider call is needed.
var macOSKeyNames = func() map[string]bool {
	names := strings.Fields(
		"a b c d e f g h i j k l m n o p q r s t u v w x y z " +
			"0 1 2 3 4 5 6 7 8 9 " +
			"- minus hyphen = equal [ leftbracket left_bracket ] rightbracket right_bracket " +
			"\\ backslash ; semicolon ' quote apostrophe , comma . period dot / slash ` grave backtick " +
			"return enter tab space spacebar escape esc backspace delete del forwarddelete insert " +
			"up down left right home end pageup page_up prior pagedown page_down next caps_lock " +
			"f1 f2 f3 f4 f5 f6 f7 f8 f9 f10 f11 f12 " +
			"kp_0 kp_1 kp_2 kp_3 kp_4 kp_5 kp_6 kp_7 kp_8 kp_9 " +
			"kp_enter kp_equal kp_multiply kp_add kp_subtract kp_decimal kp_divide kp_delete " +
			"kp_home kp_left kp_up kp_right kp_down kp_prior kp_page_up kp_next kp_page_down kp_end kp_insert")
	result := make(map[string]bool, len(names))
	for _, name := range names {
		result[name] = true
	}
	return result
}()

func normalizedMacOSKeyToken(token string) string {
	// Foundation also trims ZERO WIDTH SPACE. Swift lowercased uses full
	// scalar mappings, without contextual final-sigma handling.
	trimmed := strings.TrimFunc(token, func(r rune) bool {
		return unicode.IsSpace(r) || r == '\u200b'
	})
	lowered := cases.Lower(language.Und, cases.HandleFinalSigma(false)).String(trimmed)
	return strings.ReplaceAll(lowered, " ", "")
}

func isMacOSModifier(token string) bool {
	switch normalizedMacOSKeyToken(token) {
	case "cmd", "command", "super", "meta", "shift", "option", "alt", "control", "ctrl":
		return true
	default:
		return false
	}
}

func validateMacOSKeypress(specification string) error {
	tokens := []string{}
	for _, token := range strings.Split(specification, "+") {
		if normalized := normalizedMacOSKeyToken(token); normalized != "" {
			tokens = append(tokens, normalized)
		}
	}
	if len(tokens) == 0 {
		return fmt.Errorf("keys must contain a supported macOS key")
	}
	for _, token := range tokens[:len(tokens)-1] {
		if !isMacOSModifier(token) {
			return fmt.Errorf("unsupported macOS modifier %q", token)
		}
	}
	if token := tokens[len(tokens)-1]; !macOSKeyNames[token] {
		return fmt.Errorf("unsupported macOS key %q", token)
	}
	return nil
}

func (s *Service) preflightPlatformAXKeys(actions []axBatchAction) error {
	if s.platformBackend().HostInfo().Platform != "darwin" {
		return nil
	}
	for index, action := range actions {
		if action.actionType == "keypress" {
			if err := validateMacOSKeypress(action.key); err != nil {
				return fmt.Errorf("actions[%d].keys: %w", index, err)
			}
		}
	}
	return nil
}

func (s *Service) preflightPlatformGUIKeys(actions []ProviderDesktopAction) error {
	if s.platformBackend().HostInfo().Platform != "darwin" {
		return nil
	}
	for index, action := range actions {
		if action.Type == "keypress" {
			if err := validateMacOSKeypress(strings.Join(action.Keys, "+")); err != nil {
				return fmt.Errorf("actions[%d].keys: %w", index, err)
			}
			continue
		}
		switch action.Type {
		case "click", "double_click", "move", "drag", "scroll":
			for _, key := range action.Keys {
				if !isMacOSModifier(key) {
					return fmt.Errorf("actions[%d].keys: unsupported macOS modifier %q", index, key)
				}
			}
		}
	}
	return nil
}
