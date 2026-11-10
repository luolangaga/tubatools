package core

import (
	"fmt"
	"os"
	"sort"
	"strconv"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// MaxInlineChars is the inline character limit for one tool response.
//
// The limit prevents loss rather than saving tokens. MCP clients may replace
// an oversized result wholesale with a file pointer such as
//
//	<persisted-output> Output too large (118.1KB). Full output saved to: …
//
// and a client without file access then receives nothing usable, leaving the
// agent to guess coordinates. File output mode cannot help for the same
// reason; the content itself must fit.
//
// The default of 24000 characters keeps typical small and medium working
// trees inline while keeping trees of 40,000 to 110,000 characters out of the
// conversation history. Use find for deeper local detail; operators can raise
// COMPUTER_USE_MAX_INLINE_CHARS to debug full ancestry.
func MaxInlineChars() int {
	if raw := strings.TrimSpace(os.Getenv("COMPUTER_USE_MAX_INLINE_CHARS")); raw != "" {
		if value, err := strconv.Atoi(raw); err == nil && value > 0 {
			return value
		}
	}
	return 24000
}

// capInline trims an oversized response to the budget on line boundaries and
// states how much was cut. Silent truncation would make the agent believe it
// saw the whole tree, which is worse than losing it.
func capInline(text string) string {
	limit := MaxInlineChars()
	if len(text) <= limit {
		return text
	}
	lines := strings.Split(text, "\n")
	kept := make([]string, 0, len(lines))
	used := 0
	for _, line := range lines {
		if used+len(line)+1 > limit {
			break
		}
		kept = append(kept, line)
		used += len(line) + 1
	}
	return strings.Join(kept, "\n") + fmt.Sprintf(
		"\n\n[TRUNCATED: showing %d of %d lines (%d of %d characters). This is not a "+
			"token-saving trim — MCP clients replace oversized tool results with an "+
			"unreadable file pointer, so an untruncated tree would have reached you as "+
			"nothing at all. Use `find` to query the part you need instead of reading "+
			"the whole tree.]",
		len(kept), len(lines), used, len(text))
}

func textContentChars(result ToolCallResult) int {
	total := 0
	for _, item := range result.Content {
		if item.Type == "text" {
			total += len([]rune(item.Text))
		}
	}
	return total
}

func resultContainsInlineTruncation(result ToolCallResult) bool {
	for _, item := range result.Content {
		if item.Type == "text" &&
			strings.Contains(item.Text, "[TRUNCATED:") {
			return true
		}
	}
	return false
}

func truncateRunes(text string, limit int) string {
	if limit <= 0 {
		return ""
	}
	runes := []rune(text)
	if len(runes) <= limit {
		return text
	}
	return string(runes[:limit])
}

func EnforceAXResponseBudget(result ToolCallResult) ToolCallResult {
	limit := MaxInlineChars()
	const evidenceReserve = 2048
	alreadyTruncated := resultContainsInlineTruncation(result)
	if result.StructuredContent["find_delivery_complete"] == true {
		// Application text is not truncation evidence for atomic find units.
		alreadyTruncated = result.StructuredContent["truncation_reason"] == "response_budget"
	}
	structuralBudgetApplied := false
	coverageIncomplete := 0
	if result.StructuredContent != nil {
		structuralBudgetApplied, _ =
			result.StructuredContent["structural_budget_applied"].(bool)
		if result.StructuredContent["find_delivery_complete"] == true {
			structuralBudgetApplied = result.StructuredContent["find_context_omitted"] == true ||
				result.StructuredContent["find_display_excerpt"] == true
		}
		coverageIncomplete, _ = IntEvidence(
			result.StructuredContent["coverage_incomplete_nodes"],
		)
	}
	contentLimit := limit
	if limit > evidenceReserve {
		contentLimit = limit - evidenceReserve
	}
	truncated := alreadyTruncated || textContentChars(result) > contentLimit
	remaining := contentLimit
	marker := "\n[TRUNCATED: final AX text response budget exhausted; use AX mode=find " +
		"or a rooted observation instead of requesting a larger whole-window tree.]"
	for index := range result.Content {
		item := &result.Content[index]
		if item.Type != "text" {
			continue
		}
		length := len([]rune(item.Text))
		if length <= remaining {
			remaining -= length
			continue
		}
		truncated = true
		available := remaining
		markerLength := len([]rune(marker))
		if available <= markerLength {
			item.Text = truncateRunes(marker, available)
		} else {
			item.Text = truncateRunes(
				item.Text,
				available-markerLength,
			) + marker
		}
		remaining = 0
		for later := index + 1; later < len(result.Content); later++ {
			if result.Content[later].Type == "text" {
				result.Content[later].Text = ""
			}
		}
		break
	}
	reason := "none"
	if truncated {
		reason = "response_budget"
	} else if structuralBudgetApplied {
		reason = "structural_budget"
	}
	treeComplete := !truncated && !structuralBudgetApplied &&
		coverageIncomplete == 0
	responseChars := textContentChars(result)
	if result.StructuredContent != nil {
		result.StructuredContent["response_chars"] = responseChars
		result.StructuredContent["response_budget_chars"] = limit
		result.StructuredContent["tree_complete_for_model"] = treeComplete
		result.StructuredContent["truncation_reason"] = reason
	}
	return result
}

type axCoverageSummary struct {
	ProjectionHidden      int
	ExpandableHidden      int
	AcquisitionIncomplete int
	AcquisitionReasons    []string
}

func providerCoverageSummary(
	annotations []axProviderAnnotation,
) axCoverageSummary {
	summary := axCoverageSummary{}
	reasons := map[string]bool{}
	for _, annotation := range annotations {
		if annotation.Kind == axCellSummaryAnnotation {
			// A summary of cells left out by design, not a coverage gap.
			continue
		}
		count := annotation.Count
		if count <= 0 {
			count = 1
		}
		hasProjection := false
		hasExpandable := false
		hasAcquisitionGap := false
		for _, reason := range strings.Split(annotation.Reason, ",") {
			reason = strings.TrimSpace(reason)
			switch reason {
			case "filtered", "pressure":
				hasProjection = true
			case "collapsed_menu", "provider_children_not_rendered":
				hasExpandable = true
			case "":
				hasAcquisitionGap = true
				reasons["unknown"] = true
			default:
				hasAcquisitionGap = true
				reasons[reason] = true
			}
		}
		switch {
		case hasAcquisitionGap:
			summary.AcquisitionIncomplete += count
		case hasExpandable:
			summary.ExpandableHidden += count
		case hasProjection:
			summary.ProjectionHidden += count
		}
	}
	for reason := range reasons {
		summary.AcquisitionReasons = append(
			summary.AcquisitionReasons,
			reason,
		)
	}
	if len(summary.AcquisitionReasons) > 1 {
		sort.Strings(summary.AcquisitionReasons)
	}
	return summary
}

func evidenceIsTrue(value any) bool {
	switch typed := value.(type) {
	case bool:
		return typed
	case string:
		return strings.EqualFold(strings.TrimSpace(typed), "true")
	default:
		return false
	}
}

func axTargetedCoverageFailure(
	full map[string]any,
	mode string,
	isError bool,
) bool {
	switch mode {
	case contract.AXModeFind:
		count, present := IntEvidence(full["find_match_count"])
		return present && count == 0
	case contract.AXModeAssert:
		status := strings.ToLower(strings.TrimSpace(
			fmt.Sprint(full["assertion_status"]),
		))
		return status == "locator_missing"
	case contract.AXModeAct:
		if !isError || evidenceIsTrue(full["input_events_emitted"]) {
			return false
		}
		phase := strings.ToLower(strings.TrimSpace(
			fmt.Sprint(full["failure_phase"]),
		))
		return phase == "" || phase == "resolve" || phase == "preflight"
	default:
		return false
	}
}

func addTreeProjectionMetadata(
	result *ToolCallResult,
	snapshot *AppSnapshot,
) {
	if result == nil || snapshot == nil {
		return
	}
	if result.StructuredContent == nil {
		result.StructuredContent = map[string]any{}
	}
	coverage := providerCoverageSummary(snapshot.Annotations)
	if coverage.ProjectionHidden > 0 {
		result.StructuredContent["projection_hidden_nodes"] =
			coverage.ProjectionHidden
	}
	if coverage.ExpandableHidden > 0 {
		result.StructuredContent["expandable_hidden_nodes"] =
			coverage.ExpandableHidden
	}
	if coverage.AcquisitionIncomplete > 0 {
		result.StructuredContent["coverage_incomplete_nodes"] =
			coverage.AcquisitionIncomplete
		result.StructuredContent["coverage_incomplete_reasons"] =
			coverage.AcquisitionReasons
	}
	if snapshot.AXSnapshot != nil {
		for _, report := range snapshot.AXSnapshot.Distillation {
			if report.Name != "apply_output_budget" {
				continue
			}
			result.StructuredContent["structural_budget_applied"] =
				report.BudgetApplied
			result.StructuredContent["structural_budget_omitted_nodes"] =
				report.BudgetOmittedNodes
			break
		}
	}
}
