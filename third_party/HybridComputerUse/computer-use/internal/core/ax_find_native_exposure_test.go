package core

import (
	"reflect"
	"regexp"
	"strings"
	"testing"
)

// Keep complete canonical records but hide them from the working projection.
func findExposureFixture(t *testing.T, count int) (*Service, *AppSnapshot, []ElementRecord) {
	t.Helper()
	svc, snapshot, matches := findDeliveryMany(t, count)
	root := *snapshot.AXSnapshot.Roots[0]
	root.Children = nil
	snapshot.AXSnapshot.Roots = []*AXNode{&root}
	if AXProjectedIndices(snapshot.AXSnapshot.Roots)[matches[0].Index] {
		t.Fatal("fixture must hide canonical matches")
	}
	return svc, snapshot, matches
}

var exposureRefLabels = regexp.MustCompile(`\[ref=(r[0-9]+)\]`)

func assertFindExposureDelivered(t *testing.T, svc *Service, snapshot *AppSnapshot, result ToolCallResult) {
	t.Helper()
	want := map[int]ElementRecord{}
	projected := AXProjectedIndices(snapshot.AXSnapshot.Roots)
	for _, label := range exposureRefLabels.FindAllStringSubmatch(ResultText(result), -1) {
		entry := svc.RefRegistry.Entries[label[1]]
		if entry == nil {
			t.Fatalf("delivered ref missing registry entry: %s", label[1])
		}
		for _, record := range snapshot.Elements {
			if !projected[record.Index] && reflect.DeepEqual(record.RuntimeID, entry.RuntimeID) {
				want[record.Index] = record
			}
		}
	}
	got := svc.ExposedHiddenRefs[DeliveredSnapshotKey(snapshot)]
	if len(got) != len(want) {
		t.Fatalf("exposure count=%d, actually delivered hidden refs=%d\n%s", len(got), len(want), ResultText(result))
	}
	for index, record := range want {
		if saved, ok := got[index]; !ok || !SameCanonicalAddress(record, saved) {
			t.Errorf("delivered hidden index %d lost canonical identity", index)
		}
	}
}

func TestFindExposureNoDeliveredRef(t *testing.T) {
	for _, kind := range []string{"unaddressable", "stabilization", "no_owner", "missing_node", "offscreen", "collapsed", "budget", "required_notes"} {
		t.Run(kind, func(t *testing.T) {
			t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "24000")
			svc, snapshot, matches := findExposureFixture(t, 1)
			node := snapshot.AXSnapshot.ByIndex[matches[0].Index]
			var notes []string
			switch kind {
			case "unaddressable":
				node.Ref, node.SemanticTarget, node.LocatorKind = "", "", ""
				node.SemanticIdentityAmbiguous = true
			case "stabilization":
				// Canonical records disagree with the would-be semantic selector.
				snapshot.Elements[2].Name = "Different canonical identity"
			case "no_owner":
				snapshot.AXSnapshot.WindowRef = ""
			case "missing_node":
				delete(snapshot.AXSnapshot.ByIndex, matches[0].Index)
			case "offscreen":
				off := false
				node.Showing = &off
			case "collapsed":
				node.Parent.States.Expanded = TriFalse
			case "budget":
				t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "1")
			case "required_notes":
				notes = []string{"[expect] " + strings.Repeat("required ", 4000)}
			}
			result := findDeliveryFinal(svc.RenderFindPage(snapshot, matches, 0, 1, "target", notes, false))
			if strings.Contains(ResultText(result), ` ref="r`) {
				t.Fatalf("fixture unexpectedly delivered match ref: %s", ResultText(result))
			}
			after := *snapshot
			after.Elements = snapshot.Elements[:2]
			if note := svc.hiddenCanonicalAddressChangeNote(snapshot, &after); note != "" {
				t.Fatalf("claimed undelivered ref: %s\n%s", note, ResultText(result))
			}
		})
	}
}

func TestFindExposureBudgetLimitedPage(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "2000")
	svc, snapshot, matches := findExposureFixture(t, 8)
	page := findDeliveryFinal(svc.RenderFindPage(snapshot, matches, 0, 50, "target", nil, false))
	count, _ := IntEvidence(findDeliveryModelEvidence(t, page)["find_page_count"])
	if page.IsError || count == 0 || count >= len(matches) {
		t.Fatalf("expected budget-limited page, got count=%d: %s", count, ResultText(page))
	}
	assertFindExposureDelivered(t, svc, snapshot, page)
	cursor, _ := findDeliveryModelEvidence(t, page)["next_cursor"].(string)
	svc.ExposedHiddenRefs = map[string]map[int]ElementRecord{}
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "1")
	rejected := findDeliveryFinal(svc.continueFind(cursor, 50))
	if !rejected.IsError || len(svc.ExposedHiddenRefs) != 0 {
		t.Fatalf("cached no-fit page recorded exposure: %s", ResultText(rejected))
	}
}

