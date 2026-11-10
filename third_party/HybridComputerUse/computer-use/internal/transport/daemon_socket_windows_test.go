//go:build windows

package transport

import (
	"errors"
	"net"
	"os"
	"path/filepath"
	"strings"
	"syscall"
	"testing"
	"unsafe"
)

func shortSocket(t *testing.T) string {
	t.Helper()
	dir, err := os.MkdirTemp("", "hcu")
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { os.RemoveAll(dir) })
	if short, err := daemonShortPath(dir); err == nil {
		dir = short
	}
	path := filepath.Join(dir, "private", "d.sock")
	if err := prepareDaemonDirectory(path); err != nil {
		t.Fatal(err)
	}
	return path
}

func checkDaemonSocketPermissions(t *testing.T, socket string) {
	t.Helper()
	if err := secureDaemonSocket(socket); err != nil {
		t.Fatal(err)
	}
}

func TestWindowsDaemonSocketDefault(t *testing.T) {
	for _, key := range []string{"HCU_SOCKET", "XDG_RUNTIME_DIR", "DISPLAY", "WAYLAND_DISPLAY"} {
		t.Setenv(key, "")
	}
	t.Setenv("XDG_RUNTIME_DIR", `C:\ignored`)
	t.Setenv("DISPLAY", ":42")
	if got, want := defaultDaemonSocket(), windowsDaemonSocket(os.TempDir(), daemonShortPath); got != want {
		t.Fatalf("default=%q want %q", got, want)
	}
	t.Setenv("HCU_SOCKET", `  C:\私有\explicit.sock  `)
	if got := defaultDaemonSocket(); got != `C:\私有\explicit.sock` {
		t.Fatalf("explicit endpoint rewritten: %q", got)
	}
}

