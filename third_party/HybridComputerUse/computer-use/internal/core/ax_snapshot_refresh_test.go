package core

import (
	"strings"
	"testing"
	"time"
)

func TestActionResultFlagsUnchangedState(t *testing.T) {
	base := func() *AppSnapshot {
		return &AppSnapshot{
			WindowTitle:    "Doc",
			FocusedSummary: "a text field",
			SelectedText:   "",
			TreeLines:      []string{"0 frame Doc", "1 text hello"},
		}
	}
	if observablyChanged(base(), base()) {
		t.Fatal("identical snapshots must count as unchanged")
	}
	changedTree := base()
	changedTree.TreeLines = []string{"0 frame Doc", "1 text hello world"}
	if !observablyChanged(base(), changedTree) {
		t.Fatal("a different tree must count as changed")
	}
	shorterTree := base()
	shorterTree.TreeLines = []string{"0 frame Doc"}
	if !observablyChanged(base(), shorterTree) {
		t.Fatal("a tree with fewer lines must count as changed")
	}
	changedTitle := base()
	changedTitle.WindowTitle = "Doc *"
	if !observablyChanged(base(), changedTitle) {
		t.Fatal("a different window title must count as changed")
	}
	changedSelection := base()
	changedSelection.SelectedText = "hello"
	if !observablyChanged(base(), changedSelection) {
		t.Fatal("a different selection must count as changed")
	}
	changedFocus := base()
	changedFocus.FocusedSummary = "a button"
	if !observablyChanged(base(), changedFocus) {
		t.Fatal("a different focused element must count as changed")
	}
	// Screenshots are excluded: a blinking caret would make every action look
	// like a change and destroy the signal.
	sameButNewScreenshot := base()
	sameButNewScreenshot.ScreenshotPNGBase64 = "different-pixels"
	if observablyChanged(base(), sameButNewScreenshot) {
		t.Fatal("screenshot noise must not count as an observable change")
	}
}

func TestIdenticalSnapshotSeparatesStillBusyFromTrimmedAway(t *testing.T) {
	previous := &AppSnapshot{
		WindowHandle: "pid7/win0/frame",
		SnapshotID:   "same",
		capturedAt:   time.Now().Add(-2 * time.Second),
	}
	// The app reports itself busy, so the note may tell the agent to wait.
	busy := &AppSnapshot{
		WindowHandle: "pid7/win0/frame",
		SnapshotID:   "same",
		Busy:         &busySignal{ProgressElements: 1},
	}
	notes := continuityNotes(previous, busy)
	if len(notes) != 1 || !strings.Contains(notes[0], "APP REPORTS BUSY") {
		t.Fatalf("a busy app must be told apart from a trimmed tree, got %v", notes)
	}
	if !busy.identicalToPrevious || !busy.comparedToPrevious {
		t.Fatal("the equality verdict must reach the rendered snapshot")
	}

	// Without busy evidence the note may only say the outcome is unknown, never
	// that the action failed: saving a document, for example, takes effect
	// without changing the tree.
	quiet := &AppSnapshot{
		WindowHandle: "pid7/win0/frame",
		SnapshotID:   "same",
	}
	notes = continuityNotes(previous, quiet)
	if len(notes) != 1 || !strings.Contains(notes[0], "not proof your last action failed") {
		t.Fatalf("an unchanged tree is weak evidence, never proof, got %v", notes)
	}
	if !strings.Contains(notes[0], "an AX wait step") ||
		strings.Contains(notes[0], "`verify`") {
		t.Fatalf("continuity guidance leaked the hidden legacy assertion tool: %v", notes)
	}

	changed := &AppSnapshot{WindowHandle: "pid7/win0/frame", SnapshotID: "other"}
	if notes := continuityNotes(previous, changed); len(notes) != 0 {
		t.Fatalf("a changed snapshot needs no note, got %v", notes)
	}
	if !changed.comparedToPrevious || changed.identicalToPrevious {
		t.Fatal("a changed snapshot must still record that a comparison happened")
	}
}

