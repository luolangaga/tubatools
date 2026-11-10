package core

import (
	"errors"
	"fmt"
	"strconv"
	"strings"
)

// lookupBySelector finds the unique element matching `<role> "<name>"`,
// `"<name>"` or `<role>`.
//
// The syntax deliberately matches snapshot lines verbatim: the tree renders
// `4 push button "Save" [..]`, so the selector is `push button "Save"`, copied
// from the line with no second syntax to learn.
//
// Several matches are never resolved by picking one; the candidates are
// listed with their indices so the caller can narrow the selector.
func lookupBySelector(snapshot *AppSnapshot, selector, hint string) (*ElementRecord, error) {
	return lookupBySelectorFor(snapshot, selector, "", "")
}

func lookupBySelectorFor(snapshot *AppSnapshot, selector, declared, tool string) (*ElementRecord, error) {
	role, name, hasName, rest := parseSelector(selector)
	preds, predErr := parsePredicates(rest)
	if predErr != nil {
		e := &toolError{tool: tool, summary: fmt.Sprintf(
			"the selector %q has a trailing part I cannot read: %s.", selector, predErr)}
		e.add("Selector", strconv.Quote(selector))
		e.add("Snapshot", snapshotAge(snapshot))
		e.step("parsed selector as role=%q name=%q", role, name)
		e.step("left over after the name: %q", rest)
		e.next = selectorPredicateHelp
		return nil, e
	}
	// Identity comes from two tiers, both far ahead of positional selection.
	//
	// The tiers here are name, then description. Description is a tier of its
	// own because some toolkits describe many nodes that have no name.
	//
	// AT-SPI accessible_id would be a stronger tier, but it is unset in practice, so that tier is unavailable on the desktop.
	matches := selectorMatches(snapshot, snapshot.Elements, role, name, hasName, preds, 0)
	if len(matches) == 1 {
		copy := matches[0]
		return &copy, nil
	}
	base := func(summary string) *toolError {
		e := &toolError{tool: tool, summary: summary}
		e.add("Selector", strconv.Quote(selector))
		if declared != "" {
			e.add("Declared", strconv.Quote(declared))
		}
		e.add("Snapshot", snapshotAge(snapshot))
		return e
	}
	if len(matches) == 0 {
		e := base(fmt.Sprintf("no element matches the selector %q.", selector)).
			step("parsed selector as role=%q name=%q%s", role, name, describePredicates(preds)).
			step("searched %d elements by name, then by desc=\"…\"", len(snapshot.Elements))
		e.log = append(e.log, closestElements(snapshot, declared+" "+name, 5)...)
		e.next = "selectors are written exactly as the snapshot renders them, e.g. `push button \"Save\"`, or just `\"Save\"` to match by name alone. The quoted part matches an element's name, or its desc=\"…\" when no name matches. If the UI has changed, call AX mode=observe again."
		if len(preds) > 0 {
			// With predicates, the likeliest cause is a state change (a box
			// that is no longer checked, say) rather than a mistyped name, so
			// say that before the general advice.
			e.next = "the predicates are matched against the element's current state, which may have changed since the snapshot — try the selector without them first, then call AX mode=observe to see the state now. " + e.next
		}
		// Call out an implausibly small snapshot explicitly instead of leaving
		// the agent to infer it from the element count. A single-digit count
		// almost always means the window was still loading (for example, a
		// browser showing an error page) or a different window was captured.
		// Neither is a mistyped selector, and the generic advice, which only
		// covers that case, would lead the agent astray.
		if n := len(snapshot.Elements); n > 0 && n < 10 {
			e.next = fmt.Sprintf("this snapshot has only %d element(s), which almost always means the window was still loading or a different window got captured — not that the selector is wrong. Call AX mode=observe again before trying another selector. ", n) + e.next
		}
		return nil, e
	}

	// Never pick one of several matches: picking the first match would silently act on the wrong element,
	// while an error tells the caller at once that the match was unexpected.
	e := base(fmt.Sprintf("the selector %q is ambiguous: it matches %d elements.",
		selector, len(matches)))
	e.step("parsed selector as role=%q name=%q%s", role, name, describePredicates(preds))
	e.step("matched %d elements:", len(matches))
	for i, record := range matches {
		if i == 6 {
			e.log = append(e.log, fmt.Sprintf("    … and %d more", len(matches)-6))
			break
		}
		// Only elements with a name or description get an `aka` selector; for
		// anonymous elements the alternative is the index that already starts
		// the line.
		line := fmt.Sprintf("    %-4d %s", record.Index, describeRecord(&record))
		if record.Name != "" {
			line = fmt.Sprintf("    %-4d %-26s aka %s %q",
				record.Index, describeRecord(&record), record.ControlType, record.Name)
		} else if record.Description != "" {
			line = fmt.Sprintf("    %-4d %-26s aka %s %q",
				record.Index, describeRecord(&record), record.ControlType, record.Description)
		}
		e.log = append(e.log, line)
	}
	if role != "" && !hasName {
		e.next = "narrow it by adding the name in quotes, e.g. `" + role + " \"Save\"`, or pass one of the indices above."
	} else {
		e.next = "narrow it by adding the role, e.g. `push button \"Save\"`, or pass one of the indices above."
	}
	// Elements with the same role and name may differ only by desc. The
	// candidate list above already prints it; this hint tells the caller to
	// filter on it. The example comes from an actual candidate or is omitted,
	// because an invented desc would be copied verbatim and match nothing.
	if example := describeSelectorExample(matches); len(preds) == 0 && example != "" {
		e.next += " If the candidates above differ only by desc=\"…\", add it as a predicate: `" +
			example + "`."
	}
	return nil, e
}