func TestFindExposureVisibleMatchesStayOutOfHiddenLedger(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "24000")
	svc, snapshot, matches := findDeliveryMany(t, 3)
	result := findDeliveryFinal(svc.RenderFindPage(snapshot, matches, 0, 3, "target", nil, false))
	if result.IsError || len(findDeliveredTargets.FindAllStringSubmatch(ResultText(result), -1)) != 3 {
		t.Fatalf("visible targets missing: %s", ResultText(result))
	}
	if len(svc.ExposedHiddenRefs) != 0 {
		t.Fatal("already projected targets entered hidden-ref ledger")
	}
}

func TestFindExposureRefWithoutContext(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "24000")
	svc, snapshot, matches := findExposureFixture(t, 1)
	first := svc.RenderFindPage(snapshot, matches, 0, 1, "target", nil, false)
	if first.IsError {
		t.Fatal(ResultText(first))
	}
	// A cached result may have a target record but no context forest. The
	// atomic match unit remains sufficient delivery evidence on its own.
	cursor := svc.FindCursorRegistry.Create(snapshot, matches, 0, "target")
	resolution, err := svc.FindCursorRegistry.Resolve(cursor)
	if err != nil {
		t.Fatal(err)
	}
	cached := resolution.ResultSet.Snapshot
	cached.findHiddenRecords = map[int]ElementRecord{matches[0].Index: matches[0]}
	cached.AXSnapshot.Roots, cached.AXSnapshot.CanonicalRoots = nil, nil
	svc.ExposedHiddenRefs = map[string]map[int]ElementRecord{}
	page := findDeliveryFinal(svc.continueFind(cursor, 1))
	if page.IsError || !strings.Contains(ResultText(page), ` ref="r`) {
		t.Fatalf("missing ref-only delivery: %s", ResultText(page))
	}
	assertFindExposureDelivered(t, svc, snapshot, page)
}

func TestFindExposureCachedCanonicalIdentity(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "24000")
	svc, snapshot, matches := findExposureFixture(t, 4)
	first := svc.RenderFindPage(snapshot, matches, 0, 1, "target", nil, false)
	cursor, _ := first.StructuredContent["next_cursor"].(string)
	svc.ExposedHiddenRefs = map[string]map[int]ElementRecord{}
	before := *snapshot
	before.Elements = append([]ElementRecord(nil), snapshot.Elements...)
	before.Elements[3].RuntimeID = append([]int(nil), snapshot.Elements[3].RuntimeID...)
	snapshot.Elements[3].Name = "Live replacement"
	snapshot.Elements[3].RuntimeID[2] = 999
	page := findDeliveryFinal(svc.continueFind(cursor, 1))
	if page.IsError || strings.Contains(ResultText(page), "Live replacement") {
		t.Fatalf("cached delivery used live identity: %s", ResultText(page))
	}
	assertFindExposureDelivered(t, svc, &before, page)
	note := svc.hiddenCanonicalAddressChangeNote(&before, snapshot)
	if !strings.Contains(note, "1 serializer-hidden ref(s)") {
		t.Fatalf("cached original identity no longer detects rebound: %s", note)
	}
}

func TestFindExposureFailurePreservesEarlierDelivery(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "24000")
	svc, snapshot, matches := findExposureFixture(t, 1)
	page := findDeliveryFinal(svc.RenderFindPage(snapshot, matches, 0, 1, "target", nil, false))
	assertFindExposureDelivered(t, svc, snapshot, page)
	count := len(svc.ExposedHiddenRefs[DeliveredSnapshotKey(snapshot)])
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "1")
	rejected := svc.RenderFindPage(snapshot, matches, 0, 1, "target", nil, false)
	if !rejected.IsError || len(svc.ExposedHiddenRefs[DeliveredSnapshotKey(snapshot)]) != count {
		t.Fatal("failed later page erased earlier exposure")
	}
	after := *snapshot
	after.Elements = snapshot.Elements[:2]
	if note := svc.hiddenCanonicalAddressChangeNote(snapshot, &after); !strings.Contains(note, "1 serializer-hidden ref(s)") {
		t.Fatalf("earlier genuine delivery lost its invalidation: %s", note)
	}
}

