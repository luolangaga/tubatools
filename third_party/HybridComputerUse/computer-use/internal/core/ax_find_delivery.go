package core

import (
	"fmt"
	"strconv"
	"strings"
	"unicode/utf8"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// These are display excerpts, never selector inputs or canonical mutations.
func findDisplayExcerpt(value string, limit int) (string, bool) {
	value = strings.ToValidUTF8(value, "\uFFFD")
	if len(value) <= limit {
		return value, false
	}
	end := limit / 2
	for end > 0 && !utf8.RuneStart(value[end]) {
		end--
	}
	start := len(value) - limit/2
	for start < len(value) && !utf8.RuneStart(value[start]) {
		start++
	}
	return value[:end] + fmt.Sprintf("...[%d bytes omitted]...", start-end) + value[start:], true
}

func findDisplayNode(source *AXNode) *AXNode {
	node := *source
	node.VisibilityEvidence = cloneVisibilityEvidence(source.VisibilityEvidence)
	node.Parent, node.Children = nil, nil
	omitted := []string{}
	for _, field := range []struct {
		name  string
		value *string
	}{
		{"name", &node.Name}, {"value", &node.Properties.Value},
		{"description", &node.Properties.Description}, {"placeholder", &node.Properties.Placeholder},
	} {
		if field.name == "value" && !roleExposesWorkingValue(node.Role) {
			// The renderer does not show this value; findMatchText does.
			continue
		}
		if value, clipped := findDisplayExcerpt(*field.value, 256); clipped {
			*field.value = value
			omitted = append(omitted, field.name)
		}
	}
	if len(omitted) > 0 {
		if node.Properties.Omitted != "" {
			node.Properties.Omitted += "; "
		}
		node.Properties.Omitted += "display_excerpt=" + strings.Join(omitted, ",")
	}
	return &node
}

func FindMatchUnit(snapshot *AXSnapshot, record ElementRecord, ordinal int) string {
	node := snapshot.ByIndex[record.Index]
	var out strings.Builder
	if node != nil {
		display := findDisplayNode(node)
		fmt.Fprintf(&out, " - match=%d %s\n", ordinal, renderAXNodeLabel(display))
		for _, property := range renderAXProperties(display, AXRenderOptions{}) {
			out.WriteString(renderAXTreePropertyLine(property, 2) + "\n")
		}
	} else {
		name, clipped := findDisplayExcerpt(record.Name, 256)
		fmt.Fprintf(&out, " - match=%d %s %s\n", ordinal, canonicalRole(&record), strconv.Quote(name))
		if clipped {
			out.WriteString("  display_excerpt=name\n")
		}
	}
	if record.FindMatchedField != "" {
		text, clipped := findDisplayExcerpt(record.FindMatchedText, 256)
		fmt.Fprintf(&out, "  matched_field=%s matched_text=%q", record.FindMatchedField, text)
		if clipped {
			out.WriteString(" matched_text_truncated=true")
		}
		out.WriteByte('\n')
	} else if text, clipped, ok := findMatchText(node); ok {
		fmt.Fprintf(&out, "  text=%q", text)
		if clipped {
			out.WriteString(" text_truncated=true")
		}
		out.WriteByte('\n')
	}
	if node != nil && node.Ref != "" {
		// The flat action field: {"type":"click","ref":"rN"}.
		fmt.Fprintf(&out, " - match=%d ref=%q\n", ordinal, node.Ref)
	} else {
		reason := "canonical_node_unavailable"
		if node != nil {
			_, reason, _ = axNodeRefEligibility(snapshot, node)
			if reason == "" {
				reason = "stable_ref_unavailable"
			}
		}
		fmt.Fprintf(&out, " - match=%d addressable=false reason=%s\n", ordinal, reason)
	}
	return out.String()
}

// maxFindTextRunes bounds the text shown for a match whose role carries
// text the tree does not render.
const maxFindTextRunes = 80

// findMatchText supplies text for roles whose value the tree omits. Compare
// the excerpt with displayed fields: a clipped or suppressed description
// cannot establish that the model already received the text.
func findMatchText(node *AXNode) (string, bool, bool) {
	if node == nil || roleExposesWorkingValue(node.Role) {
		return "", false, false
	}
	if inline, clipped := axInlineCellValue(node); inline != "" && !clipped {
		return "", false, false
	}
	value := strings.TrimSpace(strings.ToValidUTF8(node.Properties.Value, "\uFFFD"))
	if comparableTreeText(value) == "" {
		return "", false, false
	}
	text, clipped := shortenAtWord(value, maxFindTextRunes)
	shown := text
	if clipped {
		shown = strings.TrimSuffix(shown, "…")
	}
	display := findDisplayNode(node)
	visible := []string{display.Name}
	for _, property := range renderAXProperties(display, AXRenderOptions{}) {
		if property[0] == "/description" || property[0] == "/placeholder" {
			visible = append(visible, property[1])
		}
	}
	for _, field := range visible {
		if strings.Contains(comparableTreeText(field), comparableTreeText(shown)) {
			return "", false, false
		}
	}
	return text, clipped, true
}

// Exercise the same content cap and evidence serialization as MCP, including the
// text-only client path. No delivered unit may depend on a later prefix trim.
func findDeliveryFits(result ToolCallResult) bool {
	if len(ResultText(result)) > MaxInlineChars() {
		return false
	}
	copy := result
	copy.Content = append([]ContentItem(nil), result.Content...)
	copy.StructuredContent = map[string]any{}
	for key, value := range result.StructuredContent {
		copy.StructuredContent[key] = value
	}
	copy = WithAXModeEvidence(copy, contract.AXModeFind)
	copy = EnforceAXResponseBudget(copy)
	if copy.StructuredContent["truncation_reason"] == "response_budget" ||
		ResultText(copy) != ResultText(result) {
		return false
	}
	copy = WithAXTextEvidence(PrepareAXEvidence(copy))
	return len(ResultText(copy)) <= MaxInlineChars()
}

func findDeliveryBudgetFailure(cached bool) ToolCallResult {
	scope := "independent_ui_read"
	if cached {
		scope = "cached_observation_page"
	}
	result := TextResult("Find budget insufficient; no matches delivered.", true)
	result.StructuredContent = map[string]any{
		"failure_code":                   "find_delivery_budget_exhausted",
		"find_delivery_budget_exhausted": true,
		"find_read_scope":                scope,
		"find_page_count":                0,
		"input_events_emitted":           false,
		"preflight_only":                 false,
	}
	return result.withEvidenceScope("error_only")
}

// Find-only error envelope exception: when no unit fits, even a configured
// one-character budget cannot erase truthful acquisition and zero-delivery
// evidence. This fixed-size envelope carries no target, cursor or UI content.
func boundFindBudgetFailure(result ToolCallResult, scope string) ToolCallResult {
	message := "Read sampled; budget exhausted.\n"
	if scope == "cached_observation_page" {
		message = "Cached read; budget exhausted.\n"
	}
	result.Content = []ContentItem{{Type: "text", Text: message}}
	// Preserve any existing failure/continuation facts rather than overwrite them.
	result.StructuredContent["evidence_scope"] = scope
	result.StructuredContent["find_page_count"] = 0
	result.StructuredContent["failure_code"] = "find_delivery_budget_exhausted"
	result.StructuredContent["preflight_only"] = false
	result.StructuredContent["input_events_emitted"] = false
	delete(result.StructuredContent, "tree_complete")
	return result
}

func (s *Service) deliverFindPage(
	snapshot *AppSnapshot, matched []ElementRecord, offset, limit int,
	query string, notes []string, followsFailedAction bool,
	projected map[int]bool, setID string,
) ToolCallResult {
	if limit <= 0 {
		limit = 20
	}
	if limit > contract.MaxAXFindPageSize {
		limit = contract.MaxAXFindPageSize
	}
	page := newFindPageBuilder(snapshot, matched, offset, query, notes, followsFailedAction)
	units, count := page.selectUnits(limit)
	if count == 0 {
		return findDeliveryBudgetFailure(setID != "")
	}
	end := offset + count
	// Context is optional. Bound every display field and append whole lines only,
	// retaining canonical ordering and semantic ancestors where space permits.
	contextSnapshot := newAXFindSnapshot(
		snapshot.AXSnapshot,
		boundFindContextNodes(buildAXFindContext(snapshot, matched[offset:end], projected)),
	)
	lines, err := RenderAXSnapshotYAML(contextSnapshot, AXRenderOptions{RootKey: "context"})
	if err != nil {
		return TextResult("AX find context renderer failed: "+err.Error(), true)
	}
	included := page.fittingContextLines(units, lines, count)
	nextCursor, continuation := "", ""
	if end < len(matched) || snapshot.findResume != nil {
		if setID == "" {
			nextCursor = s.FindCursorRegistry.Create(snapshot, matched, end, query)
		} else {
			nextCursor = s.FindCursorRegistry.Continue(setID, end)
		}
		continuation = findReservedContinuation
		if nextCursor != "" {
			continuation = findNextPageLine(nextCursor)
		}
	}
	result := page.result(units, findContextPrefix(lines, included), continuation, count, included < len(lines))
	if !findDeliveryFits(result) {
		// A future evidence change must fail closed, not expose a truncated target.
		if nextCursor != "" {
			delete(s.FindCursorRegistry.Entries, nextCursor)
		}
		return findDeliveryBudgetFailure(setID != "")
	}
	s.rememberDeliveredFindExposure(snapshot, matched[offset:end], contextSnapshot, lines[:included])
	return result
}

// findReservedContinuation is the longer of the two continuation forms when
// no cursor could be stored. Page sizing reserves room for both forms.
const findReservedContinuation = " continuation=unavailable_budget refine_query=true"

// findPageBuilder renders one find page for a fixed header, so the page can
// be measured against the response budget with different units and context.
type findPageBuilder struct {
	snapshot            *AppSnapshot
	matched             []ElementRecord
	offset              int
	header              string
	headerClipped       bool
	diagnostics         string
	followsFailedAction bool
}

func newFindPageBuilder(
	snapshot *AppSnapshot, matched []ElementRecord, offset int,
	query string, notes []string, followsFailedAction bool,
) findPageBuilder {
	title, titleClipped := findDisplayExcerpt(snapshot.WindowTitle, 256)
	queryText, queryClipped := findDisplayExcerpt(query, 256)
	header := fmt.Sprintf("window: %q\nmatches: %d query=%q", title, len(matched), queryText)
	if titleClipped || queryClipped {
		header += " header_excerpt=true"
	}
	if line := snapshot.scopeFallbackLine(); line != "" {
		header = fmt.Sprintf("window: %q\n%s\nmatches: %d query=%q", title, line, len(matched), queryText)
		if titleClipped || queryClipped {
			header += " header_excerpt=true"
		}
	}
	diagnostics := ""
	for _, note := range modelFacingAXNotes(notes) {
		diagnostics += "Note: " + note + "\n"
	}
	return findPageBuilder{
		snapshot:            snapshot,
		matched:             matched,
		offset:              offset,
		header:              header,
		headerClipped:       titleClipped || queryClipped,
		diagnostics:         diagnostics,
		followsFailedAction: followsFailedAction,
	}
}

func (page findPageBuilder) result(units, context, continuation string, count int, omitted bool) ToolCallResult {
	text := fmt.Sprintf("%s range=%d..%d%s\n%saddressability:\n%s",
		page.header, page.offset+1, page.offset+count, continuation, page.diagnostics, units)
	text += context
	if omitted {
		text += "context_omitted=true\n"
	}
	result := withFailedActionContinuity(TextResult(text, false), page.followsFailedAction)
	addTreeProjectionMetadata(&result, page.snapshot)
	result.StructuredContent["find_delivery_complete"] = true
	result.StructuredContent["find_context_omitted"] = omitted
	result.StructuredContent["find_display_excerpt"] =
		page.headerClipped || strings.Contains(units+context, "bytes omitted]")
	result.StructuredContent["find_match_count"] = len(page.matched)
	result.StructuredContent["find_page_start"] = page.offset + 1
	result.StructuredContent["find_page_count"] = count
	if strings.HasPrefix(continuation, " next_cursor=") {
		cursor := strings.TrimPrefix(continuation, " next_cursor=")
		cursor, _, _ = strings.Cut(cursor, "\n")
		result.StructuredContent["next_cursor"] = cursor
	}
	return result
}

// fits reports whether the page fits the budget. A page that does not end
// the match list must fit with either continuation form, so it is measured
// before a cursor is committed. So must the last page of a walk that
// stopped early, whose cursor continues the walk.
func (page findPageBuilder) fits(units, context string, count int, omitted bool) bool {
	if page.offset+count >= len(page.matched) && page.snapshot.findResume == nil {
		return findDeliveryFits(page.result(units, context, "", count, omitted))
	}
	return findDeliveryFits(page.result(units, context, findReservedContinuation, count, omitted)) &&
		findDeliveryFits(page.result(units, context, findNextPageLine("f18446744073709551615"), count, omitted))
}

// selectUnits returns the match units that fit, in order, up to limit.
func (page findPageBuilder) selectUnits(limit int) (string, int) {
	units, count := "", 0
	for index := page.offset; index < len(page.matched) && count < limit; index++ {
		unit := FindMatchUnit(page.snapshot.AXSnapshot, page.matched[index], index+1)
		if !page.fits(units+unit, "", count+1, true) {
			break
		}
		units += unit
		count++
	}
	return units, count
}

// fittingContextLines returns how many leading context lines fit beside
// units. It searches prefixes rather than serializing the entire response
// once per line.
func (page findPageBuilder) fittingContextLines(units string, lines []string, count int) int {
	included := len(lines)
	if !page.fits(units, findContextPrefix(lines, included), count, false) {
		low, high := 0, len(lines)-1
		for low < high {
			mid := (low + high + 1) / 2
			if page.fits(units, findContextPrefix(lines, mid), count, true) {
				low = mid
			} else {
				high = mid - 1
			}
		}
		included = low
	}
	return included
}

func findContextPrefix(lines []string, count int) string {
	if count == 0 {
		return ""
	}
	return strings.Join(lines[:count], "\n") + "\n"
}

// boundFindContextNodes copies the context forest with bounded display fields.
func boundFindContextNodes(nodes []*AXNode) []*AXNode {
	out := make([]*AXNode, 0, len(nodes))
	for _, node := range nodes {
		display := findDisplayNode(node)
		display.Children = boundFindContextNodes(node.Children)
		out = append(out, display)
	}
	return out
}

// rememberDeliveredFindExposure records hidden refs the model has now seen.
// Only the complete units and context lines surviving the final MCP budget
// check establish exposure. Ref assignment alone is not delivery evidence.
func (s *Service) rememberDeliveredFindExposure(
	snapshot *AppSnapshot, delivered []ElementRecord,
	contextSnapshot *AXSnapshot, deliveredContext []string,
) {
	remember := func(node *AXNode) {
		if node == nil || !isShortAXRef(node.Ref) {
			return
		}
		if record, hidden := snapshot.findHiddenRecords[node.ProviderIndex]; hidden {
			s.rememberExposedHiddenRef(snapshot, record)
		}
	}
	for _, record := range delivered {
		remember(snapshot.AXSnapshot.ByIndex[record.Index])
	}
	deliveredLines := make(map[string]bool, len(deliveredContext))
	for _, line := range deliveredContext {
		deliveredLines[line] = true
	}
	var rememberContext func([]*AXNode, int)
	rememberContext = func(nodes []*AXNode, depth int) {
		for _, node := range nodes {
			hasChildren := len(node.Children) > 0 ||
				len(renderAXProperties(node, AXRenderOptions{})) > 0
			if deliveredLines[renderAXTreeNodeLine(node, depth, hasChildren)] {
				remember(node)
			}
			rememberContext(node.Children, depth+1)
		}
	}
	rememberContext(contextSnapshot.Roots, 1)
}

// findNextPageLine is the continuation printed on a truncated find page: the
// cursor, then the exact call that returns the next page.
func findNextPageLine(cursor string) string {
	return " next_cursor=" + cursor + "\nnext page: " +
		`{"mode":"find","cursor":"` + cursor + `"}` +
		"; or narrow the query, e.g. name_contains"
}
