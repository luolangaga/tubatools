package core

import (
	"reflect"
	"slices"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestShortAXRefExpandsToWindowScopedSemanticRef(t *testing.T) {
	registry := NewRefRegistry(8)
	semantic := encodeScopedSemanticRef(
		`push button "Save"`,
		"pid145/win0/frame",
	)
	ref := registry.Acquire(
		"editor", "pid145/win0/frame", semantic,
		"button", "Save", "rev1",
	)
	if ref != "r1" {
		t.Fatalf("first registry ref = %q, want r1", ref)
	}
	entry, err := registry.Resolve(ref, "editor")
	if err != nil {
		t.Fatal(err)
	}
	if !strings.HasPrefix(entry.SemanticTarget, scopedSemanticRefPrefix) {
		t.Fatalf("short ref did not expand to a window-scoped semantic ref: %q", entry.SemanticTarget)
	}
	target, err := semanticTargetFromAddress(entry.SemanticTarget)
	if err != nil {
		t.Fatal(err)
	}
	if target.WindowHandle != "pid145/win0/frame" || target.Name != "Save" {
		t.Fatalf("expanded target lost identity: %#v", target)
	}
	if again := registry.Acquire(
		"editor", "pid145/win0/frame", semantic,
		"button", "Save", "rev2",
	); again != ref {
		t.Fatalf("same identity was reissued as %q instead of %q", again, ref)
	}
}

func TestIncompleteSnapshotCannotAuthorizeProviderRebind(t *testing.T) {
	service := NewService()
	windowHandle := "w2:" + strings.Repeat("a", 32)
	ref := service.RefRegistry.AcquireTyped(
		AXLocatorSemantic,
		"editor",
		windowHandle,
		`button "Save"`,
		"button",
		"Save",
		"c1",
		":1.42", "/old/save", "99123",
		":1.42", "/window", "99123",
		"frame", "Editor", "a1", "w1",
	)
	snapshot := &AppSnapshot{
		App:             AppDescriptor{Name: "Editor"},
		WindowHandle:    windowHandle,
		publicWindowRef: "w1",
		Annotations: []axProviderAnnotation{{
			Kind: "omitted", Reason: "provider_timeout",
		}},
		AXSnapshot: &AXSnapshot{
			Revision:     "c1",
			WindowHandle: windowHandle,
			WindowRef:    "w1",
		},
	}
	service.rememberSnapshot("editor", snapshot)
	resolution, result := service.resolveActionElement(
		"editor", ref, "", "click",
	)
	if result.IsError {
		t.Fatalf("semantic ref was rejected too early: %s", ResultText(result))
	}
	if resolution.semantic == nil || resolution.semantic.AllowProviderRebind {
		t.Fatalf("incomplete snapshot authorized provider rebind: %#v",
			resolution.semantic)
	}
}

func TestProviderBoundIdentitySemanticMutationSupersedesOldRef(t *testing.T) {
	registry := NewRefRegistry(16)
	first := registry.AcquireTyped(
		AXLocatorProviderBound,
		"editor", "window-1", `button "Save"`, "button", "Save", "c1",
		":1.42", "/org/a11y/atspi/accessible/77", "99123",
		":1.42", "/org/a11y/atspi/accessible/window/1", "99123",
		"", "", "", "w1",
	)
	renamed := registry.AcquireTyped(
		AXLocatorProviderBound,
		"editor", "window-9", `toggle button "Saved"`, "button", "Saved", "c2",
		":1.42", "/org/a11y/atspi/accessible/77", "99123",
		":1.42", "/org/a11y/atspi/accessible/window/1", "99123",
		"", "", "", "w1",
	)
	if first == "" || renamed == first {
		t.Fatalf("semantic mutation reused provider-bound ref: %q -> %q", first, renamed)
	}
	if _, err := registry.Resolve(first, "editor"); err == nil ||
		!strings.Contains(err.Error(), "superseded") {
		t.Fatalf("old semantic identity did not tombstone: %v", err)
	}
	entry, err := registry.Resolve(renamed, "editor")
	if err != nil {
		t.Fatal(err)
	}
	if entry.SemanticTarget != `toggle button "Saved"` || entry.Name != "Saved" {
		t.Fatalf("new semantic identity was not recorded: %#v", entry)
	}
	restarted := registry.AcquireTyped(
		AXLocatorProviderBound,
		"editor", "window-1", `toggle button "Saved"`, "button", "Saved", "c3",
		":1.99", "/org/a11y/atspi/accessible/77", "100001",
		":1.99", "/org/a11y/atspi/accessible/window/1", "100001",
		"", "", "", "w1",
	)
	if restarted == renamed {
		t.Fatal("process generation and bus change must allocate another ref")
	}
}

func TestStaleRefErrorDoesNotExposeInternalReason(t *testing.T) {
	registry := NewRefRegistry(16)
	ref := registry.AcquireTyped(
		AXLocatorProviderBound,
		"editor", "window-1", `button "Save"`, "button", "Save", "c1",
		":1.42", "/org/a11y/atspi/accessible/77", "99123",
		":1.42", "/org/a11y/atspi/accessible/window/1", "99123",
		"", "", "", "w1",
	)
	privateReason :=
		`provider :1.42 object /org/a11y/atspi/accessible/77 disappeared`
	registry.MarkStale(ref, privateReason)
	_, err := registry.Resolve(ref, "editor")
	if err == nil {
		t.Fatal("stale ref unexpectedly resolved")
	}
	if strings.Contains(err.Error(), ":1.42") ||
		strings.Contains(err.Error(), "/org/a11y/") ||
		strings.Contains(err.Error(), privateReason) {
		t.Fatalf("stale ref exposed internal identity: %v", err)
	}
	if !strings.Contains(err.Error(), "observe or find") {
		t.Fatalf("stale ref lost actionable recovery: %v", err)
	}
}

func TestProviderBoundRefDoesNotReviveAfterTombstone(t *testing.T) {
	registry := NewRefRegistry(16)
	provider := []string{
		":1.42", "/org/a11y/atspi/accessible/77", "99123",
		":1.42", "/org/a11y/atspi/accessible/window/1", "99123",
		"dialog", "Preferences", "a1", "w1",
	}
	first := registry.AcquireTyped(
		AXLocatorProviderBound,
		"editor",
		"w2:"+strings.Repeat("a", 32),
		`button "Save"`,
		"button",
		"Save",
		"c1",
		provider...,
	)
	if first == "" {
		t.Fatal("provider-bound ref was not issued")
	}
	registry.MarkStale(first, "provider_identity_disappeared")
	if _, err := registry.Resolve(first, "editor"); err == nil ||
		!strings.Contains(err.Error(), "stale") {
		t.Fatalf("tombstoned ref resolved: %v", err)
	}
	second := registry.AcquireTyped(
		AXLocatorProviderBound,
		"editor",
		"w2:"+strings.Repeat("a", 32),
		`button "Save"`,
		"button",
		"Save",
		"c2",
		provider...,
	)
	if second == first {
		t.Fatalf("reused provider identity revived stale ref %q", first)
	}
}

func TestProviderBoundShortRefCarriesLocatorKind(t *testing.T) {
	service := NewService()
	ref := service.RefRegistry.AcquireTyped(
		AXLocatorProviderBound,
		"editor",
		"w2:"+strings.Repeat("a", 32),
		`menu "Format"`,
		"menu",
		"Format",
		"c1",
		":1.42", "/format/1", "99123",
		":1.42", "/window/1", "99123",
		"frame", "Editor", "a1", "w1",
	)
	resolution, result := service.resolveActionElement(
		"editor", ref, "", "click",
	)
	if result.IsError {
		t.Fatalf("provider-bound ref rejected: %s", ResultText(result))
	}
	if resolution.semantic == nil ||
		resolution.semantic.LocatorKind != AXLocatorProviderBound {
		t.Fatalf("locator kind was lost: %#v", resolution.semantic)
	}
	if resolution.sourceRef != ref {
		t.Fatalf("source ref=%q, want %q", resolution.sourceRef, ref)
	}
}

func TestProviderBoundShortRefCarriesPrivateRuntimePaths(t *testing.T) {
	service := NewService()
	ref := service.RefRegistry.AcquireTyped(
		AXLocatorProviderBound,
		"editor",
		"w2:"+strings.Repeat("a", 32),
		`menu "Format"`,
		"menu",
		"Format",
		"c1",
		":1.42", "/format/1", "99123",
		":1.42", "/window/1", "99123",
		"frame", "Editor", "a1", "w1",
	)
	service.RefRegistry.BindRuntimePaths(
		ref,
		[]int{2, 4, 6},
		[][]int{{2, 4, 6}, {2, 7, 1}},
	)
	resolution, result := service.resolveActionElement(
		"editor", ref, "", "click",
	)
	if result.IsError {
		t.Fatalf("provider-bound ref rejected: %s", ResultText(result))
	}
	if resolution.semantic == nil ||
		!slices.Equal(resolution.semantic.RuntimeID, []int{2, 4, 6}) ||
		len(resolution.semantic.RuntimeIDAliases) != 2 ||
		!slices.Contains(resolution.semantic.ProviderRoleAliases, "menu") ||
		!slices.Equal(
			resolution.semantic.RuntimeIDAliases[1],
			[]int{2, 7, 1},
		) {
		t.Fatalf("runtime paths were lost: %#v", resolution.semantic)
	}
}

func TestCurrentRevisionSemanticRefAuthorizesOneProviderRebind(t *testing.T) {
	service := NewService()
	windowHandle := "w2:" + strings.Repeat("a", 32)
	ref := service.RefRegistry.AcquireTyped(
		AXLocatorSemantic,
		"editor",
		windowHandle,
		`button "Save"`,
		"button",
		"Save",
		"c1",
		":1.42", "/old/save", "99123",
		":1.42", "/window", "99123",
		"frame", "Editor", "a1", "w1",
	)
	service.RefRegistry.BindRuntimePaths(ref, []int{0, 0}, nil)
	snapshot := &AppSnapshot{
		App:             AppDescriptor{Name: "Editor"},
		WindowHandle:    windowHandle,
		publicWindowRef: "w1",
		AXSnapshot: &AXSnapshot{
			Revision:     "c1",
			WindowHandle: windowHandle,
			WindowRef:    "w1",
		},
	}
	service.rememberSnapshot("editor", snapshot)

	resolution, result := service.resolveActionElement(
		"editor", ref, "", "click",
	)
	if result.IsError {
		t.Fatalf("current semantic ref rejected: %s", ResultText(result))
	}
	if resolution.semantic == nil ||
		!resolution.semantic.AllowProviderRebind {
		t.Fatalf("current semantic ref did not authorize rebind: %#v", resolution.semantic)
	}

	snapshot.AXSnapshot.Revision = "c2"
	resolution, result = service.resolveActionElement(
		"editor", ref, "", "click",
	)
	if result.IsError {
		t.Fatalf("stale revision was rejected too early: %s", ResultText(result))
	}
	if resolution.semantic == nil ||
		resolution.semantic.AllowProviderRebind {
		t.Fatalf("stale revision authorized provider rebind: %#v", resolution.semantic)
	}
}

func TestProviderBindingRefreshAcceptsAuthorizedSemanticReplacement(t *testing.T) {
	registry := NewRefRegistry(16)
	windowHandle := "w2:" + strings.Repeat("a", 32)
	ref := registry.AcquireTyped(
		AXLocatorSemantic,
		"editor",
		windowHandle,
		`button "Save"`,
		"button",
		"Save",
		"c1",
		":1.42", "/old/save", "99123",
		":1.42", "/window", "99123",
		"frame", "Editor", "a1", "w1",
	)
	registry.BindRuntimePaths(ref, []int{0, 0}, nil)
	if !registry.RefreshProviderBinding(ref, &ElementRecord{
		ControlType:        "push button",
		Name:               "Save",
		RuntimeID:          []int{0, 0},
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/new/save",
		ProcessGeneration:  "99123",
	}, true) {
		t.Fatal("authorized semantic replacement did not refresh provider binding")
	}
	entry, err := registry.Resolve(ref, "")
	if err != nil {
		t.Fatal(err)
	}
	if entry.ProviderObjectPath != "/new/save" {
		t.Fatalf("provider binding=%q, want /new/save", entry.ProviderObjectPath)
	}
}

func TestProviderBindingRefreshRejectsUnauthorizedSemanticReplacement(t *testing.T) {
	registry := NewRefRegistry(16)
	windowHandle := "w2:" + strings.Repeat("a", 32)
	ref := registry.AcquireTyped(
		AXLocatorSemantic,
		"editor",
		windowHandle,
		`button "Save"`,
		"button",
		"Save",
		"c1",
		":1.42", "/old/save", "99123",
		":1.42", "/window", "99123",
		"frame", "Editor", "a1", "w1",
	)
	registry.BindRuntimePaths(ref, []int{0, 0}, nil)
	if registry.RefreshProviderBinding(ref, &ElementRecord{
		ControlType:        "push button",
		Name:               "Save",
		RuntimeID:          []int{0, 0},
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/new/save",
		ProcessGeneration:  "99123",
	}, false) {
		t.Fatal("unauthorized semantic replacement refreshed provider binding")
	}
}

func TestTableOwnerBoundRefsKeepCellsDistinctAndCarryScope(t *testing.T) {
	service := NewService()
	provider := []string{
		":1.42", "/sheet/table", "99123",
		":1.42", "/window/1", "99123",
		"frame", "Book", "a1", "w1", "table_owner",
	}
	c2 := service.RefRegistry.AcquireTyped(
		AXLocatorProviderBound,
		"calc",
		"private-window",
		`cell "C2"`,
		"cell",
		"C2",
		"c1",
		provider...,
	)
	d2 := service.RefRegistry.AcquireTyped(
		AXLocatorProviderBound,
		"calc",
		"private-window",
		`cell "D2"`,
		"cell",
		"D2",
		"c1",
		provider...,
	)
	if c2 == "" || d2 == "" || c2 == d2 {
		t.Fatalf("table-owned cell refs are not distinct: C2=%q D2=%q", c2, d2)
	}
	service.RefRegistry.BindRuntimePaths(c2, []int{0, 4, 1, 2}, nil)
	resolution, result := service.resolveActionElement(
		"", c2, "", "click",
	)
	if result.IsError {
		t.Fatalf("table-owned ref rejected: %s", ResultText(result))
	}
	if resolution.semantic == nil ||
		resolution.semantic.ProviderIdentityScope != "table_owner" ||
		!slices.Equal(resolution.semantic.RuntimeID, []int{0, 4, 1, 2}) {
		t.Fatalf("table owner identity was lost: %#v", resolution.semantic)
	}
}

func TestProviderBoundShortRefDoesNotRequireSemanticSelector(t *testing.T) {
	service := NewService()
	ref := service.RefRegistry.AcquireTyped(
		AXLocatorProviderBound,
		"thunderbird",
		"private-window",
		"",
		"row",
		"Inbox",
		"c1",
		":1.42", "/messages/inbox", "99123",
		":1.42", "/window/1", "99123",
		"frame", "Inbox", "a1", "w1",
	)
	if ref == "" {
		t.Fatal("provider-bound ref was not issued")
	}
	resolution, result := service.resolveActionElement(
		"thunderbird", ref, "", "click",
	)
	if result.IsError {
		t.Fatalf("provider-only ref rejected: %s", ResultText(result))
	}
	if resolution.semantic == nil ||
		resolution.semantic.LocatorKind != AXLocatorProviderBound ||
		resolution.semantic.ProviderObjectPath != "/messages/inbox" ||
		resolution.semantic.Role != "row" ||
		resolution.semantic.Name != "Inbox" {
		t.Fatalf("provider identity was not reconstructed: %#v", resolution.semantic)
	}
}

func TestShortRefCarriesOriginatingWindowIntoLiveResolution(t *testing.T) {
	service := NewService()
	service.RefRegistry.Acquire(
		"editor", "w2:stable-window", `button "Save"`,
		"button", "Save", "c1",
		":1.42", "/org/a11y/atspi/accessible/77", "99123",
		":1.42", "/org/a11y/atspi/accessible/window/1", "99123",
		"dialog", "Preferences",
	)
	resolution, result := service.resolveActionElement(
		"editor", "r1", "", "click",
	)
	if result.IsError {
		t.Fatalf("short ref was rejected: %#v", result)
	}
	if resolution.semantic == nil {
		t.Fatal("short ref did not produce a live semantic target")
	}
	if got := resolution.semantic.WindowHandle; got != "w2:stable-window" {
		t.Fatalf("short ref lost originating window: %q", got)
	}
	if resolution.semantic.ProviderWindowRole != "dialog" ||
		resolution.semantic.ProviderWindowName != "Preferences" {
		t.Fatalf("short ref lost provider window semantics: %#v", resolution.semantic)
	}
}

func TestObservationRootProviderBoundRefResolvesCanonicalRecord(t *testing.T) {
	service := NewService()
	ref := service.RefRegistry.AcquireTyped(
		AXLocatorProviderBound,
		"Editor",
		"pid7/win0/frame",
		"",
		CanonicalRole("paragraph"),
		"Details",
		"c1",
		":1.42",
		"/org/a11y/paragraph/1",
		"pid:7:start:10",
		"",
		"",
		"",
		"",
		"",
		"a1",
		"w1",
		"",
	)
	entry := service.RefRegistry.Entries[ref]
	if entry == nil {
		t.Fatal("provider-bound ref was not assigned")
	}
	service.RefRegistry.BindRuntimePaths(entry.Ref, []int{0, 1}, nil)

	root, err := service.expandObservationRootShortRef(
		"Editor",
		map[string]any{"ref": entry.Ref},
	)
	if err != nil {
		t.Fatalf("expand provider-bound root: %v", err)
	}
	snapshot := &AppSnapshot{
		WindowHandle: "pid7/win0/frame",
		Elements: []ElementRecord{
			{
				Index: 7, RuntimeID: []int{0, 1},
				ControlType: "paragraph", Name: "Details",
				ProviderBusName:    ":1.42",
				ProviderObjectPath: "/org/a11y/paragraph/1",
				ProcessGeneration:  "pid:7:start:10",
			},
		},
	}
	record, mode, err := resolveObservationRoot(snapshot, root)
	if err != nil {
		t.Fatalf("resolve provider-bound root: %v", err)
	}
	if record == nil || record.Index != 7 || mode != "ref" {
		t.Fatalf("resolved record=%#v mode=%q", record, mode)
	}
}

func TestCanonicalAssertionFallbackAcceptsRegistryProviderRebind(t *testing.T) {
	service := NewService()
	showing := true
	record := &ElementRecord{
		Index:              7,
		RuntimeID:          []int{0, 4, 7},
		ControlType:        "check box",
		Name:               "Automatic quotes",
		States:             "[checked]",
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/quote/old",
		ProcessGeneration:  "99123",
		Showing:            &showing,
		Visible:            &showing,
	}
	node := &AXNode{
		ID:                 "0/4/7",
		Role:               "checkbox",
		SemanticClass:      "control",
		Name:               record.Name,
		RuntimeID:          append([]int(nil), record.RuntimeID...),
		ProviderBusName:    record.ProviderBusName,
		ProviderObjectPath: record.ProviderObjectPath,
		ProcessGeneration:  record.ProcessGeneration,
		SemanticTarget:     `checkbox "Automatic quotes"`,
		LocatorKind:        AXLocatorSemantic,
		SourceRecord:       record,
		Showing:            &showing,
		Visible:            &showing,
	}
	tree := &AXSnapshot{
		AppRef:               "a1",
		AppIdentity:          "editor",
		WindowHandle:         "private-window",
		WindowRef:            "w1",
		Revision:             "c1",
		RefInventoryComplete: true,
		CanonicalRoots:       []*AXNode{node},
		Roots:                []*AXNode{node},
		ByID:                 map[string]*AXNode{"0/4/7": node},
	}
	service.RefRegistry.AssignSnapshotRefs(tree)
	current, err := service.RefRegistry.Resolve(node.Ref, "")
	if err != nil {
		t.Fatal(err)
	}
	expected := *current
	record.ProviderObjectPath = "/org/a11y/quote/rebuilt"
	node.ProviderObjectPath = record.ProviderObjectPath
	if !service.RefRegistry.RefreshProviderBinding(node.Ref, record, true) {
		t.Fatal("semantic provider rebind was rejected")
	}
	snapshot := &AppSnapshot{
		App:             AppDescriptor{Name: "editor"},
		WindowHandle:    "private-window",
		Elements:        []ElementRecord{*record},
		publicAppRef:    "a1",
		publicWindowRef: "w1",
		AXSnapshot:      tree,
	}
	if resolved, err := service.exactRefRecordFromSnapshot(
		snapshot,
		node.Ref,
		&expected,
	); err != nil || resolved != record {
		t.Fatalf("registry provider rebind broke semantic proof: record=%#v err=%v",
			resolved, err)
	}
}

func TestFinalObservationContinuationRejectsDifferentAppOwner(t *testing.T) {
	service, appRef, sourceWindowRef, _ := sheetBatchService(t)
	otherAppRef := service.AppRefRegistry.Acquire(
		"Other",
		":1.9",
		"pid:45:start:200",
		"c1",
	)
	currentWindowRef := service.WindowRefRegistry.Acquire(
		"Other",
		"w2:22222222222222222222222222222222",
		`window "Other"`,
		"",
		":1.9",
		"/org/a11y/atspi/accessible/9",
		"pid:45:start:200",
		"frame",
		"Other",
		otherAppRef,
	)
	context := &AXBatchContext{
		AppRef:       appRef,
		WindowRef:    sourceWindowRef,
		AppToken:     appRef,
		WindowHandle: "w2:0123456789abcdef0123456789abcdef",
	}
	result := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"failure_code":                "stale_window_ref",
			"source_window_status":        "gone",
			"recovery_inventory_complete": true,
			"current_window_ref":          currentWindowRef,
		},
	}
	if entry, ok := service.finalObservationContinuation(context, result); ok ||
		entry != nil {
		t.Fatalf("cross-app continuation accepted: %#v", entry)
	}
}