func TestRepeatedDiagnosticsBecomeCompactEdgeUpdates(t *testing.T) {
	service := NewService()
	full := "MODAL DIALOG: the tree below is \"Save As\", a modal dialog."

	first := service.dedupeDiagnosticNotes("Editor", []string{full})
	if len(first) != 1 || first[0] != full {
		t.Fatalf("first diagnostic occurrence must keep the full explanation: %v", first)
	}
	second := service.dedupeDiagnosticNotes("Editor", []string{full})
	if len(second) != 1 ||
		!strings.Contains(second[0], "[diagnostic modal_dialog] still present") ||
		len(second[0]) >= len(full)+80 {
		t.Fatalf("repeated diagnostic should be a compact edge update: %v", second)
	}
	service.dedupeDiagnosticNotes("Editor", nil)
	third := service.dedupeDiagnosticNotes("Editor", []string{full})
	if len(third) != 1 || third[0] != full {
		t.Fatalf("a diagnostic that disappeared and returned must be explained again: %v", third)
	}

	unchangedA := "SNAPSHOT UNCHANGED: same tree. 1.0s apart."
	unchangedB := "SNAPSHOT UNCHANGED: same tree. 3.0s apart."
	service.dedupeDiagnosticNotes("Editor", []string{unchangedA})
	repeated := service.dedupeDiagnosticNotes("Editor", []string{unchangedB})
	if len(repeated) != 1 || !strings.Contains(repeated[0], "diagnostic snapshot_unchanged") {
		t.Fatalf("volatile elapsed time must not defeat unchanged-diagnostic deduplication: %v", repeated)
	}
}

func TestUnexposedHiddenCanonicalChangesStaySilent(t *testing.T) {
	service := NewService()
	before := &AppSnapshot{
		App:          AppDescriptor{Name: "Editor", PID: 7},
		WindowHandle: "pid7/win0/frame",
		TreeLines:    []string{"\t0 frame \"Editor\""},
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Editor"},
			{Index: 7, RuntimeID: []int{0, 1}, ControlType: "section", Name: "Old"},
		},
	}
	after := &AppSnapshot{
		App:          AppDescriptor{Name: "Editor", PID: 7},
		WindowHandle: "pid7/win0/frame",
		TreeLines:    []string{"\t0 frame \"Editor\""},
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Editor"},
			{Index: 9, RuntimeID: []int{0, 1}, ControlType: "section", Name: "New"},
		},
	}

	if note := service.hiddenCanonicalAddressChangeNote(before, after); note != "" {
		t.Fatalf("unexposed serializer churn must stay out of model context: %q", note)
	}
}

func TestRootWindowSwitchAnnouncesRenumbering(t *testing.T) {
	// A second window of the same app with the same title can silently switch
	// the snapshot root: every element index shifts while the returned text
	// looks unchanged. The identical title is the case this check exists for.
	previous := &AppSnapshot{WindowHandle: "pid7/win0/frame", WindowTitle: "Home", SnapshotID: "a"}
	current := &AppSnapshot{WindowHandle: "pid7/win1/frame", WindowTitle: "Home", SnapshotID: "b"}

	notes := continuityNotes(previous, current)

	if len(notes) != 1 || !strings.Contains(notes[0], "ROOT WINDOW CHANGED") {
		t.Fatalf("a root window switch must be announced, got %v", notes)
	}
	if !strings.Contains(notes[0], "previous window are stale") {
		t.Fatal("the note must say refs owned by the previous window are stale")
	}
}

func TestRuntimeFailureFactsRemainStructured(t *testing.T) {
	result := runtimeReportedFailure(ProviderAccessibilityResult{
		OK:                   false,
		Error:                "stale",
		FailureCode:          "stale_element_ref",
		FailurePhase:         "precondition",
		InputEventsEmitted:   false,
		PartialInputPossible: false,
		FailureFactsComplete: true,
	})
	if result.StructuredContent["failure_code"] != "stale_element_ref" ||
		result.StructuredContent["failure_phase"] != "precondition" ||
		result.StructuredContent["input_events_emitted"] != false ||
		result.StructuredContent["partial_input_possible"] != false ||
		result.StructuredContent["mutation_unknown"] != false ||
		result.StructuredContent["failure_facts_complete"] != true {
		t.Fatalf("runtime failure facts were lost: %#v", result.StructuredContent)
	}
}

func TestIncompleteCurrentSnapshotCannotClaimUnchanged(t *testing.T) {
	previous := &AppSnapshot{
		publicWindowRef: "w1",
		SnapshotID:      "same",
	}
	current := &AppSnapshot{
		publicWindowRef: "w1",
		SnapshotID:      "same",
		Annotations: []axProviderAnnotation{{
			Kind: "provider_omission", Count: 1, Reason: "visible_cell_budget",
		}},
	}

	if notes := continuityNotes(previous, current); len(notes) != 0 {
		t.Fatalf("an incomplete current read cannot produce continuity claims: %v", notes)
	}
	if current.comparedToPrevious || current.identicalToPrevious {
		t.Fatalf(
			"an incomplete current read cannot be marked compared or unchanged: %#v",
			current,
		)
	}
}

