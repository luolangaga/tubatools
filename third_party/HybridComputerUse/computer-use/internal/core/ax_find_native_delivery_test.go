package core

import (
	"encoding/json"
	"fmt"
	"regexp"
	"strings"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func findDeliverySnapshot(t testing.TB, value string) (*Service, *AppSnapshot) {
	t.Helper()
	svc := NewServiceWithBackend(&recordingPlatformBackend{})
	snapshot := &AppSnapshot{
		App:          AppDescriptor{Name: "Independent", PID: 42, ProviderBusName: ":1.42", ProcessGeneration: "42"},
		WindowTitle:  "Selection",
		WindowHandle: "w2:" + strings.Repeat("a", 32),
		SnapshotID:   "find-delivery",
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Selection"},
			{Index: 1, RuntimeID: []int{0, 0}, ControlType: "entry", Name: "Container", Value: value},
			{Index: 2, RuntimeID: []int{0, 0, 0}, ControlType: "list item", Name: "Target", States: "[has-click-action]"},
		},
		Scope: contract.AXScopeWindow,
	}
	if err := svc.BuildAndRenderAXSnapshot(snapshot, contract.TreeViewWorking); err != nil {
		t.Fatal(err)
	}
	return svc, snapshot
}

func findDeliveryMany(t testing.TB, count int) (*Service, *AppSnapshot, []ElementRecord) {
	t.Helper()
	svc, snapshot := findDeliverySnapshot(t, "")
	snapshot.Elements = snapshot.Elements[:2]
	for i := 0; i < count; i++ {
		snapshot.Elements = append(snapshot.Elements, ElementRecord{
			Index: i + 2, RuntimeID: []int{0, 0, i}, ControlType: "list item",
			Name: fmt.Sprintf("Target %02d", i), States: "[has-click-action]",
			Description: strings.Repeat("d", 240),
		})
	}
	if err := svc.BuildAndRenderAXSnapshot(snapshot, contract.TreeViewWorking); err != nil {
		t.Fatal(err)
	}
	return svc, snapshot, snapshot.Elements[2:]
}

var findDeliveredTargets = regexp.MustCompile(`(?m)^ - match=(\d+) ref=("r\d+")$`)

func findDeliveryModelEvidence(t *testing.T, result ToolCallResult) map[string]any {
	t.Helper()
	if result.StructuredContent != nil {
		return result.StructuredContent
	}
	for _, item := range result.Content {
		if item.Type == "text" && strings.HasPrefix(item.Text, `{"ok":`) {
			var evidence map[string]any
			if err := json.Unmarshal([]byte(item.Text), &evidence); err != nil {
				t.Fatal(err)
			}
			return evidence
		}
	}
	t.Fatal("missing model evidence")
	return nil
}

func TestFindDeliveryTypedBudgetStillRejectsActualCuts(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "24000")
	makeResult := func(text string) ToolCallResult {
		result := TextResult(text, false)
		result.StructuredContent = map[string]any{"find_delivery_complete": true}
		return result
	}
	literal := makeResult("[TRUNCATED: example]")
	if !findDeliveryFits(literal) {
		t.Fatal("literal text was treated as a budget effect")
	}
	// Below the legacy byte cap, but above the final content budget after its
	// evidence reserve. This must still reject a genuine partial delivery.
	oversized := makeResult(strings.Repeat("x", 23000))
	if findDeliveryFits(oversized) {
		t.Fatal("actual final budget cut was accepted")
	}
	before := ResultText(oversized)
	cut := EnforceAXResponseBudget(oversized)
	if cut.StructuredContent["truncation_reason"] != "response_budget" ||
		ResultText(cut) == before {
		t.Fatal("actual cut was not recorded as typed budget state")
	}
	repeated := EnforceAXResponseBudget(cut)
	if repeated.StructuredContent["truncation_reason"] != "response_budget" ||
		findDeliveryFits(repeated) {
		t.Fatal("a second budget pass lost the prior real truncation")
	}
}

func findDeliveryFinal(result ToolCallResult) ToolCallResult {
	result = WithAXModeEvidence(result, contract.AXModeFind)
	result = EnforceAXResponseBudget(result)
	result = NormalizeAXEvidenceInvariants(result)
	return WithAXTextEvidence(PrepareAXEvidence(result))
}