func TestPostActionWindowIdentityStabilizationRejectedProbeIsTransactional(t *testing.T) {
	oldHandle := "w2:" + strings.Repeat("a", 32)
	newHandle := "w2:" + strings.Repeat("b", 32)
	before := windowTransitionSnapshot(oldHandle, "Document", true)
	immediate := windowTransitionSnapshot(oldHandle, "Rename", true)
	immediate.Elements[0].ControlType = "panel"
	sibling := windowTransitionSnapshot(newHandle, "Rename", true)
	for index := range sibling.Elements {
		sibling.Elements[index].ProviderWindowObjectPath = "/sibling-dialog"
	}

	service := NewServiceWithBackend(&recordingPlatformBackend{
		accessibilityResponse: &ProviderAccessibilityResult{
			OK:       true,
			Snapshot: sibling,
		},
	})
	if err := service.BuildAndRenderAXSnapshot(
		before,
		contract.TreeViewWorking,
	); err != nil {
		t.Fatal(err)
	}
	service.rememberSnapshot("Editor", immediate)
	refsBefore := service.RefRegistry.clone()
	windowsBefore := service.WindowRefRegistry.clone()
	appsBefore := service.AppRefRegistry.clone()

	_, _, attempted, stabilized, _ :=
		service.stabilizePostActionWindowIdentity(
			"Editor",
			before,
			immediate,
			AXServiceRequest{Observe: string(contract.ObserveDiff)},
		)
	if !attempted || stabilized {
		t.Fatalf("rejected probe status=%t/%t", attempted, stabilized)
	}
	if !reflect.DeepEqual(service.RefRegistry, refsBefore) ||
		!reflect.DeepEqual(service.WindowRefRegistry, windowsBefore) ||
		!reflect.DeepEqual(service.AppRefRegistry, appsBefore) {
		t.Fatal("rejected identity probe mutated a live ref registry")
	}
}

