package core

import (
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"strconv"
	"strings"
	"time"
	"unicode"
)

// toolError puts the chain of facts first and the advice after: what was
// looked up, what it resolved to, and in which state it failed, then an
// explicit next step. The consumer is an LLM, so the next step is labeled
// separately and speculation never enters the fact chain.
type toolError struct {
	tool    string
	summary string
	fields  [][2]string
	log     []string
	next    string
}

func (e *toolError) add(name, value string) *toolError {
	if value != "" {
		e.fields = append(e.fields, [2]string{name, value})
	}
	return e
}

func (e *toolError) step(format string, args ...any) *toolError {
	e.log = append(e.log, fmt.Sprintf(format, args...))
	return e
}

func (e *toolError) Error() string {
	var b strings.Builder
	if e.tool != "" {
		b.WriteString(e.tool + ": ")
	}
	b.WriteString(e.summary)
	if len(e.fields) > 0 {
		width := 0
		for _, f := range e.fields {
			if len(f[0]) > width {
				width = len(f[0])
			}
		}
		b.WriteString("\n")
		for _, f := range e.fields {
			b.WriteString(fmt.Sprintf("\n%-*s %s", width+1, f[0]+":", f[1]))
		}
	}
	if len(e.log) > 0 {
		b.WriteString("\n\nCall log:")
		for _, line := range e.log {
			// Indented lines continue the previous entry (as in candidate
			// lists) and get no "- " bullet: lines with leading spaces render
			// as sub-lines.
			if strings.HasPrefix(line, " ") {
				b.WriteString("\n  " + line)
			} else {
				b.WriteString("\n  - " + line)
			}
		}
	}
	if e.next != "" {
		b.WriteString("\n\nNext: " + e.next)
	}
	return b.String()
}

// snapshotAge summarizes how old the snapshot is and which element indices it
// holds.
func snapshotAge(snapshot *AppSnapshot) string {
	if snapshot == nil {
		return ""
	}
	indices := "no elements"
	if n := len(snapshot.Elements); n > 0 {
		indices = fmt.Sprintf("%d elements (indices %d..%d)", n,
			snapshot.Elements[0].Index, snapshot.Elements[n-1].Index)
	}
	if snapshot.capturedAt.IsZero() {
		return indices
	}
	return fmt.Sprintf("captured %.1fs ago, %s",
		time.Since(snapshot.capturedAt).Seconds(), indices)
}

// closestElements finds the elements that best match the declared intent,
// each with a directly usable selector.
func closestElements(snapshot *AppSnapshot, declared string, limit int) []string {
	if snapshot == nil || strings.TrimSpace(declared) == "" {
		return nil
	}
	var tokens []string
	for _, token := range strings.FieldsFunc(strings.ToLower(declared), func(r rune) bool {
		return !unicode.IsLetter(r) && !unicode.IsDigit(r)
	}) {
		if len([]rune(token)) >= 2 && !genericIntentWords[token] {
			tokens = append(tokens, token)
		}
	}
	if len(tokens) == 0 {
		return nil
	}
	var out []string
	for _, record := range snapshot.Elements {
		if !recordInAXScope(snapshot, &record) {
			continue
		}
		haystack := strings.ToLower(record.Name + " " + record.Description)
		hit := false
		for _, token := range tokens {
			if strings.Contains(haystack, token) {
				hit = true
				break
			}
		}
		if !hit {
			continue
		}
		line := fmt.Sprintf("    %-4d %s", record.Index, describeRecord(&record))
		if record.Name != "" {
			line = fmt.Sprintf("    %-4d %-26s aka %s %q",
				record.Index, describeRecord(&record), record.ControlType, record.Name)
		} else if record.Description != "" {
			line = fmt.Sprintf("    %-4d %-26s aka %s %q",
				record.Index, describeRecord(&record), record.ControlType, record.Description)
		}
		out = append(out, line)
		if len(out) >= limit {
			break
		}
	}
	return out
}

