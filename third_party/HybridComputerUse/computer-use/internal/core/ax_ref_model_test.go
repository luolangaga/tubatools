package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// An act call takes its window from the rN refs of its actions, so
// window_ref is redundant there. Both forms must send the same input to the
// same window.
func TestActWithElementRefNeedsNoWindowRef(t *testing.T) {
	sent := map[string]ProviderActionRequest{}
	for name, withWindowRef := range map[string]bool{"rN only": false, "rN and matching window_ref": true} {
		s, _, window, ref := orderedInputTestService(t)
		backend := &recordingPlatformBackend{accessibilityResponse: &ProviderAccessibilityResult{
			OK: true, Snapshot: mixedOrderedInputBatchTestSnapshot(),
			Mutation:       &ProviderMutationFacts{InputEventsEmitted: true},
			ActionEvidence: &ProviderActionEvidence{InputEmitted: true, Route: "semantic_action"},
		}}
		s.Backend = backend
		args := map[string]any{
			"mode": "act", "observe": "none",
			"actions": []any{map[string]any{"type": "click", "ref": ref}},
		}
		if withWindowRef {
			args["window_ref"] = window
		}
		result := s.callTool(contract.PublicToolAX, args)
		if result.IsError || len(backend.actionRequests) != 1 {
			t.Fatalf("%s: %s", name, ResultText(result))
		}
		request := backend.actionRequests[0]
		if request.Target == nil ||
			request.Target.WindowHandle != "w2:0123456789abcdef0123456789abcdef" ||
			request.Target.ProviderWindowObjectPath != "/window/1" {
			t.Fatalf("%s: click was not bound to the ref's window: %#v", name, request.Target)
		}
		sent[name] = request
	}
	if a, b := contract.CompactJSON(sent["rN only"]), contract.CompactJSON(sent["rN and matching window_ref"]); a != b {
		t.Fatalf("window_ref changed the provider request:\n%s\n%s", a, b)
	}
}

// The schema tells the model to leave window_ref out of act calls with refs.
func TestWindowRefHelpSaysActTakesTheWindowFromRefs(t *testing.T) {
	properties := findToolDefinition(t, "AX").InputSchema["properties"].(map[string]any)
	help := properties["window_ref"].(map[string]any)["description"].(string)
	if !strings.Contains(help, "act: omit when actions have refs") {
		t.Fatalf("window_ref help does not say act omits it: %q", help)
	}
}

// apps and observe name the application but do not offer its aN. The aN
// stays accepted as app, and so does the application name.
func TestAppsHidesAppRefButAppStillAcceptsItAndTheName(t *testing.T) {
	service, _ := windowScriptService(editorApps(
		testWindow{testHandleReport, "Quarterly report - Editor", true},
	))
	text := ResultText(service.listApps())
	if !strings.Contains(text, `w1 "Quarterly report - Editor"  app=editor  active`) || strings.Contains(text, "a1") {
		t.Fatalf("apps line: %s", text)
	}
	for _, app := range []string{"a1", "editor"} {
		request, argErr := DecodeAXRequest(map[string]any{"mode": "observe", "app": app})
		if argErr != nil {
			t.Fatalf("app %q rejected: %#v", app, argErr)
		}
		context, err := service.resolveAXReadIdentity(request.app, request.windowRef, "")
		if err != nil || context.appToken != app {
			t.Fatalf("app %q did not bind: %#v %v", app, context, err)
		}
	}
	header := (&AppSnapshot{
		App: AppDescriptor{Name: "editor"}, WindowTitle: "Quarterly report - Editor",
		publicAppRef: "a1", publicWindowRef: "w1",
	}).renderedText()
	if !strings.Contains(header, `app: "editor"`) || strings.Contains(header, "a1") ||
		!strings.Contains(header, `window: "Quarterly report - Editor" [ref=w1]`) {
		t.Fatalf("observe header: %s", header)
	}
}

