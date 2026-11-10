//go:build !windows

package transport

import (
	"fmt"
	"os"
	"path/filepath"
	"testing"
)

// shortSocket 使用足够短的目录，避免 macOS 的 sun_path 上限。
func shortSocket(t *testing.T) string {
	t.Helper()
	dir, err := os.MkdirTemp("/tmp", "hcu")
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { os.RemoveAll(dir) })
	return filepath.Join(dir, "d.sock")
}

func checkDaemonSocketPermissions(t *testing.T, socket string) {
	t.Helper()
	info, err := os.Stat(socket)
	if err != nil {
		t.Fatal(err)
	}
	if info.Mode().Perm() != 0o600 {
		t.Fatalf("socket mode %v, want 0600", info.Mode().Perm())
	}
	if dir, _ := os.Stat(filepath.Dir(socket)); dir.Mode().Perm() != 0o700 {
		t.Fatalf("socket directory mode %v, want 0700", dir.Mode().Perm())
	}
}

func TestUnixDaemonSocketDefaultRules(t *testing.T) {
	for _, key := range []string{"HCU_SOCKET", "XDG_RUNTIME_DIR", "WAYLAND_DISPLAY", "DISPLAY"} {
		t.Setenv(key, "")
	}
	for _, test := range []struct{ name, runtime, wayland, display, override, want string }{
		{"fallback", "", "", "", "", filepath.Join(os.TempDir(), fmt.Sprintf("hcu-%d", os.Getuid()), "desktop-default.sock")},
		{"X display", " /run/user/123 ", "", " :9.0 ", "", "/run/user/123/hcu/desktop-9_0.sock"},
		{"Wayland precedence", "/run/user/123", "wayland-1", ":9", "", "/run/user/123/hcu/desktop-wayland-1.sock"},
		{"Unicode sanitization", "/run/user/123", "用 a", "", "", "/run/user/123/hcu/desktop-__a.sock"},
		{"explicit endpoint", "/ignored", "ignored", "ignored", " /private/custom.sock ", "/private/custom.sock"},
	} {
		t.Run(test.name, func(t *testing.T) {
			t.Setenv("XDG_RUNTIME_DIR", test.runtime)
			t.Setenv("WAYLAND_DISPLAY", test.wayland)
			t.Setenv("DISPLAY", test.display)
			t.Setenv("HCU_SOCKET", test.override)
			if got := defaultDaemonSocket(); got != test.want {
				t.Fatalf("got %q want %q", got, test.want)
			}
		})
	}
}
