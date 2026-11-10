package transport

import (
	"os"
	"os/exec"
	"path/filepath"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

// The Python SDK and PTC adapter run against a daemon serving the fake
// desktop. This covers the drift guards: every SDK helper builds a call the
// core accepts, every schema error code has an SDK exception, and the PTC
// definitions come from the live schema.
func TestPythonSDKAgainstDaemon(t *testing.T) {
	python := "/usr/bin/python3"
	if _, err := os.Stat(python); err != nil {
		t.Skip("python3 is not installed")
	}
	t.Setenv("COMPUTER_USE_ERROR_CONTEXT", "off")
	t.Setenv("COMPUTER_USE_OUTPUT_DIR", t.TempDir())
	socket := shortSocket(t)
	server := newDaemon(socket, core.NewDesktopState(newFakeDesktopBackend()),
		core.DefaultRuntimeBackend().RuntimeEnvironment(os.Environ()), time.Minute)
	if err := server.listen(); err != nil {
		t.Fatal(err)
	}
	go server.serve()
	defer server.stop()
	script, err := filepath.Abs(filepath.Join("..", "..", "..", "sdk", "python", "tests", "test_sdk.py"))
	if err != nil {
		t.Fatal(err)
	}
	command := exec.Command(python, script)
	command.Env = append(os.Environ(), "HCU_SOCKET="+socket)
	output, err := command.CombinedOutput()
	if err != nil {
		t.Fatalf("SDK tests failed: %v\n%s", err, output)
	}
}
