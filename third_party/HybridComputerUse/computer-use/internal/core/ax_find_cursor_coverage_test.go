package core

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func requireFindAcquisition(t *testing.T, result ToolCallResult, count int, reasons ...string) {
	t.Helper()
	if result.IsError {
		t.Fatal(ResultText(result))
	}
	facts := findDeliveryModelEvidence(t, result)
	if count == 0 {
		for _, field := range []string{"acquisition", "coverage", "tree_complete", "recover"} {
			if _, present := facts[field]; present {
				t.Fatalf("complete new read retained old %s evidence: %#v", field, facts)
			}
		}
		return
	}
	acquisition, _ := facts["acquisition"].(map[string]any)
	missing, _ := IntEvidence(acquisition["reported_missing_nodes"])
	if missing != count || facts["coverage"] != "partial" || facts["tree_complete"] != false {
		t.Fatalf("find page lost acquisition evidence: %#v", facts)
	}
	// The model evidence can come from either the structured result or its
	// serialized text line. Compare both forms without changing the meaning.
	gotReasons, _ := json.Marshal(acquisition["reasons"])
	wantReasons, _ := json.Marshal(reasons)
	if string(gotReasons) != string(wantReasons) {
		t.Fatalf("find acquisition reasons = %s, want %s", gotReasons, wantReasons)
	}
	recovery, _ := facts["recover"].(map[string]any)
	if text, _ := recovery["acquisition"].(string); !strings.Contains(text, "do not guarantee complete acquisition") {
		t.Fatalf("find page lost acquisition recovery: %#v", facts)
	}
}

func TestFindCursorPagesRetainAcquisitionEvidence(t *testing.T) {
	svc, snapshot, matches := findDeliveryMany(t, 6)
	snapshot.Annotations = []axProviderAnnotation{
		{ParentRuntimeID: []int{0}, Kind: "provider_omission", Count: 5, Reason: "time_budget"},
		{ParentRuntimeID: []int{0, 0}, Kind: "provider_omission", Count: 3, Reason: "visible_cell_budget"},
		{Kind: axCellSummaryAnnotation, Count: 12, Reason: "empty_cells"},
	}
	first := svc.RenderFindPage(snapshot, matches, 0, 2, "target", nil, false)
	requireFindAcquisition(t, findDeliveryFinal(first), 8, "time_budget", "visible_cell_budget")
	cursor := findCursor(first)
	if cursor == "" {
		t.Fatal("missing cursor for remaining matches")
	}
	resolution, err := svc.FindCursorRegistry.Resolve(cursor)
	if err != nil {
		t.Fatal(err)
	}
	snapshot.Annotations[0].Count = 999
	snapshot.Annotations[0].ParentRuntimeID[0] = 99
	for page := 1; cursor != ""; page++ {
		if page > 2 {
			t.Fatal("cursor repeated instead of reaching the final page")
		}
		result := svc.continueFind(cursor, 2)
		if result.StructuredContent["evidence_scope"] != "cached_observation_page" {
			t.Fatalf("cached page claimed a fresh read: %#v", result.StructuredContent)
		}
		requireFindAcquisition(t, findDeliveryFinal(result), 8, "time_budget", "visible_cell_budget")
		cursor = findCursor(result)
	}
	annotations := resolution.ResultSet.Snapshot.Annotations
	if len(annotations) != 3 || annotations[0].ParentRuntimeID[0] != 0 {
		t.Fatalf("cursor annotations alias mutable provider data: %#v", annotations)
	}
}

func TestFindCursorBudgetIncludesProviderAnnotations(t *testing.T) {
	svc, snapshot, matches := findDeliveryMany(t, 2)
	_, baselineBytes := estimateFindResultSet(compactFindSnapshot(snapshot), matches)
	svc.FindCursorRegistry.byteBudget = baselineBytes + 1024
	snapshot.Annotations = []axProviderAnnotation{{
		Kind: "provider_omission", Count: 3, Reason: "time_budget",
		Detail: strings.Repeat("private provider diagnostic ", 1000),
	}}
	if cursor := svc.FindCursorRegistry.Create(snapshot, matches, 1, "target"); cursor != "" {
		t.Fatal("cursor storage ignored retained provider annotation bytes")
	}
}

func TestFindCursorCoverageFollowsResumedProviderRead(t *testing.T) {
	for _, incomplete := range []bool{false, true} {
		for _, format := range []string{"text", "records"} {
			t.Run(format+"/"+map[bool]string{false: "complete final read", true: "partial final read"}[incomplete], func(t *testing.T) {
				last := resumeSnapshot(nil, map[int]string{12: "Save 12", 13: "Save 13"})
				if incomplete {
					last.Snapshot.Annotations = []axProviderAnnotation{{
						ParentRuntimeID: []int{0}, Kind: "provider_omission", Count: 7, Reason: "max_tree_depth",
					}}
				}
				svc, recording := findWalkService(
					findWalkSnapshot("Open", true),
					resumeSnapshot([]int{0, 5}, nil),
					resumeSnapshot([]int{0, 9}, map[int]string{6: "Save 6", 7: "Save 7"}),
					last,
				)
				call := func(args map[string]any) (ToolCallResult, string) {
					if format == "records" {
						record := svc.CallRecord(contract.PublicToolAX, args, CallOptions{})
						if !record.OK || record.Page == nil {
							t.Fatalf("find records failed: %+v", record)
						}
						return ToolCallResult{StructuredContent: record.Evidence}, record.Page.NextCursor
					}
					result := svc.CallForModel(contract.PublicToolAX, args, false)
					return result, findCursor(result)
				}
				result, cursor := call(map[string]any{
					"mode": "find", "app": "Editor", "query": map[string]any{"name_contains": "Save"}, "limit": 1,
				})
				requireFindAcquisition(t, result, 50, "time")
				oldPageCursor := ""
				for page := 0; page < 4; page++ {
					if cursor == "" {
						t.Fatalf("missing cursor before continuation %d", page)
					}
					result, cursor = call(map[string]any{
						"mode": "find", "cursor": cursor, "limit": 1,
					})
					if page == 0 {
						oldPageCursor = cursor
					}
					wantReads := 3
					if page >= 2 {
						wantReads = 4
					}
					if len(recording.observeRequests) != wantReads {
						t.Fatalf("continuation %d made %d provider reads, want %d", page, len(recording.observeRequests), wantReads)
					}
					switch {
					case page < 2:
						requireFindAcquisition(t, result, 50, "time")
					case incomplete:
						requireFindAcquisition(t, result, 7, "max_tree_depth")
					default:
						requireFindAcquisition(t, result, 0)
					}
				}
				if cursor != "" {
					t.Fatalf("last page retained a cursor: %s", cursor)
				}
				// A new provider read must not rewrite evidence owned by an
				// older cursor, even when the new read has complete coverage.
				replayed, _ := call(map[string]any{"mode": "find", "cursor": oldPageCursor, "limit": 1})
				requireFindAcquisition(t, replayed, 50, "time")
				if len(recording.observeRequests) != 4 {
					t.Fatal("replaying a cached page unexpectedly reread the provider")
				}
			})
		}
	}
}