func TestWindowsDaemonSocketShortPath(t *testing.T) {
	for _, test := range []struct {
		name, temp, short string
		fail, called      bool
	}{
		{"short unchanged", `C:\Temp`, `C:\TEMP~1`, false, false},
		{"107 byte boundary", `C:\` + strings.Repeat("a", 76), `C:\short`, false, false},
		{"108 byte boundary", `C:\` + strings.Repeat("a", 77), `C:\short`, false, true},
		{"UTF8 length", `C:\` + strings.Repeat("用", 30), `C:\USERS~1\Temp`, false, true},
		{"no short name", `C:\` + strings.Repeat("a", 90), "", true, true},
		{"longer alias", `C:\` + strings.Repeat("a", 90), `C:\` + strings.Repeat("b", 100), false, true},
	} {
		t.Run(test.name, func(t *testing.T) {
			called := false
			got := windowsDaemonSocket(test.temp, func(input string) (string, error) {
				called = true
				if input != test.temp {
					t.Fatalf("shortened %q instead of temp", input)
				}
				if test.fail {
					return "", errors.New("no alias")
				}
				return test.short, nil
			})
			want := filepath.Join(test.temp, "hcu--1", "desktop-default.sock")
			candidate := filepath.Join(test.short, "hcu--1", "desktop-default.sock")
			if test.called && !test.fail && len(candidate) < len(want) {
				want = candidate
			}
			if got != want || called != test.called {
				t.Fatalf("got=%q called=%v want=%q called=%v", got, called, want, test.called)
			}
		})
	}
}

func TestWindowsDaemonDirectorySecurity(t *testing.T) {
	root, err := os.MkdirTemp("", "hcu-acl")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(root)
	path := filepath.Join(root, "private", "d.sock")
	if err := prepareDaemonDirectory(path); err != nil {
		t.Fatal(err)
	}
	user, err := daemonUserSID()
	if err != nil {
		t.Fatal(err)
	}
	if err := daemonCheckFile(filepath.Dir(path), user, "directory"); err != nil {
		t.Fatal(err)
	}
	// 重复初始化只检查既有目录，不重写其 owner 或 DACL。
	if err := prepareDaemonDirectory(path); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte("keep"), 0o600); err != nil {
		t.Fatal(err)
	}
	if err := prepareSocketPath(path); err == nil {
		t.Fatal("regular socket target accepted")
	}
	if data, err := os.ReadFile(path); err != nil || string(data) != "keep" {
		t.Fatalf("regular target changed: %q %v", data, err)
	}
	if err := os.Remove(path); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path+".log", []byte("keep"), 0o600); err != nil {
		t.Fatal(err)
	}
	if err := prepareDaemonDirectory(path); err != nil {
		t.Fatal(err)
	}
	if err := os.Link(path+".log", path+".lock"); err != nil {
		t.Fatal(err)
	}
	if err := prepareDaemonDirectory(path); err == nil {
		t.Fatal("hardlinked side file accepted")
	}
}

func TestWindowsDaemonRejectsUnsafeDirectory(t *testing.T) {
	root, err := os.MkdirTemp("", "hcu-deny")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(root)
	user, err := daemonUserSID()
	if err != nil {
		t.Fatal(err)
	}
	for _, test := range []struct{ name, sddl string }{
		{"broad", "O:" + user + "D:P(A;OICI;FA;;;" + user + ")(A;OICI;FR;;;WD)"},
		{"inherited", "O:" + user + "D:(A;OICI;FA;;;" + user + ")"},
	} {
		t.Run(test.name, func(t *testing.T) {
			dir := filepath.Join(root, test.name)
			text, _ := syscall.UTF16PtrFromString(test.sddl)
			var sd unsafe.Pointer
			ok, _, err := daemonConvertSD.Call(uintptr(unsafe.Pointer(text)), 1, uintptr(unsafe.Pointer(&sd)), 0)
			if ok == 0 {
				t.Fatal(err)
			}
			defer syscall.LocalFree(syscall.Handle(uintptr(sd)))
			name, _ := syscall.UTF16PtrFromString(dir)
			attributes := syscall.SecurityAttributes{Length: uint32(unsafe.Sizeof(syscall.SecurityAttributes{})), SecurityDescriptor: uintptr(sd)}
			if err := syscall.CreateDirectory(name, &attributes); err != nil {
				t.Fatal(err)
			}
			if test.name == "inherited" {
				// CreateDirectory 可能保护显式 DACL；测试必须显式启用继承并查询实际控制位。
				var acl unsafe.Pointer
				var present, defaulted uint32
				ok, _, err := daemonAdvapi.NewProc("GetSecurityDescriptorDacl").Call(uintptr(sd), uintptr(unsafe.Pointer(&present)), uintptr(unsafe.Pointer(&acl)), uintptr(unsafe.Pointer(&defaulted)))
				if ok == 0 {
					t.Fatal(err)
				}
				status, _, _ := daemonAdvapi.NewProc("SetNamedSecurityInfoW").Call(uintptr(unsafe.Pointer(name)), 1, 0x20000004, 0, 0, uintptr(acl), 0)
				if status != 0 {
					t.Fatalf("enable fixture DACL inheritance: %v", syscall.Errno(status))
				}
				var actual unsafe.Pointer
				status, _, _ = daemonAdvapi.NewProc("GetNamedSecurityInfoW").Call(uintptr(unsafe.Pointer(name)), 1, 4, 0, 0, 0, 0, uintptr(unsafe.Pointer(&actual)))
				if status != 0 {
					t.Fatal(syscall.Errno(status))
				}
				defer syscall.LocalFree(syscall.Handle(uintptr(actual)))
				var control uint16
				var revision uint32
				ok, _, err = daemonAdvapi.NewProc("GetSecurityDescriptorControl").Call(uintptr(actual), uintptr(unsafe.Pointer(&control)), uintptr(unsafe.Pointer(&revision)))
				if ok == 0 {
					t.Fatal(err)
				}
				if control&0x1000 != 0 {
					t.Fatalf("fixture DACL is still protected: control=%#x", control)
				}
			}
			path := filepath.Join(dir, "d.sock")
			if err := prepareDaemonDirectory(path); err == nil {
				t.Fatal("unsafe existing directory accepted")
			} else if test.name == "inherited" && !strings.Contains(err.Error(), "protected DACL") {
				t.Fatalf("expected unprotected DACL rejection, got %v", err)
			}
			if err := daemonCheckFile(dir, user, "directory"); err == nil {
				t.Fatal("unsafe existing directory was silently repaired")
			}
		})
	}
}

func TestWindowsDaemonRejectsLongAndDevicePaths(t *testing.T) {
	for _, path := range []string{"", strings.Repeat("x", 108), `\\server\share\hcu\d.sock`, `C:\private\d.sock:stream`, "bad\x00path"} {
		if err := prepareDaemonDirectory(path); err == nil {
			t.Fatalf("unsafe path accepted: %q", path)
		}
	}
}

func TestWindowsDaemonRejectsReparseDirectory(t *testing.T) {
	root := t.TempDir()
	target := filepath.Join(root, "target")
	if err := os.Mkdir(target, 0o700); err != nil {
		t.Fatal(err)
	}
	link := filepath.Join(root, "alias")
	if err := os.Symlink(target, link); err != nil {
		t.Skipf("symlinks require Developer Mode or privilege: %v", err)
	}
	if err := prepareDaemonDirectory(filepath.Join(link, "private", "d.sock")); err == nil {
		t.Fatal("reparse ancestor accepted")
	}
	if _, err := os.Stat(filepath.Join(target, "private")); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("wrote through reparse: %v", err)
	}
}

func TestWindowsDaemonDACL(t *testing.T) {
	user, err := daemonUserSID()
	if err != nil {
		t.Fatal(err)
	}
	for _, test := range []struct {
		name, entries string
		want          bool
	}{
		{"user system admin", "(A;;FA;;;" + user + ")(A;;FA;;;SY)(A;;FA;;;BA)", true},
		{"user only", "(A;;FA;;;" + user + ")", true},
		{"everyone read", "(A;;FA;;;" + user + ")(A;;FR;;;WD)", false},
		{"authenticated users", "(A;;FA;;;" + user + ")(A;;FA;;;AU)", false},
		{"no user access", "(A;;FA;;;SY)", false},
		{"inherit only", "(A;OICIIO;FA;;;" + user + ")", false},
		{"empty", "", false},
	} {
		t.Run(test.name, func(t *testing.T) {
			sddl, _ := syscall.UTF16PtrFromString("O:" + user + "D:P" + test.entries)
			var sd unsafe.Pointer
			ok, _, err := daemonConvertSD.Call(uintptr(unsafe.Pointer(sddl)), 1, uintptr(unsafe.Pointer(&sd)), 0)
			if ok == 0 {
				t.Fatal(err)
			}
			defer syscall.LocalFree(syscall.Handle(uintptr(sd)))
			var present, defaulted uint32
			var acl unsafe.Pointer
			ok, _, err = daemonAdvapi.NewProc("GetSecurityDescriptorDacl").Call(uintptr(sd), uintptr(unsafe.Pointer(&present)), uintptr(unsafe.Pointer(&acl)), uintptr(unsafe.Pointer(&defaulted)))
			if ok == 0 {
				t.Fatal(err)
			}
			if err := daemonCheckDACL(acl, user, false); (err == nil) != test.want {
				t.Fatalf("accepted=%v want=%v: %v", err == nil, test.want, err)
			}
		})
	}
	if err := daemonCheckDACL(nil, user, false); err == nil {
		t.Fatal("NULL DACL accepted")
	}
}

func TestWindowsDaemonSocketLiveAndStale(t *testing.T) {
	root, err := os.MkdirTemp("", "hcu-sock")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(root)
	path := filepath.Join(root, "private", "d.sock")
	if err := prepareSocketPath(path); err != nil {
		t.Fatal(err)
	}
	listener, err := net.ListenUnix("unix", &net.UnixAddr{Name: path, Net: "unix"})
	if err != nil {
		t.Fatal(err)
	}
	defer listener.Close()
	listener.SetUnlinkOnClose(false)
	if err := secureDaemonSocket(path); err != nil {
		t.Fatal(err)
	}
	if err := prepareSocketPath(path); err == nil || !strings.Contains(err.Error(), "already serving") {
		t.Fatalf("live socket: %v", err)
	}
	if err := listener.Close(); err != nil {
		t.Fatal(err)
	}
	if err := prepareSocketPath(path); err != nil {
		t.Fatalf("stale socket: %v", err)
	}
	if _, err := os.Lstat(path); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("stale socket remains: %v", err)
	}
}
