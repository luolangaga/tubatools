//go:build windows

package provider

import (
	"testing"
	"unsafe"
)

func TestWindowsNativeWindowProbeLive(t *testing.T) {
	foreground, _, _ := windowsProbeUser32.NewProc("GetForegroundWindow").Call()
	if foreground == 0 {
		t.Fatal("live native probe requires an interactive foreground window")
	}
	var pid uint32
	thread, _, _ := windowsProbeGetPID.Call(foreground, uintptr(unsafe.Pointer(&pid)))
	if thread == 0 || pid == 0 {
		t.Fatal("foreground window has no readable process identity")
	}
	if known, alive := (windowsRuntimeBackend{}).WindowForPID(int(pid)); !known || !alive {
		t.Fatalf("foreground PID %d native window state: known=%v alive=%v", pid, known, alive)
	}
	if known, alive := (windowsRuntimeBackend{}).WindowForPID(int(^uint32(0))); !known || alive {
		t.Fatalf("unused PID native window state: known=%v alive=%v", known, alive)
	}
	if windowsProviderProcesses.worker != nil {
		t.Fatal("independent window probe started the UIA provider")
	}
}
