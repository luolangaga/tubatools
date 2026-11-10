package core

import (
	"encoding/json"
	"fmt"
	"sort"
	"strings"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// This file is the code-facing rendering of the public contract. MCP,
// computer-use call, the socket daemon, the CLI, the Python SDK and the
// PTC adapter all reach the same callTool; records differ from text only in
// how core renders the result, never in what runs.
//
// The request fields below are not in the published MCP schema: models keep
// seeing exactly the text contract. A caller selects records with the
// call option, never through tool arguments, so the schema, the AX decoder
// and the golden contract stay unchanged.

// CallOptions are per-call choices made by the entry point.
type CallOptions struct {
	// Format is "text" (the model contract, the MCP default) or "records".
	Format string `json:"format,omitempty"`
	// RequestID makes an act idempotent: the daemon returns the stored
	// result for a repeated id and never sends the input again.
	RequestID string `json:"request_id,omitempty"`
	// Since names a snapshot_id from an earlier records observe; observe
	// then reports changes against it instead of the session baseline.
	Since string `json:"since,omitempty"`
}

// Record is the JSON envelope for one call in records format. Success and
// error use the same shape.
type Record struct {
	OK           bool             `json:"ok"`
	Code         string           `json:"code,omitempty"`
	Path         string           `json:"path,omitempty"`
	Message      string           `json:"message,omitempty"`
	Hint         string           `json:"hint,omitempty"`
	Op           string           `json:"op"`
	RequestID    string           `json:"request_id,omitempty"`
	Replayed     bool             `json:"replayed,omitempty"`
	InputEmitted bool             `json:"input_emitted"`
	Completed    *int             `json:"completed,omitempty"`
	Requested    *int             `json:"requested,omitempty"`
	SnapshotID   string           `json:"snapshot_id,omitempty"`
	App          *RecordApp       `json:"app,omitempty"`
	Window       *RecordWindow    `json:"window,omitempty"`
	Windows      []RecordWindow   `json:"windows,omitempty"`
	Elements     []RecordElement  `json:"elements,omitempty"`
	Matches      []RecordElement  `json:"matches,omitempty"`
	Read         []RecordRead     `json:"read,omitempty"`
	Diff         *RecordDiff      `json:"diff,omitempty"`
	Page         *RecordPage      `json:"page,omitempty"`
	Screen       *RecordScreen    `json:"screen,omitempty"`
	Images       []string         `json:"images,omitempty"`
	Notes        []string         `json:"notes,omitempty"`
	Text         string           `json:"text,omitempty"`
	Evidence     map[string]any   `json:"evidence,omitempty"`
	TimingMS     map[string]int64 `json:"timing_ms"`
}

type RecordApp struct {
	Ref  string `json:"ref,omitempty"`
	Name string `json:"name,omitempty"`
	PID  int    `json:"pid,omitempty"`
}

type RecordWindow struct {
	Ref    string `json:"ref,omitempty"`
	Title  string `json:"title"`
	App    string `json:"app,omitempty"`
	AppRef string `json:"app_ref,omitempty"`
	Active bool   `json:"active,omitempty"`
}

// RecordElement is one canonical AX element. Ref is empty for elements that
// cannot be targeted; Parent is the nearest ancestor that has a ref.
type RecordElement struct {
	Ref         string            `json:"ref,omitempty"`
	Role        string            `json:"role"`
	Name        string            `json:"name,omitempty"`
	Value       string            `json:"value,omitempty"`
	Description string            `json:"description,omitempty"`
	Placeholder string            `json:"placeholder,omitempty"`
	States      map[string]string `json:"states,omitempty"`
	Actions     []string          `json:"actions,omitempty"`
	Bounds      []float64         `json:"bounds,omitempty"`
	Depth       int               `json:"depth"`
	Parent      string            `json:"parent,omitempty"`
}

type RecordRead struct {
	Ref     string         `json:"ref"`
	OK      bool           `json:"ok"`
	Code    string         `json:"code,omitempty"`
	Message string         `json:"message,omitempty"`
	Element *RecordElement `json:"element,omitempty"`
}

type RecordDiff struct {
	Since     string          `json:"since"`
	Unchanged bool            `json:"unchanged"`
	Added     []RecordElement `json:"added,omitempty"`
	Removed   []RecordElement `json:"removed,omitempty"`
	Changed   []RecordElement `json:"changed,omitempty"`
}

type RecordPage struct {
	Returned   int    `json:"returned"`
	Total      int    `json:"total"`
	Offset     int    `json:"offset"`
	NextCursor string `json:"next_cursor,omitempty"`
}

type RecordScreen struct {
	Width           int    `json:"width"`
	Height          int    `json:"height"`
	CoordinateSpace string `json:"coordinate_space,omitempty"`
	Image           string `json:"image,omitempty"`
}

// callRecord runs one public call and renders it as a Record. The text
// contract and the records contract share validation, dispatch, stale-ref
// policy and the public error mapping; only rendering differs.
func (s *Service) CallRecord(name string, args map[string]any, options CallOptions) Record {
	started := time.Now()
	providerBefore := s.providerTime()
	record := s.dispatchRecord(name, args, options)
	record.RequestID = options.RequestID
	provider := s.providerTime() - providerBefore
	total := time.Since(started)
	record.TimingMS = map[string]int64{
		"provider": provider.Milliseconds(),
		"core":     (total - provider).Milliseconds(),
		"total":    total.Milliseconds(),
	}
	return record
}

func (s *Service) dispatchRecord(name string, args map[string]any, options CallOptions) Record {
	op := RecordOp(name, args)
	if name == contract.PublicToolAX && OptionalString(args, "mode") == contract.RecordsOpRead {
		return s.readRecords(args)
	}
	if name == contract.PublicToolAX && OptionalString(args, "mode") == contract.AXModeFind {
		if record, handled := s.findRecords(args); handled {
			record.Op = op
			return record
		}
	}
	if name == contract.PublicToolAX && OptionalString(args, "mode") == contract.AXModeObserve {
		if record, handled := s.observeRecords(args, options.Since); handled {
			record.Op = op
			return record
		}
	}
	if name == contract.PublicToolAX && OptionalString(args, "mode") == contract.AXModeAct {
		if _, explicit := args["observe"]; !explicit {
			// Code callers chain actions and read state explicitly; a diff
			// after every act costs a full read. They ask for one with
			// observe:"diff".
			args = contract.CloneObject(args)
			args["observe"] = contract.ObserveNone
		}
	}
	// Records skip the model text budget: the evidence is compacted the same
	// way, but no text is cut to fit a model context.
	result := s.callTool(name, args)
	switch name {
	case contract.PublicToolAX:
		result = PrepareAXEvidence(result)
	case contract.PublicToolGUI:
		result = prepareGUIEvidence(result)
	}
	record := recordFromResult(name, result)
	record.Op = op
	if name == contract.PublicToolAX && OptionalString(args, "mode") == contract.AXModeApps && record.OK {
		record.Windows = s.recordWindows()
	}
	return record
}

func RecordOp(name string, args map[string]any) string {
	if name == contract.PublicToolGUI {
		return "gui"
	}
	if mode := OptionalString(args, "mode"); mode != "" {
		return "ax." + mode
	}
	return "ax"
}

// recordFromResult converts a finalized text result. The public evidence
// fields come from the same compact evidence the text contract prints on
// its first line; the rendered text stays available, untruncated by the
// records path but as the core produced it.
func recordFromResult(name string, result ToolCallResult) Record {
	record := recordFromEvidence(name, result)
	for _, item := range result.Content {
		if item.Type == "image" {
			if path, err := writeImageFile(item.Data, item.MimeType); err == nil {
				record.Images = append(record.Images, path)
				if item.frame != nil {
					// An AX observe full/screenshot image is the GUI frame;
					// its screen is the one a GUI screenshot record carries.
					record.Screen = &RecordScreen{Width: item.frame.Width, Height: item.frame.Height,
						CoordinateSpace: item.frame.CoordinateSpace, Image: path}
				}
			}
		}
	}
	if record.Screen != nil && record.Screen.Image != "" {
		return record
	}
	if record.Screen != nil && len(record.Images) > 0 {
		record.Screen.Image = record.Images[0]
	}
	return record
}

// recordFromEvidence reads the public fields of a prepared result. It
// writes no files.
func recordFromEvidence(name string, result ToolCallResult) Record {
	record := Record{OK: !result.IsError}
	evidence := result.StructuredContent
	if evidence != nil && (name == contract.PublicToolAX || name == contract.PublicToolGUI) {
		record.Code, _ = evidence["code"].(string)
		record.Path, _ = evidence["path"].(string)
		record.Message, _ = evidence["message"].(string)
		record.Hint, _ = evidence["hint"].(string)
		record.InputEmitted, _ = evidence["input_emitted"].(bool)
		if completed, ok := IntEvidence(evidence["completed"]); ok {
			record.Completed = &completed
		}
		if requested, ok := IntEvidence(evidence["requested"]); ok {
			record.Requested = &requested
		}
		rest := map[string]any{}
		for key, value := range evidence {
			if !contract.ContainsString(axPublicFields, key) && key != "screen" {
				rest[key] = value
			}
		}
		if len(rest) > 0 {
			record.Evidence = rest
		}
	} else if evidence != nil {
		record.Evidence = modelVisibleEvidence(evidence)
	}
	var text []string
	for _, item := range result.Content {
		if item.Type != "text" {
			continue
		}
		if trimmed := strings.TrimRight(item.Text, "\n"); trimmed != "" {
			text = append(text, trimmed)
		}
	}
	record.Text = strings.Join(text, "\n")
	if name == contract.PublicToolGUI {
		record.Screen = guiRecordScreen(evidence)
	}
	if !record.OK && record.Code == "" {
		record.Code = contract.AXCodeRuntimeError
		record.Message = firstLine(record.Text)
	}
	return record
}

// guiRecordScreen reads the screen of a prepared GUI result.
func guiRecordScreen(evidence map[string]any) *RecordScreen {
	screen, _ := evidence["screen"].(map[string]any)
	if screen == nil {
		return nil
	}
	width, _ := IntEvidence(screen["width"])
	height, _ := IntEvidence(screen["height"])
	space, _ := screen["coordinate_space"].(string)
	return &RecordScreen{Width: width, Height: height, CoordinateSpace: space}
}

// recordWindows lists the windows of the latest apps listing in records
// form. It reads the registry, not the provider: apps just refreshed it.
func (s *Service) recordWindows() []RecordWindow {
	inventory := s.lastInventory
	if inventory == nil {
		return nil
	}
	out := make([]RecordWindow, 0, len(inventory.windows))
	for _, window := range inventory.windows {
		out = append(out, RecordWindow{
			Ref: window.ref, Title: window.title, App: window.appName,
			AppRef: window.appRef, Active: window.active,
		})
	}
	return out
}

// errorRecord renders a toolCallResult error from the shared validation
// path, so records and text agree on code, path and hint.
func errorRecord(name string, result ToolCallResult) Record {
	switch name {
	case contract.PublicToolAX:
		result = PrepareAXEvidence(result)
	case contract.PublicToolGUI:
		result = prepareGUIEvidence(result)
	}
	return recordFromResult(name, result)
}

// observeRecords renders observe as element records: the whole canonical
// tree of the window, never truncated. With since, it reports the changes
// against that earlier records snapshot.
func (s *Service) observeRecords(args map[string]any, since string) (Record, bool) {
	request, rejected, bad := s.rejectInvalidCall(contract.PublicToolAX, args)
	if bad {
		return errorRecord(contract.PublicToolAX, rejected), true
	}
	if request.observe == contract.ObserveScreenshot || request.observe == contract.ObserveFull {
		// Screenshots go through the shared path and come back as files.
		return Record{}, false
	}
	readContext, identityErr := s.resolveAXReadIdentity(request.app, request.windowRef, "")
	if identityErr != nil {
		return errorRecord(contract.PublicToolAX, axIdentityErrorResult(identityErr)), true
	}
	var root map[string]any
	switch {
	case request.root == "focused":
		root = map[string]any{"focused": true}
	case request.root != "":
		root = map[string]any{"ref": request.root}
	}
	noScreenshot := false
	serviceRequest := AXServiceRequest{
		Operation:          axServiceObserve,
		App:                readContext.appToken,
		TreeView:           contract.TreeViewWorking,
		Scope:              normalizedAXScope(request.scope),
		ObservationRoot:    root,
		IncludeScreenshot:  &noScreenshot,
		TargetWindowHandle: readContext.windowHandle,
	}
	snapshot, notes, _, failure := s.observeAppSnapshot(readContext.appToken, serviceRequest)
	if snapshot == nil {
		return errorRecord(contract.PublicToolAX, failure.withEvidenceScope("error_only")), true
	}
	record := Record{OK: true}
	fillRecordIdentity(&record, snapshot)
	record.Notes = modelFacingAXNotes(notes)
	elements := snapshotRecordElements(snapshot)
	record.SnapshotID = s.rememberRecordSnapshot(snapshot, elements)
	if since != "" {
		baseline := s.recordSnapshot(since)
		if baseline == nil {
			return Record{
				OK: false, Code: contract.AXCodeInvalidArgument, Path: "since",
				Message: fmt.Sprintf("since %q is not a snapshot of this session (it may have expired)", since),
				Hint:    `observe without since, then pass its snapshot_id`,
			}, true
		}
		record.Diff = diffRecordElements(since, baseline.elements, elements)
		return record, true
	}
	record.Elements = elements
	return record, true
}

func fillRecordIdentity(record *Record, snapshot *AppSnapshot) {
	record.App = &RecordApp{Ref: snapshot.publicAppRef, Name: snapshot.App.Name, PID: snapshot.App.PID}
	record.Window = &RecordWindow{Ref: snapshotLogicalWindowRef(snapshot), Title: snapshot.WindowTitle}
	for _, window := range snapshot.Windows {
		if window.publicRef != "" && window.publicRef != record.Window.Ref {
			record.Windows = append(record.Windows, RecordWindow{Ref: window.publicRef, Title: window.Title})
		}
	}
}

// snapshotRecordElements walks the canonical tree, so records never lose
// nodes to the model-facing distillation or text budget.
func snapshotRecordElements(snapshot *AppSnapshot) []RecordElement {
	if snapshot == nil || snapshot.AXSnapshot == nil {
		return nil
	}
	out := []RecordElement{}
	var visit func(nodes []*AXNode, depth int, parentRef string)
	visit = func(nodes []*AXNode, depth int, parentRef string) {
		for _, node := range nodes {
			element := recordElementFromNode(node, depth, parentRef)
			out = append(out, element)
			next := parentRef
			if element.Ref != "" {
				next = element.Ref
			}
			visit(node.Children, depth+1, next)
		}
	}
	visit(snapshot.AXSnapshot.CanonicalRoots, 0, "")
	return out
}

func recordElementFromNode(node *AXNode, depth int, parentRef string) RecordElement {
	element := RecordElement{
		Ref:         node.Ref,
		Role:        string(node.Role),
		Name:        node.Name,
		Value:       node.Properties.Value,
		Description: node.Properties.Description,
		Placeholder: node.Properties.Placeholder,
		States:      recordStates(node.States),
		Actions:     append([]string(nil), node.Capabilities.Actions...),
		Depth:       depth,
		Parent:      parentRef,
	}
	if record := node.SourceRecord; record != nil && record.Frame != nil {
		element.Bounds = []float64{record.Frame.X, record.Frame.Y, record.Frame.Width, record.Frame.Height}
	}
	return element
}

func recordStates(states AXStates) map[string]string {
	out := map[string]string{}
	for _, token := range renderAXStateTokens(states) {
		key, value, _ := strings.Cut(token, "=")
		out[key] = value
	}
	if len(out) == 0 {
		return nil
	}
	return out
}

// recordSnapshotEntry is one snapshot returned in records format.
type recordSnapshotEntry struct {
	id       string
	window   string
	elements []RecordElement
}

const maxRecordSnapshots = 16

func (s *Service) rememberRecordSnapshot(snapshot *AppSnapshot, elements []RecordElement) string {
	s.nextRecordSnapshot++
	id := fmt.Sprintf("s%d", s.nextRecordSnapshot)
	s.recordSnapshots = append(s.recordSnapshots, recordSnapshotEntry{
		id: id, window: snapshotLogicalWindowRef(snapshot), elements: elements,
	})
	if len(s.recordSnapshots) > maxRecordSnapshots {
		s.recordSnapshots = s.recordSnapshots[len(s.recordSnapshots)-maxRecordSnapshots:]
	}
	return id
}

func (s *Service) recordSnapshot(id string) *recordSnapshotEntry {
	for index := range s.recordSnapshots {
		if s.recordSnapshots[index].id == id {
			return &s.recordSnapshots[index]
		}
	}
	return nil
}

// diffRecordElements compares two element lists by ref. Elements without a
// ref cannot be matched across snapshots and are left out of the diff.
func diffRecordElements(since string, before, after []RecordElement) *RecordDiff {
	diff := &RecordDiff{Since: since}
	previous := map[string]RecordElement{}
	for _, element := range before {
		if element.Ref != "" {
			previous[element.Ref] = element
		}
	}
	seen := map[string]bool{}
	for _, element := range after {
		if element.Ref == "" {
			continue
		}
		seen[element.Ref] = true
		old, existed := previous[element.Ref]
		switch {
		case !existed:
			diff.Added = append(diff.Added, element)
		case !sameRecordElement(old, element):
			diff.Changed = append(diff.Changed, element)
		}
	}
	for _, element := range before {
		if element.Ref != "" && !seen[element.Ref] {
			diff.Removed = append(diff.Removed, element)
		}
	}
	diff.Unchanged = len(diff.Added)+len(diff.Removed)+len(diff.Changed) == 0
	return diff
}

func sameRecordElement(first, second RecordElement) bool {
	a, _ := json.Marshal(first)
	b, _ := json.Marshal(second)
	return string(a) == string(b)
}

// findRecords renders find as records. The first page and later cursor
// pages use the same frozen match set as the text contract; the page size
// is the only budget, never the model text budget.
func (s *Service) findRecords(args map[string]any) (Record, bool) {
	request, rejected, bad := s.rejectInvalidCall(contract.PublicToolAX, args)
	if bad {
		return errorRecord(contract.PublicToolAX, rejected), true
	}
	var snapshot *AppSnapshot
	var matched []ElementRecord
	offset := 0
	setID := ""
	var notes []string
	if request.cursor != "" {
		resolution, err := s.FindCursorRegistry.Resolve(request.cursor)
		if err != nil {
			// The same failure the text contract reports for this cursor.
			return errorRecord(contract.PublicToolAX, WithAXModeEvidence(TextResult(
				"AX find cursor: "+err.Error(), true,
			).withEvidenceScope("error_only"), contract.AXModeFind)), true
		}
		snapshot = resolution.ResultSet.Snapshot
		matched = resolution.ResultSet.Matches
		offset = resolution.Cursor.Offset
		setID = resolution.Cursor.ResultSetID
		if resume := findResumeOf(resolution); resume != nil {
			found, failure := s.resumedFindMatches(resume)
			if found == nil {
				return errorRecord(contract.PublicToolAX, failure.withEvidenceScope("error_only")), true
			}
			snapshot, matched, notes = found.snapshot, found.matched, found.notes
			offset, setID = 0, ""
			if len(matched) > 0 {
				snapshot = s.freezeFindRefs(snapshot, matched)
			}
		}
	} else {
		readContext, identityErr := s.resolveAXReadIdentity(request.app, request.windowRef, "")
		if identityErr != nil {
			return errorRecord(contract.PublicToolAX, axIdentityErrorResult(identityErr)), true
		}
		query := request.query
		name := OptionalString(query, "name_contains")
		_, nameExact := query["name_equals"]
		if nameExact {
			name = OptionalString(query, "name_equals")
		}
		text := OptionalString(query, "text_contains")
		_, textExact := query["text_equals"]
		if textExact {
			text = OptionalString(query, "text_equals")
		}
		found, failure := s.findMatches(readContext.appToken, OptionalString(query, "role"),
			name, text, OptionalString(query, "state"), nameExact, textExact,
			request.scope, readContext.windowHandle)
		if found == nil {
			return errorRecord(contract.PublicToolAX, failure.withEvidenceScope("error_only")), true
		}
		snapshot, matched, notes = found.snapshot, found.matched, found.notes
		if len(matched) > 0 {
			snapshot = s.freezeFindRefs(snapshot, matched)
		}
	}
	record := Record{OK: true, Notes: modelFacingAXNotes(notes)}
	fillRecordIdentity(&record, snapshot)
	end := offset + request.limit
	if end > len(matched) {
		end = len(matched)
	}
	record.Matches = []RecordElement{}
	for _, item := range matched[offset:end] {
		node := snapshot.AXSnapshot.ByIndex[item.Index]
		if node == nil {
			continue
		}
		parentRef := ""
		for parent := node.Parent; parent != nil; parent = parent.Parent {
			if parent.Ref != "" {
				parentRef = parent.Ref
				break
			}
		}
		element := recordElementFromNode(node, 0, parentRef)
		if element.Bounds == nil && item.Frame != nil {
			element.Bounds = []float64{item.Frame.X, item.Frame.Y, item.Frame.Width, item.Frame.Height}
		}
		record.Matches = append(record.Matches, element)
	}
	record.Page = &RecordPage{Returned: end - offset, Total: len(matched), Offset: offset}
	if end < len(matched) || snapshot.findResume != nil {
		if setID == "" {
			record.Page.NextCursor = s.FindCursorRegistry.Create(snapshot, matched, end, "")
		} else {
			record.Page.NextCursor = s.FindCursorRegistry.Continue(setID, end)
		}
	}
	// Records describe the same provider read as text pages. Rendering a
	// complete page of matches does not establish complete acquisition.
	evidence := TextResult("", false)
	addTreeProjectionMetadata(&evidence, snapshot)
	evidence.StructuredContent["ax_mode"] = contract.AXModeFind
	evidence.StructuredContent["find_delivery_complete"] = true
	evidence.StructuredContent["find_match_count"] = len(matched)
	if record.Page.NextCursor != "" {
		evidence.StructuredContent["next_cursor"] = record.Page.NextCursor
	}
	record.Evidence = recordFromEvidence(contract.PublicToolAX, PrepareAXEvidence(evidence)).Evidence
	return record, true
}

// readRecords reads the current value and states of each ref without
// rebuilding a tree: one live resolve per ref. A ref that no longer
// resolves reports stale_ref; it is never rebound.
func (s *Service) readRecords(args map[string]any) Record {
	record := Record{OK: true, Op: "ax.read"}
	refs := []string{}
	raw, _ := args["refs"].([]any)
	for _, item := range raw {
		ref, _ := item.(string)
		refs = append(refs, ref)
	}
	if len(args) != 2 || len(raw) == 0 || len(raw) > maxReadRefs {
		return Record{
			OK: false, Op: "ax.read", Code: contract.AXCodeInvalidArgument, Path: "refs",
			Message: fmt.Sprintf("read takes only refs: 1 to %d element refs", maxReadRefs),
			Hint:    `{"mode":"read","refs":["r1","r2"]}`,
		}
	}
	for _, ref := range refs {
		record.Read = append(record.Read, s.readOneRef(ref))
	}
	return record
}

const maxReadRefs = 200

func (s *Service) readOneRef(ref string) RecordRead {
	if !AXElementRefPattern.MatchString(ref) {
		return RecordRead{Ref: ref, Code: contract.AXCodeInvalidArgument, Message: "read takes element refs rN"}
	}
	entry, err := s.resolveShortAXRefOwned(ref)
	if err != nil {
		return RecordRead{Ref: ref, Code: contract.AXCodeStaleRef, Message: err.Error()}
	}
	target, err := semanticTargetFromRefEntry(entry)
	if err != nil {
		return RecordRead{Ref: ref, Code: contract.AXCodeStaleRef, Message: err.Error()}
	}
	app := FirstNonEmpty(entry.AppRef, entry.AppIdentity)
	s.authorizeProviderRebind(app, entry, target)
	operation := AXServiceRequest{
		Action:             axResolveTargetCommand{},
		App:                app,
		TargetWindowHandle: target.WindowHandle,
		SemanticTarget:     target,
		SourceRef:          ref,
	}
	if err := s.bindRuntimeAppIdentity(&operation); err != nil {
		return RecordRead{Ref: ref, Code: contract.AXCodeStaleRef, Message: err.Error()}
	}
	response, err := s.callProviderAction(providerActionRequestFromOperation(operation))
	if err != nil {
		return RecordRead{Ref: ref, Code: contract.AXCodeRuntimeError, Message: err.Error()}
	}
	if response == nil || !response.OK || response.ResolvedElement == nil {
		code := contract.AXCodeRuntimeError
		message := "the element could not be read"
		if response != nil {
			message = FirstNonEmpty(publicRuntimeFailureText(*response), response.Error, message)
			switch response.FailureCode {
			case "semantic_target_not_found", "stale_element_ref", "semantic_target_ambiguous",
				"stale_window_ref", "stale_app_ref":
				code = contract.AXCodeStaleRef
			}
		}
		return RecordRead{Ref: ref, Code: code, Message: firstLine(message)}
	}
	element := recordElementFromProvider(ref, response.ResolvedElement)
	return RecordRead{Ref: ref, OK: true, Element: &element}
}

func recordElementFromProvider(ref string, source *ElementRecord) RecordElement {
	descriptor := canonicalRoleDescriptor(ontologyProviderRole(source), nil)
	element := RecordElement{
		Ref:         ref,
		Role:        string(descriptor.ModelRole),
		Name:        normalizedTreeText(source.Name),
		Value:       normalizedTreeText(source.Value),
		Description: normalizedTreeText(source.Description),
		Placeholder: normalizedTreeText(source.Placeholder),
		States:      recordStates(typedAXStates(source)),
		Actions:     append([]string(nil), typedAXCapabilities(source, descriptor.ModelRole).Actions...),
	}
	if source.Frame != nil {
		element.Bounds = []float64{source.Frame.X, source.Frame.Y, source.Frame.Width, source.Frame.Height}
	}
	return element
}

// SortedErrorCodes is used by schema and tests.
func SortedErrorCodes() []string {
	codes := make([]string, 0, len(contract.PublicErrorCodes))
	for code := range contract.PublicErrorCodes {
		codes = append(codes, code)
	}
	sort.Strings(codes)
	return codes
}
