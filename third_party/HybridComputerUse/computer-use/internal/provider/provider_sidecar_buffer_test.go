//go:build darwin || windows || darwin_sidecar_test || windows_sidecar_test

package provider

import (
	"strings"
	"testing"
)

func TestBoundedProviderBufferKeepsTail(t *testing.T) {
	for _, test := range []struct {
		name      string
		limit     int
		writes    []string
		want      string
		truncated bool
	}{
		{"fits", 8, []string{"abc", "def"}, "abcdef", false},
		{"exact", 6, []string{"abc", "def"}, "abcdef", false},
		{"rotates_across_writes", 5, []string{"abc", "def"}, "bcdef", true},
		{"single_oversize_write", 4, []string{"abcdefgh"}, "efgh", true},
		{"write_equal_to_limit", 4, []string{"xy", "abcd"}, "abcd", true},
		{"zero_limit_empty_write", 0, []string{""}, "", false},
		{"zero_limit_drops", 0, []string{"abc"}, "", true},
	} {
		t.Run(test.name, func(t *testing.T) {
			buffer := &boundedProviderBuffer{limit: test.limit}
			for _, write := range test.writes {
				if n, err := buffer.Write([]byte(write)); n != len(write) || err != nil {
					t.Fatalf("Write(%q) = %d, %v", write, n, err)
				}
			}
			data, truncated := buffer.snapshot()
			if string(data) != test.want || truncated != test.truncated {
				t.Fatalf("snapshot = %q truncated=%v, want %q %v",
					data, truncated, test.want, test.truncated)
			}
		})
	}
}

func TestBoundedProviderLabel(t *testing.T) {
	long := strings.Repeat("x", 300)
	for _, test := range []struct{ in, want string }{
		{"  req-1 \n", "req-1"},
		{"", ""},
		{strings.Repeat("y", 256), strings.Repeat("y", 256)},
		{" " + long + " ", long[:256]},
	} {
		if got := boundedProviderLabel(test.in); got != test.want {
			t.Fatalf("boundedProviderLabel(%d bytes) = %d bytes %q", len(test.in), len(got), got)
		}
	}
}
