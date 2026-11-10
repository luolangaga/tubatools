package core

import (
	"fmt"
	"regexp"
	"sort"
	"strconv"
	"strings"
)

func unchangedNotes(windowFocusConfirmed bool, pixels pixelVerdict, attrsChanged bool) []string {
	notes := []string{}
	if attrsChanged {
		notes = append(notes, "The accessibility tree structure is unchanged, while accessibility text attributes changed (see the [text-attrs] note above). This reports only the observed attribute difference; compare it with the intended result.")
	}
	switch pixels {
	case pixelsChanged:
		notes = append(notes, "The accessibility tree is unchanged; visual change observed (see the [pixels] note above). This does not identify the cause, target, or semantic result. Inspect the screenshot or another independent source of truth.")
	case pixelsIdentical:
		note := "The accessibility tree is unchanged; no observed change in the window pixels."
		if windowFocusConfirmed {
			note += " The target application window was confirmed focused before synthesis, but control delivery and semantic effect remain unknown."
		}
		notes = append(notes, note)
	default:
		if !attrsChanged {
			note := "No accessibility change observed: window title, tree, focus and selection match the pre-action snapshot. Visual change was not measured, so control delivery and semantic effect remain unknown."
			if windowFocusConfirmed {
				note += " The target application window was confirmed focused before synthesis; that is not control-delivery evidence."
			}
			notes = append(notes, note)
		}
	}
	return notes
}

type pixelVerdict int

const (
	pixelsUnknown pixelVerdict = iota
	pixelsIdentical
	pixelsChanged
)

// systemFontFamilies are the desktop's fallback fonts. When one appears on
// either side of a family-name change, the change is a font substitution
// rather than an edit such as making text bold.
var systemFontFamilies = map[string]bool{
	"ubuntu": true, "cantarell": true, "dejavu sans": true, "dejavu serif": true,
	"dejavu sans mono": true, "liberation sans": true, "liberation serif": true,
	"liberation mono": true, "noto sans": true, "noto serif": true,
	"freesans": true, "freeserif": true, "sans-serif": true, "serif": true,
	"monospace": true, "sans": true,
}

// withoutRepaintNoise drops attribute changes caused by the page repainting
// itself and keeps only those that may result from the action.
//
// Attribute changes are otherwise easily misread as proof of an action's
// effect. When a web font finishes loading asynchronously, for example, it
// replaces the fallback font (`family-name: Ubuntu -> Open Sans`, `size: 18pt
// -> 22.5pt`) during a keystroke that had nothing to do with it. Vouching for
// an effect that did not happen is worse than giving no evidence.
//
// Two kinds of noise are removed, both conservatively:
//
//	font swap   family-name has a system fallback font on either side. The
//	            size, weight and style changes follow the new font's metrics
//	            and are dropped too, but only for this node, so a real bold
//	            edit elsewhere is still reported.
//	invisible   every *-color channel differs by at most 8/255, as in
//	            244,248,248 -> 246,249,249: antialiasing or compositing
//	            rounding that nobody can see, let alone cause by an action.
func withoutRepaintNoise(diffs []string) []string {
	fontSwap := false
	for _, diff := range diffs {
		name, from, to, ok := splitDiff(diff)
		if ok && name == "family-name" &&
			(systemFontFamilies[strings.ToLower(from)] || systemFontFamilies[strings.ToLower(to)]) {
			fontSwap = true
			break
		}
	}
	kept := make([]string, 0, len(diffs))
	for _, diff := range diffs {
		name, from, to, ok := splitDiff(diff)
		if !ok {
			kept = append(kept, diff)
			continue
		}
		if fontSwap {
			switch name {
			case "family-name", "size", "weight", "style":
				continue
			}
		}
		if strings.HasSuffix(name, "color") && imperceptibleColorShift(from, to) {
			continue
		}
		kept = append(kept, diff)
	}
	return kept
}

// splitDiff splits "size: 18pt -> 22.5pt" back into its three parts.
func splitDiff(diff string) (name, from, to string, ok bool) {
	colon := strings.Index(diff, ": ")
	arrow := strings.Index(diff, " -> ")
	if colon < 0 || arrow < 0 || arrow < colon {
		return "", "", "", false
	}
	return diff[:colon], diff[colon+2 : arrow], diff[arrow+4:], true
}

const colorNoiseThreshold = 8

// imperceptibleColorShift reports whether two "r,g,b" colors are too close to
// tell apart.
func imperceptibleColorShift(from, to string) bool {
	a := strings.Split(from, ",")
	b := strings.Split(to, ",")
	if len(a) != 3 || len(b) != 3 {
		return false
	}
	for i := range a {
		x, err1 := strconv.Atoi(strings.TrimSpace(a[i]))
		y, err2 := strconv.Atoi(strings.TrimSpace(b[i]))
		if err1 != nil || err2 != nil {
			return false
		}
		if diff := x - y; diff > colorNoiseThreshold || diff < -colorNoiseThreshold {
			return false
		}
	}
	return true
}

