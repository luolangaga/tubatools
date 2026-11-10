package core

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// windowFocusWasConfirmed reports whether the operation confirmed, before
// synthesis, that the target application window was active. It proves neither
// that a control held input focus nor that input reached a control. Clicks are
// excluded because they may take the semantic path, which never confirms
// window focus.
//
// An unchanged tree after such input does not show that the input had no
// effect: formatting changes and file state (a Ctrl+S save, a paragraph
// alignment) often never reach the accessibility tree. Notes therefore point
// the agent to visual or durable evidence instead of concluding that the input
// failed.
func windowFocusWasConfirmed(request AXServiceRequest) bool {
	switch request.operationName() {
	case "keypress", "scroll":
		return true
	}
	return false
}

// TracePath is the file that receives one JSONL line per action; tracing is
// off by default.
//
// A problem can be diagnosed from each step's before and after state without
// reproducing it, which a transcript alone does not allow. It is off by default because every
// step serializes two tree summaries and most calls are never reviewed. It is
// enabled by a path rather than a boolean, because where to write belongs to
// the caller and a diagnostic facility should not choose a directory itself.
func TracePath() string {
	return strings.TrimSpace(os.Getenv("COMPUTER_USE_TRACE_FILE"))
}

// traceSummary summarizes a tree instead of recording it whole. Full trees
// would make the trace larger than the transcript, while a review mainly needs
// to know whether and where the state changed; error-context files carry the
// detail.
func traceSummary(snapshot *AppSnapshot) map[string]any {
	if snapshot == nil {
		return nil
	}
	return map[string]any{
		"window":   snapshot.WindowTitle,
		"elements": len(snapshot.Elements),
		"focused":  snapshot.FocusedSummary,
		"selected": snapshot.SelectedText,
	}
}

func (s *Service) appendTrace(app string, request AXServiceRequest, before, after *AppSnapshot, notes []string, failed bool) {
	path := TracePath()
	if path == "" {
		return
	}
	record := map[string]any{
		"at":     time.Now().Format(time.RFC3339Nano),
		"tool":   request.operationName(),
		"app":    app,
		"failed": failed,
		"before": traceSummary(before),
		"after":  traceSummary(after),
		"notes":  notes,
	}
	// Record the action arguments without snapshot data: the request carries
	// element records and window bounds that would grow each line to
	// kilobytes.
	args := map[string]any{}
	if request.Element != nil {
		args["element_index"] = request.Element.Index
		args["element_role"] = request.Element.ControlType
		args["element_name"] = request.Element.Name
	}
	if request.SemanticTarget != nil {
		args["semantic_selector"] = request.SemanticTarget.Selector
	}
	if request.SourceRef != "" {
		args["source_ref"] = request.SourceRef
	}
	if request.Action != nil {
		request.Action.appendTraceFields(args)
	}
	record["args"] = args

	line, err := json.Marshal(record)
	if err != nil {
		return
	}
	// Append, and give up silently on failure: a diagnostic trace must not
	// fail a normal action because the disk is full. Error context follows
	// the same rule.
	if dir := filepath.Dir(path); dir != "" && dir != "." {
		if os.MkdirAll(dir, 0o755) != nil {
			return
		}
	}
	file, err := os.OpenFile(path, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o644)
	if err != nil {
		return
	}
	defer file.Close()
	file.Write(append(line, '\n'))
}
