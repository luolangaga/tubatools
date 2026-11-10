package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func anonymousRootFixture(role string, children bool) *AppSnapshot {
	snapshot := windowTransitionSnapshot("w2:"+strings.Repeat("a", 32), "Inspector", true)
	snapshot.Elements[1].Name = ""
	snapshot.Elements[1].ControlType = role
	snapshot.Elements[1].Actions = []string{"click"}
	snapshot.Elements[2].Name = "Visible child"
	snapshot.Elements[2].ControlType = "label"
	snapshot.Elements[2].States = ""
	snapshot.Elements[3].Name = ""
	snapshot.Elements[3].ControlType = role
	snapshot.Elements[3].RuntimeID = []int{0, 1}
	snapshot.Elements[3].Actions = []string{"click"}
	snapshot.Elements = snapshot.Elements[:4]
	if !children {
		snapshot.Elements = append(snapshot.Elements[:2], snapshot.Elements[3])
	}
	return snapshot
}

func TestObserveRootPreservesAnonymousFindTarget(t *testing.T) {
	for _, test := range []struct {
		role     string
		children bool
	}{
		{"toggle button", false},
		{"push button", false},
		{"panel", true},
	} {
		t.Run(test.role, func(t *testing.T) {
			service := NewServiceWithBackend(&recordingPlatformBackend{})
			full := anonymousRootFixture(test.role, test.children)
			if err := service.BuildAndRenderAXSnapshot(full, contract.TreeViewWorking); err != nil {
				t.Fatal(err)
			}
			matches := []ElementRecord{full.Elements[1], full.Elements[len(full.Elements)-1]}
			found := service.freezeFindRefs(full, matches)
			ref := found.AXSnapshot.ByIndex[1].Ref
			entry := service.RefRegistry.Entries[ref]
			if entry == nil || entry.LocatorKind != AXLocatorProviderBound {
				t.Fatalf("anonymous find target lacks exact identity: %#v", entry)
			}
			root, err := service.expandObservationRootShortRef("Editor", map[string]any{"ref": ref})
			if err != nil {
				t.Fatal(err)
			}
			fresh := anonymousRootFixture(test.role, test.children)
			if err := applyObservationRoot(fresh, root); err != nil {
				t.Fatal(err)
			}
			if err := service.BuildAndRenderAXSnapshot(fresh, contract.TreeViewWorking); err != nil {
				t.Fatal(err)
			}
			text := strings.Join(fresh.TreeLines, "\n")
			if !strings.Contains(text, "[ref="+ref+"]") {
				t.Fatalf("explicit root lost its current ref:\n%s", text)
			}
			if strings.Contains(text, "/match:") {
				t.Fatalf("observe was mislabeled as find:\n%s", text)
			}
			if test.children && !strings.Contains(text, "Visible child") {
				t.Fatalf("visible descendant was lost:\n%s", text)
			}
			if len(fresh.Elements) != len(full.Elements) || fresh.AXSnapshot.ByIndex[3] != nil {
				t.Fatal("root projection changed canonical records or included a sibling")
			}
			if current := service.RefRegistry.Entries[ref]; current.LocatorKind != AXLocatorProviderBound ||
				current.ProviderObjectPath != entry.ProviderObjectPath || current.LogicalWindowRef != entry.LogicalWindowRef {
				t.Fatal("root projection rebound the selected object")
			}
		})
	}
}

func TestObserveRootPreservesHiddenNodeWithoutGrantingAddressability(t *testing.T) {
	service := NewServiceWithBackend(&recordingPlatformBackend{})
	fresh := anonymousRootFixture("toggle button", false)
	hidden := false
	fresh.Elements[1].Showing = &hidden
	fresh.observationRoot = &fresh.Elements[1]
	if err := service.BuildAndRenderAXSnapshot(fresh, contract.TreeViewWorking); err != nil {
		t.Fatal(err)
	}
	text := strings.Join(fresh.TreeLines, "\n")
	if !strings.Contains(text, "button") || !strings.Contains(text, "addressable=false") {
		t.Fatalf("explicit hidden root was not preserved honestly:\n%s", text)
	}
	if node := fresh.AXSnapshot.ByIndex[1]; node.Ref != "" || node.AddressabilityReason != "not_currently_showing" {
		t.Fatalf("root protection granted input authority: %#v", node)
	}
}

func TestObserveRootPreservesUnnamedStructuralRoot(t *testing.T) {
	service := NewServiceWithBackend(&recordingPlatformBackend{})
	fresh := anonymousRootFixture("panel", true)
	fresh.Elements[1].Actions = nil
	fresh.Elements[1].ProviderBusName = ""
	fresh.Elements[1].ProviderObjectPath = ""
	fresh.Elements[1].ProcessGeneration = ""
	fresh.observationRoot = &fresh.Elements[1]
	if err := service.BuildAndRenderAXSnapshot(fresh, contract.TreeViewWorking); err != nil {
		t.Fatal(err)
	}
	if len(fresh.AXSnapshot.Roots) != 1 || fresh.AXSnapshot.Roots[0].ProviderIndex != 1 ||
		len(fresh.AXSnapshot.Roots[0].Children) != 1 {
		t.Fatalf("explicit structural root was unwrapped: %s", strings.Join(fresh.TreeLines, "\n"))
	}
	if fresh.AXSnapshot.ByIndex[1].Ref != "" {
		t.Fatal("structural root without stable identity gained a ref")
	}
}
