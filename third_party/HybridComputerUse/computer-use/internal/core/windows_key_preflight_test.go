package core

import (
	"os"
	"regexp"
	"strings"
	"testing"
)

func TestWindowsKeyPreflightMatchesNativeVocabulary(t *testing.T) {
	source, err := os.ReadFile("../../native/windows/ComputerUse.Windows.Runtime/WindowsKeyMapping.cs")
	if err != nil {
		t.Fatal(err)
	}
	native := make(map[string]bool)
	for _, match := range regexp.MustCompile(`"([A-Z][A-Z_]+)"`).FindAllStringSubmatch(string(source), -1) {
		native[match[1]] = true
	}
	for name := range native {
		if !windowsKeyNames[name] {
			t.Errorf("native key %q is missing from Go preflight", name)
		}
	}
	for name := range windowsKeyNames {
		if !native[name] {
			t.Errorf("Go key %q is missing from native mapping", name)
		}
	}
}

func TestWindowsKeyPreflightChordBoundaries(t *testing.T) {
	for _, key := range []string{"ctrl+shift+a", "Win+ArrowLeft", "PAGE-UP", "-", "F1", "f24", "F01", "F 2", " ; "} {
		if err := validateWindowsKeypress(key); err != nil {
			t.Errorf("valid chord %q: %v", key, err)
		}
	}
	for _, key := range []string{"", " ", "+", "Ctrl++a", "a+", "UnknownKey", "F0", "F25", "\U0001f600",
		// A single character that is not an ASCII letter, digit or OEM key
		// has no virtual key; casting it would press an unrelated key.
		"!", "?", "_", "中", "é", "ı"} {
		if err := validateWindowsKeypress(key); err == nil {
			t.Errorf("invalid chord %q passed preflight", key)
		}
	}
}

func TestWindowsKeyPreflightRejectsLaterBatchKey(t *testing.T) {
	ax := []axBatchAction{{actionType: "click"}, {actionType: "keypress", key: "Ctrl+NotAKey"}}
	if err := preflightWindowsAXKeys(ax); err == nil || !strings.Contains(err.Error(), "actions[1].keys") {
		t.Fatalf("AX batch error = %v", err)
	}
	gui := []ProviderDesktopAction{{Type: "type", Text: "hello"}, {Type: "click", Keys: []string{"Ctrl", "NotAKey"}}}
	if err := preflightWindowsGUIKeys(gui); err == nil || !strings.Contains(err.Error(), "actions[1].keys") {
		t.Fatalf("GUI batch error = %v", err)
	}
}
