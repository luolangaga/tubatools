package transport

import (
	"context"
	"os"
	"os/exec"
	"path/filepath"
	"testing"
	"time"
)

func TestPythonSDKTransportRegressions(t *testing.T) {
	python, err := exec.LookPath("python3")
	if err != nil {
		t.Skip("python3 is not installed")
	}
	script, err := filepath.Abs(filepath.Join("..", "..", "testdata", "sdk_transport_regression.py"))
	if err != nil {
		t.Fatal(err)
	}
	for _, test := range []struct {
		name   string
		inputs int
	}{
		{"wire", 0},
		{"reject-display", 0},
		{"replay", 1},
	} {
		t.Run(test.name, func(t *testing.T) {
			t.Setenv("COMPUTER_USE_OUTPUT_DIR", t.TempDir())
			_, backend, socket := startInProcessDaemon(t, time.Minute)
			ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
			defer cancel()
			command := exec.CommandContext(ctx, python, "-u", script, test.name)
			command.Env = append(os.Environ(), "HCU_SOCKET="+socket,
				"DISPLAY=:9", "WAYLAND_DISPLAY=")
			output, err := command.CombinedOutput()
			t.Logf("%s", output)
			if err != nil {
				t.Errorf("SDK transport regression: %v", err)
			}
			if got := backend.inputCount(); got != test.inputs {
				t.Errorf("provider input count = %d; want %d", got, test.inputs)
			}
		})
	}
}
