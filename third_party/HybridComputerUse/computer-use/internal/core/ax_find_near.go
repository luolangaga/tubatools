package core

import (
	"fmt"
	"sort"
	"strconv"
	"strings"
	"unicode"
)

// This file answers a query that matched nothing: find with zero matches
// and a query wait that timed out. Both list the closest current elements,
// so the next call can use one of them instead of rereading the window.
//
// It also holds the two parts of query matching that read the snapshot
// rather than one record: the role the tree shows, and state names the
// tree shows.

// queryRecordMatches applies a find query to one record of snapshot. The
// role matches the provider role, the model role, or the role the tree
// rendered for this record: a LibreOffice table cell inside a grid renders
// as gridcell, and the rendered name is the one the caller copies. A state
// matches as rendered too (axQueryStateMatches).
func queryRecordMatches(
	snapshot *AppSnapshot, record *ElementRecord,
	role, name, text, state string, nameExact, textExact bool,
) bool {
	if !recordMatchesFind(record, "", name, text, "", nameExact, textExact) {
		return false
	}
	if role != "" && !findRoleMatches(record, role) &&
		!strings.Contains(roleKey(axRenderedRole(snapshot, record)), roleKey(role)) {
		return false
	}
	return state == "" || axQueryStateMatches(record, state)
}

// axRenderedRole is the role the tree shows for record, or "".
func axRenderedRole(snapshot *AppSnapshot, record *ElementRecord) string {
	if snapshot == nil || snapshot.AXSnapshot == nil || record == nil {
		return ""
	}
	if node := snapshot.AXSnapshot.ByIndex[record.Index]; node != nil {
		return string(node.Role)
	}
	return ""
}

// axQueryStateMatches matches a state as the tree renders states. The tree
// shows keyboard focus as active, and a provider reports a boolean state
// only when it is set, so "unchecked" means the checked state is absent,
// as the act report already reads it.
func axQueryStateMatches(record *ElementRecord, state string) bool {
	want := strings.ToLower(strings.TrimSpace(state))
	if value, ok := strings.CutSuffix(want, "=false"); ok {
		return !axRecordHasState(record, axQueryStateToken(value))
	}
	want = strings.TrimSuffix(want, "=true")
	switch want {
	case "unchecked", "not checked":
		return !axRecordHasState(record, "checked")
	case "collapsed":
		return !axRecordHasState(record, "expanded")
	case "unselected", "not selected":
		return !axRecordHasState(record, "selected")
	}
	if token := axQueryStateToken(want); token != want {
		return axRecordHasState(record, token)
	}
	return containsFold(record.States, want)
}

func axQueryStateToken(state string) string {
	switch state {
	case "active", "focus":
		return "focused"
	}
	return state
}

func axRecordHasState(record *ElementRecord, token string) bool {
	for _, have := range recordStateTokens(record) {
		if strings.EqualFold(have, token) {
			return true
		}
	}
	return false
}

// axRoleFamilies groups roles that name the same kind of control. A query
// for one finds the others as candidates, never as matches: a combo box
// and a list box behave differently under input.
var axRoleFamilies = [][]string{
	{"combobox", "combo box", "listbox", "list box", "list", "popup button", "menu button", "dropdown", "select"},
	{"textbox", "text field", "textfield", "entry", "edit", "text area", "password text", "search box", "input", "text", "editable text"},
	{"checkbox", "check box", "toggle button", "switch", "check menu item", "menuitemcheckbox"},
	{"button", "push button", "toggle button", "menu button"},
	{"cell", "table cell", "gridcell", "grid cell"},
	{"menuitem", "menu item", "menuitemcheckbox", "menuitemradio", "check menu item", "radio menu item"},
	{"tab", "page tab", "tablist", "page tab list"},
	{"spinbutton", "spin button", "spinner", "slider"},
	{"radio", "radio button", "radiobutton", "radio menu item"},
	{"img", "image", "icon", "graphic"},
	{"listitem", "list item", "option", "treeitem", "tree item"},
	{"text", "label", "static", "caption", "heading", "paragraph"},
}

// axRoleFamily returns the keys of the roles in role's family, role
// included, or nil when role is in none.
func axRoleFamily(role string) map[string]bool {
	key := roleKey(role)
	var family map[string]bool
	for _, members := range axRoleFamilies {
		inFamily := false
		for _, member := range members {
			if roleKey(member) == key {
				inFamily = true
			}
		}
		if !inFamily {
			continue
		}
		if family == nil {
			family = map[string]bool{}
		}
		for _, member := range members {
			family[roleKey(member)] = true
		}
	}
	return family
}

