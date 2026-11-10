package core

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestTraceIsOffUnlessGivenAPath(t *testing.T) {
	// Tracing is enabled by giving a path, not a boolean: where to write is the
	// caller's decision, and a diagnostic facility should not pick a directory.
	t.Setenv("COMPUTER_USE_TRACE_FILE", "")
	if TracePath() != "" {
		t.Fatal("tracing must stay off without a path")
	}
	service := NewService()
	// While tracing is off, appending must have no side effects.
	service.appendTrace("gedit", AXServiceRequest{Action: AXClickCommand{}}, nil, nil, nil, false)
}

func TestTraceRecordsOneLinePerActionIncludingFailures(t *testing.T) {
	path := filepath.Join(t.TempDir(), "nested", "trace.jsonl")
	t.Setenv("COMPUTER_USE_TRACE_FILE", path)
	service := NewService()

	before := &AppSnapshot{WindowTitle: "VLC media player", Elements: make([]ElementRecord, 92)}
	after := &AppSnapshot{WindowTitle: "Simple Preferences", Elements: make([]ElementRecord, 61)}
	service.appendTrace("vlc", AXServiceRequest{
		Action:  AXClickCommand{},
		Element: &ElementRecord{Index: 7, ControlType: "menu item", Name: "Preferences"},
	}, before, after, []string{"[a11y] Resolved element_index 7 to menu item 'Preferences'."}, false)
	// Failures get a line too; recording only successes would show a smooth run
	// in which the step under investigation is missing.
	service.appendTrace("vlc", AXServiceRequest{
		Action: axSetValueCommand{Value: "x"},
	}, before, nil, nil, true)

	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("trace file was not written: %v", err)
	}
	lines := strings.Split(strings.TrimSpace(string(data)), "\n")
	if len(lines) != 2 {
		t.Fatalf("two actions should produce two lines, got %d", len(lines))
	}
	var first map[string]any
	if err := json.Unmarshal([]byte(lines[0]), &first); err != nil {
		t.Fatalf("first line is not valid JSON: %v", err)
	}
	beforeMap, _ := first["before"].(map[string]any)
	afterMap, _ := first["after"].(map[string]any)
	if beforeMap["window"] != "VLC media player" || afterMap["window"] != "Simple Preferences" {
		t.Fatalf("before/after state must show where the action took the UI, got %v -> %v", beforeMap, afterMap)
	}
	if beforeMap["elements"].(float64) != 92 || afterMap["elements"].(float64) != 61 {
		t.Fatal("trace must record element counts, the cheapest signal that the tree was rebuilt")
	}
	args, _ := first["args"].(map[string]any)
	if args["element_name"] != "Preferences" {
		t.Fatalf("action args must record the target element, got %v", args)
	}
	// The full record must not be copied, though: with whole trees the trace
	// would outgrow the transcript. Error context provides detail when needed.
	if strings.Contains(lines[0], "runtimeId") || strings.Contains(lines[0], "treeLines") {
		t.Fatal("trace must record a summary, not the whole tree")
	}

	var second map[string]any
	if err := json.Unmarshal([]byte(lines[1]), &second); err != nil {
		t.Fatalf("second line is not valid JSON: %v", err)
	}
	if second["failed"] != true {
		t.Fatal("failed actions must be marked")
	}
}