// lookupElementFor resolves a reference and, on failure, reports the full
// chain of facts rather than a one-line error.
func lookupElementFor(snapshot *AppSnapshot, elementIndex, declared, tool string) (*ElementRecord, error) {
	fail := func(summary string) *toolError {
		e := &toolError{tool: tool, summary: summary}
		e.add("Requested", strconv.Quote(elementIndex))
		e.add("Declared", func() string {
			if declared == "" {
				return ""
			}
			return strconv.Quote(declared)
		}())
		e.add("Snapshot", snapshotAge(snapshot))
		return e
	}

	if isSemanticRef(elementIndex) {
		selector, windowHandle, err := decodeSemanticRefScope(elementIndex)
		if err != nil {
			e := fail("the semantic ref is malformed.").
				step("decoding the self-contained selector from %q", elementIndex)
			e.next = "call AX mode=find again and use the returned rN ref."
			return nil, e
		}
		if windowHandle != "" && snapshot.WindowHandle != windowHandle {
			e := fail(
				"stale_window: the target belongs to a different top-level window instance.",
			)
			e.next = "observe or find the target again in the current window."
			return nil, e
		}
		return lookupBySelectorFor(snapshot, selector, declared, tool)
	}

	index, err := strconv.Atoi(elementIndex)
	if err != nil {
		return lookupBySelectorFor(snapshot, elementIndex, declared, tool)
	}
	for _, record := range snapshot.Elements {
		if record.Index == index {
			copy := record
			return &copy, nil
		}
	}
	e := fail(fmt.Sprintf("target.index %d is not in the current snapshot.", index)).
		step("resolving target.index=%d against the snapshot of %q", index, snapshot.sourceApp).
		step("that snapshot has no such index")
	if candidates := closestElements(snapshot, declared, 5); len(candidates) > 0 {
		e.step("elements matching what you said you were targeting:")
		e.log = append(e.log, candidates...)
	}
	e.next = "call AX mode=observe or mode=find again and use the returned rN ref."
	return nil, e
}

const (
	semanticRefPrefix       = "s1:"
	scopedSemanticRefPrefix = "s2:"
)

type scopedSemanticRef struct {
	Selector     string `json:"selector"`
	WindowHandle string `json:"window"`
}

func encodeSemanticRef(selector string) string {
	return semanticRefPrefix +
		base64.RawURLEncoding.EncodeToString([]byte(selector))
}

func encodeScopedSemanticRef(selector, windowHandle string) string {
	selector = strings.TrimSpace(selector)
	windowHandle = strings.TrimSpace(windowHandle)
	if windowHandle == "" {
		return encodeSemanticRef(selector)
	}
	payload, err := json.Marshal(scopedSemanticRef{
		Selector:     selector,
		WindowHandle: windowHandle,
	})
	if err != nil {
		return ""
	}
	return scopedSemanticRefPrefix +
		base64.RawURLEncoding.EncodeToString(payload)
}

func isSemanticRef(value string) bool {
	return strings.HasPrefix(value, semanticRefPrefix) ||
		strings.HasPrefix(value, scopedSemanticRefPrefix)
}

func decodeSemanticRefScope(ref string) (string, string, error) {
	prefix := ""
	switch {
	case strings.HasPrefix(ref, semanticRefPrefix):
		prefix = semanticRefPrefix
	case strings.HasPrefix(ref, scopedSemanticRefPrefix):
		prefix = scopedSemanticRefPrefix
	default:
		return "", "", errors.New("unsupported semantic ref version")
	}
	payload := strings.TrimPrefix(ref, prefix)
	if payload == "" {
		return "", "", errors.New("empty semantic ref")
	}
	decoded, err := base64.RawURLEncoding.DecodeString(payload)
	if err != nil {
		return "", "", err
	}
	if prefix == scopedSemanticRefPrefix {
		var scoped scopedSemanticRef
		if err := json.Unmarshal(decoded, &scoped); err != nil {
			return "", "", fmt.Errorf("invalid scoped semantic ref: %w", err)
		}
		selector := strings.TrimSpace(scoped.Selector)
		windowHandle := strings.TrimSpace(scoped.WindowHandle)
		if selector == "" || len(selector) > 4096 {
			return "", "", errors.New("invalid semantic selector length")
		}
		if windowHandle == "" || len(windowHandle) > 512 {
			return "", "", errors.New("invalid semantic window scope")
		}
		return selector, windowHandle, nil
	}
	selector := strings.TrimSpace(string(decoded))
	if selector == "" || len(selector) > 4096 {
		return "", "", errors.New("invalid semantic selector length")
	}
	return selector, "", nil
}