// selectorMatches returns the records among candidates that a parsed
// selector resolves to: name matches, or description matches when no name
// matches. A positive limit stops once that many name matches are found.
func selectorMatches(
	snapshot *AppSnapshot, candidates []ElementRecord,
	role, name string, hasName bool, preds []selectorPredicate, limit int,
) []ElementRecord {
	var byName, byDescription []ElementRecord
	for _, record := range candidates {
		if !selectorRoleMatches(&record, role) &&
			!(roleKey(role) == "dialog" && modalWindowRoot(snapshot, &record)) {
			continue
		}
		if len(preds) > 0 && !recordMatchesPredicates(&record, preds) {
			continue
		}
		if !hasName {
			// Without a role or predicate, an empty selector would match the
			// whole tree. A predicate alone is a narrow enough identity: a desc
			// such as `[desc="View options"]` can be the only difference
			// between same-named toggle buttons.
			if role == "" && len(preds) == 0 {
				continue
			}
			byName = append(byName, record)
		} else if equalSemanticIdentity(record.Name, name) {
			byName = append(byName, record)
		} else if equalSemanticIdentity(record.Description, name) {
			byDescription = append(byDescription, record)
		}
		if limit > 0 && len(byName) >= limit {
			break
		}
	}
	if len(byName) == 0 {
		// Fall back to description only when no name matches. Mixing
		// lower-priority matches into a higher-priority hit would turn an
		// exact name match into an ambiguity.
		return byDescription
	}
	return byName
}

// semanticIdentityIndex groups a snapshot's records by the normalized name,
// description and placeholder that a selector's quoted part and its desc
// and placeholder predicates compare with. Deriving a selector for every
// record of a snapshot then checks each candidate against one group
// instead of every record.
type semanticIdentityIndex map[string][]ElementRecord

func newSemanticIdentityIndex(snapshot *AppSnapshot) semanticIdentityIndex {
	index := semanticIdentityIndex{}
	for _, record := range snapshot.Elements {
		keys := map[string]bool{}
		for _, value := range []string{record.Name, record.Description, record.Placeholder} {
			key := normalizedSemanticWhitespace(value)
			if !keys[key] {
				keys[key] = true
				index[key] = append(index[key], record)
			}
		}
	}
	return index
}