func TestMissingPostActionSnapshotPreservesMutationEvidence(t *testing.T) {
	backend := &recordingPlatformBackend{
		accessibilityResponse: &ProviderAccessibilityResult{
			OK: true,
			Mutation: &ProviderMutationFacts{
				InputEventsEmitted:  true,
				DesktopStateMutated: true,
			},
		},
	}
	service := NewServiceWithBackend(backend)
	_, _, result := service.readSnapshot("Editor", AXServiceRequest{
		Action:    AXClickCommand{},
		App:       "Editor",
		SourceRef: "r17",
	})
	if !result.IsError {
		t.Fatalf("missing post-action snapshot must be an error: %#v", result)
	}
	if result.StructuredContent["failure_code"] != "effect_unknown" ||
		result.StructuredContent["underlying_failure_code"] !=
			"missing_post_action_snapshot" ||
		result.StructuredContent["input_events_emitted"] != true ||
		result.StructuredContent["mutation_unknown"] != true {
		t.Fatalf("post-action mutation evidence was lost: %#v", result.StructuredContent)
	}
}

func TestMissingPostActionSnapshotPromotesActionEvidence(t *testing.T) {
	backend := &recordingPlatformBackend{
		accessibilityResponse: &ProviderAccessibilityResult{
			OK:             true,
			ActionEvidence: &ProviderActionEvidence{InputEmitted: true},
		},
	}
	service := NewServiceWithBackend(backend)
	_, _, result := service.readSnapshot("Editor", AXServiceRequest{
		Action:    AXClickCommand{},
		App:       "Editor",
		SourceRef: "r17",
	})
	if !result.IsError ||
		result.StructuredContent["failure_code"] != "effect_unknown" ||
		result.StructuredContent["input_events_emitted"] != true ||
		result.StructuredContent["mutation_unknown"] != true {
		t.Fatalf("action evidence was not promoted into mutation facts: %#v", result)
	}
}

func TestEffectUnknownFailurePromotesActionEvidence(t *testing.T) {
	service := NewService()
	result := service.runtimeReportedFailure(
		"Editor",
		AXServiceRequest{Action: AXClickCommand{}, SourceRef: "r17"},
		ProviderAccessibilityResult{
			OK: false,
			Failure: &ProviderFailure{
				Code:  "effect_unknown",
				Phase: "post_action_observation",
			},
			ActionEvidence: &ProviderActionEvidence{InputEmitted: true},
		},
	)
	if result.StructuredContent["input_events_emitted"] != true ||
		result.StructuredContent["mutation_unknown"] != true {
		t.Fatalf("effect_unknown failure lost action evidence: %#v", result)
	}
}

func TestRuntimeTimeoutSeparatesReadAndActionUncertainty(t *testing.T) {
	service := NewService()
	snapshot := &AppSnapshot{App: AppDescriptor{PID: 42}}
	timeout := &RuntimeTimeoutError{
		Duration: 30 * time.Second,
		Phase:    "read_response",
	}

	read := service.RuntimeFailureResult(
		AXServiceRequest{Operation: axServiceObserve}, snapshot, timeout)
	readText := read.Content[0].Text
	if !strings.Contains(readText, "runtime_timeout=true mutation_unknown=false") ||
		!strings.Contains(readText, "a11y_responsive=false") {
		t.Fatalf("pure tree read timeout should identify A11Y non-response: %s", readText)
	}
	if read.StructuredContent["failure_code"] != "runtime_timeout" ||
		read.StructuredContent["failure_phase"] != "read_response" ||
		read.StructuredContent["mutation_unknown"] != false {
		t.Fatalf("read timeout evidence is incomplete: %#v", read.StructuredContent)
	}

	action := service.RuntimeFailureResult(
		AXServiceRequest{Action: AXClickCommand{}}, snapshot, timeout)
	actionText := action.Content[0].Text
	if !strings.Contains(actionText, "runtime_timeout=true mutation_unknown=true") ||
		!strings.Contains(actionText, "a11y_responsive=unknown") {
		t.Fatalf("action timeout must not blame A11Y or imply no mutation: %s", actionText)
	}
	if action.StructuredContent["failure_code"] != "runtime_timeout" ||
		action.StructuredContent["failure_phase"] != "read_response" ||
		action.StructuredContent["mutation_unknown"] != true ||
		action.StructuredContent["retry_same_request"] != false {
		t.Fatalf("action timeout evidence is incomplete: %#v", action.StructuredContent)
	}

	old := &AppSnapshot{capturedAt: time.Now().Add(-time.Second)}
	newest := &AppSnapshot{capturedAt: time.Now()}
	service.snapshots["old"] = old
	service.snapshots["new"] = newest
	if service.latestSnapshot() != newest {
		t.Fatal("foreground timeout should use the most recent cached snapshot identity")
	}

	source := mainGoSource(t)
	for _, marker := range []string{
		"cmd.Env = environment",
		"linuxX11WindowForPID(pid, backend.RuntimeEnvironment(nil))",
	} {
		if !strings.Contains(source, marker) {
			t.Fatalf("wmctrl timeout probe must use the backend desktop environment: missing %q", marker)
		}
	}
}

