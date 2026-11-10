package core

import (
	"fmt"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// largeEditSnapshots returns a delivered window of 400 named text boxes and a
// later state in which 120 of them changed value. The diff is far above 6,000
// characters but much shorter than the current tree.
func largeEditSnapshots(t *testing.T) (*AppSnapshot, *AppSnapshot) {
	t.Helper()
	build := func(changed int) *AppSnapshot {
		window := &AXNode{ID: "win", Role: "window", Name: "Impress",
			Ref: "r1", SemanticFingerprint: "f1:win"}
		for index := 0; index < 400; index++ {
			value := "unchanged value"
			if index < changed {
				value = fmt.Sprintf("edited value number %03d", index)
			}
			window.Children = append(window.Children, &AXNode{
				ID: fmt.Sprintf("field-%03d", index), Role: "textbox",
				Name:                fmt.Sprintf("Field %03d", index),
				Ref:                 fmt.Sprintf("r%d", index+2),
				SemanticFingerprint: fmt.Sprintf("f1:field-%03d", index),
				Properties:          AXProperties{Value: value},
				Parent:              window,
			})
		}
		typed := &AXSnapshot{
			SchemaVersion: "AXSnapshot", WindowRef: "w1",
			CanonicalRoots: []*AXNode{window}, Roots: []*AXNode{window},
		}
		lines, err := RenderAXSnapshotYAML(typed, AXRenderOptions{})
		if err != nil {
			t.Fatal(err)
		}
		return &AppSnapshot{
			sourceApp: "impress", WindowTitle: "Impress", WindowHandle: "w1:handle",
			publicWindowRef: "w1", SnapshotID: fmt.Sprintf("changed-%d", changed),
			TreeLines: lines, AXSnapshot: typed,
			projection: snapshotProjection{View: contract.TreeViewWorking, Scope: contract.AXScopeWindow},
		}
	}
	return build(0), build(120)
}

func TestLargeDiffSmallerThanTreeIsReturned(t *testing.T) {
	before, after := largeEditSnapshots(t)
	diff, ok := incrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("a diff shorter than the current tree must be returned regardless of its size")
	}
	diffChars := len(strings.Join(diff, "\n"))
	treeChars := len(strings.Join(after.TreeLines, "\n"))
	if diffChars <= 6000 || diffChars >= treeChars {
		t.Fatalf("test setup: want 6000 < diff (%d) < tree (%d)", diffChars, treeChars)
	}
	if !strings.Contains(strings.Join(diff, "\n"), "edited value number 119") {
		t.Fatal("diff lost a changed value")
	}
}

func TestContentRevisionChangeReturnsDiffWhenSmaller(t *testing.T) {
	service := NewService()
	before, after := largeEditSnapshots(t)
	before.contentRevision = "c1:0123456789abcdef01234567"
	after.contentRevision = "c1:89abcdef0123456701234567"
	service.rememberDeliveredSnapshot(before)
	result, lines, returned, compact, fullTree :=
		service.renderAXDiffOrCompactObservation(after, nil)
	if !returned || compact || fullTree ||
		result.StructuredContent["final_observation_kind"] != "diff" ||
		len(strings.Join(lines, "\n")) >= len(strings.Join(after.TreeLines, "\n")) {
		t.Fatalf("content change with a smaller diff must return the diff: kind=%v full=%t",
			result.StructuredContent["final_observation_kind"], fullTree)
	}
}
