//go:build windows || windows_sidecar_test

package provider

import "testing"

// A timeout diagnostic checks whether a cached PID still owns a window. The
// check must not start or reuse the provider whose request just timed out.
func TestWindowsWindowForPIDUsesNativeProbeWithoutProvider(t *testing.T) {
	previous := windowsNativeWindowProbe
	t.Cleanup(func() { windowsNativeWindowProbe = previous })
	for _, state := range []struct{ known, alive bool }{{true, true}, {true, false}, {false, false}} {
		calls := 0
		windowsNativeWindowProbe = func(pid uint32) (bool, bool) {
			if pid != 123 {
				t.Fatalf("probed PID=%d, want 123", pid)
			}
			calls++
			return state.known, state.alive
		}
		known, alive := windowsNativeWindowForPID(123)
		if calls != 1 || known != state.known || alive != state.alive {
			t.Fatalf("native state %+v returned known=%v alive=%v after %d probes", state, known, alive, calls)
		}
		if windowsProviderProcesses.worker != nil {
			t.Fatal("window probe started the provider")
		}
	}
}

func TestWindowsNativeWindowProbeRejectsInvalidPIDWithoutEnumeration(t *testing.T) {
	previous := windowsNativeWindowProbe
	t.Cleanup(func() { windowsNativeWindowProbe = previous })
	windowsNativeWindowProbe = func(uint32) (bool, bool) {
		t.Fatal("invalid PID reached native enumeration")
		return false, false
	}
	if known, alive := windowsNativeWindowForPID(0); !known || alive {
		t.Fatalf("invalid PID state: known=%v alive=%v", known, alive)
	}
}
