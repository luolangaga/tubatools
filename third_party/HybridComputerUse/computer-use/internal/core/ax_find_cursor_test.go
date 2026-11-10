package core

import (
	"strings"
	"testing"
	"time"
)

func TestFindCursorPagesAreBoundedAndStable(t *testing.T) {
	registry := NewFindCursorRegistry(4)
	snapshot := &AppSnapshot{
		WindowTitle: "Results",
		AXSnapshot: &AXSnapshot{
			SchemaVersion: "AXSnapshot",
			AppIdentity:   "app",
			WindowHandle:  "window",
			Revision:      "c1",
		},
	}
	matches := []ElementRecord{{Index: 1}, {Index: 2}, {Index: 3}}
	cursor := registry.Create(snapshot, matches, 1, `role="button"`)
	if cursor != "f1" {
		t.Fatalf("cursor=%q want f1", cursor)
	}
	entry, err := registry.Resolve(cursor)
	if err != nil {
		t.Fatal(err)
	}
	if entry.Cursor.Offset != 1 ||
		entry.ResultSet == nil ||
		len(entry.ResultSet.Matches) != 3 ||
		entry.ResultSet.Snapshot == snapshot ||
		entry.ResultSet.Snapshot.WindowTitle != snapshot.WindowTitle {
		t.Fatalf("cursor lost bound observation: %#v", entry)
	}
	nextCursor := registry.Continue(entry.Cursor.ResultSetID, 2)
	nextEntry, err := registry.Resolve(nextCursor)
	if err != nil {
		t.Fatal(err)
	}
	if nextEntry.Cursor.ResultSetID != entry.Cursor.ResultSetID ||
		nextEntry.ResultSet != entry.ResultSet {
		t.Fatal("continued cursor did not share the immutable result set")
	}
	registry.Reset()
	if _, err := registry.Resolve(cursor); err == nil ||
		!strings.Contains(err.Error(), "expired") {
		t.Fatalf("reset cursor did not expire: %v", err)
	}
}

func TestFindCursorDoesNotReuseFreshProviderResultsAndExpiresByTTL(t *testing.T) {
	registry := NewFindCursorRegistry(4)
	registry.idleTTL = time.Millisecond
	snapshot := &AppSnapshot{
		SnapshotID:   "snapshot-1",
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			SchemaVersion: "AXSnapshot",
			WindowHandle:  "window-1",
			Revision:      "c1",
		},
	}
	matches := []ElementRecord{{Index: 1}, {Index: 2}}
	first := registry.Create(snapshot, matches, 1, `role="button"`)
	second := registry.Create(snapshot, matches, 1, `role="button"`)
	firstResolution, err := registry.Resolve(first)
	if err != nil {
		t.Fatal(err)
	}
	secondResolution, err := registry.Resolve(second)
	if err != nil {
		t.Fatal(err)
	}
	if firstResolution.Cursor.ResultSetID ==
		secondResolution.Cursor.ResultSetID ||
		len(registry.ResultSets) != 2 {
		t.Fatal("fresh find incorrectly reused provider-backed result data")
	}
	registry.ResultSets[firstResolution.Cursor.ResultSetID].LastUsedAt =
		time.Now().Add(-time.Second)
	if _, err := registry.Resolve(first); err == nil ||
		!strings.Contains(err.Error(), "expired") {
		t.Fatalf("expired cursor remained usable: %v", err)
	}
}

func TestFindCursorExpiresAtAbsoluteDeadlineDespiteContinuedUse(t *testing.T) {
	registry := NewFindCursorRegistry(4)
	registry.idleTTL = time.Hour
	registry.absoluteTTL = time.Millisecond
	snapshot := &AppSnapshot{
		AXSnapshot: &AXSnapshot{SchemaVersion: "AXSnapshot"},
	}
	cursor := registry.Create(
		snapshot,
		[]ElementRecord{{Index: 1}, {Index: 2}},
		1,
		`role="button"`,
	)
	resolution, err := registry.Resolve(cursor)
	if err != nil {
		t.Fatal(err)
	}
	resolution.ResultSet.CreatedAt = time.Now().Add(-time.Second)
	resolution.ResultSet.LastUsedAt = time.Now()
	if _, err := registry.Resolve(cursor); err == nil ||
		!strings.Contains(err.Error(), "expired") {
		t.Fatalf("absolute TTL did not expire cursor: %v", err)
	}
}

func TestFindCursorRejectsOversizedResultSet(t *testing.T) {
	registry := NewFindCursorRegistry(4)
	registry.nodeBudget = 1
	root := &AXNode{ID: "0", Name: "root"}
	root.Children = []*AXNode{{ID: "0/0", Name: "child", Parent: root}}
	snapshot := &AppSnapshot{
		SnapshotID: "snapshot-1",
		AXSnapshot: &AXSnapshot{CanonicalRoots: []*AXNode{root}},
	}
	if cursor := registry.Create(
		snapshot,
		[]ElementRecord{{Index: 1}, {Index: 2}},
		1,
		`role="button"`,
	); cursor != "" {
		t.Fatalf("oversized result set received cursor %q", cursor)
	}
}
