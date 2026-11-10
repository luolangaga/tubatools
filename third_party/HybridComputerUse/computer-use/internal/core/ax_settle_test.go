package core

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// Probe #24: GUI settle_ms is published as an integer, but a numeric string
// such as "800" used to be parsed and accepted.
func TestGUISettleMSRejectsNumericString(t *testing.T) {
	if _, err := optionalSettleMS(map[string]any{"settle_ms": "800"}); err == nil {
		t.Fatal(`settle_ms "800" was accepted`)
	}
	result := NewService().dispatch(contract.PublicToolGUI, map[string]any{
		"actions":   []any{map[string]any{"type": "wait"}},
		"settle_ms": "800",
	})
	if !result.IsError || !strings.Contains(ResultText(result), "settle_ms") {
		t.Fatalf("GUI accepted a string settle_ms: %#v", result)
	}
	if got, err := optionalSettleMS(map[string]any{"settle_ms": json.Number("800")}); err != nil || got != 800 {
		t.Fatalf("integer settle_ms rejected: %d %v", got, err)
	}
}