// candidates returns a group that holds every record the selector can
// match, or all records when the selector has no identity text.
func (index semanticIdentityIndex) candidates(
	snapshot *AppSnapshot, name string, hasName bool, preds []selectorPredicate,
) []ElementRecord {
	if index == nil {
		return snapshot.Elements
	}
	if hasName {
		return index[normalizedSemanticWhitespace(name)]
	}
	for _, pred := range preds {
		if pred.kind == "desc" || pred.kind == "placeholder" {
			return index[normalizedSemanticWhitespace(pred.value)]
		}
	}
	return snapshot.Elements
}

// uniqueSelectorMatch reports the one record a selector resolves to, like
// lookupBySelector without building an error. A nil index searches every
// record.
func uniqueSelectorMatch(
	snapshot *AppSnapshot, index semanticIdentityIndex, selector string,
) (*ElementRecord, bool) {
	role, name, hasName, rest := parseSelector(selector)
	preds, err := parsePredicates(rest)
	if err != nil {
		return nil, false
	}
	candidates := index.candidates(snapshot, name, hasName, preds)
	matches := selectorMatches(snapshot, candidates, role, name, hasName, preds, 2)
	if len(matches) != 1 {
		return nil, false
	}
	return &matches[0], true
}

func selectorRoleMatches(record *ElementRecord, requested string) bool {
	requested = roleKey(requested)
	if requested == "" {
		return true
	}
	provider := canonicalRole(record)
	return roleKey(provider) == requested ||
		roleKey(string(canonicalRoleDescriptor(provider, nil).ModelRole)) == requested
}

// modalWindowRoot reports whether record is the root of a top-level window
// that the provider reports as modal. Toolkits expose some modal dialogs as
// a plain frame or window (Thunderbird's "New Folder"), so
// the role alone does not show that a dialog is open; a selector for role
// dialog also matches such a window.
func modalWindowRoot(snapshot *AppSnapshot, record *ElementRecord) bool {
	if snapshot == nil || record == nil ||
		strings.TrimSpace(record.ProviderObjectPath) == "" {
		return false
	}
	for _, window := range snapshot.Windows {
		if window.Modal &&
			window.ProviderObjectPath == record.ProviderObjectPath &&
			window.ProviderBusName == record.ProviderBusName {
			return true
		}
	}
	return false
}

// roleKey folds a role for comparison: case, spaces, underscores and
// hyphens are ignored, so "menu item", "menuitem", "menu_item" and
// "Menu-Item" name the same role.
func roleKey(role string) string {
	var out strings.Builder
	for _, char := range strings.ToLower(role) {
		switch char {
		case ' ', '_', '-', '\t':
			continue
		}
		out.WriteRune(char)
	}
	return out.String()
}

// parseSelector splits a selector into role, name, whether a name was quoted,
// and the remainder after the closing quote. The remainder must be returned
// rather than dropped: ignoring it would silently discard constraints such as
//
//	push button "Save" [checked]
//	push button "OK" >> nth=0
//
// and report an unconstrained match as success. parsePredicates enforces the
// predicates it recognizes and rejects everything else.
func parseSelector(selector string) (string, string, bool, string) {
	selector = strings.TrimSpace(selector)
	start := strings.Index(selector, "\"")
	// The role segment ends at the first `[` or the first `"`, whichever
	// comes first. Otherwise `toggle button [desc="Show operations"]` would
	// take the predicate's quote as the start of the name and parse the role
	// as `toggle button [desc=`.
	if bracket := strings.Index(selector, "["); bracket >= 0 &&
		(start < 0 || bracket < start) {
		return strings.TrimSpace(selector[:bracket]), "", false,
			strings.TrimSpace(selector[bracket:])
	}
	if start < 0 {
		// With neither a quote nor a bracket, the whole selector is the role.
		return selector, "", false, ""
	}
	end := closingQuote(selector, start)
	if end <= start {
		return strings.TrimSpace(selector[:start]), "", false, ""
	}
	name := unescapeQuoted(selector[start+1 : end])
	return strings.TrimSpace(selector[:start]), name,
		true, strings.TrimSpace(selector[end+1:])
}

