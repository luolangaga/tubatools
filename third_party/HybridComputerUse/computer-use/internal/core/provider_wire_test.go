package core

import (
	"encoding/json"
	"io/fs"
	"os"
	"reflect"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

var providerFixtureFS = os.DirFS("../..").(fs.ReadFileFS)

type providerFixtureManifest struct {
	SchemaVersion         int                     `json:"schema_version"`
	Protocol              string                  `json:"protocol"`
	RequiredOperations    []ProviderWireOperation `json:"required_operations"`
	RequiredFailurePhases []string                `json:"required_failure_phases"`
	Fixtures              []providerFixtureEntry  `json:"fixtures"`
}

type providerFixtureEntry struct {
	Name          string `json:"name"`
	Direction     string `json:"direction"`
	File          string `json:"file"`
	RequestFile   string `json:"request_file,omitempty"`
	ErrorContains string `json:"error_contains,omitempty"`
}

type providerNativeFixtureManifest struct {
	SchemaVersion         int                    `json:"schema_version"`
	Protocol              string                 `json:"protocol"`
	OrderedInputPlatforms map[string]bool        `json:"ordered_input_platforms"`
	Fixtures              []providerFixtureEntry `json:"fixtures"`
}

func readProviderFixture(t *testing.T, name string) []byte {
	t.Helper()
	data, err := providerFixtureFS.ReadFile("testdata/provider-wire/" + name)
	if err != nil {
		t.Fatal(err)
	}
	return data
}

func TestProviderNativeFixtureCorpus(t *testing.T) {
	var manifest providerNativeFixtureManifest
	if err := strictFixtureDecode(readProviderFixture(t, "native-manifest.json"), &manifest); err != nil {
		t.Fatal(err)
	}
	if manifest.Protocol != ProviderProtocolName || manifest.SchemaVersion != ProviderProtocolVersion {
		t.Fatalf("invalid native fixture protocol: %+v", manifest)
	}
	if !reflect.DeepEqual(manifest.OrderedInputPlatforms, map[string]bool{"darwin": true, "windows": false, "linux": false}) {
		t.Fatalf("optional operation support changed: %v", manifest.OrderedInputPlatforms)
	}
	seen := map[string]bool{}
	invalid := map[string]string{}
	for _, fixture := range manifest.Fixtures {
		if fixture.Direction == "invalid_request" {
			invalid[fixture.File] = fixture.ErrorContains
		}
	}
	for _, fixture := range manifest.Fixtures {
		t.Run(fixture.Name, func(t *testing.T) {
			data := readProviderFixture(t, fixture.File)
			switch fixture.Direction {
			case "request":
				request, err := DecodeProviderWireRequest(data)
				if err != nil {
					t.Fatal(err)
				}
				if request.OrderedInput == nil || request.Operation != ProviderWireOrderedInput {
					t.Fatal("native request must exercise the optional ordered-input operation")
				}
				assertProviderFixtureRoundTrip(t, data, request)
				seen["request"] = true
			case "invalid_request":
				var request ProviderWireRequest
				if err := strictFixtureDecode(data, &request); err != nil {
					t.Fatal(err)
				}
				if request.Protocol != ProviderProtocolName || request.SchemaVersion != ProviderProtocolVersion || request.Operation != ProviderWireOrderedInput {
					t.Fatal("negative fixture must reach ordered-input validation with a valid envelope")
				}
				if _, err := DecodeProviderWireRequest(data); err == nil || fixture.ErrorContains == "" || !strings.Contains(err.Error(), fixture.ErrorContains) {
					t.Fatalf("invalid request rejected for wrong reason: %v", err)
				}
				seen["invalid_request"] = true
			case "response":
				response, err := DecodeProviderWireResponse(data)
				if err != nil {
					t.Fatal(err)
				}
				var request ProviderWireRequest
				if err := strictFixtureDecode(readProviderFixture(t, fixture.RequestFile), &request); err != nil {
					t.Fatal(err)
				}
				if response.RequestID != request.RequestID || response.Operation != request.Operation {
					t.Fatal("response is not correlated to its request fixture")
				}
				assertProviderFixtureRoundTrip(t, data, response)
				if response.RequestRejection != nil {
					want := invalid[fixture.RequestFile]
					if err := request.Validate(); err == nil || want == "" || !strings.Contains(err.Error(), want) {
						t.Fatalf("rejection fixture must bind a recognized invalid payload: %v", err)
					}
					seen["request_rejection"] = true
					return
				}
				if request.OrderedInput == nil || response.OrderedInput == nil {
					t.Fatal("ordered-input payload is missing")
				}
				if err := response.OrderedInput.ValidateFor(request.OrderedInput.ProviderOrderedInputRequest); err != nil {
					t.Fatal(err)
				}
				switch {
				case response.OrderedInput.OK:
					seen["success"] = true
				case response.OrderedInput.RetrySameRequest:
					if !response.ProviderResetRequired || ProviderMutationCanInvalidate(&response.OrderedInput.Mutation) {
						t.Fatal("retry fixture must exercise a zero-mutation provider reset")
					}
					seen["retry"] = true
				case response.OrderedInput.Mutation.PartialInputPossible:
					seen["partial"] = true
				default:
					t.Fatal("unclassified native response fixture")
				}
			default:
				t.Fatalf("unknown native fixture direction %q", fixture.Direction)
			}
		})
	}
	for _, required := range []string{"request", "invalid_request", "success", "partial", "retry", "request_rejection"} {
		if !seen[required] {
			t.Errorf("native fixtures do not exercise %s", required)
		}
	}
}

func TestProviderNativeOrderedRequestBoundaries(t *testing.T) {
	for _, test := range []struct {
		name   string
		mutate func(*ProviderOrderedInputRequest)
		want   string
	}{
		{"contract", func(r *ProviderOrderedInputRequest) { r.ContractVersion++ }, "contract version"},
		{"identity", func(r *ProviderOrderedInputRequest) { r.AppIdentity = nil }, "exact app_identity"},
		{"owner", func(r *ProviderOrderedInputRequest) { r.AppIdentity.ProcessGeneration = "other" }, "ownership"},
		{"window", func(r *ProviderOrderedInputRequest) { r.ExpectedWindow.WindowHandle += "b" }, "match the private window_handle"},
		{"steps", func(r *ProviderOrderedInputRequest) { r.Steps[1].ActionIndex++ }, "strictly contiguous"},
		{"boundary", func(r *ProviderOrderedInputRequest) { *r.FinalObservation.CaptureSnapshot = false }, "canonical snapshot boundary"},
	} {
		t.Run(test.name, func(t *testing.T) {
			request, err := DecodeProviderWireRequest(readProviderFixture(t, "ordered-input.request.json"))
			if err != nil {
				t.Fatal(err)
			}
			test.mutate(&request.OrderedInput.ProviderOrderedInputRequest)
			data, err := json.Marshal(request)
			if err != nil {
				t.Fatal(err)
			}
			if _, err := DecodeProviderWireRequest(data); err == nil || !strings.Contains(err.Error(), test.want) {
				t.Fatalf("changed ordered request rejected for wrong reason: %v", err)
			}
		})
	}
}

func TestProviderNativeOrderedResponseBoundaries(t *testing.T) {
	request, err := DecodeProviderWireRequest(readProviderFixture(t, "ordered-input.request.json"))
	if err != nil {
		t.Fatal(err)
	}
	for _, test := range []struct {
		name, file, want string
		mutate           func(*ProviderOrderedInputResult)
	}{
		{"step correlation", "success", "action_index", func(r *ProviderOrderedInputResult) { r.Steps[1].ActionIndex++ }},
		{"final window", "success", "expected window", func(r *ProviderOrderedInputResult) { r.Snapshot.WindowHandle += "b" }},
		{"aggregate emission", "success", "contradicts step evidence", func(r *ProviderOrderedInputResult) { r.Mutation.InputEventsEmitted = false }},
		{"partial uncertainty", "partial", "mutation_unknown", func(r *ProviderOrderedInputResult) { r.Mutation.MutationUnknown = false }},
		{"partial replay", "partial", "same-request retry", func(r *ProviderOrderedInputResult) { r.RetrySameRequest = true }},
		{"continue after failure", "partial", "continued after", func(r *ProviderOrderedInputResult) { r.Steps[2] = r.Steps[0]; r.Steps[2].ActionIndex = 4 }},
		{"retry continuity", "retry", "same-request retry", func(r *ProviderOrderedInputResult) { r.Steps[0].WindowContinuity = ProviderOrderedInputWindowUnknown }},
	} {
		t.Run(test.name, func(t *testing.T) {
			response, err := DecodeProviderWireResponse(readProviderFixture(t, "ordered-input-"+test.file+".response.json"))
			if err != nil {
				t.Fatal(err)
			}
			if err := response.OrderedInput.ValidateFor(request.OrderedInput.ProviderOrderedInputRequest); err != nil {
				t.Fatal(err)
			}
			test.mutate(response.OrderedInput)
			data, err := json.Marshal(response)
			if err != nil {
				t.Fatal(err)
			}
			decoded, err := DecodeProviderWireResponse(data)
			if err != nil {
				t.Fatalf("must reach result correlation, not fail envelope decoding: %v", err)
			}
			if err := decoded.OrderedInput.ValidateFor(request.OrderedInput.ProviderOrderedInputRequest); err == nil || !strings.Contains(err.Error(), test.want) {
				t.Fatalf("changed ordered result rejected for wrong reason: %v", err)
			}
		})
	}
}

func TestProviderNativeRequestRejectionBoundaries(t *testing.T) {
	for _, test := range []struct {
		name   string
		mutate func(*ProviderWireResponse)
		want   string
	}{
		{"missing dispatch fact", func(r *ProviderWireResponse) { r.RequestRejection.DispatchAttempted = nil }, "pre-dispatch validation facts"},
		{"dispatch attempted", func(r *ProviderWireResponse) { *r.RequestRejection.DispatchAttempted = true }, "pre-dispatch validation facts"},
		{"wrong phase", func(r *ProviderWireResponse) { r.RequestRejection.Phase = "emission" }, "pre-dispatch validation facts"},
		{"missing reason", func(r *ProviderWireResponse) { r.RequestRejection.Message = "" }, "pre-dispatch validation facts"},
		{"reset", func(r *ProviderWireResponse) { r.ProviderResetRequired = true }, "cannot accompany"},
		{"domain result", func(r *ProviderWireResponse) { r.Accessibility = &ProviderAccessibilityResult{OK: true} }, "cannot accompany"},
	} {
		t.Run(test.name, func(t *testing.T) {
			response, err := DecodeProviderWireResponse(readProviderFixture(t, "request-rejection.response.json"))
			if err != nil {
				t.Fatal(err)
			}
			test.mutate(&response)
			data, err := json.Marshal(response)
			if err != nil {
				t.Fatal(err)
			}
			if _, err := DecodeProviderWireResponse(data); err == nil || !strings.Contains(err.Error(), test.want) {
				t.Fatalf("changed rejection rejected for wrong reason: %v", err)
			}
		})
	}
}

type providerInvalidFixture struct {
	Name    string          `json:"name"`
	Request json.RawMessage `json:"request"`
}

func TestProviderFixtureCorpus(t *testing.T) {
	manifestData, err := providerFixtureFS.ReadFile(
		"testdata/provider-wire/manifest.json",
	)
	if err != nil {
		t.Fatal(err)
	}
	var manifest providerFixtureManifest
	if err := strictFixtureDecode(manifestData, &manifest); err != nil {
		t.Fatal(err)
	}
	wantOperations := []ProviderWireOperation{
		ProviderWireObserveApps,
		ProviderWireObserve,
		ProviderWireResolveAndAct,
		ProviderWireDesktopBatch,
		ProviderWireWindowList,
		ProviderWireWindowAction,
	}
	if manifest.SchemaVersion != ProviderProtocolVersion {
		t.Fatalf("schema version = %d, want %d",
			manifest.SchemaVersion, ProviderProtocolVersion)
	}
	if manifest.Protocol != ProviderProtocolName {
		t.Fatalf("protocol = %q, want %q",
			manifest.Protocol, ProviderProtocolName)
	}
	if !reflect.DeepEqual(manifest.RequiredOperations, wantOperations) {
		t.Fatalf("required operations = %v, want %v",
			manifest.RequiredOperations, wantOperations)
	}
	if !reflect.DeepEqual(
		manifest.RequiredFailurePhases,
		[]string{"precondition", "emission", "timeout"},
	) {
		t.Fatalf("required failure phases = %v",
			manifest.RequiredFailurePhases)
	}

	seenOperations := make(map[ProviderWireOperation]bool)
	seenFailurePhases := make(map[string]bool)
	for _, fixture := range manifest.Fixtures {
		fixture := fixture
		t.Run(fixture.Name, func(t *testing.T) {
			path := "testdata/provider-wire/" + fixture.File
			data, err := providerFixtureFS.ReadFile(path)
			if err != nil {
				t.Fatal(err)
			}
			switch fixture.Direction {
			case "request":
				request, err := DecodeProviderWireRequest(data)
				if err != nil {
					t.Fatal(err)
				}
				seenOperations[request.Operation] = true
				assertProviderFixtureRoundTrip(t, data, request)
			case "response":
				response, err := DecodeProviderWireResponse(data)
				if err != nil {
					t.Fatal(err)
				}
				seenOperations[response.Operation] = true
				switch {
				case response.Accessibility != nil &&
					response.Accessibility.Failure != nil:
					seenFailurePhases[response.Accessibility.Failure.Phase] = true
				case response.Desktop != nil &&
					response.Desktop.Failure != nil:
					seenFailurePhases[response.Desktop.Failure.Phase] = true
				case response.Window != nil &&
					response.Window.Failure != nil:
					seenFailurePhases[response.Window.Failure.Phase] = true
				}
				assertProviderFixtureRoundTrip(t, data, response)
			default:
				t.Fatalf("unknown fixture direction %q", fixture.Direction)
			}
		})
	}
	for _, operation := range manifest.RequiredOperations {
		if !seenOperations[operation] {
			t.Errorf("operation %q has no fixture", operation)
		}
	}
	for _, phase := range manifest.RequiredFailurePhases {
		if !seenFailurePhases[phase] {
			t.Errorf("failure phase %q has no fixture", phase)
		}
	}
}

func assertProviderFixtureRoundTrip(t *testing.T, original []byte, value any) {
	t.Helper()
	encoded, err := json.Marshal(value)
	if err != nil {
		t.Fatal(err)
	}
	assertFixtureJSONEquivalent(t, original, encoded)
}

func TestProviderStrictDecodeRejectsUnknownFields(t *testing.T) {
	data := []byte(`{
		"protocol":"computer-use-provider",
		"schema_version":2,
		"request_id":"req-unknown",
		"operation":"observe_apps",
		"observe_apps":{"protocol_version":2},
		"unexpected":true
	}`)
	_, err := DecodeProviderWireRequest(data)
	if err == nil || !strings.Contains(err.Error(), "unknown field") {
		t.Fatalf("strict decode error = %v, want unknown field", err)
	}
}

func TestProviderTargetAutomationIdentitySurvivesWire(t *testing.T) {
	target := providerTargetFromOperation(&semanticTarget{
		Selector: `entry "Name"`, AutomationID: "name-entry", HasName: true,
	})
	encoded, err := json.Marshal(target)
	if err != nil {
		t.Fatal(err)
	}
	var decoded ProviderSemanticTarget
	if err := DecodeProviderWireJSON(encoded, &decoded); err != nil {
		t.Fatal(err)
	}
	if decoded.AutomationID != "name-entry" {
		t.Fatalf("target identity lost: %s", encoded)
	}
}

func TestProviderSharedInvalidRequestCorpus(t *testing.T) {
	data, err := providerFixtureFS.ReadFile(
		"testdata/provider-wire/invalid-requests.json",
	)
	if err != nil {
		t.Fatal(err)
	}
	var fixtures []providerInvalidFixture
	if err := strictFixtureDecode(data, &fixtures); err != nil {
		t.Fatal(err)
	}
	if len(fixtures) == 0 {
		t.Fatal("invalid request corpus is empty")
	}
	for _, fixture := range fixtures {
		fixture := fixture
		t.Run(fixture.Name, func(t *testing.T) {
			if _, err := DecodeProviderWireRequest(fixture.Request); err == nil {
				t.Fatalf(
					"Go provider decoder accepted invalid request: %s",
					fixture.Request,
				)
			}
		})
	}
}

func TestProviderValidationRejectsAmbiguousOrMismatchedPayloads(t *testing.T) {
	tests := []struct {
		name    string
		request ProviderWireRequest
		want    string
	}{
		{
			name: "missing payload",
			request: NewProviderWireRequest(
				"req-missing",
				ProviderWireObserve,
			),
			want: "exactly one payload",
		},
		{
			name: "multiple payloads",
			request: func() ProviderWireRequest {
				request := NewProviderWireRequest(
					"req-multiple",
					ProviderWireObserve,
				)
				request.Observe = &ProviderObserveRequest{
					ProtocolVersion: ProviderProtocolVersion,
					App:             "Editor",
				}
				request.WindowList = &ProviderWindowListRequest{
					ProtocolVersion: ProviderProtocolVersion,
					App:             "Editor",
				}
				return request
			}(),
			want: "exactly one payload",
		},
		{
			name: "operation mismatch",
			request: func() ProviderWireRequest {
				request := NewProviderWireRequest(
					"req-mismatch",
					ProviderWireObserve,
				)
				request.WindowList = &ProviderWindowListRequest{
					ProtocolVersion: ProviderProtocolVersion,
					App:             "Editor",
				}
				return request
			}(),
			want: "does not match",
		},
	}
	for _, test := range tests {
		test := test
		t.Run(test.name, func(t *testing.T) {
			err := test.request.Validate()
			if err == nil || !strings.Contains(err.Error(), test.want) {
				t.Fatalf("validation error = %v, want %q", err, test.want)
			}
		})
	}
}

func TestProviderDecoderRejectsTrailingJSON(t *testing.T) {
	_, err := DecodeProviderWireRequest(
		[]byte(`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"observe_apps",` +
			`"observe_apps":{"protocol_version":2}} {}`),
	)
	if err == nil {
		t.Fatal("strict fixture decoder accepted trailing JSON")
	}
}

func TestProviderResponseDecoderRejectsUnknownSnapshotField(t *testing.T) {
	_, err := DecodeProviderWireResponse([]byte(
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"observe",` +
			`"accessibility":{"ok":true,"snapshot":{` +
			`"windowTitle":"Editor","unexpectedSelection":{}}}}`,
	))
	if err == nil || !strings.Contains(err.Error(), "unknown field") {
		t.Fatalf("decoder error = %v, want unknown field", err)
	}
}

func TestProviderSnapshotAcceptsTypedWindowSelection(t *testing.T) {
	response, err := DecodeProviderWireResponse([]byte(
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"observe",` +
			`"accessibility":{"ok":true,"snapshot":{` +
			`"windowTitle":"Editor","windowSelection":{` +
			`"reason":"active","candidateCount":1,` +
			`"candidates":[{"index":0,"title":"Editor","selected":true}]}}}}`,
	))
	if err != nil {
		t.Fatalf("DecodeProviderWireResponse() error = %v", err)
	}
	selection := response.Accessibility.Snapshot.WindowSelection
	if selection == nil || selection.Reason != "active" {
		t.Fatalf("window selection = %#v, want reason active", selection)
	}
	if selection.CandidateCount != 1 ||
		len(selection.Candidates) != 1 ||
		!selection.Candidates[0].Selected {
		t.Fatalf("window selection candidates = %#v", selection)
	}
}