// axRecordRoleKeys are the role spellings of a record: provider, model, and
// rendered.
func axRecordRoleKeys(snapshot *AppSnapshot, record *ElementRecord) []string {
	model := string(canonicalRoleDescriptor(ontologyProviderRole(record), nil).ModelRole)
	keys := []string{}
	for _, role := range []string{record.ControlType, record.LocalizedControlType, model, axRenderedRole(snapshot, record)} {
		if key := roleKey(role); key != "" && key != roleKey(string(RoleGeneric)) {
			keys = append(keys, key)
		}
	}
	return keys
}

// axNearQuery is a query that matched nothing, with its text reduced to
// comparable words.
type axNearQuery struct {
	role, name, text, state string
	words                   []string
}

func newAXNearQuery(role, name, text, state string) axNearQuery {
	return axNearQuery{role: role, name: name, text: text, state: state,
		words: axSignificantWords(name + " " + text)}
}

// axSignificantWords lowercases text and splits it into words of letters
// and digits, without short connectives: "Find & Replace" and "Find and
// Replace" give the same words.
func axSignificantWords(text string) []string {
	words := []string{}
	for _, word := range strings.FieldsFunc(strings.ToLower(text), func(r rune) bool {
		return !unicode.IsLetter(r) && !unicode.IsDigit(r)
	}) {
		switch word {
		case "and", "or", "the", "a", "an", "of", "to", "in", "on", "for":
			continue
		}
		words = append(words, word)
	}
	return words
}

// axWordOverlap is the share of want's words that appear in have, where a
// word also matches a word it starts (spacing / spacing:) or one that
// differs in one letter (colour / color).
func axWordOverlap(want []string, have string) float64 {
	if len(want) == 0 {
		return 0
	}
	haveWords := axSignificantWords(have)
	hits := 0
	for _, word := range want {
		for _, candidate := range haveWords {
			if axWordsMatch(word, candidate) {
				hits++
				break
			}
		}
	}
	return float64(hits) / float64(len(want))
}

func axWordsMatch(a, b string) bool {
	if a == b {
		return true
	}
	if len(a) >= 3 && len(b) >= 3 && (strings.HasPrefix(a, b) || strings.HasPrefix(b, a)) {
		return true
	}
	return len(a) >= 5 && len(b) >= 5 && axEditDistanceAtMostOne(a, b)
}

func axEditDistanceAtMostOne(a, b string) bool {
	if len(a) > len(b) {
		a, b = b, a
	}
	if len(b)-len(a) > 1 {
		return false
	}
	for index := 0; index < len(a); index++ {
		if a[index] == b[index] {
			continue
		}
		if len(a) == len(b) {
			return a[index+1:] == b[index+1:]
		}
		return a[index:] == b[index+1:]
	}
	return true
}

// axNearCandidate is one current element close to a query.
type axNearCandidate struct {
	record *ElementRecord
	score  float64
	// roleOnly marks a candidate that differs from the query only in its
	// role family, which is the one case a corrected call can name.
	sameWords bool
}

// axNearElements ranks the records of snapshot against query. A candidate
// must share words with the query text, or, for a query with a role and
// no text, be in the role's family. Exact role matches score above family
// matches, and the full text above part of it.
func axNearElements(snapshot *AppSnapshot, query axNearQuery, limit int) []axNearCandidate {
	if snapshot == nil || limit <= 0 {
		return nil
	}
	family := axRoleFamily(query.role)
	wantRole := roleKey(query.role)
	candidates := []axNearCandidate{}
	for index := range snapshot.Elements {
		record := &snapshot.Elements[index]
		if !recordInAXScope(snapshot, record) {
			continue
		}
		roleScore := 0.0
		for _, key := range axRecordRoleKeys(snapshot, record) {
			switch {
			case wantRole != "" && strings.Contains(key, wantRole):
				roleScore = 2
			case family[key] && roleScore < 1:
				roleScore = 1
			}
		}
		textScore := 0.0
		if len(query.words) > 0 {
			// The name is what the caller copies into the next query, so a
			// name match outranks the same words in a value or description.
			textScore = axWordOverlap(query.words, record.Name)
			others := record.Value + " " + record.Description + " " + record.Placeholder
			if other := 0.75 * axWordOverlap(query.words, others); other > textScore {
				textScore = other
			}
			if textScore == 0 {
				continue
			}
		} else if roleScore == 0 {
			continue
		}
		score := 2*textScore + roleScore
		if query.state != "" && axQueryStateMatches(record, query.state) {
			score += 0.5
		}
		if strings.TrimSpace(record.Name) != "" {
			// A named element can be told apart from its neighbours.
			score += 0.25
		}
		candidates = append(candidates, axNearCandidate{record: record, score: score,
			sameWords: len(query.words) > 0 && textScore >= 0.75 && roleScore >= 1})
	}
	sort.SliceStable(candidates, func(i, j int) bool { return candidates[i].score > candidates[j].score })
	if len(candidates) > limit {
		candidates = candidates[:limit]
	}
	return candidates
}

