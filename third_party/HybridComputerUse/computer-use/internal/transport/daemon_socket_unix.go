//go:build !windows

package transport

import (
	"errors"
	"fmt"
	"net"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/provider"
)

// defaultDaemonSocket 保留 Unix 的 XDG、临时目录和 display 命名规则。
func defaultDaemonSocket() string {
	if path := strings.TrimSpace(os.Getenv("HCU_SOCKET")); path != "" {
		return path
	}
	base := strings.TrimSpace(os.Getenv("XDG_RUNTIME_DIR"))
	if base == "" {
		base = filepath.Join(os.TempDir(), fmt.Sprintf("hcu-%d", os.Getuid()))
	} else {
		base = filepath.Join(base, "hcu")
	}
	display := strings.TrimSpace(os.Getenv("WAYLAND_DISPLAY"))
	if display == "" {
		display = strings.TrimSpace(os.Getenv("DISPLAY"))
	}
	name := strings.Map(func(r rune) rune {
		if (r >= 'a' && r <= 'z') || (r >= 'A' && r <= 'Z') || (r >= '0' && r <= '9') || r == '-' {
			return r
		}
		return '_'
	}, strings.TrimPrefix(display, ":"))
	if name == "" {
		name = "default"
	}
	return filepath.Join(base, "desktop-"+name+".sock")
}

// prepareSocketPath 保留 Unix 的属主、权限和陈旧 socket 检查。
func prepareSocketPath(path string) error {
	dir := filepath.Dir(path)
	if err := os.MkdirAll(dir, 0o700); err != nil {
		return err
	}
	info, err := os.Stat(dir)
	if err != nil {
		return err
	}
	if !provider.FileOwnedByUID(info, os.Getuid()) {
		return fmt.Errorf("socket directory %s is owned by another user", dir)
	}
	if info.Mode().Perm()&0o077 != 0 {
		if err := os.Chmod(dir, 0o700); err != nil {
			return err
		}
	}
	existing, err := os.Lstat(path)
	if errors.Is(err, os.ErrNotExist) {
		return nil
	}
	if err != nil {
		return err
	}
	if !provider.FileOwnedByUID(existing, os.Getuid()) {
		return fmt.Errorf("socket %s is owned by another user; refusing to replace it", path)
	}
	if existing.Mode()&os.ModeSocket == 0 {
		return fmt.Errorf("%s exists and is not a socket", path)
	}
	if conn, dialErr := net.DialTimeout("unix", path, 500*time.Millisecond); dialErr == nil {
		conn.Close()
		return fmt.Errorf("a daemon is already serving %s", path)
	}
	return os.Remove(path)
}

func secureDaemonSocket(path string) error { return os.Chmod(path, 0o600) }
