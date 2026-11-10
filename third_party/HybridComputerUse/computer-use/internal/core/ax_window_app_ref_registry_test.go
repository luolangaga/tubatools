package core

import (
	"strings"
	"testing"
)

func TestWindowRefRegistryUsesShortStableSessionRefs(t *testing.T) {
	registry := NewWindowRefRegistry(2)
	handleA := "w2:" + strings.Repeat("a", 32)
	handleB := "w2:" + strings.Repeat("b", 32)
	handleC := "w2:" + strings.Repeat("c", 32)

	first := registry.Acquire(
		"chrome", handleA, `frame "New Tab"`, "c1",
	)
	if first != "w1" {
		t.Fatalf("first window ref = %q, want w1", first)
	}
	renamed := registry.Acquire(
		"chrome", handleA, `frame "Search results"`, "c2",
	)
	if renamed != first {
		t.Fatalf("title/selector mutation changed logical window ref: %q -> %q",
			first, renamed)
	}
	second := registry.Acquire(
		"chrome", handleB, `dialog "Open File"`, "c2",
	)
	if second != "w2" {
		t.Fatalf("second window ref = %q, want w2", second)
	}
	if registry.Acquire(
		"chrome", handleC, `dialog "Save File"`, "c3",
	) != "w3" {
		t.Fatal("new native identity did not receive a new monotonic ref")
	}
	if _, err := registry.Resolve(first, "chrome"); err == nil ||
		!strings.Contains(err.Error(), "expired") {
		t.Fatalf("evicted ref must fail explicitly: %v", err)
	}
	if got := registry.Acquire(
		"chrome", "w2:"+strings.Repeat("d", 33), `dialog "Bad"`, "c4",
	); got != "" {
		t.Fatalf("malformed internal handle received public ref %q", got)
	}
}

func TestAppRefRegistryKeepsGenerationIdentity(t *testing.T) {
	registry := NewAppRefRegistry(8)
	first := registry.Acquire("LibreOffice", ":1.84", "pid:10:start:100", "c1")
	if first != "a1" {
		t.Fatalf("first app ref = %q, want a1", first)
	}
	if again := registry.Acquire(
		"LibreOffice Calc", ":1.84", "pid:10:start:100", "c2",
	); again != first {
		t.Fatalf("same provider generation changed ref: %q -> %q", first, again)
	}
	restarted := registry.Acquire(
		"LibreOffice", ":1.91", "pid:10:start:200", "c3",
	)
	if restarted == first {
		t.Fatalf("restarted app reused ref %q", first)
	}
}

func TestSharedNativeHandleDoesNotRebindModalRefToDocument(t *testing.T) {
	registry := NewWindowRefRegistry(16)
	handle := "w2:0123456789abcdef0123456789abcdef"
	dialogRef := registry.Acquire(
		"soffice", handle, `dialog "Document Recovery"`, "c1",
		":1.84", "/app/dialog/recovery", "pid:10:start:100",
		"dialog", "Document Recovery", "a1",
	)
	documentRef := registry.Acquire(
		"soffice", handle, `window "slides.pptx - LibreOffice Impress"`, "c2",
		":1.84", "/app/window/document", "pid:10:start:100",
		"window", "slides.pptx - LibreOffice Impress", "a1",
	)
	if dialogRef == "" || documentRef == "" || dialogRef == documentRef {
		t.Fatalf("logical replacement reused window ref: dialog=%q document=%q", dialogRef, documentRef)
	}
	if _, err := registry.Resolve(dialogRef, ""); err == nil ||
		!strings.Contains(err.Error(), "logical_window_replaced") {
		t.Fatalf("old modal ref was not tombstoned: %v", err)
	}
}

func TestSharedNativeHandleDoesNotRebindChooserRefToDocument(t *testing.T) {
	registry := NewWindowRefRegistry(16)
	handle := "w2:0123456789abcdef0123456789abcdef"
	chooserRef := registry.Acquire(
		"soffice", handle, `filechooser "Save As"`, "c1",
		":1.84", "/app/chooser/save", "pid:10:start:100",
		"file chooser", "Save As", "a1",
	)
	documentRef := registry.Acquire(
		"soffice", handle, `window "draft.odt - LibreOffice Writer"`, "c2",
		":1.84", "/app/window/document", "pid:10:start:100",
		"window", "draft.odt - LibreOffice Writer", "a1",
	)
	if chooserRef == "" || documentRef == "" || chooserRef == documentRef {
		t.Fatalf("logical replacement reused chooser ref: chooser=%q document=%q",
			chooserRef, documentRef)
	}
	if _, err := registry.Resolve(chooserRef, ""); err == nil ||
		!strings.Contains(err.Error(), "logical_window_replaced") {
		t.Fatalf("old chooser ref was not tombstoned: %v", err)
	}
}

