package core

import (
	"encoding/json"
	"errors"
	"fmt"
	"strconv"
	"strings"
	"time"
)

const defaultAXFindCursorLimit = 32

const defaultAXFindCursorNodeBudget = 50000
const defaultAXFindCursorByteBudget = 64 << 20
const defaultAXFindCursorIdleTTL = 2 * time.Minute
const defaultAXFindCursorAbsoluteTTL = 10 * time.Minute

type AXFindCursorEntry struct {
	Ref          string
	ResultSetID  string
	Offset       int
	LastUsedTick uint64
}

type AXFindResultSet struct {
	ID           string
	Snapshot     *AppSnapshot
	Matches      []ElementRecord
	Query        string
	LastUsedTick uint64
	CreatedAt    time.Time
	LastUsedAt   time.Time
	NodeCount    int
	ApproxBytes  int
}

type AXFindCursorResolution struct {
	Cursor    AXFindCursorEntry
	ResultSet *AXFindResultSet
}

type FindCursorRegistry struct {
	nextCursor  uint64
	nextSet     uint64
	tick        uint64
	limit       int
	nodeBudget  int
	byteBudget  int
	idleTTL     time.Duration
	absoluteTTL time.Duration
	Entries     map[string]*AXFindCursorEntry `json:"-"`
	ResultSets  map[string]*AXFindResultSet   `json:"-"`
}

func NewFindCursorRegistry(limit int) *FindCursorRegistry {
	if limit <= 0 {
		limit = defaultAXFindCursorLimit
	}
	return &FindCursorRegistry{
		nextCursor:  1,
		nextSet:     1,
		limit:       limit,
		nodeBudget:  defaultAXFindCursorNodeBudget,
		byteBudget:  defaultAXFindCursorByteBudget,
		idleTTL:     defaultAXFindCursorIdleTTL,
		absoluteTTL: defaultAXFindCursorAbsoluteTTL,
		Entries:     map[string]*AXFindCursorEntry{},
		ResultSets:  map[string]*AXFindResultSet{},
	}
}

func parseAXFindCursor(ref string) (uint64, bool) {
	if len(ref) < 2 || ref[0] != 'f' {
		return 0, false
	}
	value, err := strconv.ParseUint(ref[1:], 10, 64)
	return value, err == nil && value > 0
}

func (registry *FindCursorRegistry) Create(
	snapshot *AppSnapshot,
	matches []ElementRecord,
	offset int,
	query string,
) string {
	// A cursor past the last match continues a walk that stopped early.
	if registry == nil || snapshot == nil || offset < 0 || offset > len(matches) ||
		offset == len(matches) && snapshot.findResume == nil {
		return ""
	}
	now := time.Now()
	registry.pruneExpired(now)
	compactSnapshot := compactFindSnapshot(snapshot)
	nodeCount, approxBytes := estimateFindResultSet(compactSnapshot, matches)
	if nodeCount > registry.nodeBudget || approxBytes > registry.byteBudget {
		return ""
	}
	registry.tick++
	setID := "fs" + strconv.FormatUint(registry.nextSet, 10)
	registry.nextSet++
	registry.ResultSets[setID] = &AXFindResultSet{
		ID:           setID,
		Snapshot:     compactSnapshot,
		Matches:      cloneFindRecords(matches),
		Query:        query,
		LastUsedTick: registry.tick,
		CreatedAt:    now,
		LastUsedAt:   now,
		NodeCount:    nodeCount,
		ApproxBytes:  approxBytes,
	}
	ref := registry.createCursor(setID, offset)
	registry.evict()
	return ref
}

func (registry *FindCursorRegistry) createCursor(
	resultSetID string,
	offset int,
) string {
	if registry == nil || registry.ResultSets[resultSetID] == nil || offset < 0 {
		return ""
	}
	registry.tick++
	ref := "f" + strconv.FormatUint(registry.nextCursor, 10)
	registry.nextCursor++
	registry.Entries[ref] = &AXFindCursorEntry{
		Ref:          ref,
		ResultSetID:  resultSetID,
		Offset:       offset,
		LastUsedTick: registry.tick,
	}
	return ref
}

func (registry *FindCursorRegistry) Continue(
	resultSetID string,
	offset int,
) string {
	registry.pruneExpired(time.Now())
	ref := registry.createCursor(resultSetID, offset)
	registry.evict()
	return ref
}

