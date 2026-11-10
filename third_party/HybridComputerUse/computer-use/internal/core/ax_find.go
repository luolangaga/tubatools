package core

import (
	"fmt"
	"strconv"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func containsFold(haystack, needle string) bool {
	return strings.Contains(strings.ToLower(haystack), strings.ToLower(needle))
}

func recordMatchesFind(
	record *ElementRecord,
	role, name, text, state string,
	nameExact, textExact bool,
) bool {
	if record == nil {
		return false
	}
	if role != "" && !findRoleMatches(record, role) {
		return false
	}
	if name != "" {
		if nameExact {
			if !strings.EqualFold(
				strings.TrimSpace(record.Name),
				strings.TrimSpace(name),
			) {
				return false
			}
		} else if !containsFold(record.Name, name) {
			return false
		}
	}
	if state != "" && !containsFold(record.States, state) {
		return false
	}
	if text != "" {
		fields := []string{
			record.Name,
			record.Description,
			record.Value,
			record.Placeholder,
		}
		hit := false
		for _, field := range fields {
			if textExact {
				hit = strings.EqualFold(
					strings.TrimSpace(field),
					strings.TrimSpace(text),
				)
			} else {
				hit = containsFold(field, text)
			}
			if hit {
				break
			}
		}
		if !hit {
			return false
		}
	}
	return true
}

// findRoleMatches reports whether the record's provider or model role
// contains role. Roles compare by roleKey, so "menu_item" finds a
// "menu item" just as "menuitem" does.
func findRoleMatches(record *ElementRecord, role string) bool {
	want := roleKey(role)
	modelRole := canonicalRoleDescriptor(ontologyProviderRole(record), nil).ModelRole
	for _, have := range []string{record.ControlType, record.LocalizedControlType, string(modelRole)} {
		if strings.Contains(roleKey(have), want) {
			return true
		}
	}
	return false
}

func MatchedFindTextField(
	record *ElementRecord,
	text string,
	textExact bool,
) (string, string, bool) {
	if record == nil || text == "" {
		return "", "", false
	}
	for _, field := range []struct {
		name  string
		value string
	}{
		{name: "name", value: record.Name},
		{name: "description", value: record.Description},
		{name: "value", value: record.Value},
		{name: "placeholder", value: record.Placeholder},
	} {
		matched := containsFold(field.value, text)
		if textExact {
			matched = strings.EqualFold(
				strings.TrimSpace(field.value),
				strings.TrimSpace(text),
			)
		}
		if matched {
			return field.name, field.value, true
		}
	}
	return "", "", false
}

func describeQuery(
	role, name, text, state string,
	nameExact, textExact bool,
) string {
	parts := []string{}
	for _, pair := range [][2]string{
		{"role", role}, {"name", name}, {"text", text}, {"state", state},
	} {
		if pair[1] != "" {
			operator := "~"
			if (pair[0] == "name" && nameExact) ||
				(pair[0] == "text" && textExact) {
				operator = "="
			}
			parts = append(parts, fmt.Sprintf(
				"%s%s%q",
				pair[0],
				operator,
				pair[1],
			))
		}
	}
	return strings.Join(parts, " ")
}

// findMatch is the read step of find: the refreshed snapshot and the
// records that match the query, in canonical order.
type findMatch struct {
	snapshot *AppSnapshot
	// nearSnapshot lists the closest elements when nothing matched. A find
	// walk holds records only for candidates, so it keeps the observe read.
	nearSnapshot        *AppSnapshot
	matched             []ElementRecord
	query               string
	notes               []string
	followsFailedAction bool
}

func (s *Service) findMatches(
	app, role, name, text, state string,
	nameExact, textExact bool,
	scope string,
	targetWindowHandle ...string,
) (*findMatch, ToolCallResult) {
	if app == "" {
		return nil, TextResult("Missing required argument: app", true)
	}
	if role == "" && name == "" && text == "" && state == "" {
		return nil, TextResult("find needs at least one of role, name, text, or state. For a fresh tree, call AX mode=observe.", true)
	}
	noScreenshot := false
	request := AXServiceRequest{
		Operation:         axServiceObserve,
		App:               app,
		IncludeScreenshot: &noScreenshot,
		Scope:             normalizedAXScope(scope),
	}
	if len(targetWindowHandle) > 0 {
		request.TargetWindowHandle = strings.TrimSpace(targetWindowHandle[0])
	}
	snapshot, notes, result := s.refreshProjectedSnapshot(app, request)
	if result.IsError {
		return nil, result
	}
	matched := findQueryMatches(snapshot, role, name, text, state, nameExact, textExact)
	// The observe read stops at the render budget. When it found nothing and
	// stopped early, the Linux provider walks again with the find caps; see
	// ProviderObservationOptions.FindWalk. A match in the budgeted read
	// returns at once: the caller sees partial coverage and can narrow.
	nearSnapshot := snapshot
	if len(matched) == 0 &&
		s.platformBackend().HostInfo().Platform == "linux" &&
		providerCoverageSummary(snapshot.Annotations).AcquisitionIncomplete > 0 {
		request.FindWalk = true
		request.FindQuery = &ProviderFindQuery{
			Name: name, NameExact: nameExact, Text: text, TextExact: textExact,
		}
		walked, walkNotes, walkResult := s.readFindWalk(app, request)
		if walkResult.IsError {
			return nil, walkResult
		}
		snapshot, notes = walked, walkNotes
		matched = findQueryMatches(snapshot, role, name, text, state, nameExact, textExact)
		noteFindResume(snapshot, axFindResume{
			app: app, scope: request.Scope,
			role: role, name: name, text: text, state: state,
			nameExact: nameExact, textExact: textExact,
		})
	}
	found := &findMatch{snapshot: snapshot, nearSnapshot: nearSnapshot, matched: matched}
	if note := s.consumeFailedObservation(app, snapshot); note != "" {
		notes = append([]string{note}, notes...)
		found.followsFailedAction = true
	}
	found.notes = compactFindNotes(notes)
	found.query = describeQuery(role, name, text, state, nameExact, textExact)
	return found, ToolCallResult{}
}

func findQueryMatches(
	snapshot *AppSnapshot,
	role, name, text, state string,
	nameExact, textExact bool,
) []ElementRecord {
	matched := []ElementRecord{}
	for _, record := range snapshot.Elements {
		if !recordInAXScope(snapshot, &record) {
			continue
		}
		if queryRecordMatches(
			snapshot,
			&record,
			role,
			name,
			text,
			state,
			nameExact,
			textExact,
		) {
			if field, value, ok :=
				MatchedFindTextField(&record, text, textExact); ok {
				record.FindMatchedField = field
				record.FindMatchedText = value
			}
			matched = append(matched, record)
		}
	}
	return matched
}

func (s *Service) find(
	app, role, name, text, state string,
	limit int,
	nameExact, textExact bool,
	scope string,
	targetWindowHandle ...string,
) ToolCallResult {
	if limit <= 0 {
		limit = 20
	}
	found, failure := s.findMatches(app, role, name, text, state,
		nameExact, textExact, scope, targetWindowHandle...)
	if found == nil {
		return failure
	}
	return s.renderFindMatches(found, limit, newAXNearQuery(role, name, text, state))
}

// renderFindMatches renders the first page of found, or its zero-match
// answer with the closest elements.
func (s *Service) renderFindMatches(found *findMatch, limit int, near axNearQuery) ToolCallResult {
	snapshot, matched, query := found.snapshot, found.matched, found.query
	nearSnapshot := found.nearSnapshot
	notes, followsFailedAction := found.notes, found.followsFailedAction
	var out strings.Builder
	if len(matched) == 0 {
		cursor, continuation := s.findZeroMatchCursor(snapshot, query)
		fmt.Fprintf(
			&out,
			"window: %q\nmatches: 0 query=%s%s\n",
			snapshot.WindowTitle,
			strconv.Quote(query),
			continuation,
		)
		if line := snapshot.scopeFallbackLine(); line != "" {
			out.WriteString(line + "\n")
		}
		out.WriteString(s.findZeroMatchText(nearSnapshot, near))
		response := withFailedActionContinuity(
			snapshot.textWithNotes(out.String(), notes),
			followsFailedAction,
		)
		addTreeProjectionMetadata(&response, snapshot)
		response.StructuredContent["find_match_count"] = 0
		if cursor != "" {
			response.StructuredContent["next_cursor"] = cursor
		}
		return response
	}
	return s.RenderFindPage(
		snapshot,
		matched,
		0,
		limit,
		query,
		notes,
		followsFailedAction,
	)
}

// continueFind returns the page a cursor points to. A cursor past the last
// match of a walk that stopped early continues the walk.
func (s *Service) continueFind(cursor string, limit int) ToolCallResult {
	resolution, err := s.FindCursorRegistry.Resolve(cursor)
	if err != nil {
		return TextResult(
			"AX find cursor: "+err.Error(), true,
		).withEvidenceScope("error_only")
	}
	if limit <= 0 {
		limit = 20
	}
	if limit > contract.MaxAXFindPageSize {
		return TextResult(fmt.Sprintf(
			"limit must be at most %d", contract.MaxAXFindPageSize,
		), true).withEvidenceScope("error_only")
	}
	if resume := findResumeOf(resolution); resume != nil {
		found, failure := s.resumedFindMatches(resume)
		if found == nil {
			return failure
		}
		return s.renderFindMatches(found, limit, resume.nearQuery()).
			withEvidenceScope("independent_ui_read")
	}
	entry := resolution.Cursor
	resultSet := resolution.ResultSet
	return s.RenderFindPage(
		resultSet.Snapshot,
		resultSet.Matches,
		entry.Offset,
		limit,
		resultSet.Query,
		nil,
		false,
		entry.ResultSetID,
	).withEvidenceScope("cached_observation_page")
}

func (s *Service) RenderFindPage(
	snapshot *AppSnapshot,
	matched []ElementRecord,
	offset,
	limit int,
	query string,
	notes []string,
	followsFailedAction bool,
	resultSetID ...string,
) ToolCallResult {
	if snapshot == nil || snapshot.AXSnapshot == nil {
		return TextResult(
			"AX find cursor has no recoverable snapshot; run AX mode=find again",
			true,
		).withEvidenceScope("error_only")
	}
	if offset < 0 || offset >= len(matched) {
		return TextResult(
			"AX find cursor is exhausted; run AX mode=find again",
			true,
		).withEvidenceScope("error_only")
	}
	setID := ""
	if len(resultSetID) > 0 {
		setID = strings.TrimSpace(resultSetID[0])
	}
	projected := AXProjectedIndices(snapshot.AXSnapshot.Roots)
	if setID == "" {
		snapshot = s.freezeFindRefs(snapshot, matched)
		projected = AXProjectedIndices(snapshot.AXSnapshot.Roots)
	}
	return s.deliverFindPage(snapshot, matched, offset, limit, query, notes,
		followsFailedAction, projected, setID)
}

// freezeFindRefs assigns refs to every match and its context once, before
// the first page, and returns the snapshot that later pages read from.
func (s *Service) freezeFindRefs(snapshot *AppSnapshot, matched []ElementRecord) *AppSnapshot {
	projected := AXProjectedIndices(snapshot.AXSnapshot.Roots)
	allFindNodes := buildAXFindContext(snapshot, matched, projected)
	allFindSnapshot := newAXFindSnapshot(
		snapshot.AXSnapshot, allFindNodes,
	)
	s.RefRegistry.AssignSnapshotRefs(allFindSnapshot)
	s.stabilizeFindRefs(snapshot, allFindSnapshot)
	cursorSnapshot := *snapshot
	cursorSnapshot.AXSnapshot = allFindSnapshot
	cursorSnapshot.Elements = append([]ElementRecord(nil), matched...)
	// Ref assignment freezes identity; only delivered content proves exposure.
	cursorSnapshot.findHiddenRecords = map[int]ElementRecord{}
	for _, record := range snapshot.Elements {
		if !projected[record.Index] && allFindSnapshot.ByIndex[record.Index] != nil {
			cursorSnapshot.findHiddenRecords[record.Index] = cloneFindRecord(record)
		}
	}
	return &cursorSnapshot
}

func (s *Service) stabilizeFindRefs(
	source *AppSnapshot,
	findSnapshot *AXSnapshot,
) {
	if s == nil || s.RefRegistry == nil ||
		source == nil || findSnapshot == nil {
		return
	}
	var visit func([]*AXNode)
	visit = func(nodes []*AXNode) {
		for _, node := range nodes {
			if node.FindMatch && node.Ref != "" {
				entry := s.RefRegistry.Entries[node.Ref]
				if entry != nil && (entry.WindowHandle != source.WindowHandle ||
					entry.WindowHandle != findSnapshot.WindowHandle ||
					entry.LogicalWindowRef != "" && entry.LogicalWindowRef != findSnapshot.WindowRef) {
					node.Ref = ""
					if canonical := findSnapshot.ByID[node.ID]; canonical != nil {
						canonical.Ref = ""
					}
					visit(node.Children)
					continue
				}
				if entry != nil && entry.LocatorKind == AXLocatorSemantic {
					selector := strings.TrimSpace(entry.SemanticTarget)
					var current *ElementRecord
					var err error
					if isSemanticRef(selector) {
						var scopedWindow string
						selector, scopedWindow, err = decodeSemanticRefScope(selector)
						if err == nil && scopedWindow != "" && scopedWindow != source.WindowHandle {
							err = fmt.Errorf("semantic ref belongs to another window")
						}
					}
					if err == nil {
						current, err = lookupBySelector(source, selector, "")
					}
					if err != nil {
						node.Ref = ""
						if axNodeProviderIdentityComplete(node) {
							node.Ref = s.RefRegistry.AcquireNode(
								findSnapshot,
								node,
								AXLocatorProviderBound,
							)
						}
						if canonical := findSnapshot.ByID[node.ID]; canonical != nil {
							canonical.Ref = node.Ref
						}
					} else if current != nil {
						s.RefRegistry.RefreshProviderBinding(node.Ref, current, true)
						if entry.Lifecycle != AXRefActive {
							node.Ref = ""
							if canonical := findSnapshot.ByID[node.ID]; canonical != nil {
								canonical.Ref = ""
							}
						}
					}
				}
			}
			visit(node.Children)
		}
	}
	visit(findSnapshot.Roots)
}
