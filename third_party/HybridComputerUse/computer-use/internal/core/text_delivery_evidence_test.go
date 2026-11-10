package core

import (
	"encoding/json"
	"fmt"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestTextDeliveryEvidenceReachesModel(t *testing.T) {
	for _, file := range []string{"text-first-message-unknown.response.json", "text-focus-changed.response.json", "text-segment-unknown.response.json"} {
		t.Run(file, func(t *testing.T) {
			response, err := DecodeProviderWireResponse(readProviderFixture(t, file))
			if err != nil {
				t.Fatal(err)
			}
			assertProviderFixtureRoundTrip(t, readProviderFixture(t, file), response)
			var evidence *RuntimeActionEvidence
			var mutation *ProviderMutationFacts
			if response.Desktop != nil {
				evidence, mutation = response.Desktop.ActionEvidence, response.Desktop.Mutation
			} else {
				evidence, mutation = response.Accessibility.ActionEvidence, response.Accessibility.Mutation
			}
			for _, tool := range []string{contract.PublicToolAX, contract.PublicToolGUI} {
				for _, screenshot := range []bool{false, true} {
					var result ToolCallResult
					service := NewService()
					if tool == contract.PublicToolGUI {
						desktop := ProviderDesktopResult{OK: false, Error: "Text delivery interrupted; observe before replaying.", Mutation: mutation, ActionEvidence: evidence, Failure: &ProviderFailure{Code: "desktop_input_failed", Phase: "emission"}}
						if screenshot {
							desktop.Snapshot = &AppSnapshot{ScreenshotPNGBase64: testPNGBase64(t, 10, 10), Width: 10, Height: 10, CoordinateSpace: "screen_image_px"}
						}
						result = service.guiResponse(desktop)
					} else {
						ax := ProviderAccessibilityResult{OK: false, Error: "Text delivery interrupted; observe before replaying.", Mutation: mutation, ActionEvidence: evidence, Failure: &ProviderFailure{Code: "desktop_input_failed", Phase: "emission"}}
						ax.PrepareForService()
						result = service.renderRuntimeReportedFailure("", AXServiceRequest{}, ax, "", nil, nil)
						result.StructuredContent["ax_mode"] = "act"
						if screenshot { // The batch path must retain the failing action's evidence.
							result = prependAXBatchSummary(result, "", map[string]any{"actions_requested": 2, "actions_completed_without_call_error": 0, "failed_action_index": 0, "actions": []map[string]any{batchActionEvidenceEntry(0, "type", result)}})
						}
					}
					visible := FinalizeForModel(tool, result, false)
					text := ResultText(visible)
					if strings.Count(text, `"text_delivery"`) != 1 {
						t.Fatalf("%s evidence missing or duplicated: %s", tool, text)
					}
					for _, forbidden := range []string{"input.window_messages", "document_replace", `"message":258`, `"message":194`, "runtime_action_evidence", "emission_route"} {
						if strings.Contains(text, forbidden) {
							t.Fatalf("private evidence %q leaked: %s", forbidden, text)
						}
					}
					wantFields := []string{`"attempted_messages":1`, `"source_utf16_count":1`, `"effect_verified":false`, `"sequence_phase":"character"`}
					if file == "text-segment-unknown.response.json" {
						// The completed prefix is one message, not one accepted
						// character of the seven-unit segment still in doubt.
						wantFields = []string{`"source_utf16_index":2`, `"source_utf16_count":7`, `"source_utf16_length":9`, `"attempted_messages":2`, `"returned_messages":1`, `"unknown_message_index":1`, `"effect_verified":false`, `"sequence_phase":"selection_text"`}
					}
					for _, want := range wantFields {
						if !strings.Contains(text, want) {
							t.Fatalf("missing %s: %s", want, text)
						}
					}
					if evidence.UnknownMessageIndex != nil {
						if !strings.Contains(text, fmt.Sprintf(`"unknown_message_index":%d`, *evidence.UnknownMessageIndex)) || !strings.Contains(text, fmt.Sprintf(`"input_emitted":%t`, mutation.InputEventsEmitted)) {
							t.Fatalf("unknown first send became confirmed emission: %s", text)
						}
					} else if !strings.Contains(text, `"unknown_message_index":null`) {
						t.Fatalf("no outstanding message became index zero: %s", text)
					}
				}
			}
		})
	}
}

func TestTextDeliveryEvidencePreservesInterruptedSequence(t *testing.T) {
	for _, test := range []struct {
		phase string
		count int
	}{
		{"enter_character", 2},
		{"enter_up", 2},
		{"surrogate_low", 1},
		{"selection_text", 7},
		{"wake", 0},
	} {
		t.Run(test.phase, func(t *testing.T) {
			response, err := DecodeProviderWireResponse(readProviderFixture(t, "text-first-message-unknown.response.json"))
			if err != nil {
				t.Fatal(err)
			}
			input := response.Desktop.ActionEvidence
			input.SourceUTF16Index, input.AttemptedMessages, input.ReturnedMessages = 2, 4, 3
			input.SourceUTF16Count, input.SourceUTF16Length = &test.count, 9
			unknown := 3
			input.UnknownMessageIndex, input.SequencePhase = &unknown, test.phase
			if test.phase == "wake" {
				input.SourceUTF16Index, input.ReturnedMessages = 9, 4
				input.UnknownMessageIndex = nil
			}
			data, err := json.Marshal(response)
			if err != nil {
				t.Fatal(err)
			}
			decoded, err := DecodeProviderWireResponse(data)
			if err != nil {
				t.Fatal(err)
			}
			public := publicTextDelivery(decoded.Desktop.ActionEvidence)
			if public["source_utf16_index"] != input.SourceUTF16Index || public["source_utf16_count"] != test.count || public["source_utf16_length"] != 9 || public["attempted_messages"] != 4 || public["returned_messages"] != input.ReturnedMessages || public["sequence_phase"] != test.phase || public["effect_verified"] != false {
				t.Fatalf("lost interrupted sequence: %#v", public)
			}
			unknownIndex := public["unknown_message_index"].(*int)
			if test.phase == "wake" {
				if unknownIndex != nil {
					t.Fatalf("wake failure invented an unknown text message: %#v", public)
				}
			} else if unknownIndex == nil || *unknownIndex != 3 {
				t.Fatalf("lost unknown message index: %#v", public)
			}
		})
	}
}

func TestTextDeliveryEvidenceKeepsUnreportedSpanUnknown(t *testing.T) {
	response, err := DecodeProviderWireResponse(readProviderFixture(t, "text-first-message-unknown.response.json"))
	if err != nil {
		t.Fatal(err)
	}
	response.Desktop.ActionEvidence.SourceUTF16Count = nil
	data, err := json.Marshal(response)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(string(data), "source_utf16_count") {
		t.Fatalf("missing source span became a known wire value: %s", data)
	}
	decoded, err := DecodeProviderWireResponse(data)
	if err != nil {
		t.Fatal(err)
	}
	if _, present := publicTextDelivery(decoded.Desktop.ActionEvidence)["source_utf16_count"]; present {
		t.Fatal("missing source span became a known public value")
	}
}