func (registry *FindCursorRegistry) Resolve(
	ref string,
) (*AXFindCursorResolution, error) {
	id, ok := parseAXFindCursor(ref)
	if !ok {
		return nil, fmt.Errorf("invalid AX find cursor %q", ref)
	}
	if registry == nil {
		return nil, errors.New("AX find cursor registry is unavailable")
	}
	registry.pruneExpired(time.Now())
	entry := registry.Entries[strings.TrimSpace(ref)]
	if entry == nil {
		if id < registry.nextCursor {
			return nil, fmt.Errorf(
				"AX find cursor %q expired; run AX mode=find again",
				ref,
			)
		}
		return nil, fmt.Errorf("unknown AX find cursor %q", ref)
	}
	resultSet := registry.ResultSets[entry.ResultSetID]
	if resultSet == nil {
		delete(registry.Entries, entry.Ref)
		return nil, fmt.Errorf(
			"AX find cursor %q expired; run AX mode=find again",
			ref,
		)
	}
	registry.tick++
	entry.LastUsedTick = registry.tick
	resultSet.LastUsedTick = registry.tick
	resultSet.LastUsedAt = time.Now()
	return &AXFindCursorResolution{
		Cursor:    *entry,
		ResultSet: resultSet,
	}, nil
}

func (registry *FindCursorRegistry) Reset() {
	if registry == nil {
		return
	}
	registry.Entries = map[string]*AXFindCursorEntry{}
	registry.ResultSets = map[string]*AXFindResultSet{}
}

func (registry *FindCursorRegistry) evict() {
	for registry != nil && len(registry.Entries) > registry.limit {
		oldestRef := ""
		var oldestTick uint64
		for ref, entry := range registry.Entries {
			if oldestRef == "" || entry.LastUsedTick < oldestTick {
				oldestRef = ref
				oldestTick = entry.LastUsedTick
			}
		}
		delete(registry.Entries, oldestRef)
	}
	for registry != nil &&
		(len(registry.ResultSets) > registry.limit ||
			registry.totalResultSetNodes() > registry.nodeBudget ||
			registry.totalResultSetBytes() > registry.byteBudget) {
		oldestID := ""
		var oldestTick uint64
		for id, resultSet := range registry.ResultSets {
			if oldestID == "" || resultSet.LastUsedTick < oldestTick {
				oldestID = id
				oldestTick = resultSet.LastUsedTick
			}
		}
		registry.removeResultSet(oldestID)
	}
}

func (registry *FindCursorRegistry) pruneExpired(now time.Time) {
	if registry == nil {
		return
	}
	for id, resultSet := range registry.ResultSets {
		idleExpired := registry.idleTTL > 0 &&
			now.Sub(resultSet.LastUsedAt) > registry.idleTTL
		absoluteExpired := registry.absoluteTTL > 0 &&
			now.Sub(resultSet.CreatedAt) > registry.absoluteTTL
		if idleExpired || absoluteExpired {
			registry.removeResultSet(id)
		}
	}
}

func (registry *FindCursorRegistry) removeResultSet(id string) {
	if registry == nil {
		return
	}
	delete(registry.ResultSets, id)
	for ref, entry := range registry.Entries {
		if entry.ResultSetID == id {
			delete(registry.Entries, ref)
		}
	}
}

func (registry *FindCursorRegistry) totalResultSetNodes() int {
	total := 0
	for _, resultSet := range registry.ResultSets {
		total += resultSet.NodeCount
	}
	return total
}

func (registry *FindCursorRegistry) totalResultSetBytes() int {
	total := 0
	for _, resultSet := range registry.ResultSets {
		total += resultSet.ApproxBytes
	}
	return total
}

func cloneFindRecord(record ElementRecord) ElementRecord {
	record.RuntimeID = append([]int(nil), record.RuntimeID...)
	record.RuntimeIDAliases = copyRuntimeIDs(record.RuntimeIDAliases)
	record.Actions = append([]string(nil), record.Actions...)
	record.VisibilityEvidence = cloneVisibilityEvidence(record.VisibilityEvidence)
	for _, field := range []**bool{&record.Showing, &record.Visible, &record.SetValueSupported} {
		if *field != nil {
			value := **field
			*field = &value
		}
	}
	if record.Frame != nil {
		value := *record.Frame
		record.Frame = &value
	}
	if record.TextAttributes != nil {
		attributes := make(map[string]string, len(record.TextAttributes))
		for key, value := range record.TextAttributes {
			attributes[key] = value
		}
		record.TextAttributes = attributes
	}
	if record.Relations != nil {
		relations := make(map[string][]axRelationTarget, len(record.Relations))
		for key, value := range record.Relations {
			relations[key] = append([]axRelationTarget(nil), value...)
		}
		record.Relations = relations
	}
	return record
}