func TestProviderResolvedElementAcceptsTypedRuntimeAliases(t *testing.T) {
	response, err := DecodeProviderWireResponse([]byte(
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"resolve_and_act",` +
			`"accessibility":{"ok":true,"resolved_element":{` +
			`"index":0,"runtimeId":[0,1],"runtimeAliases":[[0,1],[2,1]]}}}`,
	))
	if err != nil {
		t.Fatalf("DecodeProviderWireResponse() error = %v", err)
	}
	element := response.Accessibility.ResolvedElement
	if element == nil || len(element.RuntimeIDAliases) != 2 {
		t.Fatalf("resolved element aliases = %#v, want two aliases", element)
	}
}

func TestProviderResponseAcceptsTypedRecoveryContext(t *testing.T) {
	response, err := DecodeProviderWireResponse([]byte(
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"resolve_and_act",` +
			`"accessibility":{"ok":false,` +
			`"failure":{"code":"effect_unknown",` +
			`"phase":"post_action_observation"},` +
			`"mutation":{"input_events_emitted":true,` +
			`"mutation_unknown":true},` +
			`"recovery_context":{"sourceAppStatus":"alive",` +
			`"sourceWindowStatus":"gone","inventoryComplete":true,` +
			`"currentWindow":{"windowHandle":` +
			`"w2:fedcba9876543210fedcba9876543210",` +
			`"title":"Document Recovery","role":"dialog",` +
			`"providerBusName":":1.42",` +
			`"providerObjectPath":"/org/a11y/window/1",` +
			`"processGeneration":"42:100"}}}}`,
	))
	if err != nil {
		t.Fatalf("DecodeProviderWireResponse() error = %v", err)
	}
	recovery := response.Accessibility.RecoveryContext
	if recovery == nil ||
		recovery.SourceAppStatus != "alive" ||
		recovery.SourceWindowStatus != "gone" ||
		!recovery.InventoryComplete ||
		recovery.CurrentWindow == nil ||
		recovery.CurrentWindow.Title != "Document Recovery" {
		t.Fatalf("recovery context = %#v", recovery)
	}
}