func TestVerifyPreservesProjectionWithoutReplacingActionCache(t *testing.T) {
	prune := false
	boxes := true
	cached := &AppSnapshot{
		App: AppDescriptor{Name: "Editor", PID: 42},
		projection: snapshotProjection{
			TextLimit: "max", MaxTreeNodes: 321, MaxTreeDepth: 17,
			Prune: &prune, Boxes: &boxes,
		},
	}
	service := NewService()
	service.rememberSnapshot("Editor", cached)

	request := AXServiceRequest{Operation: axServiceObserve, App: "Editor"}
	applyProjection(&request, service.currentSnapshot("Editor").projection)
	if request.TextLimit != "max" || request.MaxTreeNodes != 321 ||
		request.MaxTreeDepth != 17 || request.Prune == nil || *request.Prune ||
		request.Boxes == nil || !*request.Boxes {
		t.Fatalf("read-only query lost the cached projection: %#v", request)
	}
	if service.currentSnapshot("Editor") != cached {
		t.Fatal("building a read-only request must not replace the action cache")
	}

	source := mainGoSource(t)
	for _, marker := range []string{
		"primary, _, result := s.readProjectedSnapshot(app, request)",
	} {
		if !strings.Contains(source, marker) {
			t.Fatalf("verify projection contract missing %q", marker)
		}
	}
}

func TestMutatingAXRequestIsNeverReplayedAfterWindowChange(t *testing.T) {
	previous := expectSnapshot()
	previous.WindowHandle = "w2:" + strings.Repeat("a", 32)
	previous.Refs = map[string]elementRef{"r1": {Index: 1}}
	current := expectSnapshot()
	current.WindowHandle = "w2:" + strings.Repeat("b", 32)
	backend := &recordingPlatformBackend{
		accessibilityResponse: &ProviderAccessibilityResult{
			OK:       true,
			Snapshot: current,
			Mutation: &ProviderMutationFacts{InputEventsEmitted: true},
		},
	}
	service := NewServiceWithBackend(backend)
	service.rememberSnapshot("Editor", previous)
	_, _, result := service.readSnapshot("Editor", AXServiceRequest{
		Action:    AXClickCommand{},
		App:       "Editor",
		SourceRef: "r1",
	})
	if result.IsError {
		t.Fatalf("mutating request failed: %s", ResultText(result))
	}
	if len(backend.calls) != 1 || backend.calls[0] != "resolve_and_act" {
		t.Fatalf("mutating request was replayed: %#v", backend.calls)
	}
}

func TestTransportFailurePreservesActionUncertainty(t *testing.T) {
	service := NewService()
	result := service.RuntimeFailureResult(
		AXServiceRequest{Action: AXClickCommand{}},
		nil,
		&RuntimeTransportError{
			Operation:   ProviderWireResolveAndAct,
			Phase:       "decode_response",
			RequestSent: true,
		},
	)
	if result.StructuredContent["failure_code"] != "runtime_transport_failure" ||
		result.StructuredContent["failure_phase"] != "decode_response" ||
		result.StructuredContent["mutation_unknown"] != true {
		t.Fatalf("transport uncertainty was lost: %#v", result.StructuredContent)
	}
}

func TestSnapshotCacheLookupIsAsLooseAsAppResolution(t *testing.T) {
	// Snapshots are cached under the resolved name ("Google Chrome"), while the
	// agent's next action may say "chrome". If cache lookup were stricter than
	// app resolution, an agent that has just observed the app would be told
	// that no app state is available, the most confusing answer possible.
	// Cache lookup must therefore be exactly as loose as app resolution.
	service := NewService()
	service.rememberSnapshot("", &AppSnapshot{App: AppDescriptor{Name: "Google Chrome"}})
	for _, spelling := range []string{"chrome", "Chrome", "google-chrome", "google chrome"} {
		if service.currentSnapshot(spelling) == nil {
			t.Fatalf("%q should find the cached Google Chrome snapshot", spelling)
		}
	}
	// It must not be so loose that it matches another app, which would send
	// actions to the wrong target.
	if service.currentSnapshot("thunderbird") != nil {
		t.Fatal("loose matching must not match an unrelated app name")
	}
}