func TestNativeDocumentWindowSurvivesTitleAndProviderRebuild(t *testing.T) {
	registry := NewWindowRefRegistry(16)
	handle := "w2:0123456789abcdef0123456789abcdef"
	first := registry.Acquire(
		"soffice", handle, `window "draft.odt - LibreOffice Writer"`, "c1",
		":1.84", "/app/window/old", "pid:10:start:100",
		"window", "draft.odt - LibreOffice Writer", "a1",
	)
	second := registry.Acquire(
		"soffice", handle, `window "final.odt - LibreOffice Writer"`, "c2",
		":1.84", "/app/window/rebuilt", "pid:10:start:100",
		"window", "final.odt - LibreOffice Writer", "a1",
	)
	if first == "" || second != first {
		t.Fatalf("stable native document window changed ref: first=%q second=%q", first, second)
	}
}

func TestTransientWindowSurvivesTitleOnlyChange(t *testing.T) {
	registry := NewWindowRefRegistry(16)
	handle := "w2:0123456789abcdef0123456789abcdef"
	first := registry.Acquire(
		"soffice", handle, `dialog "Options - General"`, "c1",
		":1.84", "/app/dialog/options", "pid:10:start:100",
		"dialog", "Options - General", "a1",
	)
	second := registry.Acquire(
		"soffice", handle, `dialog "Options - View"`, "c2",
		":1.84", "/app/dialog/options", "pid:10:start:100",
		"dialog", "Options - View", "a1",
	)
	if first == "" || second != first {
		t.Fatalf(
			"title-only modal mutation changed logical window ref: %q -> %q",
			first,
			second,
		)
	}
}

func TestTransientWindowRejectsSameRoleProviderReplacement(t *testing.T) {
	registry := NewWindowRefRegistry(16)
	handle := "w2:0123456789abcdef0123456789abcdef"
	first := registry.Acquire(
		"soffice", handle, `dialog "Question"`, "c1",
		":1.84", "/app/dialog/old", "pid:10:start:100",
		"dialog", "Question", "a1",
	)
	second := registry.Acquire(
		"soffice", handle, `dialog "Question"`, "c2",
		":1.84", "/app/dialog/later", "pid:10:start:100",
		"dialog", "Question", "a1",
	)
	if first == "" || second == "" || second == first {
		t.Fatalf(
			"replacement dialog reused logical window ref: %q -> %q",
			first,
			second,
		)
	}
	if _, err := registry.Resolve(first, ""); err == nil ||
		!strings.Contains(err.Error(), "logical_window_replaced") {
		t.Fatalf("old dialog ref was not tombstoned: %v", err)
	}
}

func TestIncompleteReadMarksRefForRevalidationNotTombstone(t *testing.T) {
	service, _, _, elementRef := sheetBatchService(t)
	service.runtimeReportedFailure("", AXServiceRequest{
		SourceRef: elementRef,
		SemanticTarget: &semanticTarget{
			LocatorKind: AXLocatorProviderBound,
		},
	}, ProviderAccessibilityResult{
		FailureCode:          "observation_incomplete",
		FailureSubject:       "element",
		FailureFactsComplete: false,
		Error:                "temporary AX read failure",
	})
	entry := service.RefRegistry.Entries[elementRef]
	if entry == nil || entry.Lifecycle != AXRefNeedsRevalidation {
		t.Fatalf("incomplete read lifecycle=%#v", entry)
	}
	reissued := service.RefRegistry.Acquire(
		entry.AppIdentity,
		entry.WindowHandle,
		entry.SemanticTarget,
		entry.Role,
		entry.Name,
		"c2",
		entry.ProviderBusName,
		entry.ProviderObjectPath,
		entry.ProcessGeneration,
		entry.ProviderWindowBusName,
		entry.ProviderWindowObjectPath,
		entry.ProviderWindowProcessGeneration,
		entry.ProviderWindowRole,
		entry.ProviderWindowName,
		entry.AppRef,
		entry.LogicalWindowRef,
	)
	if reissued != elementRef ||
		service.RefRegistry.Entries[elementRef].Lifecycle != AXRefActive {
		t.Fatalf("complete rediscovery did not reactivate %s", elementRef)
	}
}

