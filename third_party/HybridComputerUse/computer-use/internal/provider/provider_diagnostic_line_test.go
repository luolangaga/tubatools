//go:build linux || windows || windows_sidecar_test

package provider

import (
	"bufio"
	"errors"
	"io"
	"strings"
	"testing"
)

func TestReadProviderDiagnosticLineRecoversAfterOversize(t *testing.T) {
	first := strings.Repeat("x", 96)
	reader := bufio.NewReaderSize(
		strings.NewReader(first+"\nnext diagnostic\n"),
		8,
	)
	line, originalBytes, truncated, err :=
		readProviderDiagnosticLine(reader, 32)
	if err != nil {
		t.Fatal(err)
	}
	if string(line) != strings.Repeat("x", 32) {
		t.Fatalf("bounded diagnostic = %q", line)
	}
	if originalBytes != len(first) {
		t.Fatalf("original bytes = %d, want %d", originalBytes, len(first))
	}
	if !truncated {
		t.Fatal("oversized diagnostic was not marked truncated")
	}

	line, originalBytes, truncated, err =
		readProviderDiagnosticLine(reader, 32)
	if err != nil {
		t.Fatal(err)
	}
	if string(line) != "next diagnostic" || originalBytes != 15 || truncated {
		t.Fatalf(
			"next diagnostic = %q bytes=%d truncated=%v",
			line,
			originalBytes,
			truncated,
		)
	}
}

func TestReadProviderDiagnosticLineBoundaries(t *testing.T) {
	for _, test := range []struct {
		name      string
		raw       string
		limit     int
		want      string
		original  int
		truncated bool
		err       error
	}{
		{"empty", "", 8, "", 0, false, io.EOF},
		{"empty_line", "\n", 8, "", 0, false, nil},
		{"exact_limit", "abcdefgh\n", 8, "abcdefgh", 8, false, nil},
		{"unterminated", "abcdefgh", 8, "abcdefgh", 8, false, io.EOF},
		{"oversize_eof", strings.Repeat("x", 97), 32, strings.Repeat("x", 32), 97, true, io.EOF},
		{"zero_limit", "ab\n", 0, "a", 2, true, nil},
	} {
		t.Run(test.name, func(t *testing.T) {
			reader := bufio.NewReaderSize(strings.NewReader(test.raw), 16)
			line, original, truncated, err := readProviderDiagnosticLine(reader, test.limit)
			if string(line) != test.want || original != test.original ||
				truncated != test.truncated || !errors.Is(err, test.err) {
				t.Fatalf("line=%q original=%d truncated=%v err=%v", line, original, truncated, err)
			}
		})
	}
}
