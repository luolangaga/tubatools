package transport

import (
	"strings"
	"testing"
)

func TestRuntimeNotesDescribeNativeRequirements(t *testing.T) {
	for platform, want := range map[string]string{
		"linux": "AT-SPI2", "darwin": "Screen Recording",
		"windows": "interactive user session", "unsupported": "No native desktop backend",
	} {
		if got := runtimeNotes(platform); !strings.Contains(got, want) {
			t.Errorf("%s notes = %q, missing %q", platform, got, want)
		}
	}
}
