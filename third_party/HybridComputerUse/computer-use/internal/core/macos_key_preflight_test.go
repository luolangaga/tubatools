package core

import (
	"encoding/json"
	"os"
	"reflect"
	"regexp"
	"strconv"
	"strings"
	"testing"
)

type macOSKeyPreflightBackend struct {
	*recordingPlatformBackend
}

func (macOSKeyPreflightBackend) HostInfo() HostBackendInfo {
	return HostBackendInfo{Platform: "darwin"}
}

func TestMacOSKeyPreflightRejectsWholeAXBatch(t *testing.T) {
	for _, keys := range [][]any{
		{"ARROWRIGHT"}, {"WAT"}, {"HYPER", "A"}, {"A", "B"}, {"SHIFT"}, {"+"},
		{"R\u0130GHT"}, {"SH\u0130FT", "A"}, {"I\u0307"}, {"\u017FHIFT", "A"},
		{"\uff32\uff29\uff27\uff28\uff34"}, {"R\tIGHT"}, {"R\u00a0IGHT"}, {"R\u200bIGHT"},
	} {
		for _, targeted := range []bool{false, true} {
			s, app, window, ref := sheetBatchService(t)
			recording := &recordingPlatformBackend{}
			s.Backend = macOSKeyPreflightBackend{recording}
			s.rememberFailedObservation(app, nil)
			invalid := map[string]any{"type": "keypress", "keys": keys}
			if targeted {
				invalid["ref"] = ref
			}
			result := s.callTool("AX", map[string]any{
				"mode": "act", "actions": []any{
					map[string]any{"type": "click", "ref": ref},
					map[string]any{"type": "keypress", "keys": []any{"SUPER", "A"}},
					map[string]any{"type": "type", "text": "preflight must prevent this"},
					invalid,
				},
			})
			if !result.IsError || len(recording.calls) != 0 {
				t.Fatalf("keys=%v targeted=%v sent calls=%v result=%#v",
					keys, targeted, recording.calls, result)
			}
			if !strings.Contains(ResultText(result), "unsupported macOS") &&
				!strings.Contains(ResultText(result), "supported macOS key") {
				t.Fatalf("did not reach platform preflight: %#v", result)
			}
			if !s.hasPendingFailedObservation(app) {
				t.Fatal("preflight consumed pending observation gate")
			}
			if _, err := s.resolveShortAXWindowRefOwned(window); err != nil {
				t.Fatalf("preflight invalidated window: %v", err)
			}
		}
	}
}

func TestMacOSKeyPreflightRejectsGUIBeforeLayoutRead(t *testing.T) {
	for _, tail := range []map[string]any{
		{"type": "keypress", "keys": []any{"ARROWRIGHT"}},
		{"type": "keypress", "keys": []any{"HYPER", "A"}},
		{"type": "keypress", "keys": []any{"R\u0130GHT"}},
		{"type": "keypress", "keys": []any{"SH\u0130FT", "A"}},
		{"type": "click", "x": 2, "y": 2, "keys": []any{"ENTER"}},
		{"type": "click", "x": 2, "y": 2, "keys": []any{"SH\u0130FT"}},
		{"type": "double_click", "x": 2, "y": 2, "keys": []any{"SH\u0130FT"}},
		{"type": "move", "x": 2, "y": 2, "keys": []any{"SH\u0130FT"}},
		{"type": "drag", "path": []any{map[string]any{"x": 1, "y": 1}, map[string]any{"x": 2, "y": 2}}, "keys": []any{"SH\u0130FT"}},
		{"type": "scroll", "x": 2, "y": 2, "scroll_y": 1, "keys": []any{"SH\u0130FT"}},
	} {
		s := NewServiceWithBackend(macOSKeyPreflightBackend{&recordingPlatformBackend{}})
		backend := s.Backend.(macOSKeyPreflightBackend).recordingPlatformBackend
		s.rememberFailedObservation("a1", nil)
		result := s.callTool("GUI", map[string]any{"actions": []any{
			map[string]any{"type": "click", "x": 1, "y": 1}, tail,
		}})
		if !result.IsError || len(backend.calls) != 0 {
			t.Fatalf("invalid GUI batch performed layout read/input: calls=%v result=%#v", backend.calls, result)
		}
		// Mouse modifiers are a published enum, so the schema rejects them
		// first; keypress names are free text and reach the macOS preflight.
		text := ResultText(result)
		if !strings.Contains(text, "unsupported macOS") &&
			!strings.Contains(text, "is unsupported; it must be CTRL, CONTROL, SHIFT, ALT, or SUPER") {
			t.Fatalf("did not reach a key preflight: %#v", result)
		}
		if !s.hasPendingFailedObservation("a1") {
			t.Fatal("GUI preflight consumed pending AX observation gate")
		}
	}
}

func TestMacOSKeyPreflightNativeVocabularyParity(t *testing.T) {
	data, err := os.ReadFile("../../native/macos/Sources/ComputerUseProviderKit/KeyMapping.swift")
	if err != nil {
		t.Fatal(err)
	}
	source := string(data)
	_, table, found := strings.Cut(source, "private static let keyCodeMap:")
	if !found {
		t.Fatal("native key map moved; update parity extraction explicitly")
	}
	literals := regexp.MustCompile(`(?m)^\s*("(?:\\.|[^"\\])*"):\s*CGKeyCode\(`).FindAllStringSubmatch(table, -1)
	native := map[string]bool{}
	for _, match := range literals {
		key, err := strconv.Unquote(match[1])
		if err != nil {
			t.Fatal(err)
		}
		native[key] = true
		if err := validateMacOSKeypress("SuPeR+SHIFT+" + key); err != nil {
			t.Fatalf("native key %q rejected: %v", key, err)
		}
	}
	if len(native) == 0 || !reflect.DeepEqual(native, macOSKeyNames) {
		t.Fatalf("Go/native key vocabulary drift: Go=%v native=%v", macOSKeyNames, native)
	}
	_, modifiers, found := strings.Cut(source, "private static func parseModifier(")
	if !found {
		t.Fatal("native modifier parser moved")
	}
	modifiers, _, found = strings.Cut(modifiers, "private static func normalize(")
	if !found {
		t.Fatal("native modifier parser boundary moved")
	}
	for _, line := range regexp.MustCompile(`(?m)^\s*case (.+):$`).FindAllStringSubmatch(modifiers, -1) {
		for _, quoted := range strings.Split(line[1], ", ") {
			token, err := strconv.Unquote(quoted)
			if err != nil || !isMacOSModifier(token) {
				t.Fatalf("native modifier %s unsupported by Go: %v", quoted, err)
			}
		}
	}
}