func windowActionRole(role string) bool {
	switch strings.ToLower(strings.TrimSpace(role)) {
	case "frame", "window", "dialog", "alert",
		"file chooser", "color chooser":
		return true
	default:
		return false
	}
}

func windowActionSelector(snapshot *AppSnapshot) string {
	if snapshot == nil || snapshot.WindowAbsent ||
		strings.TrimSpace(snapshot.WindowHandle) == "" {
		return ""
	}
	for index := range snapshot.Elements {
		record := &snapshot.Elements[index]
		if len(record.RuntimeID) != 1 || !windowActionRole(record.ControlType) {
			continue
		}
		selector := semanticSelectorForRecord(snapshot, *record)
		if selector == "" {
			selector = selectorHead(record.ControlType, record.Name)
		}
		return selector
	}
	return ""
}

func renderedWindowInventory(windows []TopLevelWindow) []string {
	lines := make([]string, 0, len(windows))
	for _, window := range windows {
		if window.Selected {
			continue
		}
		role := strings.TrimSpace(window.Role)
		if role == "" {
			role = "window"
		}
		ref := window.publicRef
		if ref == "" {
			lines = append(lines, fmt.Sprintf(
				"  - %s %q [window-action-ref=unavailable]",
				role, window.Title,
			))
		} else {
			lines = append(lines, fmt.Sprintf(
				"  - %s %q [ref=%s]",
				role, window.Title, ref,
			))
		}
	}
	return lines
}

func selectorHead(role, name string) string {
	role = strings.TrimSpace(role)
	name = strings.TrimSpace(name)
	if name == "" {
		return role
	}
	if role == "" {
		return strconv.Quote(name)
	}
	return role + " " + strconv.Quote(name)
}

// semanticSelectorForRecord returns the shortest selector that uniquely
// resolves the record in this snapshot. It never uses index, path, geometry,
// state, or value because those are transient rather than semantic identity.
func semanticSelectorForRecord(
	snapshot *AppSnapshot,
	record ElementRecord,
) string {
	return indexedSemanticSelector(snapshot, nil, record)
}

func indexedSemanticSelector(
	snapshot *AppSnapshot,
	index semanticIdentityIndex,
	record ElementRecord,
) string {
	role := strings.TrimSpace(record.ControlType)
	candidates := []string{}
	if record.Name != "" {
		candidates = append(candidates, selectorHead(role, record.Name))
		if record.Description != "" &&
			record.Description != record.Name {
			candidates = append(candidates, fmt.Sprintf(
				"%s [desc=%s]",
				selectorHead(role, record.Name),
				strconv.Quote(record.Description),
			))
		}
		if record.Placeholder != "" {
			candidates = append(candidates, fmt.Sprintf(
				"%s [placeholder=%s]",
				selectorHead(role, record.Name),
				strconv.Quote(record.Placeholder),
			))
		}
	} else if record.Description != "" {
		candidates = append(candidates,
			fmt.Sprintf("%s [desc=%s]", role, strconv.Quote(record.Description)),
		)
	} else if record.Placeholder != "" {
		candidates = append(candidates,
			fmt.Sprintf("%s [placeholder=%s]", role, strconv.Quote(record.Placeholder)),
		)
	}
	for _, selector := range candidates {
		selector = strings.TrimSpace(selector)
		if selector == "" {
			continue
		}
		match, ok := uniqueSelectorMatch(snapshot, index, selector)
		if ok && match.Index == record.Index {
			return selector
		}
	}
	return ""
}

func semanticRefForRecord(
	snapshot *AppSnapshot,
	record ElementRecord,
) string {
	return indexedSemanticRef(snapshot, nil, record)
}

func indexedSemanticRef(
	snapshot *AppSnapshot,
	index semanticIdentityIndex,
	record ElementRecord,
) string {
	selector := indexedSemanticSelector(snapshot, index, record)
	if selector == "" {
		return ""
	}
	return encodeScopedSemanticRef(selector, snapshot.WindowHandle)
}