// textAttributeChanges compares the formatting attributes of the same element
// in two snapshots.
//
// The rendered snapshot carries only a few semantic attributes ([checked],
// [level=1]) and leaves arbitrary style queries to assertions: a snapshot is a
// summary for the agent and an assertion is a precise query, so they need not
// share fields.
// Likewise, AT-SPI exposes justification, size, weight and fg-color through
// Atspi.Text.get_default_attributes() even though the rendered tree omits
// them, so a formatting change that leaves the tree text unchanged is still
// observable without comparing pixels.
//
// The comparison costs no extra AT-SPI call: attributes travel with each
// record and the pre-action snapshot is already cached. Records are paired by
// tree path and identity, never by index; see the pairing note below.
func textAttributeChanges(before, after *AppSnapshot) []string {
	if before == nil || after == nil {
		return nil
	}
	pathKey := func(runtimeID []int) string {
		if len(runtimeID) == 0 {
			return ""
		}
		parts := make([]string, 0, len(runtimeID))
		for _, n := range runtimeID {
			parts = append(parts, strconv.Itoa(n))
		}
		return strings.Join(parts, ".")
	}
	rolesByPath := map[string]string{}
	for _, record := range after.Elements {
		if key := pathKey(record.RuntimeID); key != "" {
			rolesByPath[key] = strings.ToLower(strings.TrimSpace(record.ControlType))
		}
	}
	// Pair candidates by runtime ID (the tree path), then check role, name
	// and automation id. role+name alone collides: several unnamed `text`
	// nodes share one key, which would attribute one node's hover highlight
	// to another and report a formatting change after a no-op. The path is
	// not an identity on its own either, because an insertion or deletion can
	// move another control onto the same path.
	key := func(r ElementRecord) string {
		path := pathKey(r.RuntimeID)
		if path == "" {
			return ""
		}
		return "path:" + path
	}
	old := map[string]ElementRecord{}
	for _, record := range before.Elements {
		if k := key(record); k != "" && len(record.TextAttributes) > 0 {
			old[k] = record
		}
	}
	var changes []string
	seen := map[string]bool{}
	for _, record := range after.Elements {
		if len(record.TextAttributes) == 0 {
			continue
		}
		if !documentTextFormattingRecord(record, rolesByPath, pathKey) {
			continue
		}
		k := key(record)
		previous, ok := old[k]
		if !ok || seen[k] ||
			previous.ControlType != record.ControlType ||
			previous.Name != record.Name ||
			(previous.NameHash != "" || record.NameHash != "") &&
				previous.NameHash != record.NameHash ||
			(previous.AutomationID != "" || record.AutomationID != "") &&
				previous.AutomationID != record.AutomationID {
			continue
		}
		seen[k] = true
		var diffs []string
		for name, value := range record.TextAttributes {
			if was, had := previous.TextAttributes[name]; !had || was != value {
				from := was
				if !had {
					from = "unset"
				}
				if had && !plausibleTextAttribute(name, from) ||
					!plausibleTextAttribute(name, value) {
					continue
				}
				diffs = append(diffs, fmt.Sprintf("%s: %s -> %s", name, from, value))
			}
		}
		if len(diffs) == 0 {
			continue
		}
		diffs = withoutRepaintNoise(diffs)
		if len(diffs) == 0 {
			continue
		}
		sort.Strings(diffs)
		label := describeRecord(&record)
		changes = append(changes, fmt.Sprintf("%s — %s", label, strings.Join(diffs, "; ")))
	}
	sort.Strings(changes)
	return changes
}

func documentTextFormattingRecord(
	record ElementRecord,
	rolesByPath map[string]string,
	pathKey func([]int) string,
) bool {
	role := strings.ToLower(strings.TrimSpace(record.ControlType))
	if role == "paragraph" || strings.HasPrefix(role, "document") {
		return true
	}
	if role != "text" {
		return false
	}
	for length := len(record.RuntimeID) - 1; length > 0; length-- {
		ancestorRole := rolesByPath[pathKey(record.RuntimeID[:length])]
		if ancestorRole == "paragraph" || strings.HasPrefix(ancestorRole, "document") {
			return true
		}
	}
	return false
}

var spreadsheetRangeValue = regexp.MustCompile(`(?i)^[A-Z]{1,4}[0-9]+(?::[A-Z]{1,4}[0-9]+)?$`)

func plausibleTextAttribute(name, value string) bool {
	value = strings.TrimSpace(value)
	if value == "" || len(value) > 256 || strings.ContainsAny(value, "\r\n") {
		return false
	}
	if name == "family-name" && spreadsheetRangeValue.MatchString(value) {
		return false
	}
	if strings.HasSuffix(name, "color") {
		parts := strings.Split(value, ",")
		if len(parts) != 3 {
			return false
		}
		for _, part := range parts {
			channel, err := strconv.Atoi(strings.TrimSpace(part))
			if err != nil || channel < 0 || channel > 65535 {
				return false
			}
		}
	}
	return true
}

