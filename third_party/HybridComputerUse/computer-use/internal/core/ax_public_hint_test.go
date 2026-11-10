package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func publicLine(t *testing.T, result ToolCallResult) string {
	t.Helper()
	visible := WithAXTextEvidence(PrepareAXEvidence(result))
	if len(visible.Content) == 0 {
		t.Fatal("no content")
	}
	return visible.Content[0].Text
}

// A truncated find prints the exact call that returns the next page, and
// that call works.
func TestTruncatedFindPrintsTheNextPageCall(t *testing.T) {
	svc, snapshot, matches := findExposureFixture(t, 8)
	first := findDeliveryFinal(svc.RenderFindPage(snapshot, matches, 0, 3, "target", nil, false))
	text := ResultText(first)
	cursor, _ := first.StructuredContent["next_cursor"].(string)
	if cursor == "" {
		t.Fatalf("truncated page has no cursor: %s", text)
	}
	call := `{"mode":"find","cursor":"` + cursor + `"}`
	if !strings.Contains(text, "matches: 8") || !strings.Contains(text, "range=1..3 next_cursor="+cursor) ||
		!strings.Contains(text, "next page: "+call) {
		t.Fatalf("truncated page must print the next-page call %s:\n%s", call, text)
	}
	request, argErr := DecodeAXRequest(decodeJSONForTest(t, call))
	if argErr != nil {
		t.Fatalf("printed call is not valid: %#v", argErr)
	}
	next := findDeliveryFinal(svc.axFind(request))
	if next.IsError || !strings.Contains(ResultText(next), "range=4..") {
		t.Fatalf("next page call failed: %s", ResultText(next))
	}
}

// Every public error code is produced from the internal facts that mean it,
// and every error line carries a hint.
func TestEveryPublicErrorCodeHasAHint(t *testing.T) {
	for code, full := range map[string]map[string]any{
		contract.AXCodeInvalidArgument: {contract.AXPublicErrorCodeKey: contract.AXCodeInvalidArgument, contract.AXPublicErrorHintKey: `{"mode":"apps"}`},
		contract.AXCodeStaleRef:        {"failure_code": "stale_element_ref"},
		contract.AXCodeWindowChanged:   {"failure_code": "stale_target", "failure_phase": "between_actions"},
		contract.AXCodeWaitTimeout:     {"failure_code": "wait_timeout"},
		contract.AXCodeEffectUnknown:   {"failure_code": "effect_unknown", "input_events_emitted": true},
		contract.AXCodeAppUnavailable:  {"failure_code": "ax_app_unavailable"},
		contract.AXCodeRuntimeError:    {"failure_code": "runtime_timeout"},
		contract.AXCodeAmbiguousWindow: {contract.AXPublicErrorCodeKey: contract.AXCodeAmbiguousWindow, contract.AXPublicErrorHintKey: `{"type":"activate","ref":"w1"}`},
		contract.AXCodeWindowNotFound:  {contract.AXPublicErrorCodeKey: contract.AXCodeWindowNotFound, contract.AXPublicErrorHintKey: `{"type":"activate","ref":"wN"}`},
		contract.AXCodeNoFocusedWindow: {contract.AXPublicErrorCodeKey: contract.AXCodeNoFocusedWindow, contract.AXPublicErrorHintKey: `{"type":"activate","title":"window title"}`},
	} {
		public := axPublicEvidence(full, true, []ContentItem{{Type: "text", Text: "detail"}})
		if public["code"] != code {
			t.Fatalf("%s: code=%v from %#v", code, public["code"], full)
		}
		if hint, _ := public["hint"].(string); hint == "" {
			t.Fatalf("%s has no hint", code)
		}
		if public["ok"] != false || public["message"] != "detail" {
			t.Fatalf("%s: %#v", code, public)
		}
	}
}

func TestNeverIssuedRefIsStaleRefWithHint(t *testing.T) {
	result := NewService().callTool(contract.PublicToolAX, map[string]any{
		"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": "r103"}},
	})
	line := publicLine(t, result)
	if !strings.HasPrefix(line, `{"ok":false,"code":"stale_ref"`) || !strings.Contains(line, `"hint":`) ||
		!strings.Contains(line, `"input_emitted":false`) {
		t.Fatalf("evidence line %s", line)
	}
}

// A native window ref that no longer names its window cannot be recovered
// by find inside that window; only a new apps listing gives a valid wN.
func TestStaleNativeWindowRefHintsAppsListing(t *testing.T) {
	public := axPublicEvidence(map[string]any{
		"failure_code":    "invalid_target",
		"failure_phase":   "precondition",
		"failure_subject": "window",
	}, true, nil)
	if public["code"] != "stale_ref" || public["hint"] != `{"mode":"apps"}` {
		t.Fatalf("stale window ref evidence = %#v", public)
	}
}