func elementIntentMismatch(record *ElementRecord, declared string) string {
	declared = strings.TrimSpace(declared)
	if declared == "" || record == nil {
		return ""
	}
	// Check only named elements. Matching against the role is useless because
	// declarations and roles both almost always contain "button". Unnamed
	// elements often take their identity from an adjacent label (for example,
	// the unnamed spin buttons of a position-and-size dialog), so no
	// declaration can match them and a check would only reject correct
	// actions.
	if strings.TrimSpace(record.Name) == "" {
		return ""
	}
	if !containsASCIILetter(declared) {
		// A declaration without ASCII letters (for example, Chinese text)
		// cannot be compared with names, which are usually English.
		return ""
	}
	// Very short names without a description cannot be checked either.
	// Tokenization below skips tokens shorter than two characters to drop
	// noise such as "a" or "of", so an element named "L" (a size filter,
	// a lettered radio option, a column header) could never match a correct
	// declaration such as "Size L checkbox" and would always be rejected.
	// Missing a mismatch is better than a guaranteed false rejection.
	if len([]rune(strings.TrimSpace(record.Name))) <= 2 &&
		strings.TrimSpace(record.Description) == "" {
		return ""
	}
	haystack := strings.ToLower(record.Name + " " + record.Description)
	matched := false
	specific := 0
	for _, token := range strings.FieldsFunc(strings.ToLower(declared), func(r rune) bool {
		return !unicode.IsLetter(r) && !unicode.IsDigit(r)
	}) {
		if len([]rune(token)) < 2 || genericIntentWords[token] {
			continue
		}
		specific++
		if strings.Contains(haystack, token) {
			matched = true
			break
		}
	}
	if specific == 0 || matched {
		// The declaration has no distinguishing word ("the button"), or it
		// matched.
		return ""
	}
	return fmt.Sprintf(
		"target.index %d resolves to %s, which does not match the declared target (%q). "+
			"This usually means the index came from an earlier snapshot. "+
			"Call AX mode=observe or mode=find again and use a current rN ref.",
		record.Index, describeRecord(record), declared)
}

// genericIntentWords are words that fit any element, so matching them proves
// nothing; "button", for example, appears in most declarations and roles.
var genericIntentWords = map[string]bool{
	"the": true, "a": true, "an": true, "of": true, "in": true, "on": true,
	"for": true, "to": true, "this": true, "that": true, "and": true, "with": true,
	"button": true, "item": true, "box": true, "field": true, "menu": true,
	"list": true, "cell": true, "pane": true, "panel": true, "window": true,
	"dialog": true, "label": true, "icon": true, "tab": true, "bar": true,
	"view": true, "control": true, "element": true, "text": true, "entry": true,
	"checkbox": true, "check": true, "radio": true, "toggle": true, "spin": true,
	"push": true, "click": true, "select": true, "option": true,
}

func containsASCIILetter(text string) bool {
	for _, r := range text {
		if (r >= 'a' && r <= 'z') || (r >= 'A' && r <= 'Z') {
			return true
		}
	}
	return false
}

// renderElementReference is the single renderer for agent-facing element
// references outside tree lines: find results, predicate evidence and focus
// summaries.
//
// Its one hard contract is round-tripping: any string it returns, pasted
// verbatim into an expect target (parseExpectTarget), must parse back to the
// same reference. Each form maps to one parser branch: `push button "Save"`
// to the selector, `"Save"` to the selector's empty-role branch, and
// `role="push button"` to the field form. An unquoted "role name" string
// would span both fields and never match, so the tool would reject the very
// text it printed.
func renderElementReference(role, name string) string {
	role = strings.TrimSpace(role)
	name = strings.TrimSpace(name)
	switch {
	case role == "" && name == "":
		return "an element with neither a role nor a name"
	case name == "":
		// Prose such as "an unnamed push button" cannot be read back; the
		// field form is both accurate and pasteable.
		return fmt.Sprintf("role=%q", role)
	case role == "":
		return fmt.Sprintf("%q", name)
	default:
		return fmt.Sprintf("%s %q", role, name)
	}
}

func describeRecord(record *ElementRecord) string {
	if record == nil {
		return "(no element)"
	}
	return renderElementReference(record.ControlType, record.Name)
}
