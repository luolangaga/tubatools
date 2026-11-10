package core

import (
	"fmt"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestContentScopeHasIndependentCacheIdentityAndDoesNotPersist(t *testing.T) {
	window := &AppSnapshot{
		WindowHandle:    "0x1",
		publicWindowRef: "w1",
		SnapshotID:      "same",
		App:             AppDescriptor{Name: "App", PID: 7},
		projection: snapshotProjection{
			View:  contract.TreeViewWorking,
			Scope: contract.AXScopeWindow,
		},
	}
	content := *window
	content.projection.Scope = contract.AXScopeContent
	if deliveredTreeKey(window) == deliveredTreeKey(&content) ||
		DeliveredSnapshotKey(window) == DeliveredSnapshotKey(&content) {
		t.Fatal("window and content projections must not share delivery caches")
	}

	request := AXServiceRequest{Scope: contract.AXScopeContent}
	applyProjection(&request, snapshotProjection{
		View:  contract.TreeViewFull,
		Scope: contract.AXScopeWindow,
	})
	if request.Scope != contract.AXScopeContent {
		t.Fatal("a cached projection must not overwrite request-local content scope")
	}
}

func TestContentRevisionDrivesContentTreeDeliveryIdentity(t *testing.T) {
	first := &AppSnapshot{
		WindowHandle:    "0x1",
		publicWindowRef: "w1",
		SnapshotID:      "full-a",
		contentRevision: "c1:0123456789abcdef01234567",
		projection: snapshotProjection{
			View:  contract.TreeViewWorking,
			Scope: contract.AXScopeContent,
		},
	}
	second := *first
	second.SnapshotID = "full-b"
	if deliveredTreeKey(first) != deliveredTreeKey(&second) {
		t.Fatal("browser chrome-only snapshot changes must not resend identical content")
	}
	second.contentRevision = "c1:89abcdef0123456701234567"
	if deliveredTreeKey(first) == deliveredTreeKey(&second) {
		t.Fatal("changed scoped content must have a new delivery identity")
	}
}

func TestModelVisibleContinuityDetectsRootChangeAcrossHiddenSamples(t *testing.T) {
	service := NewService()
	visible := &AppSnapshot{
		App:             AppDescriptor{Name: "Editor", PID: 7},
		WindowTitle:     "Document",
		WindowHandle:    "pid7/win0/frame",
		publicWindowRef: "w1",
		SnapshotID:      "visible-a",
		sourceApp:       "editor",
		projection:      snapshotProjection{View: contract.TreeViewWorking},
	}
	service.rememberDeliveredSnapshot(visible)

	hidden := &AppSnapshot{
		App:             AppDescriptor{Name: "Editor", PID: 7},
		WindowTitle:     "Document",
		WindowHandle:    "pid7/win1/frame",
		publicWindowRef: "w2",
		SnapshotID:      "hidden-b",
		projection:      snapshotProjection{View: contract.TreeViewWorking},
	}
	service.rememberSnapshot("editor", hidden)

	current := *hidden
	baseline := service.deliveredSnapshotFor(&current)
	if baseline != nil {
		t.Fatalf(
			"a different logical window must not reuse another window's continuity baseline: %#v",
			baseline,
		)
	}
}

func TestGetAppStateSuppressesOnlyIdenticalRepeatedTrees(t *testing.T) {
	service := NewService()
	snapshot := &AppSnapshot{
		WindowTitle:         "Doc",
		WindowHandle:        "pid7/win0/frame",
		publicWindowRef:     "w1",
		SnapshotID:          "snapshot-a",
		TreeLines:           []string{"0 frame Doc", "1 paragraph Body"},
		comparedToPrevious:  true,
		identicalToPrevious: true,
	}
	first := service.getAppStateResult(snapshot, nil, false)
	if text := first.Content[0].Text; !strings.Contains(text, "1 paragraph Body") {
		t.Fatalf("a tree not yet delivered in full must be returned even when unchanged: %q", text)
	}

	compact := service.getAppStateResult(snapshot, nil, false)
	if text := compact.Content[0].Text; !strings.Contains(text, "Tree unchanged") ||
		strings.Contains(text, "1 paragraph Body") {
		t.Fatalf("default reads should reference an already-delivered identical tree: %q", text)
	}
	if compact.StructuredContent["tree_reference_only"] != true ||
		compact.StructuredContent["full_tree_returned"] != false {
		t.Fatalf("compact tree reference lacks structured delivery facts: %#v",
			compact.StructuredContent)
	}

	forced := service.getAppStateResult(snapshot, nil, true)
	if text := forced.Content[0].Text; !strings.Contains(text, "1 paragraph Body") {
		t.Fatalf("force_full=true must preserve its complete-tree contract: %q", text)
	}

	fullView := *snapshot
	fullView.projection.View = contract.TreeViewFull
	raw := service.getAppStateResult(&fullView, nil, false)
	if text := raw.Content[0].Text; !strings.Contains(text, "1 paragraph Body") {
		t.Fatalf("a different serializer view must not reuse the working-view ledger: %q", text)
	}

	changed := *snapshot
	changed.SnapshotID = "snapshot-b"
	changed.TreeLines = []string{"0 frame Doc", "1 paragraph New body"}
	fresh := service.getAppStateResult(&changed, nil, false)
	if text := fresh.Content[0].Text; !strings.Contains(text, "1 paragraph New body") {
		t.Fatalf("a changed snapshot must return the new full evidence: %q", text)
	}
}

func TestDeliveredTreeLedgerIsBounded(t *testing.T) {
	service := NewService()
	for index := 0; index <= contract.MaxDeliveredTrees; index++ {
		service.rememberDeliveredTree(fmt.Sprintf("snapshot-%d", index))
	}
	if len(service.observations.lastFullTree) > contract.MaxDeliveredTrees {
		t.Fatalf(
			"delivered tree ledger grew without bound: %d",
			len(service.observations.lastFullTree),
		)
	}
}

func TestIncrementalBaselineIgnoresUndeliveredSnapshots(t *testing.T) {
	service := NewService()
	delivered := &AppSnapshot{
		App:             AppDescriptor{Name: "Editor", PID: 7},
		WindowHandle:    "pid7/win0/frame",
		publicWindowRef: "w1",
		TreeLines:       []string{"\t0 frame \"Editor\""},
	}
	for index := 1; index < 20; index++ {
		delivered.TreeLines = append(
			delivered.TreeLines,
			fmt.Sprintf("\t\t%d paragraph \"Stable %d\"", index, index),
		)
	}
	delivered.TreeLines[10] = "\t\t10 paragraph \"Delivered\""
	hidden := *delivered
	hidden.TreeLines = append([]string(nil), delivered.TreeLines...)
	hidden.TreeLines[10] = "\t\t10 paragraph \"Hidden by find\""
	after := hidden
	after.TreeLines = append([]string(nil), hidden.TreeLines...)
	after.TreeLines[10] = "\t\t10 paragraph \"After action\""

	service.rememberDeliveredSnapshot(delivered)
	service.rememberSnapshot("Editor", &hidden)

	baseline := service.deliveredSnapshotFor(&after)
	if baseline != delivered {
		t.Fatalf("undelivered cache state replaced the model-visible baseline: %#v", baseline)
	}
	diff, ok := incrementalTree(baseline, &after)
	if !ok {
		t.Fatal("expected a compact diff against the delivered baseline")
	}
	text := strings.Join(diff, "\n")
	if !strings.Contains(text, "Delivered") ||
		!strings.Contains(text, "After action") ||
		strings.Contains(text, "Hidden by find") {
		t.Fatalf("diff used an undelivered baseline:\n%s", text)
	}
}

func TestHiddenCanonicalAddressChangesAreReported(t *testing.T) {
	service := NewService()
	before := &AppSnapshot{
		App:             AppDescriptor{Name: "Editor", PID: 7},
		WindowHandle:    "pid7/win0/frame",
		publicWindowRef: "w1",
		TreeLines:       []string{"\t0 frame \"Editor\""},
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Editor"},
			{Index: 7, RuntimeID: []int{0, 1}, ControlType: "section", Name: "Old"},
		},
	}
	after := &AppSnapshot{
		App:             AppDescriptor{Name: "Editor", PID: 7},
		WindowHandle:    "pid7/win0/frame",
		publicWindowRef: "w1",
		TreeLines:       []string{"\t0 frame \"Editor\""},
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Editor"},
			{Index: 9, RuntimeID: []int{0, 1}, ControlType: "section", Name: "New"},
		},
	}
	service.rememberExposedHiddenRef(before, before.Elements[1])

	note := service.hiddenCanonicalAddressChangeNote(before, after)

	for _, fragment := range []string{
		"[canonical-address]",
		"1 serializer-hidden ref(s) previously returned by find",
		"call find again",
	} {
		if !strings.Contains(note, fragment) {
			t.Fatalf("hidden canonical address change note missing %q: %q", fragment, note)
		}
	}
}