// twoEditorProcesses runs the same application name in two processes. The
// first has a main window and a dialog; the second has one window. A third
// application must not appear in the ambiguity listing.
func twoEditorProcesses() []AppDescriptor {
	window := func(bus, generation, handle, role, title string, active bool) ProviderAppWindow {
		return ProviderAppWindow{
			Title: title, Role: role, Handle: handle, ProviderBusName: bus,
			ProviderObjectPath: "/w/" + title, ProcessGeneration: generation,
			Active: active, InputTarget: true,
		}
	}
	return []AppDescriptor{
		{
			Name: "Editor", BundleIdentifier: "editor", PID: 10, InputCapable: true,
			ProviderBusName: ":1.10", ProcessGeneration: "pid:10:start:1",
			Windows: []ProviderAppWindow{
				window(":1.10", "pid:10:start:1", testHandleReport, "frame", "Report - Editor", true),
				window(":1.10", "pid:10:start:1", testHandleNotes, "dialog", "Save As", false),
			},
		},
		{
			Name: "Editor", BundleIdentifier: "editor", PID: 11, InputCapable: true,
			ProviderBusName: ":1.11", ProcessGeneration: "pid:11:start:1",
			Windows: []ProviderAppWindow{
				window(":1.11", "pid:11:start:1", "w2:dddddddddddddddddddddddddddddddd", "frame", "Draft - Editor", false),
			},
		},
		{
			Name: "mail", BundleIdentifier: "mail", PID: 20, InputCapable: true,
			ProviderBusName: ":1.20", ProcessGeneration: "pid:20:start:1",
			Windows: []ProviderAppWindow{
				window(":1.20", "pid:20:start:1", testHandleMail, "frame", "Inbox - Mail", false),
			},
		},
	}
}

// An app name that matches two processes fails before input and lists the
// wN and title of every window of both processes, so the next call can name
// one window.
func TestAmbiguousAppNameListsTheWindowsOfEachMatchingProcess(t *testing.T) {
	for _, mode := range []string{"observe", "find"} {
		service, backend := windowScriptService(twoEditorProcesses())
		backend.accessibilityResponse = &ProviderAccessibilityResult{
			Error: `appTargetAmbiguous("editor"): 2 input-capable accessibility applications match ` +
				`this bootstrap query: Editor. No action was attempted. Pass the wN of one window ` +
				`as window_ref; AX {"mode":"apps"} lists the windows.`,
			Failure:        &ProviderFailure{Code: "ax_app_ambiguous", Phase: "precondition", FactsComplete: true},
			CandidateCount: 2,
			Candidates: []ProviderTargetCandidate{
				{Name: "Editor", ProviderBusName: ":1.10", ProcessGeneration: "pid:10:start:1"},
				{Name: "Editor", ProviderBusName: ":1.11", ProcessGeneration: "pid:11:start:1"},
			},
		}
		args := map[string]any{"mode": mode, "app": "editor"}
		if mode == "find" {
			args["query"] = map[string]any{"role": "button"}
		}
		result := service.callTool(contract.PublicToolAX, args)
		if !result.IsError || len(backend.actionRequests) != 0 {
			t.Fatalf("%s: ambiguous app must fail before input: %s", mode, ResultText(result))
		}
		text := ResultText(result)
		for _, want := range []string{
			`w1 "Report - Editor"  app=Editor  active`,
			`w2 "Save As"  app=Editor`,
			`w3 "Draft - Editor"  app=Editor`,
			"pass one wN as window_ref",
		} {
			if !strings.Contains(text, want) {
				t.Fatalf("%s: listing lacks %q:\n%s", mode, want, text)
			}
		}
		if strings.Contains(text, "Inbox - Mail") || strings.Contains(text, "aN") {
			t.Fatalf("%s: listing names another app or the aN:\n%s", mode, text)
		}
		line := publicLine(t, result)
		if !strings.HasPrefix(line, `{"ok":false,"code":"invalid_argument","path":"app"`) ||
			!strings.Contains(line, `"hint":"{\"mode\":\"observe\",\"window_ref\":\"w1\"}"`) ||
			!strings.Contains(line, `"input_emitted":false`) {
			t.Fatalf("%s: evidence line %s", mode, line)
		}
	}
}

// Without process identities the failure still points at apps.
func TestAmbiguousAppWithoutCandidatesHintsApps(t *testing.T) {
	service, backend := windowScriptService(twoEditorProcesses())
	backend.accessibilityResponse = &ProviderAccessibilityResult{
		Error:   `appTargetAmbiguous("editor"): 2 input-capable accessibility applications match.`,
		Failure: &ProviderFailure{Code: "ax_app_ambiguous", Phase: "precondition", FactsComplete: true},
	}
	result := service.callTool(contract.PublicToolAX, map[string]any{"mode": "observe", "app": "editor"})
	line := publicLine(t, result)
	if !strings.Contains(line, `"path":"app"`) || !strings.Contains(line, `"hint":"{\"mode\":\"apps\"}"`) {
		t.Fatalf("evidence line %s", line)
	}
}