// closingQuote finds the quote that closes the one at start, skipping `\"`
// escapes. Quotes must pair exactly: taking the last quote in the string would
// swallow trailing predicates such as `[desc="…"]` into the name.
func closingQuote(text string, start int) int {
	for i := start + 1; i < len(text); i++ {
		if text[i] == '\\' {
			i++
			continue
		}
		if text[i] == '"' {
			return i
		}
	}
	return -1
}

// unescapeQuoted reverses the escaping applied by the provider's quoted().
func unescapeQuoted(text string) string {
	var out strings.Builder
	for i := 0; i < len(text); i++ {
		if text[i] == '\\' && i+1 < len(text) {
			i++
			switch text[i] {
			case 'n':
				out.WriteByte('\n')
			case 'r':
				out.WriteByte('\r')
			case 't':
				out.WriteByte('\t')
			default:
				out.WriteByte(text[i])
			}
			continue
		}
		out.WriteByte(text[i])
	}
	return out.String()
}

// selectorPredicate is one constraint from a selector's trailing brackets;
// predicates combine with AND.
type selectorPredicate struct {
	kind  string // state | desc | placeholder | action
	value string
}

// selectorStateWords are the state predicates a selector string may carry.
// Public selectors accept only the compact working-projection spelling.
// Canonical records may retain provider-facing state words internally, but
// those are not a second public protocol.
//
// [disabled] is deliberately absent: AT-SPI ENABLED/SENSITIVE is unreliable
// on Linux (some toolkits leave it unset on fully operable controls), so the
// snapshot never prints it and selectors must not filter on it. [level] is
// absent too: it is an ARIA attribute with no provider counterpart here, and a
// predicate that can never match would only mislead the agent.
var selectorStateWords = map[string]bool{
	"checked":  true,
	"expanded": true,
	"selected": true,
	"focused":  true,
	"click":    true,
}

var publicSelectorStateWords = map[string]bool{
	"checked":  true,
	"expanded": true,
	"selected": true,
	"focused":  true,
}

const selectorPredicateHelp = "Supported trailing predicates are the ones the snapshot prints: " +
	"[checked] [expanded] [selected] [focused] [click] " +
	"[desc=\"…\"] [placeholder=\"…\"] [a=…]. " +
	"The leading index, the {x,y,w,h} box and the trailing : \"value\" are not part of a selector — drop them. " +
	"Chained (>>), positional (nth=) and layout (:right-of) selectors do not exist here."

// parsePredicates splits the remainder into constraints. Anything it does not
// recognize is an error rather than skipped, because a skipped constraint
// would silently widen the match.
func parsePredicates(rest string) ([]selectorPredicate, error) {
	rest = strings.TrimSpace(rest)
	var out []selectorPredicate
	for i := 0; i < len(rest); {
		if rest[i] == ' ' {
			i++
			continue
		}
		if rest[i] != '[' {
			return nil, fmt.Errorf("expected `[` but found %q", rest[i:])
		}
		end := closingBracket(rest, i)
		if end < 0 {
			return nil, fmt.Errorf("`[` is never closed in %q", rest[i:])
		}
		group := strings.TrimSpace(rest[i+1 : end])
		i = end + 1
		parsed, err := parsePredicateGroup(group)
		if err != nil {
			return nil, err
		}
		out = append(out, parsed...)
	}
	return out, nil
}

// closingBracket finds the matching `]`, skipping any inside quotes: desc
// values can be whole sentences that contain `]`.
func closingBracket(text string, start int) int {
	inQuote := false
	for i := start + 1; i < len(text); i++ {
		switch {
		case text[i] == '\\' && inQuote:
			i++
		case text[i] == '"':
			inQuote = !inQuote
		case text[i] == ']' && !inQuote:
			return i
		}
	}
	return -1
}