func TestDeliveredTreeLedgerRequiresActualInlineDelivery(t *testing.T) {
	snapshot := &AppSnapshot{
		WindowHandle:        "pid7/win0/frame",
		SnapshotID:          "large",
		TreeLines:           []string{"0 frame Doc", strings.Repeat("x", 500)},
		comparedToPrevious:  true,
		identicalToPrevious: true,
	}

	t.Run("inline truncation", func(t *testing.T) {
		t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "120")
		service := NewService()
		first := service.getAppStateResult(snapshot, nil, false)
		if first.StructuredContent["tree_delivery_incomplete"] != true ||
			first.StructuredContent["full_tree_returned"] != false {
			t.Fatalf("truncated tree was falsely marked delivered: %#v", first.StructuredContent)
		}
		second := service.getAppStateResult(snapshot, nil, false)
		if second.StructuredContent["tree_reference_only"] == true {
			t.Fatalf("an incompletely delivered tree must not become a compact reference")
		}
	})

	t.Run("file output", func(t *testing.T) {
		t.Setenv("COMPUTER_USE_OUTPUT_MODE", "file")
		t.Setenv("COMPUTER_USE_OUTPUT_THRESHOLD", "1")
		t.Setenv("COMPUTER_USE_OUTPUT_DIR", t.TempDir())
		service := NewService()
		result := service.getAppStateResult(snapshot, nil, false)
		if result.StructuredContent["tree_delivery_incomplete"] != true ||
			len(service.observations.lastFullTree) != 0 {
			t.Fatalf("file-only tree was falsely marked delivered: result=%#v ledger=%#v",
				result.StructuredContent, service.observations.lastFullTree)
		}
	})
}

