package transport

import (
	"encoding/json"
	"fmt"
	"reflect"
	"strings"
	"testing"
	"unicode/utf8"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func findDeliveryWire(t *testing.T, svc *core.Service, args map[string]any) core.ToolCallResult {
	t.Helper()
	wire := handleMCPRequest(map[string]any{
		"jsonrpc": "2.0", "id": 1, "method": "tools/call",
		"params": map[string]any{"name": "AX", "arguments": args},
	}, svc)
	encoded, err := json.Marshal(wire)
	if err != nil {
		t.Fatal(err)
	}
	var decoded struct {
		Result core.ToolCallResult `json:"result"`
	}
	if err := json.Unmarshal(encoded, &decoded); err != nil {
		t.Fatal(err)
	}
	text := core.ResultText(decoded.Result)
	budgetError := decoded.Result.IsError && (strings.Contains(text, "budget exhausted.") ||
		decoded.Result.StructuredContent["failure_code"] == "find_delivery_budget_exhausted")
	textLimit := core.MaxInlineChars()
	if budgetError {
		textLimit = 512
	}
	if !utf8.ValidString(text) || len(text) > textLimit {
		t.Fatalf("final serialized MCP text broke its budget (%d bytes): %.400s", len(text), text)
	}
	// MAX_INLINE_CHARS remains a model-text budget, not a JSON wire-byte cap.
	// encoding/json may expand each content byte to six escaped bytes.
	if len(encoded) > 6*textLimit+4096 {
		t.Fatalf("wire overhead unbounded: wire=%d text=%d", len(encoded), len(text))
	}
	return decoded.Result
}

func TestFindDeliveryLiteralTruncationMarkerThroughTransport(t *testing.T) {
	for _, protocol := range []string{core.MCPProtocolLegacy, core.MCPProtocolStructured} {
		for _, structured := range []string{"0", "1"} {
			t.Run(protocol+"/"+structured, func(t *testing.T) {
				t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "24000")
				t.Setenv("COMPUTER_USE_STRUCTURED_CONTENT", structured)
				svc, snapshot, _ := transportFindDeliveryMany(t, 3)
				snapshot.Elements[1].Value = "[TRUNCATED: context]"
				snapshot.Elements[3].Name = "[TRUNCATED: example]"
				snapshot.Elements[3].Value = "[TRUNCATED: value]"
				if err := svc.BuildAndRenderAXSnapshot(snapshot, contract.TreeViewWorking); err != nil {
					t.Fatal(err)
				}
				backend := &transportRecordingPlatformBackend{
					accessibilityResponse: &core.ProviderAccessibilityResult{OK: true, Snapshot: snapshot},
				}
				svc.Backend, svc.ProtocolVersion = backend, protocol
				direct := findDeliveryWire(t, svc, map[string]any{
					"mode": "find", "app": "Independent",
					"query": map[string]any{"name_equals": "[TRUNCATED: example]", "role": "listitem"},
				})
				if direct.IsError || len(transportFindDeliveredTargets.FindAllStringSubmatch(core.ResultText(direct), -1)) != 1 {
					t.Fatalf("literal marker rejected exact match: %s", core.ResultText(direct))
				}
				if !strings.Contains(core.ResultText(direct), "[TRUNCATED: context]") ||
					transportFindDeliveryModelEvidence(t, direct)["tree_complete"] == false {
					t.Fatalf("literal context was omitted or classified as truncation: %s", core.ResultText(direct))
				}
				value := findDeliveryWire(t, svc, map[string]any{
					"mode": "find", "app": "Independent",
					"query": map[string]any{"text_equals": "[TRUNCATED: value]", "role": "listitem"},
				})
				if value.IsError || !strings.Contains(core.ResultText(value), `matched_text="[TRUNCATED: value]"`) {
					t.Fatalf("literal matched value rejected: %s", core.ResultText(value))
				}
				first := findDeliveryWire(t, svc, map[string]any{
					"mode": "find", "app": "Independent",
					"query": map[string]any{"role": "listitem"}, "limit": 1,
				})
				cursor, _ := transportFindDeliveryModelEvidence(t, first)["next_cursor"].(string)
				if cursor == "" {
					t.Fatalf("missing first-page cursor: %s", core.ResultText(first))
				}
				reads := len(backend.observeRequests)
				for ordinal := 2; ordinal <= 3; ordinal++ {
					page := findDeliveryWire(t, svc, map[string]any{"mode": "find", "cursor": cursor, "limit": 1})
					targets := transportFindDeliveredTargets.FindAllStringSubmatch(core.ResultText(page), -1)
					evidence := transportFindDeliveryModelEvidence(t, page)
					if page.IsError || len(targets) != 1 || targets[0][1] != fmt.Sprint(ordinal) ||
						evidence["find_page_count"] != float64(1) || evidence["tree_complete"] == false {
						t.Fatalf("literal marker stuck later page %d: %s", ordinal, core.ResultText(page))
					}
					cursor, _ = evidence["next_cursor"].(string)
				}
				if cursor != "" || len(backend.observeRequests) != reads {
					t.Fatal("literal marker pagination refreshed provider or failed to finish")
				}
			})
		}
	}
}

