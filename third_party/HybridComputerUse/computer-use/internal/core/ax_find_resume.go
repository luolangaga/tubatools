package core

// A Linux find walk (ProviderObservationOptions.FindWalk) stops when its
// node, character or time budget runs out and reports the first node it did
// not visit as AppSnapshot.FindResume. The find answer then ends with a
// next_cursor; continuing it walks the same window again with the same
// query, from that node on, so no match is reported twice.

// axFindResume is what a find cursor needs to continue a stopped walk.
type axFindResume struct {
	app, windowHandle, scope string
	role, name, text, state  string
	nameExact, textExact     bool
	after                    []int
}

func (resume *axFindResume) providerQuery() *ProviderFindQuery {
	return &ProviderFindQuery{
		Name: resume.name, NameExact: resume.nameExact,
		Text: resume.text, TextExact: resume.textExact,
	}
}

func (resume *axFindResume) nearQuery() axNearQuery {
	return newAXNearQuery(resume.role, resume.name, resume.text, resume.state)
}

// noteFindResume attaches to snapshot the cursor state of a walk that
// stopped early; resume carries the query and window of that walk.
func noteFindResume(snapshot *AppSnapshot, resume axFindResume) {
	if snapshot == nil || len(snapshot.FindResume) == 0 {
		return
	}
	resume.windowHandle = snapshot.WindowHandle
	resume.after = append([]int(nil), snapshot.FindResume...)
	snapshot.findResume = &resume
}

// runtimePathAfter reports whether path is resume or comes after it in
// pre-order. An ancestor of resume does not: the resumed walk visits it
// again only to reach resume, and an earlier walk already reported it.
func runtimePathAfter(path, resume []int) bool {
	for index := range path {
		if index >= len(resume) {
			return true
		}
		if path[index] != resume[index] {
			return path[index] > resume[index]
		}
	}
	return len(path) == len(resume)
}

// resumedFindMatches walks the window of resume again from its stop node.
func (s *Service) resumedFindMatches(resume *axFindResume) (*findMatch, ToolCallResult) {
	noScreenshot := false
	request := AXServiceRequest{
		Operation:          axServiceObserve,
		App:                resume.app,
		IncludeScreenshot:  &noScreenshot,
		Scope:              resume.scope,
		TargetWindowHandle: resume.windowHandle,
		FindWalk:           true,
		FindQuery:          resume.providerQuery(),
		FindResumeAfter:    append([]int(nil), resume.after...),
	}
	snapshot, notes, result := s.readFindWalk(resume.app, request)
	if result.IsError {
		return nil, result
	}
	if snapshot.WindowHandle != resume.windowHandle {
		return nil, TextResult(
			"AX find cursor: the window it was searching is gone; run AX mode=find again",
			true,
		).withEvidenceScope("error_only")
	}
	matched := []ElementRecord{}
	for _, record := range findQueryMatches(snapshot, resume.role, resume.name,
		resume.text, resume.state, resume.nameExact, resume.textExact) {
		if runtimePathAfter(record.RuntimeID, resume.after) {
			matched = append(matched, record)
		}
	}
	noteFindResume(snapshot, *resume)
	near := s.currentSnapshot(resume.app)
	if near == nil || near.WindowHandle != snapshot.WindowHandle {
		near = snapshot
	}
	return &findMatch{
		snapshot:     snapshot,
		nearSnapshot: near,
		matched:      matched,
		query: describeQuery(resume.role, resume.name, resume.text,
			resume.state, resume.nameExact, resume.textExact),
		notes: compactFindNotes(notes),
	}, ToolCallResult{}
}

// findResumeOf returns the stopped walk a cursor continues, or nil when the
// cursor points into its result set's matches.
func findResumeOf(resolution *AXFindCursorResolution) *axFindResume {
	if resolution == nil || resolution.ResultSet == nil ||
		resolution.ResultSet.Snapshot == nil ||
		resolution.Cursor.Offset < len(resolution.ResultSet.Matches) {
		return nil
	}
	return resolution.ResultSet.Snapshot.findResume
}

// findZeroMatchCursor returns the cursor of a zero-match answer whose walk
// stopped early, and the header continuation that shows it.
func (s *Service) findZeroMatchCursor(snapshot *AppSnapshot, query string) (string, string) {
	if snapshot == nil || snapshot.findResume == nil {
		return "", ""
	}
	cursor := s.FindCursorRegistry.Create(snapshot, nil, 0, query)
	if cursor == "" {
		return "", ""
	}
	return cursor, findNextPageLine(cursor)
}
