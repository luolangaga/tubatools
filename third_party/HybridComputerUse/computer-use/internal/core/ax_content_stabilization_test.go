package core

import (
	"fmt"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// contentStabilizationSnapshot is one application window with enough
// unchanged nodes that a one-node diff is much shorter than the tree.
func contentStabilizationSnapshot(id, status string) *AppSnapshot {
	snapshot := windowTransitionSnapshot(
		"w2:0123456789abcdef0123456789abcdef", "Document", true,
	)
	snapshot.SnapshotID = id
	snapshot.Elements[0].ControlType = "frame"
	snapshot.Elements[4].Name = status
	for index := 0; index < 40; index++ {
		snapshot.Elements = append(snapshot.Elements, ElementRecord{
			Index:       5 + index,
			RuntimeID:   []int{0, 2 + index},
			ControlType: "label",
			Name:        fmt.Sprintf("Unchanged label %02d", index),
		})
	}
	for index := range snapshot.Elements {
		record := &snapshot.Elements[index]
		record.ProviderBusName = ":1.42"
		record.ProviderObjectPath = fmt.Sprintf("/frame/%d", index)
		record.ProcessGeneration = "pid:42:start:100"
		record.ProviderWindowBusName = ":1.42"
		record.ProviderWindowObjectPath = "/frame"
		record.ProviderWindowProcessGeneration = "pid:42:start:100"
		record.ProviderWindowRole = "frame"
		record.ProviderWindowName = "Document"
	}
	return snapshot
}

// When the first post-action read shows no change and the 250 ms re-read
// does, the result is the diff against the delivered state, not the tree.
func TestContentStabilizationReturnsDiffAgainstDeliveredState(t *testing.T) {
	backend := &recordingPlatformBackend{
		accessibilityResponses: []ProviderAccessibilityResult{
			{OK: true, Snapshot: contentStabilizationSnapshot("v1", "Ready")},
			{OK: true, Snapshot: contentStabilizationSnapshot("v1", "Ready")},
			{OK: true, Snapshot: contentStabilizationSnapshot("v2", "Saved")},
		},
	}
	service := NewServiceWithBackend(backend)
	off := false
	initial := service.getAppState(
		"Editor", nil, nil, nil, nil, &off, nil, contract.TreeViewWorking, "", nil,
	)
	if initial.IsError || !strings.Contains(ResultText(initial), `status "Ready"`) {
		t.Fatalf("initial observe failed: %s", ResultText(initial))
	}

	result := service.actionResult("Editor", AXServiceRequest{
		App:               "Editor",
		Action:            axKeypressCommand{Key: "Tab"},
		Observe:           contract.ObserveDiff,
		IncludeScreenshot: &off,
		TreeView:          contract.TreeViewWorking,
	})
	text := ResultText(result)
	if result.IsError || result.StructuredContent["content_stabilized"] != true {
		t.Fatalf("content stabilization did not run: %#v\n%s",
			result.StructuredContent, text)
	}
	if result.StructuredContent["final_observation_kind"] != "diff" ||
		!strings.Contains(text, `status "Saved"`) ||
		strings.Contains(text, "Unchanged label 07") {
		t.Fatalf("stabilized change must be a diff against the delivered state:\n%s", text)
	}
	if !strings.Contains(text, "re-read 250 ms later") {
		t.Fatalf("stabilization note missing:\n%s", text)
	}
}
