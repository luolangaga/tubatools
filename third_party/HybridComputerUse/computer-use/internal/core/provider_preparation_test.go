package core

import (
	"encoding/json"
	"os"
	"strings"
	"testing"
)

func nativeFailureFixtures(t *testing.T) map[string]json.RawMessage {
	t.Helper()
	data, err := os.ReadFile("../../native/windows/ComputerUse.Windows.PointerEnvelope.Tests/failure-wire-fixtures.json")
	if err != nil {
		t.Fatal(err)
	}
	var fixtures map[string]json.RawMessage
	if err := json.Unmarshal(data, &fixtures); err != nil {
		t.Fatal(err)
	}
	return fixtures
}

func TestNativeFailureEnvelopesStrictTypedRoundTrip(t *testing.T) {
	fixtures := nativeFailureFixtures(t)
	if len(fixtures) != 15 {
		t.Fatalf("failure envelope coverage changed: %d", len(fixtures))
	}
	for name, data := range fixtures {
		t.Run(name, func(t *testing.T) {
			response, err := DecodeProviderWireResponse(data)
			if err != nil {
				t.Fatal(err)
			}
			encoded, err := json.Marshal(response)
			if err != nil {
				t.Fatal(err)
			}
			if _, err := DecodeProviderWireResponse(encoded); err != nil {
				t.Fatal(err)
			}
			if r := response.Accessibility; r != nil {
				r.PrepareForService()
				if name == "focus" && r.FocusPreparation != focusPreparationUnknown {
					t.Fatal("lost focus preparation")
				}
				if name == "pointer" && r.PointerPreparation != pointerPreparationUnknown {
					t.Fatal("lost pointer preparation")
				}
			}
			var raw map[string]any
			if err := json.Unmarshal(data, &raw); err != nil {
				t.Fatal(err)
			}
			for _, domain := range []string{"accessibility", "desktop", "window"} {
				if payload, ok := raw[domain].(map[string]any); ok {
					payload["mutation"].(map[string]any)["unreviewed_native_fact"] = true
				}
			}
			unknown, _ := json.Marshal(raw)
			if _, err := DecodeProviderWireResponse(unknown); err == nil || !strings.Contains(err.Error(), "unknown field") {
				t.Fatalf("strict decoder accepted unknown native fact: %v", err)
			}
		})
	}
}

func TestPreparationRejectsContradictoryWireFacts(t *testing.T) {
	for _, kind := range []string{"focus", "pointer"} {
		for _, change := range []string{"success", "missing_ok", "complete", "null_complete", "emitted", "partial", "known", "desktop_false", "desktop_null", "snapshot", "recovery", "readback", "evidence_input", "tag_null", "tag_empty", "tag_unknown", "wrong_phase", "wrong_code", "wrong_domain", "ordered_domain", "both_tags"} {
			t.Run(kind+"/"+change, func(t *testing.T) {
				var wire map[string]any
				if err := json.Unmarshal(nativeFailureFixtures(t)[kind], &wire); err != nil {
					t.Fatal(err)
				}
				p := wire["accessibility"].(map[string]any)
				m := p["mutation"].(map[string]any)
				f := p["failure"].(map[string]any)
				switch change {
				case "success":
					p["ok"] = true
				case "missing_ok":
					delete(p, "ok")
				case "complete":
					f["facts_complete"] = true
				case "null_complete":
					f["facts_complete"] = nil
				case "emitted":
					m["input_events_emitted"] = true
				case "partial":
					m["partial_input_possible"] = true
				case "known":
					m["mutation_unknown"] = false
				case "desktop_false":
					m["desktop_state_mutated"] = false
				case "desktop_null":
					m["desktop_state_mutated"] = nil
				case "snapshot":
					p["snapshot"] = map[string]any{}
				case "recovery":
					p["recovery_context"] = map[string]any{}
				case "readback":
					p["action_evidence"].(map[string]any)["readback_sampled"] = true
				case "evidence_input":
					p["action_evidence"].(map[string]any)["input_emitted"] = true
				case "tag_null":
					m[kind+"_preparation"] = nil
				case "tag_empty":
					m[kind+"_preparation"] = ""
				case "tag_unknown":
					m[kind+"_preparation"] = "unknown"
				case "wrong_phase":
					f["phase"] = "precondition"
				case "wrong_code":
					f["code"] = "effect_unknown"
				case "wrong_domain":
					wire["operation"], wire["window"] = "window_action", p
					delete(wire, "accessibility")
				case "ordered_domain":
					wire["operation"], wire["ordered_input"] = "ordered_input", p
					delete(wire, "accessibility")
					delete(p, "action_evidence")
				case "both_tags":
					m["focus_preparation"], m["pointer_preparation"] = focusPreparationUnknown, pointerPreparationUnknown
				}
				data, _ := json.Marshal(wire)
				if _, err := DecodeProviderWireResponse(data); err == nil {
					t.Fatal("contradictory preparation accepted")
				}
			})
		}
	}
}

func TestPreparationFailureStopsBatchWithoutRecovery(t *testing.T) {
	for _, kind := range []string{"focus", "pointer"} {
		for _, untargeted := range []bool{false, true} {
			t.Run(kind+map[bool]string{false: "/element", true: "/window"}[untargeted], func(t *testing.T) {
				s, _, window, ref := orderedInputTestService(t)
				response, err := DecodeProviderWireResponse(nativeFailureFixtures(t)[kind])
				if err != nil {
					t.Fatal(err)
				}
				backend := &recordingPlatformBackend{accessibilityResponse: response.Accessibility}
				s.Backend = backend
				action := map[string]any{"type": "click", "ref": ref}
				args := map[string]any{"mode": "act", "observe": "diff"}
				if untargeted {
					action = map[string]any{"type": "keypress", "keys": []any{"ENTER"}}
					args["window_ref"] = window
				}
				args["actions"] = []any{action, map[string]any{"type": "keypress", "keys": []any{"TAB"}},
					map[string]any{"type": "wait", "window": "Sheet", "timeout_ms": 1}}
				result := s.callTool("AX", args)
				if !result.IsError || len(backend.actionRequests) != 1 || len(backend.observeRequests) != 0 {
					t.Fatalf("preparation failure continued input or auto-observed: result=%#v calls=%v", result, backend.calls)
				}
				facts := publicBatchEvidence(t, result)
				batch := assertPublicBatchCounts(t, facts, 3, 0)
				if facts[kind+"_preparation"] == nil || facts["mutation_unknown"] != true || facts["input_emitted"] != false || facts["partial_input_possible"] != false || facts["observation_required"] != false || facts["final_observation_returned"] != false {
					t.Fatalf("lost honest preparation facts: %#v", facts)
				}
				if batch["actions"].([]any)[0].(map[string]any)[kind+"_preparation"] == nil {
					t.Fatalf("lost per-action preparation: %#v", batch)
				}
				continuation := facts["continuation"].(map[string]any)
				if continuation["state"] != "stop" || continuation["retry_same_request"] != false ||
					facts["recovery_read_attempted"] != false || facts["final_observation_kind"] != "error_only" ||
					facts["failed_action_input_emitted"] != false || result.StructuredContent["error_context_captured"] != false {
					t.Fatalf("stop evidence lost or hidden recovery attempted: %#v", facts)
				}
				if strings.Contains(ResultText(result), "observe fresh state before recovery") ||
					!strings.Contains(ResultText(result), "Stop;") {
					t.Fatalf("preparation stop replaced by recovery advice: %s", ResultText(result))
				}
			})
		}
	}
}