func TestSnapshotCacheLooseLookupRequiresOneUniqueAddressSpace(t *testing.T) {
	service := NewService()
	first := &AppSnapshot{App: AppDescriptor{Name: "Code"}}
	second := &AppSnapshot{App: AppDescriptor{Name: "Code Helper"}}
	service.snapshots["visual studio code"] = first
	service.snapshots["code helper"] = second
	if got := service.currentSnapshot("code"); got != nil {
		t.Fatalf("ambiguous loose app alias must fail closed, got %#v", got)
	}
}

func TestRememberSnapshotRemovesAliasesToTheReplacedAddressSpace(t *testing.T) {
	for _, query := range []string{"chrome", ""} {
		service := NewService()
		old := &AppSnapshot{App: AppDescriptor{Name: "Google Chrome", PID: 41}}
		service.snapshots["chrome"] = old
		service.snapshots["google chrome"] = old
		service.snapshots["old window title"] = old
		service.snapshots["41"] = old

		fresh := &AppSnapshot{App: AppDescriptor{Name: "Google Chrome", PID: 41}}
		service.rememberSnapshot(query, fresh)
		if got := service.currentSnapshot("old window title"); got != nil {
			t.Fatalf("query %q left an alias to the replaced snapshot: %#v", query, got)
		}
		for _, alias := range []string{"chrome", "google chrome", "41"} {
			if got := service.currentSnapshot(alias); got != fresh {
				t.Fatalf("query %q fresh alias %q resolved to %#v, want fresh snapshot",
					query, alias, got)
			}
		}
	}
}

func TestObservationRootProviderBoundRefDoesNotFallBackToSemanticText(t *testing.T) {
	service := NewService()
	service.RefRegistry.Entries["r1"] = &AXRefEntry{
		Ref: "r1", LocatorKind: AXLocatorProviderBound,
		Lifecycle: AXRefActive, AppIdentity: "Editor",
		WindowHandle:       "pid7/win0/frame",
		SemanticTarget:     `button "Repeated"`,
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/button/1",
		ProcessGeneration:  "pid:7:start:10",
	}
	root, err := service.expandObservationRootShortRef(
		"Editor",
		map[string]any{"ref": "r1"},
	)
	if err != nil {
		t.Fatal(err)
	}
	if root["_resolved_ref_entry"] == nil || root["ref"] != nil {
		t.Fatalf("provider-bound root fell back to semantic selector: %#v", root)
	}
}

func TestReplacementWindowContinuationRequiresSameLineage(t *testing.T) {
	source := &AXRefEntry{
		WindowHandle:                    "w2:native-1",
		ProviderWindowBusName:           ":1.20",
		ProviderWindowProcessGeneration: "pid-100-start-5",
		ProviderWindowRole:              "frame",
	}
	same := &ProviderReplacementWindow{
		WindowHandle:       "w2:native-1",
		ProviderBusName:    ":1.20",
		ProcessGeneration:  "pid-100-start-5",
		Role:               "window",
		ProviderObjectPath: "/new/provider/path",
	}
	if !replacementWindowContinuesSource(source, same) {
		t.Fatal("same native window lineage should permit find-only continuation")
	}
	for name, changed := range map[string]*ProviderReplacementWindow{
		"native window": {
			WindowHandle:      "w2:native-2",
			ProviderBusName:   ":1.20",
			ProcessGeneration: "pid-100-start-5",
			Role:              "window",
		},
		"provider bus": {
			WindowHandle:      "w2:native-1",
			ProviderBusName:   ":1.21",
			ProcessGeneration: "pid-100-start-5",
			Role:              "window",
		},
		"process generation": {
			WindowHandle:      "w2:native-1",
			ProviderBusName:   ":1.20",
			ProcessGeneration: "pid-100-start-6",
			Role:              "window",
		},
		"role family": {
			WindowHandle:      "w2:native-1",
			ProviderBusName:   ":1.20",
			ProcessGeneration: "pid-100-start-5",
			Role:              "dialog",
		},
	} {
		if replacementWindowContinuesSource(source, changed) {
			t.Fatalf("%s change must not be advertised as ready continuation", name)
		}
	}
}