func TestFindDeliveryActualTransportProtocols(t *testing.T) {
	for _, protocol := range []string{core.MCPProtocolLegacy, core.MCPProtocolStructured} {
		for _, structured := range []string{"0", "1"} {
			t.Run(protocol+"/"+structured, func(t *testing.T) {
				t.Setenv("COMPUTER_USE_STRUCTURED_CONTENT", structured)
				t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "24000")
				t.Setenv("COMPUTER_USE_OUTPUT_MODE", "stdout")
				svc, snapshot := transportFindDeliverySnapshot(t, strings.Repeat("x", 30000))
				backend := &transportRecordingPlatformBackend{
					accessibilityResponse: &core.ProviderAccessibilityResult{OK: true, Snapshot: snapshot},
				}
				svc.Backend, svc.ProtocolVersion = backend, protocol
				result := findDeliveryWire(t, svc, map[string]any{
					"mode": "find", "app": "Independent",
					"query": map[string]any{"name_equals": "Target", "role": "listitem"}, "limit": 2,
				})
				text := core.ResultText(result)
				if result.IsError || len(transportFindDeliveredTargets.FindAllStringSubmatch(text, -1)) != 1 {
					t.Fatalf("transport lost unique matched target: %s", text)
				}
				if strings.Contains(text, "recover=find|root") {
					t.Fatal("successful find repeats unusable find recovery")
				}
				if protocol == core.MCPProtocolLegacy || structured == "0" {
					if result.StructuredContent != nil || !strings.Contains(text, `"find_page_count":1`) {
						t.Fatalf("legacy delivery evidence missing: %s", text)
					}
				} else if result.StructuredContent["find_page_count"] != float64(1) {
					t.Fatalf("structured delivery evidence missing: %#v", result.StructuredContent)
				}
				if len(backend.observeRequests) != 1 {
					t.Fatalf("find read count=%d", len(backend.observeRequests))
				}
				svc, snapshot, matches := transportFindDeliveryMany(t, 8)
				svc.ProtocolVersion = protocol
				t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "3800")
				cursor := svc.FindCursorRegistry.Create(snapshot, matches, 0, "listitem")
				page := findDeliveryWire(t, svc, map[string]any{"mode": "find", "cursor": cursor, "limit": 50})
				if page.IsError || len(transportFindDeliveredTargets.FindAllStringSubmatch(core.ResultText(page), -1)) == 0 {
					t.Fatalf("protocol lost budgeted cursor page: %s", core.ResultText(page))
				}
			})
		}
	}
}