// readTextAttributeChange reports whether the notes contain a formatting
// attribute change. That is the strongest evidence tier: it says that
// justification changed from left to right, where pixels can only say that a
// small fraction of the window changed, so an unchanged tree no longer
// suggests that the action failed.
func readTextAttributeChange(notes []string) bool {
	for _, note := range notes {
		if strings.HasPrefix(note, "[text-attrs]") {
			return true
		}
	}
	return false
}

// readPixelVerdict reads from the runtime's `[pixels]` note whether the window
// pixels changed.
func readPixelVerdict(notes []string) pixelVerdict {
	for _, note := range notes {
		if !strings.HasPrefix(note, "[pixels]") {
			continue
		}
		if strings.Contains(note, "persistent_change=false") ||
			strings.Contains(note, "sampled_pixel_change=none") {
			return pixelsIdentical
		}

		return pixelsChanged
	}
	return pixelsUnknown
}

func compactFindNotes(notes []string) []string {
	return modelFacingAXNotes(notes)
}

func observeNoneNotes(notes []string) []string {
	out := make([]string, 0, len(notes))
	for _, note := range notes {
		phase, _ := observationPhaseAndPayload(note)
		category := diagnosticCategory(note)
		if phase == "immediate" && category != "" {
			continue
		}
		if strings.HasPrefix(note, "[expect]") ||
			strings.HasPrefix(note, "[semantic-resolution]") ||
			strings.HasPrefix(note, "[canonical-address]") ||
			strings.Contains(note, `"predicate_verified":`) ||
			category == "root_window_changed" ||
			category == "modal_dialog" ||
			category == "dialog_in_front" ||
			category == "foreign_modal" ||
			category == "foreign_window" {
			out = append(out, note)
		}
	}
	return out
}

// modelFacingAXNotes removes implementation diagnostics while preserving facts
// that can change the agent's next safe decision. Full explanations remain in
// operator telemetry and error-context artifacts.
func modelFacingAXNotes(notes []string) []string {
	out := make([]string, 0, len(notes))
	seen := map[string]bool{}
	appendOnce := func(value string) {
		if value == "" || seen[value] {
			return
		}
		seen[value] = true
		out = append(out, value)
	}
	for _, note := range notes {
		category := diagnosticCategory(note)
		if category == "" && strings.Contains(note, "diagnostic snapshot_unchanged") {
			category = "snapshot_unchanged"
		}
		if category == "" && strings.Contains(note, "diagnostic snapshot_busy") {
			category = "snapshot_busy"
		}
		switch category {
		case "snapshot_unchanged", "content_scope_unchanged":
			appendOnce("[state] semantic_state=unchanged")
			continue
		case "snapshot_busy":
			appendOnce("[state] semantic_state=unchanged busy=true recovery=wait_then_observe")
			continue
		case "root_window_changed":
			appendOnce("[state] window_changed=true indices_invalidated=true")
			continue
		case "modal_dialog":
			appendOnce("[state] blocking=modal_dialog")
			continue
		case "dialog_in_front":
			appendOnce("[state] blocking=dialog_in_front")
			continue
		case "content_plane_uncertain", "accessibility_content_uncertain":
			appendOnce("[state] ax_content=uncertain")
			continue
		case "foreign_modal":
			appendOnce("[state] blocking=foreign_modal")
			continue
		case "foreign_window":
			appendOnce("[state] foreground=other_window")
			continue
		}
		if strings.HasPrefix(note, "[semantic-resolution]") ||
			strings.HasPrefix(note, "[a11y] Resolved ") ||
			strings.HasPrefix(note, "[continuity]") ||
			strings.HasPrefix(note, "[diagnostic ") {
			continue
		}
		if strings.HasPrefix(note, "[pixels]") {
			if readPixelVerdict([]string{note}) == pixelsIdentical {
				appendOnce("[state] visual_change=false")
			} else {
				appendOnce("[state] visual_change=true")
			}
			continue
		}
		if strings.HasPrefix(note, "[a11y]") {
			if offset := strings.Index(note, "[state]"); offset >= 0 {
				fact := strings.TrimSpace(note[offset:])
				if end := strings.Index(fact, "."); end >= 0 {
					fact = strings.TrimSpace(fact[:end])
				}
				appendOnce(fact)
			}
			continue
		}
		if strings.HasPrefix(note, "The accessibility tree is unchanged") ||
			strings.HasPrefix(note, "No accessibility change observed") {
			appendOnce("[state] ax_change=false")
			continue
		}
		appendOnce(note)
	}
	return out
}