func TestIncompleteSnapshotDoesNotReplaceCompleteDiffBaseline(t *testing.T) {
	service := NewService()
	complete := &AppSnapshot{
		sourceApp:       "editor",
		WindowHandle:    "window-1",
		publicWindowRef: "w1",
		projection:      snapshotProjection{View: contract.TreeViewWorking, Scope: contract.AXScopeWindow},
	}
	incomplete := *complete
	incomplete.Annotations = []axProviderAnnotation{{
		Kind: "provider_omission", Count: 1, Reason: "provider_probe_error",
	}}
	service.rememberDeliveredSnapshot(complete)
	service.rememberDeliveredSnapshot(&incomplete)
	if got := service.deliveredSnapshotFor(complete); got != complete {
		t.Fatalf("incomplete snapshot replaced complete diff baseline: got %#v want %#v",
			got, complete)
	}
}

func TestIncompletePresentedSnapshotDoesNotDriveContinuity(t *testing.T) {
	service := NewService()
	complete := &AppSnapshot{
		App:             AppDescriptor{Name: "Editor", PID: 7},
		publicWindowRef: "w1",
		SnapshotID:      "complete-a",
		sourceApp:       "editor",
		projection:      snapshotProjection{View: contract.TreeViewWorking},
	}
	partial := *complete
	partial.SnapshotID = "partial-b"
	partial.Annotations = []axProviderAnnotation{{
		Kind: "provider_omission", Count: 1, Reason: "provider_probe_error",
	}}
	service.rememberDeliveredSnapshot(complete)
	service.rememberDeliveredSnapshot(&partial)

	current := *complete
	baseline := service.deliveredSnapshotFor(&current)
	if baseline != complete {
		t.Fatalf(
			"partial observation replaced the last complete continuity baseline: got %#v want %#v",
			baseline,
			complete,
		)
	}
	notes := continuityNotes(baseline, &current)
	if len(notes) != 1 || !current.identicalToPrevious {
		t.Fatalf(
			"complete A -> partial B -> complete A must compare against A: notes=%v current=%#v",
			notes,
			current,
		)
	}
}