// axNearLabel is a candidate as the tree shows it: role, name, and state.
// It carries no ref: only a find result issues refs for candidates.
func axNearLabel(snapshot *AppSnapshot, record *ElementRecord) string {
	if snapshot != nil && snapshot.AXSnapshot != nil {
		if node := snapshot.AXSnapshot.ByIndex[record.Index]; node != nil {
			display := findDisplayNode(node)
			label := string(display.Role)
			if display.Name != "" {
				name, _ := findDisplayExcerpt(display.Name, 80)
				label += " " + strconv.Quote(name)
			}
			if value := strings.Join(strings.Fields(display.Properties.Value), " "); value != "" && !containsFold(display.Name, value) {
				if clipped := truncateRunes(value, 40); clipped != value {
					value = clipped + "…"
				}
				label += " value=" + strconv.Quote(value)
			}
			if states := renderAXStateTokens(display.States); len(states) > 0 {
				label += " [" + strings.Join(states, " ") + "]"
			}
			return label
		}
	}
	name, _ := findDisplayExcerpt(record.Name, 80)
	label := canonicalRole(record)
	if name != "" {
		label += " " + strconv.Quote(name)
	}
	return label
}

// axRolesSummary lists the roles present in snapshot, most frequent first,
// for a role query whose role family is absent.
func axRolesSummary(snapshot *AppSnapshot, limit int) string {
	counts := map[string]int{}
	order := []string{}
	for index := range snapshot.Elements {
		record := &snapshot.Elements[index]
		if !recordInAXScope(snapshot, record) {
			continue
		}
		role := axRenderedRole(snapshot, record)
		if role == "" {
			role = string(canonicalRoleDescriptor(ontologyProviderRole(record), nil).ModelRole)
		}
		switch CanonicalRole(role) {
		case "", RoleGeneric, "group", "window", "dialog", "frame":
			continue
		}
		if counts[role] == 0 {
			order = append(order, role)
		}
		counts[role]++
	}
	sort.SliceStable(order, func(i, j int) bool { return counts[order[i]] > counts[order[j]] })
	if len(order) > limit {
		order = order[:limit]
	}
	parts := make([]string, 0, len(order))
	for _, role := range order {
		parts = append(parts, fmt.Sprintf("%s %d", role, counts[role]))
	}
	return strings.Join(parts, ", ")
}

// findZeroMatchText renders the closest elements for a find that matched
// nothing. It issues refs for them through the same path as matches, so
// each can be clicked or read next.
func (s *Service) findZeroMatchText(snapshot *AppSnapshot, query axNearQuery) string {
	near := axNearElements(snapshot, query, axFindNearLimit)
	var out strings.Builder
	if len(near) > 0 && snapshot.AXSnapshot != nil {
		records := make([]ElementRecord, 0, len(near))
		for _, candidate := range near {
			records = append(records, *candidate.record)
		}
		frozen := s.freezeFindRefs(snapshot, records)
		s.rememberDeliveredFindExposure(frozen, records, newAXFindSnapshot(frozen.AXSnapshot, nil), nil)
		out.WriteString("closest (not matches):\n")
		for index := range records {
			label := axNearLabel(frozen, &records[index])
			if node := frozen.AXSnapshot.ByIndex[records[index].Index]; node != nil && node.Ref != "" {
				label += " [ref=" + node.Ref + "]"
			}
			out.WriteString(" - " + label + "\n")
		}
	}
	familyPresent := false
	for _, candidate := range near {
		if !candidate.sameWords && len(query.words) > 0 {
			continue
		}
		familyPresent = true
	}
	if query.role != "" && !familyPresent {
		if roles := axRolesSummary(snapshot, 10); roles != "" {
			out.WriteString("roles here: " + roles + "\n")
		}
	}
	return out.String()
}

const axFindNearLimit = 5