// App and window refs share one lifecycle rule: only active and
// needs_revalidation keep the identity, so a later Acquire of a released
// identity issues a new ref.
func TestAppAndWindowRefLifecycleReleasesIdentity(t *testing.T) {
	const handle = "w2:0123456789abcdef0123456789abcdef"
	for _, test := range []struct {
		lifecycle AXRefLifecycle
		released  bool
	}{
		{AXRefActive, false},
		{AXRefNeedsRevalidation, false},
		{AXRefStale, true},
		{AXRefExpired, true},
		{AXRefSuperseded, true},
	} {
		t.Run(string(test.lifecycle), func(t *testing.T) {
			apps := NewAppRefRegistry(0)
			app := apps.Acquire("Editor", ":1.84", "pid:10:start:100", "c1")
			apps.MarkLifecycle(" "+app+" ", test.lifecycle, " reason ")
			appEntry := apps.entries[app]
			reacquiredApp := apps.Acquire("Editor", ":1.84", "pid:10:start:100", "c2")

			windows := NewWindowRefRegistry(0)
			window := windows.Acquire("editor", handle, `frame "Doc"`, "c1")
			windows.MarkLifecycle(" "+window+" ", test.lifecycle, " reason ")
			windowEntry := windows.entries[window]
			reacquiredWindow := windows.Acquire("editor", handle, `frame "Doc"`, "c2")

			if test.released {
				if appEntry.Lifecycle != test.lifecycle || appEntry.LifecycleReason != "reason" ||
					windowEntry.Lifecycle != test.lifecycle || windowEntry.LifecycleReason != "reason" {
					t.Fatalf("app=%+v window=%+v", *appEntry, *windowEntry)
				}
				if reacquiredApp == app || reacquiredWindow == window {
					t.Fatalf("released identity kept its ref: app %s->%s window %s->%s",
						app, reacquiredApp, window, reacquiredWindow)
				}
				return
			}
			// Acquire reactivates a kept identity under the same ref.
			if reacquiredApp != app || reacquiredWindow != window ||
				appEntry.Lifecycle != AXRefActive || windowEntry.Lifecycle != AXRefActive {
				t.Fatalf("kept identity changed: app %s->%s (%s) window %s->%s (%s)",
					app, reacquiredApp, appEntry.Lifecycle, window, reacquiredWindow, windowEntry.Lifecycle)
			}
		})
	}
	// Unknown refs and nil registries are ignored.
	NewAppRefRegistry(0).MarkLifecycle("a9", AXRefStale, "")
	NewWindowRefRegistry(0).MarkLifecycle("w9", AXRefStale, "")
	(*AppRefRegistry)(nil).MarkLifecycle("a1", AXRefStale, "")
	(*WindowRefRegistry)(nil).MarkLifecycle("w1", AXRefStale, "")
}

func TestParseRefProviderIdentityTrimsOnlyTheTrailingFields(t *testing.T) {
	values := []string{
		" :1.2 ", " /obj ", " g1 ", " :1.3 ", " /win ", " g2 ", " frame ", " Doc ",
		" a1 ", " w1 ", " process_instance ", " auto ", " push button ",
	}
	got := parseRefProviderIdentity(values)
	want := refProviderIdentity{
		busName: " :1.2 ", objectPath: " /obj ", processGeneration: " g1 ",
		windowBusName: " :1.3 ", windowObjectPath: " /win ", windowProcessGeneration: " g2 ",
		windowRole: " frame ", windowName: " Doc ",
		appRef: "a1", logicalWindowRef: "w1", scope: "process_instance",
		automationID: "auto", providerRole: "push button",
	}
	if got != want {
		t.Fatalf("parseRefProviderIdentity = %#v, want %#v", got, want)
	}
	if !got.complete() {
		t.Fatal("bus, object and generation are present; identity must be complete")
	}
	short := parseRefProviderIdentity([]string{":1.2", " "})
	if short.complete() || short.processGeneration != "" || short.providerRole != "" {
		t.Fatalf("missing positions must read as empty: %#v", short)
	}
}