func TestProviderResponseRejectsInvalidRecoveryContext(t *testing.T) {
	tests := []string{
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"resolve_and_act",` +
			`"accessibility":{"ok":false,"recovery_context":{` +
			`"sourceAppStatus":"maybe"}}}`,
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"resolve_and_act",` +
			`"accessibility":{"ok":false,"recovery_context":{` +
			`"sourceAppStatus":"present"}}}`,
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"resolve_and_act",` +
			`"accessibility":{"ok":false,"recovery_context":{` +
			`"sourceWindowStatus":"alive"}}}`,
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"resolve_and_act",` +
			`"accessibility":{"ok":false,"recovery_context":{` +
			`"sourceWindowStatus":"gone","inventoryComplete":true,` +
			`"currentWindow":{"windowHandle":"w2:not-private"}}}}`,
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"resolve_and_act",` +
			`"accessibility":{"ok":false,"recovery_context":{` +
			`"sourceAppStatus":"alive","sourceWindowStatus":"unknown",` +
			`"currentWindow":{"windowHandle":` +
			`"w2:fedcba9876543210fedcba9876543210"}}}}`,
	}
	for _, input := range tests {
		if _, err := DecodeProviderWireResponse([]byte(input)); err == nil {
			t.Fatalf("invalid recovery context accepted: %s", input)
		}
	}
}

func TestProviderObserveStaleWindowRecoveryRoundTrip(t *testing.T) {
	response, err := DecodeProviderWireResponse([]byte(
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"observe",` +
			`"accessibility":{"ok":false,` +
			`"failure":{"code":"stale_window_ref","phase":"precondition",` +
			`"facts_complete":true,"subject":"window"},` +
			`"recovery_context":{"sourceAppStatus":"alive",` +
			`"sourceWindowStatus":"gone","inventoryComplete":true,` +
			`"currentWindow":{"windowHandle":` +
			`"w2:fedcba9876543210fedcba9876543210",` +
			`"title":"Document","role":"frame",` +
			`"providerBusName":":1.42",` +
			`"providerObjectPath":"/org/a11y/window/1",` +
			`"processGeneration":"42:100"}}}}`,
	))
	if err != nil {
		t.Fatalf("DecodeProviderWireResponse() error = %v", err)
	}
	recovery := response.Accessibility.RecoveryContext
	if recovery == nil ||
		recovery.SourceWindowStatus != "gone" ||
		recovery.CurrentWindow == nil ||
		recovery.CurrentWindow.Title != "Document" {
		t.Fatalf("observe recovery context = %#v", recovery)
	}
}

