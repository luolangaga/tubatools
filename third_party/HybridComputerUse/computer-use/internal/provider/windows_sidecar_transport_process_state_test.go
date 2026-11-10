//go:build windows || windows_sidecar_test

package provider

import (
	"strings"
	"testing"
)

func TestWindowsTasklistProcessState(t *testing.T) {
	for _, test := range []struct {
		name   string
		output string
		pid    int
		want   string
	}{
		{"exact PID", "\"editor.exe\",\"123\",\"Console\",\"1\",\"12,345 K\"\r\n", 123, "running"},
		{"quoted image", "\"my, \"\"editor\"\".exe\",\"123\",\"Console\",\"1\",\"12 K\"\n", 123, "running"},
		{"empty", "", 123, "missing"},
		{"English notice", "INFO: No tasks are running which match the specified criteria.\r\n", 123, "missing"},
		{"localized notice", "INFORMATION: Keine Aufgaben mit den angegebenen Kriterien vorhanden.\r\n", 123, "missing"},
		{"CJK notice", "\u4fe1\u606f: \u6ca1\u6709\u8fd0\u884c\u7684\u4efb\u52a1\u5339\u914d\u6307\u5b9a\u6807\u51c6\u3002\r\n", 123, "missing"},
		{"unrelated PID", "\"editor.exe\",\"124\",\"Console\",\"1\",\"12 K\"\n", 123, "missing"},
		{"PID prefix", "\"editor.exe\",\"1234\",\"Console\",\"1\",\"12 K\"\n", 123, "missing"},
		{"PID in another column", "\"123.exe\",\"456\",\"Console\",\"123\",\"123 K\"\n", 123, "missing"},
		{"second row match", "\"a.exe\",\"456\",\"Console\",\"1\",\"12 K\"\n\"b.exe\",\"123\",\"Console\",\"1\",\"12 K\"\n", 123, "running"},
		{"invalid PID field", "\"editor.exe\",\"not-a-pid\",\"Console\",\"1\",\"12 K\"\n", 123, "unknown"},
		{"overflow PID", "\"editor.exe\",\"999999999999999999999\",\"Console\",\"1\",\"12 K\"\n", 123, "unknown"},
		{"malformed CSV", "\"editor.exe\",\"123", 123, "unknown"},
		{"malformed tail after match", "\"editor.exe\",\"123\",\"Console\",\"1\",\"12 K\"\n\"broken", 123, "unknown"},
		{"oversized output", strings.Repeat("x", maxWindowsTasklistOutputBytes+1), 123, "unknown"},
		{"invalid requested PID", "", 0, "missing"},
	} {
		t.Run(test.name, func(t *testing.T) {
			if got := windowsTasklistProcessState([]byte(test.output), test.pid); got != test.want {
				t.Fatalf("state = %q, want %q", got, test.want)
			}
		})
	}
}