func TestFindDeliveryBudgetPagesFreezeAndReplay(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "3800")
	t.Setenv("COMPUTER_USE_STRUCTURED_CONTENT", "1")
	svc, snapshot, matches := transportFindDeliveryMany(t, 8)
	first := svc.RenderFindPage(snapshot, matches, 0, 50, `role~"listitem"`, nil, false)
	count, _ := core.IntEvidence(first.StructuredContent["find_page_count"])
	if first.IsError || count < 1 || count >= len(matches) {
		t.Fatalf("expected budget-driven page even limit >= total: count=%d %s", count, core.ResultText(first))
	}
	cursor, _ := first.StructuredContent["next_cursor"].(string)
	resolution, err := svc.FindCursorRegistry.Resolve(cursor)
	if err != nil || resolution.Cursor.Offset != count {
		t.Fatalf("cursor does not start after complete delivered units: %#v %v", resolution, err)
	}
	targets := transportFindDeliveredTargets.FindAllStringSubmatch(core.ResultText(transportFindDeliveryFinal(first)), -1)
	if len(targets) != count {
		t.Fatalf("page count=%d but delivered units=%d", count, len(targets))
	}
	// Fresh canonical state cannot alter a cached result set or its original refs.
	snapshot.AXSnapshot.ByIndex[count+2].Name = "REPLACED LIVE"
	snapshot.Elements[count+2].Name = "REPLACED LIVE"
	backend := &transportRecordingPlatformBackend{err: fmt.Errorf("cursor must not call provider")}
	svc.Backend, svc.ProtocolVersion = backend, core.MCPProtocolStructured
	seen := map[string]bool{}
	for _, target := range targets {
		seen[target[1]] = true
	}
	for cursor != "" {
		args := map[string]any{"mode": "find", "cursor": cursor, "limit": 50}
		page := findDeliveryWire(t, svc, args)
		replay := findDeliveryWire(t, svc, args)
		got := transportFindDeliveredTargets.FindAllStringSubmatch(core.ResultText(page), -1)
		repeated := transportFindDeliveredTargets.FindAllStringSubmatch(core.ResultText(replay), -1)
		if page.IsError || !reflect.DeepEqual(got, repeated) || strings.Contains(core.ResultText(page), "REPLACED LIVE") {
			t.Fatalf("cached replay changed targets: %s", core.ResultText(page))
		}
		pageCount, _ := core.IntEvidence(page.StructuredContent["find_page_count"])
		if pageCount != len(got) || pageCount == 0 {
			t.Fatalf("page metadata disagrees with delivered units: %#v", page)
		}
		for _, target := range got {
			if seen[target[1]] {
				t.Fatalf("duplicate ordinal %s", target[1])
			}
			seen[target[1]] = true
		}
		cursor, _ = page.StructuredContent["next_cursor"].(string)
		if cursor != "" {
			next, err := svc.FindCursorRegistry.Resolve(cursor)
			if err != nil || next.Cursor.Offset != len(seen) {
				t.Fatalf("offset skipped undelivered match: %#v %v", next, err)
			}
		}
	}
	if len(seen) != 8 || len(backend.calls) != 0 {
		t.Fatalf("cached pagination coverage=%d backend calls=%v", len(seen), backend.calls)
	}
}

