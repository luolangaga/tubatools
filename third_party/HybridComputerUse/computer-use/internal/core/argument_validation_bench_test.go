package core

import (
	"encoding/json"
	"errors"
	"strings"
	"testing"
)

// benchmarkCalls are representative valid public calls. Each one ends at a
// backend that fails immediately, so the benchmark measures the per-call
// request path up to the provider boundary, including argument checks.
var benchmarkCalls = []struct {
	name string
	tool string
	args string
}{
	{"ax_observe", "AX", `{"mode":"observe","app":"soffice","observe":"tree","scope":"window"}`},
	{"ax_find", "AX", `{"mode":"find","app":"soffice","query":{"role":"push button","name_contains":"Save","limit":10}}`},
	{"ax_act", "AX", `{"mode":"act","window_ref":"WINDOW","actions":[{"type":"keypress","keys":["CTRL","S"]},{"type":"type","text":"report.odt"},{"type":"keypress","keys":["ENTER"],"postcondition":{"type":"window_title_contains","text":"report","timeout_ms":2000}}]}`},
	{"gui_batch", "GUI", `{"actions":[{"type":"click","x":120,"y":40},{"type":"type","text":"hello"},{"type":"keypress","keys":["CTRL","S"]}],"settle_ms":300}`},
}

func decodeBenchmarkArguments(b *testing.B, raw string) map[string]any {
	b.Helper()
	decoder := json.NewDecoder(strings.NewReader(raw))
	decoder.UseNumber()
	var args map[string]any
	if err := decoder.Decode(&args); err != nil {
		b.Fatal(err)
	}
	return args
}

func BenchmarkCallToolStaticPath(b *testing.B) {
	b.Setenv("COMPUTER_USE_ERROR_CONTEXT", "off")
	for _, call := range benchmarkCalls {
		b.Run(call.name, func(b *testing.B) {
			service, _, windowRef, _ := sheetBatchService(&testing.T{})
			service.Backend = &recordingPlatformBackend{err: errors.New("benchmark backend")}
			args := decodeBenchmarkArguments(b, strings.ReplaceAll(call.args, "WINDOW", windowRef))
			b.ReportAllocs()
			b.ResetTimer()
			for i := 0; i < b.N; i++ {
				service.callTool(call.tool, args)
			}
		})
	}
}
