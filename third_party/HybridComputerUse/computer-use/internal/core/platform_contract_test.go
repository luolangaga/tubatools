package core

import (
	"os"
	"strings"
	"testing"
)

func TestPlatformNeutralModulesDoNotOwnLinuxExecution(t *testing.T) {
	forbidden := []string{
		`"os/exec"`,
		`"/proc/`,
		`exec.Command`,
		`xprop`,
		`wmctrl`,
	}
	entries, err := os.ReadDir(".")
	if err != nil {
		t.Fatal(err)
	}
	checked := 0
	for _, entry := range entries {
		name := entry.Name()
		if !strings.HasSuffix(name, ".go") || strings.HasSuffix(name, "_test.go") {
			continue
		}
		body, err := os.ReadFile(name)
		if err != nil {
			t.Fatalf("read %s: %v", name, err)
		}
		// Files with a build constraint belong to one platform backend.
		if strings.HasPrefix(string(body), "//go:build ") {
			continue
		}
		checked++
		for _, marker := range forbidden {
			if strings.Contains(string(body), marker) {
				t.Fatalf("%s contains platform execution marker %q", name, marker)
			}
		}
	}
	if checked == 0 {
		t.Fatal("no platform-neutral source files were checked")
	}
}