func TestFindDeliveryLongAncestorThroughFinalEvidence(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "24000")
	t.Setenv("COMPUTER_USE_OUTPUT_MODE", "stdout")
	value := strings.Repeat("x", 30000)
	svc, snapshot := findDeliverySnapshot(t, value)
	result := svc.RenderFindPage(snapshot, snapshot.Elements[2:], 0, 2,
		`role~"listitem" name="Target"`, nil, false)
	final := findDeliveryFinal(result)
	text := ResultText(final)
	if final.IsError || !strings.Contains(text, `listitem "Target"`) ||
		!strings.Contains(text, ` ref="r`) {
		t.Fatalf("find lost the matched target at the model boundary:\n%s", text)
	}
	if strings.Contains(text, "[TRUNCATED:") || len([]rune(text)) > MaxInlineChars() {
		t.Fatalf("find delivery was not budgeted before the final response: %d", len([]rune(text)))
	}
	if snapshot.AXSnapshot.ByIndex[1].Properties.Value != value ||
		snapshot.Elements[1].Value != value {
		t.Fatal("find delivery changed canonical data")
	}
}

func TestFindDeliveryDiagnosticsCannotBeSilentlyDropped(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "1000")
	svc, snapshot := findDeliverySnapshot(t, "")
	result := svc.RenderFindPage(snapshot, snapshot.Elements[2:], 0, 2, "target",
		[]string{"[expect] " + strings.Repeat("important ", 1000)}, true)
	if !result.IsError || result.StructuredContent["next_cursor"] != nil ||
		strings.Contains(ResultText(findDeliveryFinal(result)), " ref=\"r") {
		t.Fatalf("oversized required diagnostics silently dropped: %s", ResultText(result))
	}
}

func TestFindDeliveryMissingCanonicalNodeAndRefFailure(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "24000")
	_, snapshot := findDeliverySnapshot(t, "")
	node := snapshot.AXSnapshot.ByIndex[2]
	node.FindMatch, node.Ref = true, ""
	unit := FindMatchUnit(snapshot.AXSnapshot, snapshot.Elements[2], 1)
	if !strings.Contains(unit, "reason=stable_ref_unavailable") {
		t.Fatalf("eligible but unissued ref has empty reason: %s", unit)
	}
	delete(snapshot.AXSnapshot.ByIndex, 2)
	unit = FindMatchUnit(snapshot.AXSnapshot, snapshot.Elements[2], 1)
	if !strings.Contains(unit, "canonical_node_unavailable") || strings.Contains(unit, " ref=\"r") {
		t.Fatalf("missing canonical node received fabricated target: %s", unit)
	}
}

func TestFindDeliveryCursorStorageFailureAndExpiry(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "3800")
	svc, snapshot, matches := findDeliveryMany(t, 8)
	svc.FindCursorRegistry.byteBudget = 1
	result := svc.RenderFindPage(snapshot, matches, 0, 50, "target", nil, false)
	if result.IsError || !strings.Contains(ResultText(result), "continuation=unavailable_budget") ||
		result.StructuredContent["next_cursor"] != nil {
		t.Fatalf("cursor storage failure hidden: %s", ResultText(result))
	}
	svc.FindCursorRegistry = NewFindCursorRegistry(1)
	first := svc.RenderFindPage(snapshot, matches, 0, 1, "target", nil, false)
	cursor := first.StructuredContent["next_cursor"].(string)
	svc.RenderFindPage(snapshot, matches, 0, 1, "target", nil, false)
	if page := svc.continueFind(cursor, 1); !page.IsError || !strings.Contains(ResultText(page), "expired") {
		t.Fatalf("evicted cursor was usable: %s", ResultText(page))
	}
	last := svc.RenderFindPage(snapshot, matches, 0, 1, "target", nil, false)
	cursor = last.StructuredContent["next_cursor"].(string)
	resolution, _ := svc.FindCursorRegistry.Resolve(cursor)
	resolution.ResultSet.CreatedAt = time.Now().Add(-defaultAXFindCursorAbsoluteTTL - time.Second)
	if page := svc.continueFind(cursor, 1); !page.IsError || !strings.Contains(ResultText(page), "expired") {
		t.Fatalf("expired cursor was usable: %s", ResultText(page))
	}
}

func BenchmarkFindDeliveryContext(b *testing.B) {
	b.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "24000")
	svc, snapshot, matches := findDeliveryMany(b, 50)
	b.ReportAllocs()
	b.ResetTimer()
	for i := 0; i < b.N; i++ {
		result := svc.RenderFindPage(snapshot, matches, 0, 50, "target", nil, false)
		if result.IsError {
			b.Fatal(ResultText(result))
		}
	}
}