func TestMacOSKeyPreflightKeepsNativeNormalization(t *testing.T) {
	for _, key := range []string{
		"SUPER+RIGHT", "cmd+shift+,", " Alt + PAGE_UP ", "Control+KP_ADD",
		"Command+Shift+left bracket", "++cmd++a+", "META+OPTION+F12",
		"I", "\u212a", "\u212aP_ENTER", "R IGHT",
		"\t\u0085\u00a0\u2028\u2029\u3000\u200bRIGHT\u200b\u3000",
	} {
		if err := validateMacOSKeypress(key); err != nil {
			t.Errorf("%q: %v", key, err)
		}
	}
}

func TestMacOSKeyPreflightUnicodeLowercase(t *testing.T) {
	for input, expected := range map[string]string{
		"R\u0130GHT":        "ri\u0307ght",
		"SH\u0130FT":        "shi\u0307ft",
		"\u212aP_ENTER":     "kp_enter",
		"\u017f":            "\u017f",
		"\u039f\u03a3":      "\u03bf\u03c3",
		"\u200bRIGHT\u200b": "right",
		"R\u200bIGHT":       "r\u200bight",
	} {
		if got := normalizedMacOSKeyToken(input); got != expected {
			t.Errorf("normalize(%q)=%q, want %q", input, got, expected)
		}
	}
}

func TestMacOSKeyPreflightNativeDifferential(t *testing.T) {
	path := os.Getenv("COMPUTER_USE_MACOS_KEY_DIFFERENTIAL_FILE")
	if path == "" {
		t.Skip("requires separately bound native parser evidence")
	}
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	var samples []struct {
		Input      string `json:"input"`
		Normalized string `json:"normalized"`
		Key        bool   `json:"key"`
		Modifier   bool   `json:"modifier"`
	}
	if err := json.Unmarshal(data, &samples); err != nil {
		t.Fatal(err)
	}
	if len(samples) < 100 {
		t.Fatal("native differential corpus is incomplete")
	}
	for _, sample := range samples {
		if got := normalizedMacOSKeyToken(sample.Input); got != sample.Normalized {
			t.Errorf("native normalize mismatch %q: Go=%q native=%q", sample.Input, got, sample.Normalized)
		}
		if got := validateMacOSKeypress(sample.Input) == nil; got != sample.Key {
			t.Errorf("native key acceptance mismatch %q: Go=%v native=%v", sample.Input, got, sample.Key)
		}
		if got := isMacOSModifier(sample.Input); got != sample.Modifier {
			t.Errorf("native modifier acceptance mismatch %q: Go=%v native=%v", sample.Input, got, sample.Modifier)
		}
	}
}

func TestMacOSKeyPreflightValidInputStillDispatches(t *testing.T) {
	recording := &recordingPlatformBackend{}
	s := NewServiceWithBackend(macOSKeyPreflightBackend{recording})
	if err := s.preflightPlatformAXKeys([]axBatchAction{{actionType: "keypress", key: "SUPER+RIGHT"}}); err != nil {
		t.Fatal(err)
	}
	s.callTool("GUI", map[string]any{
		"actions": []any{map[string]any{"type": "keypress", "keys": []any{"SUPER", ","}}},
	})
	if len(recording.calls) != 1 || recording.calls[0] != "desktop_execute_batch" {
		t.Fatalf("valid GUI key failed to dispatch: %v", recording.calls)
	}
}

func TestMacOSKeyPreflightDoesNotRestrictOtherBackends(t *testing.T) {
	s := NewServiceWithBackend(&recordingPlatformBackend{})
	if err := s.preflightPlatformAXKeys([]axBatchAction{{actionType: "keypress", key: "F24"}}); err != nil {
		t.Fatal(err)
	}
	if err := s.preflightPlatformGUIKeys([]ProviderDesktopAction{{Type: "keypress", Keys: []string{"F24"}}}); err != nil {
		t.Fatal(err)
	}
}

func TestMacOSKeyPreflightPreservesAcceptedPayload(t *testing.T) {
	s := NewServiceWithBackend(macOSKeyPreflightBackend{&recordingPlatformBackend{}})
	for _, key := range []string{"I", "\u212a", "\u212aP_ENTER", "\u200bRIGHT\u200b", " Alt + PAGE_UP "} {
		ax := []axBatchAction{{actionType: "keypress", key: key}}
		gui := []ProviderDesktopAction{{Type: "keypress", Keys: []string{key}}}
		if err := s.preflightPlatformAXKeys(ax); err != nil {
			t.Fatal(err)
		}
		if err := s.preflightPlatformGUIKeys(gui); err != nil {
			t.Fatal(err)
		}
		if ax[0].key != key || !reflect.DeepEqual(gui[0].Keys, []string{key}) {
			t.Fatalf("preflight rewrote accepted key %q", key)
		}
	}
}