func parsePredicateGroup(group string) ([]selectorPredicate, error) {
	if group == "" {
		return nil, errors.New("`[]` is empty")
	}
	for _, prefix := range []string{"desc=", "placeholder=", "a="} {
		if !strings.HasPrefix(group, prefix) {
			continue
		}
		value := strings.TrimSpace(strings.TrimPrefix(group, prefix))
		kind := strings.TrimSuffix(prefix, "=")
		if kind == "a" {
			var out []selectorPredicate
			for _, one := range strings.Split(value, ",") {
				if one = strings.TrimSpace(one); one != "" {
					out = append(out, selectorPredicate{kind: "action", value: one})
				}
			}
			if len(out) == 0 {
				return nil, fmt.Errorf("`[%s]` lists no action", group)
			}
			return out, nil
		}
		if len(value) < 2 || value[0] != '"' || value[len(value)-1] != '"' {
			return nil, fmt.Errorf("`[%s]` needs a quoted value, as the snapshot prints it", group)
		}
		return []selectorPredicate{{
			kind:  strings.TrimSuffix(kind, "="),
			value: unescapeQuoted(value[1 : len(value)-1]),
		}}, nil
	}
	// The snapshot prints states as several words in one bracket
	// (`[selected focused]`), so one group yields one constraint per word.
	var out []selectorPredicate
	for _, word := range strings.Fields(group) {
		word = strings.ToLower(word)
		if !selectorStateWords[word] {
			return nil, fmt.Errorf("`[%s]` is not a predicate I know", group)
		}
		if word == "click" {
			word = "has-click-action"
		}
		out = append(out, selectorPredicate{kind: "state", value: word})
	}
	return out, nil
}

// describeSelectorExample builds a directly copyable selector from a desc that
// actually exists among the candidates. It returns "" when no candidate has a
// usable desc, and the caller then omits the hint.
func describeSelectorExample(matches []ElementRecord) string {
	for _, record := range matches {
		if record.Description == "" || record.Description == record.Name {
			continue
		}
		head := record.ControlType
		if record.Name != "" {
			head += " " + strconv.Quote(record.Name)
		}
		return fmt.Sprintf("%s [desc=%s]", head, strconv.Quote(record.Description))
	}
	return ""
}

// describePredicates echoes the parsed predicates into the call log, so the
// caller can see at a glance how its brackets were read, especially when the
// filter matched nothing.
func describePredicates(preds []selectorPredicate) string {
	if len(preds) == 0 {
		return ""
	}
	parts := make([]string, 0, len(preds))
	for _, pred := range preds {
		if pred.kind == "state" {
			parts = append(parts, pred.value)
			continue
		}
		parts = append(parts, fmt.Sprintf("%s=%q", pred.kind, pred.value))
	}
	return " and predicates [" + strings.Join(parts, "] [") + "]"
}

func recordMatchesPredicates(record *ElementRecord, preds []selectorPredicate) bool {
	for _, pred := range preds {
		switch pred.kind {
		case "state":
			// States holds the rendered " [checked expanded]". Compare whole
			// words; Contains would match "selected" inside "unselected".
			found := false
			for _, word := range strings.Fields(strings.Trim(record.States, " []")) {
				if strings.EqualFold(word, pred.value) {
					found = true
					break
				}
			}
			if !found {
				return false
			}
		case "desc":
			if !equalSemanticIdentity(record.Description, pred.value) {
				return false
			}
		case "placeholder":
			if !equalSemanticIdentity(record.Placeholder, pred.value) {
				return false
			}
		case "action":
			found := false
			for _, action := range record.Actions {
				if strings.EqualFold(action, pred.value) {
					found = true
					break
				}
			}
			if !found {
				return false
			}
		}
	}
	return true
}