func TestFindDeliveryLongEscapedIdentityAndMatchedTail(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "7000")
	long := strings.Repeat("界\\\"\n", 600) + "TAIL-IDENTITY"
	svc, snapshot := transportFindDeliverySnapshot(t, "")
	snapshot.WindowTitle = long
	snapshot.Elements[0].Name = long
	snapshot.Elements[2].Name = long
	snapshot.Elements[2].ProviderBusName = ":1.42"
	snapshot.Elements[2].ProviderObjectPath = "/independent/target"
	snapshot.Elements[2].ProcessGeneration = "42"
	snapshot.Elements[2].Value = strings.Repeat("v", 30000)
	snapshot.Elements[2].Description = strings.Repeat("d", 30000) + "TAIL-MATCH"
	if err := svc.BuildAndRenderAXSnapshot(snapshot, contract.TreeViewWorking); err != nil {
		t.Fatal(err)
	}
	snapshot.AXSnapshot.ByIndex[2].Properties.Omitted = "provider_children_unavailable"
	canonicalName := snapshot.AXSnapshot.ByIndex[2].Name
	record := snapshot.Elements[2]
	field, text, ok := core.MatchedFindTextField(&record, "TAIL-MATCH", false)
	if !ok || text != record.Description {
		t.Fatal("matched field was shortened before delivery")
	}
	record.FindMatchedField, record.FindMatchedText = field, text
	query := `name=` + long
	result := svc.RenderFindPage(snapshot, []core.ElementRecord{record}, 0, 2, query,
		[]string{"[expect] " + strings.Repeat("note ", 80)}, true)
	final := transportFindDeliveryFinal(result)
	output := core.ResultText(final)
	for _, want := range []string{
		"TAIL-IDENTITY", "TAIL-MATCH", "header_excerpt=true", "display_excerpt=name",
		"provider_children_unavailable", "matched_field=description", "matched_text_truncated=true",
		`"current_state_cause":"independent"`, ` ref="r`,
	} {
		if !strings.Contains(output, want) {
			t.Fatalf("lost %q in final response: %s", want, output)
		}
	}
	if final.IsError || len(output) > core.MaxInlineChars() || !utf8.ValidString(output) {
		t.Fatalf("invalid escaped/multibyte delivery: %s", output)
	}
	if snapshot.AXSnapshot.ByIndex[2].Name != canonicalName ||
		snapshot.AXSnapshot.ByIndex[2].Properties.Omitted != "provider_children_unavailable" {
		t.Fatal("excerpt mutated canonical identity or omission evidence")
	}
	target := transportFindDeliveredTargets.FindStringSubmatch(output)
	var ref string
	if len(target) != 3 || json.Unmarshal([]byte(target[2]), &ref) != nil {
		t.Fatal("invalid target ref")
	}
	entry := svc.RefRegistry.Entries[ref]
	if entry == nil || entry.Name != record.Name || entry.WindowHandle != snapshot.WindowHandle {
		t.Fatalf("display excerpt changed ref identity: %#v", entry)
	}
	for _, protocol := range []string{core.MCPProtocolLegacy, core.MCPProtocolStructured} {
		for _, structured := range []string{"0", "1"} {
			t.Setenv("COMPUTER_USE_STRUCTURED_CONTENT", structured)
			svc.ProtocolVersion = protocol
			t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "7000")
			svc.Backend = &transportRecordingPlatformBackend{
				accessibilityResponse: &core.ProviderAccessibilityResult{OK: true, Snapshot: snapshot},
			}
			longTarget := findDeliveryWire(t, svc, map[string]any{
				"mode": "find", "app": "Independent",
				"query": map[string]any{"text_contains": "TAIL-MATCH", "role": "listitem"}, "limit": 2,
			})
			if longTarget.IsError || !strings.Contains(core.ResultText(longTarget), "TAIL-MATCH") ||
				len(transportFindDeliveredTargets.FindAllStringSubmatch(core.ResultText(longTarget), -1)) != 1 {
				t.Fatalf("long identity/matched text lost in actual transport: %s", core.ResultText(longTarget))
			}
			// Freeze the original complete identity through the production find path.
			t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "3600")
			svc2, pageSnapshot, matches := transportFindDeliveryMany(t, 8)
			svc2.ProtocolVersion = protocol
			pageSnapshot.WindowTitle = long
			first := svc2.RenderFindPage(pageSnapshot, matches, 0, 1, query, nil, false)
			cursor, ok := first.StructuredContent["next_cursor"].(string)
			if first.IsError || !ok {
				t.Fatalf("long header failed to produce cached page: %s", core.ResultText(first))
			}
			page := findDeliveryWire(t, svc2, map[string]any{"mode": "find", "cursor": cursor, "limit": 50})
			if page.IsError || !strings.Contains(core.ResultText(page), "header_excerpt=true") {
				t.Fatalf("long header lost at transport: %s", core.ResultText(page))
			}
		}
	}
}

