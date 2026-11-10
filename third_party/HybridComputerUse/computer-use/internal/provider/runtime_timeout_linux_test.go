//go:build linux

package provider

import (
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func TestRuntimeTimeoutDiagnosticsSeparateProcessWindowAndBridgeState(t *testing.T) {
	cases := []struct {
		name   string
		text   string
		needle string
	}{
		{
			name:   "no cached identity",
			text:   core.FormatRuntimeTimeoutDiagnostic(0, false, false, false, false),
			needle: "a11y_responsive=false cached_pid_present=unknown window_for_cached_pid=unknown",
		},
		{
			name:   "process exited",
			text:   core.FormatRuntimeTimeoutDiagnostic(42, true, false, true, false),
			needle: "a11y_responsive=false cached_pid_present=false window_for_cached_pid=false cached_pid=42",
		},
		{
			name:   "bridge hung with window",
			text:   core.FormatRuntimeTimeoutDiagnostic(42, true, true, true, true),
			needle: "a11y_responsive=false cached_pid_present=true window_for_cached_pid=true cached_pid=42",
		},
		{
			name:   "process rebuilding window",
			text:   core.FormatRuntimeTimeoutDiagnostic(42, true, true, true, false),
			needle: "a11y_responsive=false cached_pid_present=true window_for_cached_pid=false cached_pid=42",
		},
	}
	for _, testCase := range cases {
		if !strings.Contains(testCase.text, testCase.needle) {
			t.Fatalf("%s diagnostic missing %q: %s",
				testCase.name, testCase.needle, testCase.text)
		}
	}

	root := t.TempDir()
	if processPIDExists(123, root) {
		t.Fatal("missing proc pid must report false")
	}
	if err := os.Mkdir(filepath.Join(root, "123"), 0o755); err != nil {
		t.Fatal(err)
	}
	if !processPIDExists(123, root) {
		t.Fatal("existing proc pid directory must report true")
	}
	if err := os.WriteFile(
		filepath.Join(root, "123", "stat"),
		[]byte("123 (vlc media player) Z 1 2 3 4"),
		0o644,
	); err != nil {
		t.Fatal(err)
	}
	if processPIDExists(123, root) || processPIDState(123, root) != "zombie" {
		t.Fatal("a zombie proc entry must not be treated as a live application")
	}
	if text := core.FormatZombieRuntimeDiagnostic(123); !strings.Contains(
		text,
		"cached_pid_present=zombie",
	) || !strings.Contains(text, "cannot become ready") {
		t.Fatalf("zombie diagnostic is not actionable: %s", text)
	}
	if err := os.WriteFile(
		filepath.Join(root, "123", "stat"),
		[]byte("123 (normal app) S 1 2 3 4"),
		0o644,
	); err != nil {
		t.Fatal(err)
	}
	if !processPIDExists(123, root) || processPIDState(123, root) != "running" {
		t.Fatal("a non-zombie proc entry must remain live")
	}
	output := "0x01200007  0  123 host Window A\n0x01400007  0  999 host Window B\n"
	if !wmctrlOutputHasPID(output, 123) || wmctrlOutputHasPID(output, 321) {
		t.Fatal("wmctrl PID parser returned the wrong liveness result")
	}
}
