package transport

import (
	"context"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strconv"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func sdkTestPython(t *testing.T) string {
	t.Helper()
	if python := os.Getenv("HCU_TEST_PYTHON"); python != "" {
		resolved, err := exec.LookPath(python)
		if err != nil {
			t.Fatalf("HCU_TEST_PYTHON is unusable: %v", err)
		}
		return resolved
	}
	for _, name := range []string{"python3", "python"} {
		if python, err := exec.LookPath(name); err == nil {
			return python
		}
	}
	t.Skip("install Python or set HCU_TEST_PYTHON")
	return ""
}

// 使用专用临时目录验证默认路径；绝不连接用户正在使用的 daemon。
// Windows 上实际执行 Winsock，其他平台的通过不能代替 Windows 验证。
func TestPythonSDKConnections(t *testing.T) {
	python := sdkTestPython(t)
	base := ""
	if runtime.GOOS != "windows" {
		base = "/tmp"
	}
	root, err := os.MkdirTemp(base, "hcu")
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { os.RemoveAll(root) })
	temp := filepath.Join(root, "中文 空格")
	if err := os.Mkdir(temp, 0o700); err != nil {
		t.Fatal(err)
	}
	for _, key := range []string{"TMPDIR", "TMP", "TEMP"} {
		t.Setenv(key, temp)
	}
	for _, key := range []string{"HCU_SOCKET", "HCU_SESSION", "XDG_RUNTIME_DIR", "DISPLAY", "WAYLAND_DISPLAY"} {
		t.Setenv(key, "")
	}
	t.Setenv("PYTHONUTF8", "1")
	t.Setenv("COMPUTER_USE_ERROR_CONTEXT", "off")
	t.Setenv("COMPUTER_USE_OUTPUT_DIR", filepath.Join(root, "output"))
	socket := defaultDaemonSocket()
	t.Setenv("HCU_TEST_ENDPOINT", socket)
	t.Setenv("HCU_TEST_DAEMON_PID", strconv.Itoa(os.Getpid()))

	for _, script := range []string{"test_connection.py", "test_sdk.py"} {
		t.Run(script, func(t *testing.T) {
			socket := socket
			if script == "test_sdk.py" {
				// 完整旧测试假设首次 ref 为 w1，避免读取前一组保存的 .refs。
				socket = filepath.Join(filepath.Dir(socket), "sdk.sock")
			}
			backend := newFakeDesktopBackend()
			server := newDaemon(socket, core.NewDesktopState(backend),
				core.DefaultRuntimeBackend().RuntimeEnvironment(os.Environ()), time.Minute)
			if err := server.listen(); err != nil {
				t.Fatal(err)
			}
			done := make(chan struct{})
			go func() {
				defer close(done)
				_ = server.serve()
			}()
			t.Cleanup(func() {
				server.stop()
				<-done
			})
			path, err := filepath.Abs(filepath.Join("..", "..", "..", "sdk", "python", "tests", script))
			if err != nil {
				t.Fatal(err)
			}
			args := []string{"-u", path, "-v"}
			if script == "test_connection.py" {
				args = append(args, "--daemon")
			} else {
				// 复用完整 SDK/PTC 测试，桌面操作全部落在标准 fake backend。
				t.Setenv("HCU_SOCKET", socket)
			}
			ctx, cancel := context.WithTimeout(context.Background(), 2*time.Minute)
			defer cancel()
			command := exec.CommandContext(ctx, python, args...)
			command.Env = os.Environ()
			output, err := command.CombinedOutput()
			t.Logf("%s", output)
			if err != nil {
				t.Fatalf("Python SDK connection tests failed: %v", err)
			}
			if script == "test_connection.py" && backend.inputCount() != 1 {
				t.Fatalf("partial failure and replay emitted %d inputs; want 1", backend.inputCount())
			}
		})
	}
}