func TestFindDeliveryUnaddressableAndTinyBudgets(t *testing.T) {
	svc, snapshot := transportFindDeliverySnapshot(t, "")
	node := snapshot.AXSnapshot.ByIndex[2]
	node.Ref, node.SemanticTarget, node.LocatorKind = "", "", ""
	node.SemanticIdentityAmbiguous = true
	result := svc.RenderFindPage(snapshot, snapshot.Elements[2:], 0, 2, "target", nil, false)
	text := core.ResultText(transportFindDeliveryFinal(result))
	if result.IsError || !strings.Contains(text, "addressable=false reason=stable_identity_unavailable") ||
		strings.Contains(text, ` ref="r`) {
		t.Fatalf("unaddressability not delivered honestly: %s", text)
	}
	for _, budget := range []string{"1", "32", "128", "256"} {
		t.Run(budget, func(t *testing.T) {
			t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", budget)
			svc, snapshot := transportFindDeliverySnapshot(t, "")
			result := svc.RenderFindPage(snapshot, snapshot.Elements[2:], 0, 2, "target", nil, false)
			final := transportFindDeliveryFinal(result)
			if !final.IsError || len(core.ResultText(final)) > 512 ||
				strings.Contains(core.ResultText(final), ` ref="r`) ||
				len(svc.FindCursorRegistry.Entries) != 0 || len(svc.FindCursorRegistry.ResultSets) != 0 {
				t.Fatalf("tiny budget exposed partial unit or cursor: %#v text=%s", final, core.ResultText(final))
			}
			// No-delivery continuation must leave its input cursor unchanged.
			for _, protocol := range []string{core.MCPProtocolLegacy, core.MCPProtocolStructured} {
				for _, structured := range []string{"0", "1"} {
					t.Setenv("COMPUTER_USE_STRUCTURED_CONTENT", structured)
					svc.ProtocolVersion = protocol
					cursor := svc.FindCursorRegistry.Create(snapshot, snapshot.Elements[2:], 0, "target")
					before := len(svc.FindCursorRegistry.Entries)
					page := findDeliveryWire(t, svc, map[string]any{"mode": "find", "cursor": cursor, "limit": 2})
					resolution, err := svc.FindCursorRegistry.Resolve(cursor)
					if !page.IsError || err != nil || resolution.Cursor.Offset != 0 ||
						len(svc.FindCursorRegistry.Entries) != before {
						t.Fatalf("tiny budget advanced a cursor: %#v %v", page, err)
					}
					payload, _ := json.Marshal(page)
					if strings.Contains(string(payload), `"preflight_only":true`) ||
						!strings.Contains(core.ResultText(page)+string(payload), "cached_observation_page") {
						t.Fatalf("cached no-fit error lost truthful scope: %s", payload)
					}
				}
			}
			for _, protocol := range []string{core.MCPProtocolLegacy, core.MCPProtocolStructured} {
				for _, structured := range []string{"0", "1"} {
					t.Setenv("COMPUTER_USE_STRUCTURED_CONTENT", structured)
					svc, snapshot := transportFindDeliverySnapshot(t, "")
					backend := &transportRecordingPlatformBackend{
						accessibilityResponse: &core.ProviderAccessibilityResult{OK: true, Snapshot: snapshot},
					}
					svc.Backend, svc.ProtocolVersion = backend, protocol
					page := findDeliveryWire(t, svc, map[string]any{
						"mode": "find", "app": "Independent", "query": map[string]any{"name_equals": "Target"},
					})
					payload, _ := json.Marshal(page)
					if !page.IsError || len(backend.observeRequests) != 1 ||
						strings.Contains(string(payload), `"preflight_only":true`) ||
						!strings.Contains(core.ResultText(page)+string(payload), "independent_ui_read") {
						t.Fatalf("sampled no-fit error lost truthful scope or reread: %s calls=%v", payload, backend.calls)
					}
				}
			}
		})
	}
}

