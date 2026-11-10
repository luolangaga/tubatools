package core

import (
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// Two sessions on one desktop share the address space that refs resolve in,
// but each keeps its own record of what it was shown.
func TestSessionsShareDesktopStateButNotBaselines(t *testing.T) {
	desktop := NewDesktopState(&recordingPlatformBackend{})
	first := NewSessionService(desktop)
	second := NewSessionService(desktop)

	if first.RefRegistry != second.RefRegistry ||
		first.WindowRefRegistry != second.WindowRefRegistry ||
		first.AppRefRegistry != second.AppRefRegistry {
		t.Fatal("sessions on one desktop must share the ref registries")
	}
	ref := first.RefRegistry.Acquire("editor", "w2:"+repeatHex("a"), "button:Save", "button", "Save", "rev1")
	if _, err := second.RefRegistry.Resolve(ref, ""); err != nil {
		t.Fatalf("ref issued to one session must resolve in another: %v", err)
	}

	first.rememberDeliveredTree("w1|rev1|working|window|")
	if second.observations.lastFullTree["w1|rev1|working|window|"] {
		t.Fatal("a tree delivered to one session must not count as delivered to another")
	}
	if first.FindCursorRegistry == second.FindCursorRegistry {
		t.Fatal("find cursors are per session")
	}
}

// GUI input from one session invalidates cached accessibility state for
// every session; the other session drops its own caches on its next call.
func TestGUIInvalidationReachesOtherSessions(t *testing.T) {
	desktop := NewDesktopState(&recordingPlatformBackend{})
	first := NewSessionService(desktop)
	second := NewSessionService(desktop)
	second.ExposedHiddenRefs["key"] = map[int]ElementRecord{1: {Index: 1}}

	first.invalidateAccessibilitySnapshots()
	if len(second.ExposedHiddenRefs) != 1 {
		t.Fatal("the other session's caches change only on its own next call")
	}
	second.callTool(contract.PublicToolAX, map[string]any{"mode": "bogus"})
	if len(second.ExposedHiddenRefs) != 0 {
		t.Fatal("a session must drop caches that another session's GUI input made stale")
	}
}

func repeatHex(digit string) string {
	out := ""
	for len(out) < 32 {
		out += digit
	}
	return out
}