func TestSnapshotCachesRequireLogicalWindowRef(t *testing.T) {
	first := &AppSnapshot{
		App:        AppDescriptor{Name: "Editor", PID: 7},
		SnapshotID: "first",
	}
	second := &AppSnapshot{
		App:        AppDescriptor{Name: "Editor", PID: 7},
		SnapshotID: "second",
	}
	if DeliveredSnapshotKey(first) != "" || DeliveredSnapshotKey(second) != "" {
		t.Fatal("app name and PID must not substitute for logical window identity")
	}
	service := NewService()
	service.rememberDeliveredSnapshot(first)
	if got := service.deliveredSnapshotFor(second); got != nil {
		t.Fatalf("snapshots without wN must not share a diff baseline: %#v", got)
	}
}

func TestShortRefAcceptsTheSamePublicAppAliasThatProducedIt(t *testing.T) {
	service := NewService()
	snapshot := &AppSnapshot{
		App: AppDescriptor{Name: "soffice"},
		AXSnapshot: &AXSnapshot{
			AppIdentity: "soffice",
		},
	}
	service.rememberSnapshot("libreoffice", snapshot)
	ref := service.RefRegistry.Acquire(
		"soffice",
		"window-1",
		`s2:window-bound-target`,
		"button",
		"Save",
		"revision-1",
	)
	expanded, err := service.expandShortAXRef("libreoffice", ref)
	if err != nil {
		t.Fatalf("same public app alias rejected its own ref: %v", err)
	}
	if expanded != `s2:window-bound-target` {
		t.Fatalf("expanded ref=%q", expanded)
	}
}

func TestCompactActionObservationDoesNotAdvanceDeliveredTreeBaseline(t *testing.T) {
	service := NewService()
	previous := editorFormSnapshot()
	previous.sourceApp = "editor"
	previous.publicWindowRef = "w1"
	previous.SnapshotID = "previous"
	previous.projection = snapshotProjection{View: contract.TreeViewWorking, Scope: contract.AXScopeWindow}
	service.rememberDeliveredSnapshot(previous)

	current := editorFormSnapshot()
	current.sourceApp = "editor"
	current.publicWindowRef = "w1"
	current.SnapshotID = "current"
	current.projection = snapshotProjection{View: contract.TreeViewWorking, Scope: contract.AXScopeWindow}
	current.TreeLines = append([]string(nil), previous.TreeLines...)
	current.TreeLines[1] = strings.Repeat("changed ", 900)
	compact := compactActionObservationLines(current)
	response := current.resultWithTreeOverride(nil, compact)
	if advanced := service.rememberDeliveredActionObservation(
		current,
		response,
		compact,
		true,
		false,
		true,
	); advanced {
		t.Fatal("focused compact context must not advance the complete delivered-tree baseline")
	}
	if got := service.deliveredSnapshotFor(current); got != previous {
		t.Fatalf("compact context replaced the model-visible baseline: got %#v want %#v", got, previous)
	}
}

