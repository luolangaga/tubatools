package core

import (
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

type windowsRecordingBackend struct {
	*recordingPlatformBackend
}

func (windowsRecordingBackend) HostInfo() HostBackendInfo {
	return HostBackendInfo{Platform: "windows"}
}

// The 250 ms re-read after an action whose read showed no change tells the
// Windows provider that it checks that action again, so the provider may
// read a LibreOffice Calc sheet in part, as right after the action. Other
// providers reject the field and never receive it, and no other read
// carries it.
func TestContentStabilizationMarksTheWindowsRecheck(t *testing.T) {
	for _, platform := range []string{"windows", "test"} {
		recording := &recordingPlatformBackend{
			accessibilityResponses: []ProviderAccessibilityResult{
				{OK: true, Snapshot: contentStabilizationSnapshot("v1", "Ready")},
				{OK: true, Snapshot: contentStabilizationSnapshot("v1", "Ready")},
				{OK: true, Snapshot: contentStabilizationSnapshot("v2", "Saved")},
			},
		}
		var backend PlatformBackend = recording
		if platform == "windows" {
			backend = windowsRecordingBackend{recording}
		}
		service := NewServiceWithBackend(backend)
		off := false
		initial := service.getAppState(
			"Editor", nil, nil, nil, nil, &off, nil, contract.TreeViewWorking, "", nil,
		)
		if initial.IsError {
			t.Fatalf("%s: initial observe failed: %s", platform, ResultText(initial))
		}
		result := service.actionResult("Editor", AXServiceRequest{
			App:               "Editor",
			Action:            axKeypressCommand{Key: "Tab"},
			Observe:           contract.ObserveDiff,
			IncludeScreenshot: &off,
			TreeView:          contract.TreeViewWorking,
		})
		if result.IsError || result.StructuredContent["content_stabilized"] != true {
			t.Fatalf("%s: content stabilization did not run: %s", platform, ResultText(result))
		}
		observes := recording.observeRequests
		if len(observes) != 2 || len(recording.actionRequests) != 1 {
			t.Fatalf("%s: want an observe, an action and the re-read: %v", platform, recording.calls)
		}
		if observes[0].Observation.RecheckAfterAction ||
			recording.actionRequests[0].Observation.RecheckAfterAction {
			t.Fatalf("%s: only the re-read is a recheck", platform)
		}
		if got := observes[1].Observation.RecheckAfterAction; got != (platform == "windows") {
			t.Fatalf("%s: re-read recheck_after_action = %v", platform, got)
		}
	}
}