func cloneFindRecords(records []ElementRecord) []ElementRecord {
	clones := make([]ElementRecord, len(records))
	for index, record := range records {
		clones[index] = cloneFindRecord(record)
	}
	return clones
}

func compactFindSnapshot(snapshot *AppSnapshot) *AppSnapshot {
	if snapshot == nil {
		return nil
	}
	clone := *snapshot
	clone.ScreenshotPNGBase64 = ""
	clone.ScreenshotMimeType = ""
	clone.ScreenshotEncoding = ""
	clone.desktopFrame = nil
	clone.TreeLines = nil
	clone.Elements = nil
	clone.Refs = nil
	// Cached pages describe this provider read, including any unread branches.
	// Keep its annotations independent of later changes to the live snapshot.
	clone.Annotations = append([]axProviderAnnotation(nil), snapshot.Annotations...)
	for index := range clone.Annotations {
		clone.Annotations[index].ParentRuntimeID = append([]int(nil), snapshot.Annotations[index].ParentRuntimeID...)
	}
	clone.DesktopLayout = nil
	if snapshot.findHiddenRecords != nil {
		clone.findHiddenRecords = make(map[int]ElementRecord, len(snapshot.findHiddenRecords))
		for index, record := range snapshot.findHiddenRecords {
			clone.findHiddenRecords[index] = cloneFindRecord(record)
		}
	}
	if snapshot.AXSnapshot != nil {
		axClone := *snapshot.AXSnapshot
		axClone.CanonicalRoots = cloneAXForest(
			snapshot.AXSnapshot.CanonicalRoots, nil,
		)
		axClone.Roots = axClone.CanonicalRoots
		indexAXSnapshot(&axClone)
		var stripSourceRecords func([]*AXNode)
		stripSourceRecords = func(nodes []*AXNode) {
			for _, node := range nodes {
				if node == nil {
					continue
				}
				node.SourceRecord = nil
				stripSourceRecords(node.Children)
			}
		}
		stripSourceRecords(axClone.CanonicalRoots)
		clone.AXSnapshot = &axClone
	}
	return &clone
}

func estimateFindResultSet(
	snapshot *AppSnapshot,
	matches []ElementRecord,
) (nodes, bytes int) {
	if snapshot != nil && snapshot.AXSnapshot != nil {
		var visit func([]*AXNode)
		visit = func(items []*AXNode) {
			for _, node := range items {
				if node == nil {
					continue
				}
				nodes++
				bytes += 256 + len(node.ID) + len(node.Name) +
					len(node.ProviderRole) + len(node.Properties.Description) +
					len(node.Properties.Value) + len(node.Properties.Placeholder)
				visit(node.Children)
			}
		}
		visit(snapshot.AXSnapshot.CanonicalRoots)
	}
	for _, match := range matches {
		bytes += 256 + len(match.Name) + len(match.Value) +
			len(match.Description) + len(match.Placeholder) +
			len(match.ControlType) + len(match.ProviderObjectPath)
	}
	if encoded, err := json.Marshal(struct {
		Snapshot      *AXSnapshot            `json:"snapshot"`
		Matches       []ElementRecord        `json:"matches"`
		HiddenRecords map[int]ElementRecord  `json:"hidden_records"`
		Annotations   []axProviderAnnotation `json:"annotations,omitempty"`
	}{
		Snapshot: func() *AXSnapshot {
			if snapshot == nil {
				return nil
			}
			return snapshot.AXSnapshot
		}(),
		Matches: matches,
		HiddenRecords: func() map[int]ElementRecord {
			if snapshot == nil {
				return nil
			}
			return snapshot.findHiddenRecords
		}(),
		Annotations: func() []axProviderAnnotation {
			if snapshot == nil {
				return nil
			}
			return snapshot.Annotations
		}(),
	}); err == nil && len(encoded) > bytes {
		bytes = len(encoded)
	}
	return nodes, bytes
}