func TestContentAndRepeatedTreeOutputKeepRevisionPrivate(t *testing.T) {
	snapshot := editorFormSnapshot()
	snapshot.contentRevision = "c1:deadbeef"
	snapshot.Scope = contract.AXScopeContent
	snapshot.scopeRoot = &ElementRecord{
		ControlType: "document web",
		Name:        "Example",
	}
	rendered := snapshot.renderedText()
	repeated := strings.Join(treeUnchangedReferenceLines(len(snapshot.TreeLines)), "\n")
	for _, output := range []string{rendered, repeated} {
		for _, private := range []string{"c1:deadbeef", "revision:"} {
			if strings.Contains(output, private) {
				t.Fatalf("model output leaked %q:\n%s", private, output)
			}
		}
	}
	if !strings.Contains(repeated, "Tree unchanged:") {
		t.Fatalf("repeated-tree decision fact was lost: %q", repeated)
	}
}

func TestAssertionShortRefDoesNotDoubleEncodeScopedTarget(t *testing.T) {
	scoped := encodeScopedSemanticRef(`entry "Name"`, "w2:window")
	entry := &AXRefEntry{
		SemanticTarget: scoped,
		WindowHandle:   "w2:window",
	}
	if got := assertionTargetFromRefEntry(entry); got != scoped {
		t.Fatalf("scoped target was double encoded: %q", got)
	}
	raw := &AXRefEntry{
		SemanticTarget: `entry "Name"`,
		WindowHandle:   "w2:window",
	}
	got := assertionTargetFromRefEntry(raw)
	selector, window, err := decodeSemanticRefScope(got)
	if err != nil || selector != `entry "Name"` || window != "w2:window" {
		t.Fatalf(
			"raw selector was not scoped once: selector=%q window=%q err=%v",
			selector, window, err,
		)
	}
}

func TestTransientCommitProbeReturnsReplacementWindow(t *testing.T) {
	oldHandle := "w2:" + strings.Repeat("a", 32)
	newHandle := "w2:" + strings.Repeat("b", 32)
	dialog := windowTransitionSnapshot(oldHandle, "Open File", true)
	document := windowTransitionSnapshot(newHandle, "Document", true)
	for index := range dialog.Elements {
		dialog.Elements[index].ProviderWindowRole = "dialog"
	}
	for index := range document.Elements {
		document.Elements[index].ProviderWindowRole = "frame"
		document.Elements[index].ProviderWindowObjectPath = "/document"
		document.Elements[index].ProviderWindowName = "Document"
	}
	actionEvidence := &RuntimeActionEvidence{Route: "keyboard"}
	dialog.actionExecution = actionEvidence

	service := NewServiceWithBackend(&recordingPlatformBackend{})
	if err := service.BuildAndRenderAXSnapshot(
		dialog,
		contract.TreeViewWorking,
	); err != nil {
		t.Fatal(err)
	}
	sourceWindowRef := snapshotLogicalWindowRef(dialog)
	appRef := dialog.publicAppRef
	if !isShortAXWindowRef(sourceWindowRef) || !isShortAXAppRef(appRef) {
		t.Fatalf("fixture refs missing: app=%q window=%q",
			appRef, sourceWindowRef)
	}
	backend := &recordingPlatformBackend{
		accessibilityResponses: []ProviderAccessibilityResult{
			{
				OK:    false,
				Error: "stale_window_ref: source dialog is gone",
				Failure: &ProviderFailure{
					Code:          "stale_window_ref",
					Phase:         "precondition",
					FactsComplete: true,
					Subject:       "window",
				},
				RecoveryContext: &ProviderRecoveryContext{
					SourceAppStatus:    "alive",
					SourceWindowStatus: "gone",
					InventoryComplete:  true,
					CurrentWindow: &ProviderReplacementWindow{
						WindowHandle:       newHandle,
						Title:              "Document",
						Role:               "frame",
						ProviderBusName:    ":1.42",
						ProviderObjectPath: "/document",
						ProcessGeneration:  "pid:42:start:100",
					},
				},
			},
			{OK: true, Snapshot: document},
		},
	}
	service.Backend = backend
	service.rememberSnapshot("Editor", dialog)

	got, notes, attempted, stabilized :=
		service.stabilizePostActionTransientCommit(
			"Editor",
			dialog,
			AXServiceRequest{
				Action:  axKeypressCommand{Key: "ENTER"},
				Observe: string(contract.ObserveDiff),
			},
		)
	if !attempted || !stabilized {
		t.Fatalf("transient transition status=%t/%t", attempted, stabilized)
	}
	if got == dialog ||
		got.WindowHandle != newHandle ||
		got.actionExecution != actionEvidence {
		t.Fatalf("replacement window was not returned: %#v", got)
	}
	if len(notes) == 0 ||
		!strings.Contains(notes[0], "bounded no-input probe") {
		t.Fatalf("transition note=%#v", notes)
	}
	if len(backend.observeRequests) != 2 {
		t.Fatalf("transition reads=%d, want source proof plus final read",
			len(backend.observeRequests))
	}
}

