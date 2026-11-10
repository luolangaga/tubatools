package provider

import "testing"

func TestDarwinTimeoutWindowDiagnosticDoesNotReenterProvider(t *testing.T) {
	for _, state := range []string{"running", "unknown", "", "missing", "zombie", "exited"} {
		known, alive := darwinTimeoutWindowForPID(42, state)
		wantKnown := state == "missing" || state == "zombie" || state == "exited"
		if known != wantKnown || alive {
			t.Errorf("state %q: got (%t, %t), want (%t, false)", state, known, alive, wantKnown)
		}
	}
	for _, pid := range []int{-1, 0} {
		if known, alive := darwinTimeoutWindowForPID(pid, "running"); !known || alive {
			t.Errorf("pid %d: got (%t, %t), want (true, false)", pid, known, alive)
		}
	}
}