func TestProviderResponseRejectsRecoveryOutsideEffectUnknown(t *testing.T) {
	tests := []string{
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"observe",` +
			`"accessibility":{"ok":false,"failure":{"code":"effect_unknown",` +
			`"phase":"post_action_observation"},"mutation":{` +
			`"input_events_emitted":true,"mutation_unknown":true},` +
			`"recovery_context":{"sourceAppStatus":"unknown"}}}`,
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"observe",` +
			`"accessibility":{"ok":false,"failure":{` +
			`"code":"stale_window_ref","phase":"precondition",` +
			`"facts_complete":false},"recovery_context":{` +
			`"sourceAppStatus":"unknown"}}}`,
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"observe",` +
			`"accessibility":{"ok":false,"failure":{` +
			`"code":"stale_window_ref","phase":"precondition",` +
			`"facts_complete":true},"mutation":{` +
			`"input_events_emitted":true},"recovery_context":{` +
			`"sourceAppStatus":"unknown"}}}`,
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"resolve_and_act",` +
			`"accessibility":{"ok":true,"failure":{"code":"effect_unknown",` +
			`"phase":"post_action_observation"},"mutation":{` +
			`"input_events_emitted":true,"mutation_unknown":true},` +
			`"recovery_context":{"sourceAppStatus":"unknown"}}}`,
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"resolve_and_act",` +
			`"accessibility":{"ok":false,"failure":{"code":"stale_element_ref",` +
			`"phase":"precondition"},"mutation":{"input_events_emitted":false},` +
			`"recovery_context":{"sourceAppStatus":"exited",` +
			`"sourceWindowStatus":"gone","inventoryComplete":true}}}`,
		`{"protocol":"computer-use-provider","schema_version":2,` +
			`"request_id":"req","operation":"resolve_and_act",` +
			`"accessibility":{"ok":false,"failure":{"code":"effect_unknown",` +
			`"phase":"post_action_observation"},"mutation":{` +
			`"input_events_emitted":true,"mutation_unknown":false},` +
			`"recovery_context":{"sourceAppStatus":"unknown"}}}`,
	}
	for _, input := range tests {
		if _, err := DecodeProviderWireResponse([]byte(input)); err == nil {
			t.Fatalf("invalid recovery envelope accepted: %s", input)
		}
	}
}

func TestProviderValidationRejectsPayloadVersionDrift(t *testing.T) {
	request := NewProviderWireRequest("req-version", ProviderWireObserveApps)
	request.ObserveApps = &ProviderObserveAppsRequest{ProtocolVersion: 1}
	err := request.Validate()
	if err == nil || !strings.Contains(err.Error(), "payload protocol version") {
		t.Fatalf("validation error = %v, want payload protocol version", err)
	}
}

func TestProviderActionRoundTripsExpectedWindow(t *testing.T) {
	request := NewProviderWireRequest("req-window", ProviderWireResolveAndAct)
	request.Action = &ProviderActionRequest{
		ProtocolVersion: ProviderProtocolVersion,
		Intent:          AccessibilityKeypress,
		App:             "Editor",
		WindowHandle:    "w2:0123456789abcdef0123456789abcdef",
		ExpectedWindow: &ProviderWindowExpectation{
			WindowHandle:       "w2:0123456789abcdef0123456789abcdef",
			ProviderBusName:    ":1.42",
			ProviderObjectPath: "/org/a11y/window/1",
			ProcessGeneration:  "42:100",
			Role:               "dialog",
			Name:               "Confirm",
		},
		Keypress: &ProviderKeypressInput{Key: "ENTER"},
	}
	encoded, err := json.Marshal(request)
	if err != nil {
		t.Fatal(err)
	}
	decoded, err := DecodeProviderWireRequest(encoded)
	if err != nil {
		t.Fatal(err)
	}
	if decoded.Action.ExpectedWindow == nil ||
		decoded.Action.ExpectedWindow.Name != "Confirm" {
		t.Fatalf("expected window lost in round trip: %#v", decoded.Action)
	}
}

func TestProviderValidationRejectsInvalidTypedPayloads(t *testing.T) {
	tests := []struct {
		name    string
		request ProviderWireRequest
		want    string
	}{
		{
			name: "unknown accessibility intent",
			request: func() ProviderWireRequest {
				request := NewProviderWireRequest(
					"req-action", ProviderWireResolveAndAct)
				request.Action = &ProviderActionRequest{
					ProtocolVersion: ProviderProtocolVersion,
					Intent:          "unknown",
					App:             "Editor",
				}
				return request
			}(),
			want: "unsupported provider accessibility intent",
		},
		{
			name: "resolve target without target",
			request: func() ProviderWireRequest {
				request := NewProviderWireRequest(
					"req-resolve", ProviderWireResolveAndAct)
				request.Action = &ProviderActionRequest{
					ProtocolVersion: ProviderProtocolVersion,
					Intent:          AccessibilityResolveTarget,
					App:             "Editor",
				}
				return request
			}(),
			want: "requires an exact target",
		},
		{
			name: "type without text",
			request: func() ProviderWireRequest {
				request := NewProviderWireRequest(
					"req-type", ProviderWireResolveAndAct)
				request.Action = &ProviderActionRequest{
					ProtocolVersion: ProviderProtocolVersion,
					Intent:          AccessibilityType,
					App:             "Editor",
					WindowHandle:    "w1",
					Type:            &ProviderTypeInput{},
				}
				return request
			}(),
			want: "type text is required",
		},
		{
			name: "keypress without key",
			request: func() ProviderWireRequest {
				request := NewProviderWireRequest(
					"req-keypress", ProviderWireResolveAndAct)
				request.Action = &ProviderActionRequest{
					ProtocolVersion: ProviderProtocolVersion,
					Intent:          AccessibilityKeypress,
					App:             "Editor",
					WindowHandle:    "w1",
					Keypress:        &ProviderKeypressInput{},
				}
				return request
			}(),
			want: "keypress key is required",
		},
		{
			name: "set value without target",
			request: func() ProviderWireRequest {
				value := "Ada"
				request := NewProviderWireRequest(
					"req-set-value", ProviderWireResolveAndAct)
				request.Action = &ProviderActionRequest{
					ProtocolVersion: ProviderProtocolVersion,
					Intent:          AccessibilitySetValue,
					App:             "Editor",
					SetValue: &ProviderSetValueInput{
						Value: &value,
					},
				}
				return request
			}(),
			want: "set_value requires an exact target",
		},
		{
			name: "click with wrong typed input",
			request: func() ProviderWireRequest {
				request := NewProviderWireRequest(
					"req-click", ProviderWireResolveAndAct)
				request.Action = &ProviderActionRequest{
					ProtocolVersion: ProviderProtocolVersion,
					Intent:          AccessibilityClick,
					App:             "Editor",
					Target: &ProviderSemanticTarget{
						Selector: `button "Save"`,
						HasName:  true,
					},
					Keypress: &ProviderKeypressInput{Key: "ENTER"},
				}
				return request
			}(),
			want: "click requires click input only",
		},
		{
			name: "unknown desktop action",
			request: func() ProviderWireRequest {
				request := NewProviderWireRequest(
					"req-desktop", ProviderWireDesktopBatch)
				request.Desktop = &ProviderDesktopRequest{
					ProtocolVersion: ProviderProtocolVersion,
					Actions: []ProviderDesktopAction{{
						Type: "teleport",
					}},
				}
				return request
			}(),
			want: "unsupported action type",
		},
		{
			name: "window action without target",
			request: func() ProviderWireRequest {
				request := NewProviderWireRequest(
					"req-window", ProviderWireWindowAction)
				request.WindowAction = &ProviderWindowActionRequest{
					ProtocolVersion: ProviderProtocolVersion,
					Action:          WindowClose,
					App:             "Editor",
				}
				return request
			}(),
			want: "requires an exact target",
		},
	}
	for _, test := range tests {
		test := test
		t.Run(test.name, func(t *testing.T) {
			err := test.request.Validate()
			if err == nil || !strings.Contains(err.Error(), test.want) {
				t.Fatalf("validation error = %v, want %q", err, test.want)
			}
		})
	}
}

func TestProviderActionDecodeRejectsIntentFieldMixing(t *testing.T) {
	_, err := DecodeProviderWireRequest([]byte(`{
		"protocol":"computer-use-provider",
		"schema_version":2,
		"request_id":"req-mixed",
		"operation":"resolve_and_act",
		"action":{
			"protocol_version":2,
			"intent":"type",
			"app":"Editor",
			"window_handle":"w1",
			"input":{"text":"hello","key":"ENTER"}
		}
	}`))
	if err == nil || !strings.Contains(err.Error(), "unknown field") {
		t.Fatalf("decode error = %v, want action-specific unknown field", err)
	}
}

func TestProviderWireExcludesGoOwnedPolicyFields(t *testing.T) {
	legacy := AXServiceRequest{
		Operation: axServiceObserve,
		App:       "Editor",
		Observe:   contract.ObserveFull,
		TreeView:  "canonical",
		Scope:     "content",
		SourceRef: "r7",
		KnownRefs: map[string]elementRef{"r7": {Index: 7, Role: "button"}},
		Action:    axScrollCommand{WindowCentre: true},
	}
	request := NewProviderWireRequest("req-private", ProviderWireObserve)
	observe := providerObserveRequestFromOperation(legacy)
	request.Observe = &observe
	encoded, err := json.Marshal(request)
	if err != nil {
		t.Fatal(err)
	}
	var wire map[string]any
	if err := json.Unmarshal(encoded, &wire); err != nil {
		t.Fatal(err)
	}
	payload, ok := wire["observe"].(map[string]any)
	if !ok {
		t.Fatalf("observe payload missing: %s", encoded)
	}
	observation, ok := payload["observation"].(map[string]any)
	if !ok {
		t.Fatalf("observation payload missing: %s", encoded)
	}
	for _, forbidden := range []string{
		"observe", "tree_view", "scope", "source_ref",
		"known_refs", "window_centre",
	} {
		if _, leaked := observation[forbidden]; leaked {
			t.Fatalf("provider wire leaked Go-owned field %s: %s",
				forbidden, encoded)
		}
	}

	sourceIndex := 7
	actionRequest := NewProviderWireRequest(
		"req-action-private",
		ProviderWireResolveAndAct,
	)
	action := providerActionRequestFromOperation(AXServiceRequest{
		Action: axResolveTargetCommand{},
		App:    "Editor",
		SemanticTarget: &semanticTarget{
			Selector:    `button "Save"`,
			SourceIndex: &sourceIndex,
			Role:        "button",
			Name:        "Save",
			HasName:     true,
		},
	})
	actionRequest.Action = &action
	actionBytes, err := json.Marshal(actionRequest)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(string(actionBytes), "sourceIndex") {
		t.Fatalf("provider wire leaked Go-owned sourceIndex: %s", actionBytes)
	}
	if action.Target == nil ||
		action.Target.SourceIndex == nil ||
		*action.Target.SourceIndex != sourceIndex {
		t.Fatalf("typed provider request lost private source index: %#v", action)
	}
}

func TestProviderDesktopSettleBound(t *testing.T) {
	valid := ProviderDesktopRequest{
		ProtocolVersion: ProviderProtocolVersion,
		Actions:         []ProviderDesktopAction{{Type: "screenshot"}},
		SettleMS:        contract.GUIMaxSettleMS,
	}
	if err := validateProviderDesktopRequest(valid); err != nil {
		t.Fatalf("provider settle_ms=%d should be accepted: %v", contract.GUIMaxSettleMS, err)
	}
	valid.SettleMS++
	if err := validateProviderDesktopRequest(valid); err == nil ||
		!strings.Contains(err.Error(), "between 0 and 3000") {
		t.Fatalf("provider settle_ms above limit error = %v", err)
	}
}

// DecodeProviderWireRequest strictly decodes and validates a request the
// way the native providers do. The Go runtime only encodes requests, so this
// decoder exists for contract tests.
func DecodeProviderWireRequest(data []byte) (ProviderWireRequest, error) {
	var request ProviderWireRequest
	if err := DecodeProviderWireJSON(data, &request); err != nil {
		return ProviderWireRequest{}, err
	}
	if err := request.Validate(); err != nil {
		return ProviderWireRequest{}, err
	}
	return request, nil
}
