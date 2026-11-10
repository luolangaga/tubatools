package core

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

type textOrderBackend struct {
	*reportBackend
	platform string
	document bool
}

func (b *textOrderBackend) ResolveAndAct(request ProviderActionRequest) (ProviderAccessibilityResult, error) {
	if request.SetValue != nil && b.document {
		if request.SetValue.RequireSemanticWrite {
			b.actionRequests = append(b.actionRequests, request)
			result := ProviderAccessibilityResult{OK: false, Error: "pending queued input",
				Failure:  &ProviderFailure{Code: "desktop_input_failed", Phase: "preparation", FactsComplete: true},
				Mutation: &ProviderMutationFacts{}}
			result.PrepareForService()
			return result, nil
		}
		result, err := b.reportBackend.ResolveAndAct(request)
		if request.SetValue.Value != nil && *request.SetValue.Value == "" {
			result.ActionEvidence.Route = "uia.text_select_and_send_input.document_replace"
		}
		return result, err
	}
	return b.reportBackend.ResolveAndAct(request)
}

func (b *textOrderBackend) HostInfo() HostBackendInfo {
	return HostBackendInfo{Platform: b.platform}
}

func TestWindowsDocumentReplacementHonorsPendingQueue(t *testing.T) {
	for _, tc := range []struct {
		name, first, second     string
		document, stop, pending bool
	}{
		{"key-document", "keypress", "set_value", true, true, true},
		{"key-value", "keypress", "set_value", false, false, true},
		{"clear-document-type", "set_value", "type", true, true, true},
		{"clear-document-document", "set_value", "set_value", true, true, true},
		{"clear-value-type", "set_value", "type", false, false, false},
		{"document-alone", "", "set_value", true, false, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			s, report := reportService(t, reportMain())
			s.Backend = &textOrderBackend{reportBackend: report, platform: "windows", document: tc.document}
			window, refs := reportObserve(t, s, "Size")
			actions := []any{}
			if tc.first == "keypress" {
				actions = append(actions, map[string]any{"type": "keypress", "keys": []any{"CTRL+A"}})
			}
			if tc.first == "set_value" {
				actions = append(actions, map[string]any{"type": "set_value", "ref": refs["Size"], "value": ""})
			}
			last := map[string]any{"type": tc.second}
			if tc.second == "type" {
				last["text"] = "marker"
			} else {
				last["value"] = "marker"
				last["ref"] = refs["Size"]
			}
			actions = append(actions, last)
			result := s.callTool(contract.PublicToolAX, map[string]any{"mode": "act", "window_ref": window, "actions": actions})
			if result.IsError != tc.stop {
				t.Fatalf("stop=%v want %v: %s", result.IsError, tc.stop, ResultText(result))
			}
			if tc.second == "set_value" {
				request := report.actionRequests[len(report.actionRequests)-1]
				encoded, err := json.Marshal(request)
				if err != nil {
					t.Fatal(err)
				}
				if request.SetValue.RequireSemanticWrite != tc.pending || strings.Contains(string(encoded), `"require_semantic_write":true`) != tc.pending {
					t.Fatalf("private constraint lost or leaked: %s", encoded)
				}
			} else if tc.stop && len(report.actionRequests) != 1 {
				t.Fatalf("clear sent later type: %#v", report.actionRequests)
			}
		})
	}
}

func TestWindowsBatchStopsTextAfterUnconfirmedQueuedInput(t *testing.T) {
	for _, first := range []string{"keypress", "click"} {
		for _, platform := range []string{"windows", "linux"} {
			t.Run(platform+"/"+first, func(t *testing.T) {
				s, report := reportService(t, reportMain())
				s.Backend = &textOrderBackend{reportBackend: report, platform: platform}
				window, refs := reportObserve(t, s, "Size")
				step := map[string]any{"type": first}
				if first == "keypress" {
					step["keys"] = []any{"CTRL+A"}
				} else {
					step["ref"] = refs["Size"]
				}
				result := s.callTool(contract.PublicToolAX, map[string]any{
					"mode": "act", "window_ref": window, "actions": []any{
						step, map[string]any{"type": "type", "text": "marker"},
					},
				})
				if platform != "windows" {
					if result.IsError || len(report.actionRequests) != 2 {
						t.Fatalf("non-Windows sequence changed: %s", ResultText(result))
					}
					return
				}
				public := axPublicEvidence(result.StructuredContent, result.IsError, result.Content)
				encodedPublic, _ := json.Marshal(public)
				if strings.Contains(string(encodedPublic), "runtime_action_evidence") || strings.Contains(string(encodedPublic), "semantic_action") {
					t.Fatalf("private route leaked: %s", encodedPublic)
				}
				if !result.IsError || len(report.actionRequests) != 1 ||
					public["completed"] != 1 || public["input_emitted"] != true {
					t.Fatalf("lost prefix or sent text: requests=%d public=%#v result=%s", len(report.actionRequests), public, ResultText(result))
				}
				// A fresh call after caller observation is permitted; the guard
				// must not permanently disable keyboard or text operations.
				reportObserve(t, s)
				result = s.callTool(contract.PublicToolAX, map[string]any{
					"mode": "act", "window_ref": window, "actions": []any{
						map[string]any{"type": "type", "text": "marker"},
					},
				})
				if result.IsError || len(report.actionRequests) != 2 {
					t.Fatalf("separate text request rejected: %s", ResultText(result))
				}
			})
		}
	}
}