func TestSemanticStaleMarksRefForRevalidation(t *testing.T) {
	service, _, _, elementRef := sheetBatchService(t)
	service.runtimeReportedFailure("", AXServiceRequest{
		SourceRef: elementRef,
		SemanticTarget: &semanticTarget{
			LocatorKind: AXLocatorSemantic,
		},
	}, ProviderAccessibilityResult{
		FailureCode:          "stale_element_ref",
		FailureSubject:       "element",
		FailureFactsComplete: true,
		Error:                "provider object changed",
	})
	entry := service.RefRegistry.Entries[elementRef]
	if entry == nil || entry.Lifecycle != AXRefNeedsRevalidation {
		t.Fatalf("semantic stale lifecycle=%#v", entry)
	}
}

func TestElementIncompleteDoesNotInvalidateOwnerRefs(t *testing.T) {
	service, appRef, windowRef, elementRef := sheetBatchService(t)
	service.runtimeReportedFailure("", AXServiceRequest{
		App:       appRef,
		SourceRef: elementRef,
	}, ProviderAccessibilityResult{
		FailureCode:          "observation_incomplete",
		FailureSubject:       "element",
		FailureFactsComplete: false,
		Error:                "temporary element scan failure",
	})
	if _, err := service.AppRefRegistry.Resolve(appRef); err != nil {
		t.Fatalf("element failure invalidated app ref: %v", err)
	}
	if _, err := service.WindowRefRegistry.Resolve(windowRef, ""); err != nil {
		t.Fatalf("element failure invalidated window ref: %v", err)
	}
	if entry := service.RefRegistry.Entries[elementRef]; entry == nil ||
		entry.Lifecycle != AXRefNeedsRevalidation {
		t.Fatalf("element ref lifecycle=%#v", entry)
	}
}

func TestWindowIncompleteDoesNotInvalidateAppOrElement(t *testing.T) {
	service, appRef, windowRef, elementRef := sheetBatchService(t)
	service.runtimeReportedFailure("", AXServiceRequest{
		App:       appRef,
		SourceRef: windowRef,
	}, ProviderAccessibilityResult{
		FailureCode:          "observation_incomplete",
		FailureSubject:       "window",
		FailureFactsComplete: false,
		Error:                "temporary window inventory failure",
	})
	if _, err := service.AppRefRegistry.Resolve(appRef); err != nil {
		t.Fatalf("window failure invalidated app ref: %v", err)
	}
	if entry := service.WindowRefRegistry.entries[windowRef]; entry == nil ||
		entry.Lifecycle != AXRefNeedsRevalidation {
		t.Fatalf("window ref lifecycle=%#v", entry)
	}
	if _, err := service.RefRegistry.Resolve(elementRef, ""); err != nil {
		t.Fatalf("window failure invalidated unrelated element ref: %v", err)
	}
}

func TestStaleElementDoesNotAdvertiseDifferentWindowAsReady(t *testing.T) {
	service, _, oldWindowRef, elementRef := sheetBatchService(t)
	newHandle := "w2:fedcba9876543210fedcba9876543210"
	result := service.runtimeReportedFailure("", AXServiceRequest{
		SourceRef: elementRef,
		SemanticTarget: &semanticTarget{
			LocatorKind: AXLocatorProviderBound,
		},
	}, ProviderAccessibilityResult{
		FailureCode:          "stale_element_ref",
		FailureSubject:       "element",
		FailureFactsComplete: true,
		Error:                "provider window disappeared",
		ReplacementWindow: &ProviderReplacementWindow{
			WindowHandle:       newHandle,
			Title:              "Question",
			Role:               "dialog",
			ProviderBusName:    ":1.84",
			ProviderObjectPath: "/dialog/2",
			ProcessGeneration:  "pid:10:start:100",
		},
	})
	replacement, _ := result.StructuredContent["replacement_window_ref"].(string)
	if replacement != "" {
		t.Fatalf("different window lineage was advertised as replacement=%q", replacement)
	}
	if oldWindowRef == "" {
		t.Fatal("fixture did not create the original window ref")
	}
}