func TestRootedAndWindowObservationsUseDifferentCaches(t *testing.T) {
	window := editorFormSnapshot()
	window.publicWindowRef = "w1"
	window.projection = snapshotProjection{
		View: contract.TreeViewWorking, Scope: contract.AXScopeWindow,
	}
	rooted := *window
	rooted.projection.Root = `{"focused":true}`
	if deliveredTreeKey(window) == deliveredTreeKey(&rooted) ||
		DeliveredSnapshotKey(window) == DeliveredSnapshotKey(&rooted) {
		t.Fatal("rooted and whole-window observations share delivery caches")
	}
}

func TestEffectUnknownReturnsFreshWindowWithoutClaimingSuccess(t *testing.T) {
	service, _, oldWindowRef, elementRef := sheetBatchService(t)
	newHandle := "w2:fedcba9876543210fedcba9876543210"
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
			SourceWindowStatus: "gone",
			InventoryComplete:  true,
			CurrentWindow: &ProviderReplacementWindow{
				WindowHandle:       newHandle,
				Title:              "Document Recovery",
				Role:               "dialog",
				ProviderBusName:    ":1.84",
				ProviderObjectPath: "/window/recovery",
				ProcessGeneration:  "pid:10:start:100",
			},
		},
	})
	if !result.IsError ||
		result.StructuredContent["failure_code"] != "effect_unknown" ||
		result.StructuredContent["mutation_unknown"] != true {
		t.Fatalf("effect-unknown honesty was lost: %#v", result)
	}
	currentRef, _ := result.StructuredContent["current_window_ref"].(string)
	if !isShortAXWindowRef(currentRef) || currentRef == oldWindowRef {
		t.Fatalf("fresh recovery window ref=%q old=%q", currentRef, oldWindowRef)
	}
	if _, err := service.WindowRefRegistry.Resolve(oldWindowRef, ""); err == nil {
		t.Fatal("confirmed-gone source window remained active")
	}
	if _, err := service.RefRegistry.Resolve(elementRef, ""); err == nil {
		t.Fatal("element owned by confirmed-gone window remained active")
	}
	continuation := result.StructuredContent["continuation"].(map[string]any)
	if continuation["next_mode"] != "find" ||
		continuation["fresh_window_ref"] != currentRef {
		t.Fatalf("recovery continuation=%#v", continuation)
	}
}