func TestCompleteInventoryKeepsPresentTemporarilyHiddenRef(t *testing.T) {
	showing := true
	node := &AXNode{
		ID: "0/0", Role: "button", Name: "Save",
		SemanticClass:      "control",
		LocatorKind:        AXLocatorSemantic,
		SemanticTarget:     encodeScopedSemanticRef(`button "Save"`, "window-1"),
		RuntimeID:          []int{0, 0},
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/save",
		ProcessGeneration:  "99123",
		Showing:            &showing,
		Visible:            &showing,
	}
	registry := NewRefRegistry(16)
	first := &AXSnapshot{
		AppRef: "a1", AppIdentity: "editor",
		WindowHandle: "window-1", WindowRef: "w1", Revision: "c1",
		RefInventoryComplete: true,
		Roots:                []*AXNode{node},
		ByID:                 map[string]*AXNode{"0/0": node},
	}
	registry.AssignSnapshotRefs(first)
	ref := node.Ref
	if ref == "" {
		t.Fatal("initial ref was not issued")
	}

	hidden := false
	hiddenNode := *node
	hiddenNode.Ref = ""
	hiddenNode.Showing = &hidden
	hiddenNode.Visible = &hidden
	second := &AXSnapshot{
		AppRef: "a1", AppIdentity: "editor",
		WindowHandle: "window-1", WindowRef: "w1", Revision: "c2",
		RefInventoryComplete: true,
		Roots:                []*AXNode{&hiddenNode},
		ByID:                 map[string]*AXNode{"0/0": &hiddenNode},
	}
	registry.AssignSnapshotRefs(second)
	entry, err := registry.Resolve(ref, "")
	if err != nil {
		t.Fatalf("present hidden node was incorrectly retired: %v", err)
	}
	if entry.LastSeenRevision != "c2" {
		t.Fatalf("present hidden node last seen revision=%q, want c2",
			entry.LastSeenRevision)
	}
}