func TestStaleElementReturnsSameLineageWindowForFindOnlyRecovery(t *testing.T) {
	service, _, oldWindowRef, elementRef := sheetBatchService(t)
	source := service.RefRegistry.Entries[elementRef]
	result := service.runtimeReportedFailure("", AXServiceRequest{
		SourceRef: elementRef,
		SemanticTarget: &semanticTarget{
			LocatorKind: AXLocatorProviderBound,
		},
	}, ProviderAccessibilityResult{
		FailureCode:          "stale_element_ref",
		FailureSubject:       "element",
		FailureFactsComplete: true,
		Error:                "provider object changed",
		ReplacementWindow: &ProviderReplacementWindow{
			WindowHandle:       source.WindowHandle,
			Title:              "Sheet",
			Role:               "window",
			ProviderBusName:    source.ProviderWindowBusName,
			ProviderObjectPath: "/window/rebuilt",
			ProcessGeneration:  source.ProviderWindowProcessGeneration,
		},
	})
	replacement, _ := result.StructuredContent["replacement_window_ref"].(string)
	if replacement != oldWindowRef {
		t.Fatalf(
			"same-lineage replacement=%q want=%q source=%#v result=%#v",
			replacement, oldWindowRef, source, result.StructuredContent,
		)
	}
	if result.StructuredContent["recovery"] != "find" {
		t.Fatalf("recovery=%#v", result.StructuredContent["recovery"])
	}
}

func TestEffectUnknownAppExitTombstonesOwnedIdentityGraph(t *testing.T) {
	service, appRef, windowRef, elementRef := sheetBatchService(t)
	result := service.runtimeReportedFailure("", AXServiceRequest{
		SourceRef: elementRef,
	}, ProviderAccessibilityResult{
		FailureCode:          "effect_unknown",
		FailurePhase:         "post_action_observation",
		InputEventsEmitted:   true,
		MutationUnknown:      true,
		FailureFactsComplete: false,
		Error:                "post_action_observation_failed",
		Recovery: &ProviderRecoveryContext{
			SourceAppStatus:    "exited",
			SourceWindowStatus: "gone",
			InventoryComplete:  true,
		},
	})
	if result.StructuredContent["recovery"] != "observe_apps" {
		t.Fatalf("app-exit recovery=%#v", result.StructuredContent)
	}
	if _, err := service.AppRefRegistry.Resolve(appRef); err == nil {
		t.Fatal("confirmed-exited app remained active")
	}
	if _, err := service.WindowRefRegistry.Resolve(windowRef, ""); err == nil {
		t.Fatal("window owned by confirmed-exited app remained active")
	}
	if _, err := service.RefRegistry.Resolve(elementRef, ""); err == nil {
		t.Fatal("element owned by confirmed-exited app remained active")
	}
}

func TestIncompleteRecoveryDoesNotTombstoneIdentityGraph(t *testing.T) {
	service, appRef, windowRef, elementRef := sheetBatchService(t)
	result := service.runtimeReportedFailure("", AXServiceRequest{
		SourceRef: elementRef,
	}, ProviderAccessibilityResult{
		FailureCode:          "effect_unknown",
		FailurePhase:         "post_action_observation",
		InputEventsEmitted:   true,
		MutationUnknown:      true,
		FailureFactsComplete: false,
		Error:                "post_action_observation_failed",
		Recovery: &ProviderRecoveryContext{
			SourceAppStatus:    "alive",
			SourceWindowStatus: "unknown",
			InventoryComplete:  false,
		},
	})
	if result.StructuredContent["recovery_inventory_complete"] != false {
		t.Fatalf("incomplete recovery=%#v", result.StructuredContent)
	}
	if _, err := service.AppRefRegistry.Resolve(appRef); err != nil {
		t.Fatalf("incomplete inventory invalidated app: %v", err)
	}
	if _, err := service.WindowRefRegistry.Resolve(windowRef, ""); err != nil {
		t.Fatalf("incomplete inventory invalidated window: %v", err)
	}
	if _, err := service.RefRegistry.Resolve(elementRef, ""); err != nil {
		t.Fatalf("incomplete inventory invalidated element: %v", err)
	}
	if _, exists := result.StructuredContent["current_window_ref"]; exists {
		t.Fatalf("incomplete inventory advertised current window: %#v", result)
	}
}

func TestIncompleteStaleWindowRefRequiresRevalidation(t *testing.T) {
	service, _, windowRef, _ := sheetBatchService(t)
	result := service.runtimeReportedFailure("", AXServiceRequest{
		SourceRef: windowRef,
	}, ProviderAccessibilityResult{
		FailureCode:          "stale_window_ref",
		FailureSubject:       "window",
		FailureFactsComplete: false,
		Error:                "provider could not resolve the source window",
		Recovery: &ProviderRecoveryContext{
			SourceAppStatus:    "alive",
			SourceWindowStatus: "unknown",
			InventoryComplete:  false,
		},
	})
	if !result.IsError ||
		result.StructuredContent["failure_code"] != "stale_window_ref" {
		t.Fatalf("stale window failure evidence was lost: %#v", result)
	}
	if _, err := service.WindowRefRegistry.Resolve(windowRef, ""); err == nil ||
		!strings.Contains(err.Error(), string(AXRefNeedsRevalidation)) {
		t.Fatalf("stale window ref remained active after incomplete failure: %v", err)
	}
}