func TestFindExposureCursorBudgetIncludesProvenance(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "24000")
	svc, snapshot, matches := findExposureFixture(t, 3)
	page := svc.RenderFindPage(snapshot, matches, 0, 1, "target", nil, false)
	cursor, _ := page.StructuredContent["next_cursor"].(string)
	resolution, err := svc.FindCursorRegistry.Resolve(cursor)
	if err != nil {
		t.Fatal(err)
	}
	cached := resolution.ResultSet.Snapshot
	without := *cached
	without.findHiddenRecords = nil
	_, base := estimateFindResultSet(&without, matches)
	_, withProvenance := estimateFindResultSet(cached, matches)
	if withProvenance <= base {
		t.Fatal("retained hidden canonical records escaped cursor byte budget")
	}
	svc.FindCursorRegistry.byteBudget = base
	if ref := svc.FindCursorRegistry.Create(cached, matches, 1, "target"); ref != "" {
		t.Fatal("oversized provenance admitted to cursor cache")
	}
}

func TestFindExposurePagesAndContext(t *testing.T) {
	for _, budget := range []string{"24000", "2000", "1100"} {
		t.Run(budget, func(t *testing.T) {
			t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", budget)
			svc, snapshot, matches := findExposureFixture(t, 8)
			first := findDeliveryFinal(svc.RenderFindPage(snapshot, matches, 0, 1, "target", nil, false))
			if first.IsError {
				t.Fatalf("fixture page failed: %s", ResultText(first))
			}
			if budget == "1100" && !strings.Contains(ResultText(first), "context_omitted=true") {
				t.Fatal("small-budget fixture must omit context")
			}
			assertFindExposureDelivered(t, svc, snapshot, first)
			cursor, _ := findDeliveryModelEvidence(t, first)["next_cursor"].(string)
			if cursor == "" {
				t.Fatal("missing continuation")
			}
			// Check each cached page in isolation; it must carry original hidden
			// provenance without using live provider state or reissuing refs.
			svc.ExposedHiddenRefs = map[string]map[int]ElementRecord{}
			page := findDeliveryFinal(svc.continueFind(cursor, 1))
			if page.IsError {
				t.Fatalf("cached page failed: %s", ResultText(page))
			}
			assertFindExposureDelivered(t, svc, snapshot, page)
			before := len(svc.ExposedHiddenRefs[DeliveredSnapshotKey(snapshot)])
			replay := findDeliveryFinal(svc.continueFind(cursor, 1))
			assertFindExposureDelivered(t, svc, snapshot, replay)
			if len(svc.ExposedHiddenRefs[DeliveredSnapshotKey(snapshot)]) != before {
				t.Fatal("replay duplicated exposure")
			}
		})
	}
}

func TestFindExposureGenuineChanges(t *testing.T) {
	for _, kind := range []string{"unchanged", "value_only", "removed", "rebound", "window"} {
		t.Run(kind, func(t *testing.T) {
			t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "24000")
			svc, snapshot, matches := findExposureFixture(t, 1)
			// Isolate the matched ref from optional ancestor context.
			snapshot.AXSnapshot.ByIndex[1].Ref = ""
			result := findDeliveryFinal(svc.RenderFindPage(snapshot, matches, 0, 1, "target", nil, false))
			if result.IsError || len(findDeliveredTargets.FindAllStringSubmatch(ResultText(result), -1)) != 1 {
				t.Fatalf("missing genuine delivered target: %s", ResultText(result))
			}
			after := *snapshot
			after.Elements = append([]ElementRecord(nil), snapshot.Elements...)
			switch kind {
			case "value_only":
				after.Elements[2].Value = "new value"
			case "removed":
				after.Elements = after.Elements[:2]
			case "rebound":
				after.Elements[2].RuntimeID = []int{0, 0, 99}
			case "window":
				after.WindowHandle = "w2:" + strings.Repeat("b", 32)
			}
			note := svc.hiddenCanonicalAddressChangeNote(snapshot, &after)
			wantChange := kind == "removed" || kind == "rebound" || kind == "window"
			if (note != "") != wantChange {
				t.Fatalf("change=%s note=%q", kind, note)
			}
			if wantChange && svc.hiddenCanonicalAddressChangeNote(snapshot, &after) != "" {
				t.Fatal("consumed invalidation reported twice")
			}
		})
	}
}