func TestFindDeliveryWireEscapingIsSeparateFromTextBudget(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "24000")
	t.Setenv("COMPUTER_USE_STRUCTURED_CONTENT", "0")
	svc, snapshot, matches := transportFindDeliveryMany(t, 20)
	for i := range matches {
		matches[i].Name = fmt.Sprintf("%02d", i) + strings.Repeat("<", 240)
		matches[i].Description = ""
		snapshot.Elements[i+2] = matches[i]
	}
	if err := svc.BuildAndRenderAXSnapshot(snapshot, contract.TreeViewWorking); err != nil {
		t.Fatal(err)
	}
	svc.Backend = &transportRecordingPlatformBackend{
		accessibilityResponse: &core.ProviderAccessibilityResult{OK: true, Snapshot: snapshot},
	}
	result := findDeliveryWire(t, svc, map[string]any{
		"mode": "find", "app": "Independent", "query": map[string]any{"role": "listitem"}, "limit": 20,
	})
	wire, _ := json.Marshal(jsonRPCResult(1, result))
	t.Logf("decoded_text_bytes=%d decoded_runes=%d encoded_wire_bytes=%d cap=%d",
		len(core.ResultText(result)), len([]rune(core.ResultText(result))), len(wire), core.MaxInlineChars())
	if result.IsError || len(wire) <= core.MaxInlineChars() {
		t.Fatalf("escaping fixture did not exercise independent wire overhead: %s", core.ResultText(result))
	}
}

func TestFindExposureTransport(t *testing.T) {
	for _, protocol := range []string{core.MCPProtocolLegacy, core.MCPProtocolStructured} {
		for _, structured := range []string{"0", "1"} {
			t.Run(fmt.Sprintf("%s/%s", protocol, structured), func(t *testing.T) {
				t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "2000")
				t.Setenv("COMPUTER_USE_STRUCTURED_CONTENT", structured)
				svc, snapshot, matches := transportFindExposureFixture(t, 8)
				first := svc.RenderFindPage(snapshot, matches, 0, 1, "target", nil, false)
				cursor, _ := first.StructuredContent["next_cursor"].(string)
				svc.ProtocolVersion = protocol
				svc.ExposedHiddenRefs = map[string]map[int]core.ElementRecord{}
				result := findDeliveryWire(t, svc, map[string]any{"mode": "find", "cursor": cursor, "limit": 1})
				if result.IsError {
					t.Fatalf("transport rejected page: %s", core.ResultText(result))
				}
				transportAssertFindExposureDelivered(t, svc, snapshot, result)
			})
		}
	}
}

func TestFindTextExcerptDeliveredWithinBudget(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "4000")
	svc, snapshot, _ := transportFindDeliveryMany(t, 12)
	for i := 2; i < len(snapshot.Elements); i++ {
		snapshot.Elements[i].ControlType = "paragraph"
		snapshot.Elements[i].Value = strings.Repeat("界🙂\\\"\n", 100)
	}
	if err := svc.BuildAndRenderAXSnapshot(snapshot, contract.TreeViewWorking); err != nil {
		t.Fatal(err)
	}
	svc.Backend = &transportRecordingPlatformBackend{accessibilityResponse: &core.ProviderAccessibilityResult{OK: true, Snapshot: snapshot}}
	result := findDeliveryWire(t, svc, map[string]any{"mode": "find", "app": "Independent", "query": map[string]any{"role": "paragraph"}})
	text := core.ResultText(result)
	if result.IsError || !strings.Contains(text, " text=") || !strings.Contains(text, "text_truncated=true") {
		t.Fatalf("missing bounded excerpts: %s", text)
	}
	targets := transportFindDeliveredTargets.FindAllStringSubmatch(text, -1)
	if len(targets) == 0 || strings.Count(text, " text=") != len(targets) {
		t.Fatalf("excerpt/ref units were split: %s", text)
	}
	record := snapshot.Elements[2]
	record.FindMatchedField, record.FindMatchedText = "value", record.Value
	unit := core.FindMatchUnit(snapshot.AXSnapshot, record, 1)
	if strings.Contains(unit, "  text=") || !strings.Contains(unit, "matched_text=") {
		t.Fatalf("matched value repeated: %s", unit)
	}
}
