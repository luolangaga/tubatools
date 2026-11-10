package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestCoordinateOwnershipRecoveryPreservesProgress(t *testing.T) {
	for _, completed := range []int{0, 2} {
		full := map[string]any{
			"failure_code":                         "coordinate_target_unverified",
			"input_events_emitted":                 completed > 0,
			"mutation_unknown":                     completed > 0,
			"actions_completed_without_call_error": completed,
			"actions_requested":                    4,
			"retry_same_request":                   false,
		}
		public := axPublicEvidence(full, true, []ContentItem{{Type: "text", Text: "point owner is different_window"}})
		wantCode := contract.AXCodeRuntimeError
		if completed > 0 {
			wantCode = contract.AXCodeEffectUnknown
		}
		if public["code"] != wantCode || public["completed"] != completed || public["input_emitted"] != (completed > 0) {
			t.Fatalf("recovery changed input/progress evidence: %#v", public)
		}
		message, _ := public["message"].(string)
		if !strings.Contains(message, "GUI screenshot") || !strings.Contains(message, "remaining actions") ||
			!strings.Contains(message, "different_window") || public["hint"] != GUIScreenshotCall {
			t.Fatalf("ownership recovery lost the cause or actionable observation: %#v", public)
		}
		args := decodeJSONForTest(t, public["hint"].(string))
		if err := contract.ValidateToolArguments(contract.PublicToolGUI, args); err != nil {
			t.Fatalf("GUI recovery hint was rejected: %v", err)
		}
	}
}

func TestWindowChangeRecoveryUsesAddressableCurrentWindow(t *testing.T) {
	for _, title := range []string{"Report", "Save changes?", ""} {
		t.Run(title, func(t *testing.T) {
			window := reportMain()
			window.title = title
			s, _ := reportService(t, window)
			current, _ := reportObserve(t, s)
			context := &AXBatchContext{AppToken: "Editor", WindowRef: "w999", WindowHandle: reportDialogHandle}
			result, stopped := s.axBatchContinuityStop(context,
				[]axBatchAction{{actionType: "click"}, {actionType: "wait"}, {actionType: "type"}}, 1,
				[]map[string]any{{"input_events_emitted": true}, {"input_events_emitted": false}},
				observation{mode: contract.ObserveNone})
			public := axPublicEvidence(result.StructuredContent, result.IsError, result.Content)
			if !stopped || public["code"] != contract.AXCodeWindowChanged || public["completed"] != 2 || public["input_emitted"] != true {
				t.Fatalf("window transition must preserve the stopped prefix: %#v", public)
			}
			text := ResultText(result)
			if strings.Contains(text, "opened") || strings.Contains(text, "by title") || !strings.Contains(text, "remaining") {
				t.Fatalf("recovery invented a new named window or lost the remaining-action boundary: %s", text)
			}
			args := decodeJSONForTest(t, public["hint"].(string))
			if args["window_ref"] != current || args["mode"] != "observe" {
				t.Fatalf("hint does not observe the current window: %#v", args)
			}
			if observed := s.callTool(contract.PublicToolAX, args); observed.IsError {
				t.Fatalf("printed recovery call failed: %s", ResultText(observed))
			}
		})
	}
}

func TestWindowChangeRecoveryDoesNotInventPopupAddress(t *testing.T) {
	for _, ref := range []string{"", "w999"} {
		s, _ := reportService(t, reportMain())
		reportObserve(t, s)
		snapshot := s.currentSnapshot("Editor")
		snapshot.WindowTitle = ""
		snapshot.publicWindowRef = ref
		context := &AXBatchContext{AppToken: "Editor", WindowRef: "w998", WindowHandle: reportDialogHandle}
		result, stopped := s.axBatchContinuityStop(context,
			[]axBatchAction{{actionType: "click"}, {actionType: "type"}}, 0,
			[]map[string]any{{"input_events_emitted": true}}, observation{mode: contract.ObserveNone})
		public := axPublicEvidence(result.StructuredContent, result.IsError, result.Content)
		if !stopped || public["completed"] != 1 || public["hint"] != GUIScreenshotCall ||
			!strings.Contains(ResultText(result), "GUI screenshot") || result.StructuredContent["retry_same_request"] != false {
			t.Fatalf("anonymous/stale popup must stop and request visual evidence: %#v %s", public, ResultText(result))
		}
	}
}

func TestWindowChangeRecoveryWithNoCurrentWindowListsApps(t *testing.T) {
	s := NewService()
	context := &AXBatchContext{AppToken: "Editor"}
	for _, snapshot := range []*AppSnapshot{nil, {WindowAbsent: true, publicWindowRef: "w999"}} {
		s.snapshots["Editor"] = snapshot
		message, hint, currentRef := s.axBatchContinuityRecovery(context)
		if hint != `{"mode":"apps"}` || !strings.Contains(message, "remaining") || currentRef != "" {
			t.Fatalf("missing window needs discovery, not a stale ref: %s %s", message, hint)
		}
	}
}