func TestCompleteProviderAbsenceTombstonesRef(t *testing.T) {
	service, _, _, elementRef := sheetBatchService(t)
	entry := service.RefRegistry.Entries[elementRef]
	entry.LocatorKind = AXLocatorProviderBound
	service.runtimeReportedFailure("", AXServiceRequest{
		SourceRef: elementRef,
		SemanticTarget: &semanticTarget{
			LocatorKind: AXLocatorProviderBound,
		},
	}, ProviderAccessibilityResult{
		FailureCode:          "stale_element_ref",
		FailureFactsComplete: true,
		Error:                "target absent after a complete scan",
	})
	if entry.Lifecycle != AXRefStale {
		t.Fatalf("complete absence lifecycle=%s", entry.Lifecycle)
	}
	if _, err := service.RefRegistry.Resolve(elementRef, ""); err == nil {
		t.Fatal("tombstoned ref resolved")
	}
}

func TestAppTombstoneCascadesToOwnedWindowsAndElements(t *testing.T) {
	service, appRef, windowRef, elementRef := sheetBatchService(t)
	service.markAppIdentityLifecycle(appRef, AXRefStale, "process_restarted")
	if _, err := service.AppRefRegistry.Resolve(appRef); err == nil {
		t.Fatal("stale app ref resolved")
	}
	if _, err := service.WindowRefRegistry.Resolve(windowRef, ""); err == nil {
		t.Fatal("owned window survived stale app")
	}
	if _, err := service.RefRegistry.Resolve(elementRef, ""); err == nil {
		t.Fatal("owned element survived stale app")
	}
}

func TestAmbiguityCandidateUsesItsOwnWindowRef(t *testing.T) {
	service, appRef, oldWindowRef, _ := sheetBatchService(t)
	oldHandle := "w2:0123456789abcdef0123456789abcdef"
	newHandle := "w2:fedcba9876543210fedcba9876543210"
	service.snapshots[appRef] = &AppSnapshot{
		App: AppDescriptor{
			Name:              "LibreOffice",
			ProviderBusName:   ":1.84",
			ProcessGeneration: "pid:10:start:100",
		},
		WindowHandle:    oldHandle,
		publicAppRef:    appRef,
		publicWindowRef: oldWindowRef,
		SnapshotID:      "c1",
	}
	result := service.runtimeReportedFailure(appRef, AXServiceRequest{}, ProviderAccessibilityResult{
		Error:          "semantic_target_ambiguous",
		FailureCode:    "semantic_target_ambiguous",
		CandidateCount: 1,
		Candidates: []ProviderTargetCandidate{{
			Role:               "push button",
			Name:               "OK",
			Path:               `dialog "Question" > button "OK"`,
			WindowHandle:       newHandle,
			ProviderBusName:    ":1.84",
			ProviderObjectPath: "/dialog/ok",
			ProcessGeneration:  "pid:10:start:100",
		}},
	})
	candidates := result.StructuredContent["candidates"].([]map[string]any)
	gotWindow := candidates[0]["window_ref"].(string)
	if gotWindow == oldWindowRef {
		t.Fatalf("new-window candidate inherited old window ref %s", oldWindowRef)
	}
	entry, err := service.WindowRefRegistry.Resolve(gotWindow, "")
	if err != nil || entry.WindowHandle != newHandle {
		t.Fatalf("candidate window binding=%#v err=%v", entry, err)
	}
}

func TestProviderIdentityDetailsStayOutOfPublicFailureText(t *testing.T) {
	response := ProviderAccessibilityResult{
		Error: "provider_identity_ambiguous path='/org/a11y/atspi/accessible/77' " +
			"bus=:1.84 pid=4242 process_generation=pid:4242:start:9 " +
			"window=w2:0123456789abcdef0123456789abcdef",
	}
	text := publicRuntimeFailureText(response)
	if strings.Contains(text, "/org/a11y/") ||
		strings.Contains(text, ":1.84") ||
		strings.Contains(text, "4242") ||
		strings.Contains(text, "w2:") ||
		strings.Contains(text, "provider_identity_ambiguous") {
		t.Fatalf("provider identity leaked: %q", text)
	}
}
