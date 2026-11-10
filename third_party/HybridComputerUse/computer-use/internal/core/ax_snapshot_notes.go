package core

import (
	"fmt"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"strings"
	"time"
)

func SameCanonicalAddress(first, second ElementRecord) bool {
	if first.ControlType != second.ControlType ||
		first.LocalizedControlType != second.LocalizedControlType ||
		first.Name != second.Name ||
		first.NameHash != second.NameHash ||
		first.AutomationID != second.AutomationID ||
		len(first.RuntimeID) != len(second.RuntimeID) {
		return false
	}
	for index := range first.RuntimeID {
		if first.RuntimeID[index] != second.RuntimeID[index] {
			return false
		}
	}
	return true
}

func (s *Service) hiddenCanonicalAddressChangeNote(
	before,
	after *AppSnapshot,
) string {
	if before == nil || after == nil {
		return ""
	}
	key := DeliveredSnapshotKey(before)
	exposed := s.ExposedHiddenRefs[key]
	if len(exposed) == 0 {
		return ""
	}
	afterRecords := map[int]ElementRecord{}
	for _, record := range after.Elements {
		afterRecords[record.Index] = record
	}
	changed := []int{}
	rootChanged := before.WindowHandle != "" &&
		after.WindowHandle != "" &&
		before.WindowHandle != after.WindowHandle
	for index, first := range exposed {
		second, hadSecond := afterRecords[index]
		if !rootChanged && hadSecond && SameCanonicalAddress(first, second) {
			continue
		}
		changed = append(changed, index)
		delete(exposed, index)
	}
	if len(changed) == 0 {
		return ""
	}
	if len(exposed) == 0 {
		delete(s.ExposedHiddenRefs, key)
	}
	return fmt.Sprintf(
		"[canonical-address] %d serializer-hidden ref(s) previously returned by find "+
			"were removed or rebound across this action. These refs are stale; call "+
			"find again before acting.",
		len(changed),
	)
}

// observablyChanged compares the parts of two snapshots that the agent can
// observe. Screenshots are excluded: pixel noise such as a blinking cursor
// would always register as a change.
func observablyChanged(before, after *AppSnapshot) bool {
	if before == nil || after == nil {
		return true
	}
	if before.WindowTitle != after.WindowTitle ||
		before.FocusedSummary != after.FocusedSummary ||
		before.SelectedText != after.SelectedText {
		return true
	}
	if len(before.TreeLines) != len(after.TreeLines) {
		return true
	}
	for i := range before.TreeLines {
		if before.TreeLines[i] != after.TreeLines[i] {
			return true
		}
	}
	return false
}

// looseKey reduces an app name to lowercase letters and digits for lenient
// cache lookup. It must follow the same rule as loose() in the Linux provider;
// if the two diverged, an app could resolve but then miss the cache.
func looseKey(text string) string {
	var out strings.Builder
	for _, r := range strings.ToLower(text) {
		if (r >= 'a' && r <= 'z') || (r >= '0' && r <= '9') {
			out.WriteRune(r)
		}
	}
	return out.String()
}

// continuityNotes describes how this snapshot relates to the previous one and
// fills in, in place, the snapshot fields that only the history-holding side
// can answer. It separates cases that otherwise look identical:
//
//  1. Whether an unchanged tree means that an asynchronous operation is still
//     running or that information was trimmed. Both look the same in the
//     tree, and a wrong guess costs a wasted retry.
//  2. Whether the root window changed. When an app opens a second window with
//     the same title, the root can switch silently and every element index
//     shifts while the returned text stays the same.
func continuityNotes(previous, current *AppSnapshot) []string {
	if current == nil {
		return nil
	}
	current.comparedToPrevious = false
	current.identicalToPrevious = false
	current.previousSnapshotID = ""
	current.previousHandle = ""
	if previous == nil {
		return nil
	}
	if providerCoverageSummary(
		current.Annotations,
	).AcquisitionIncomplete > 0 {
		return nil
	}
	current.previousSnapshotID = previous.SnapshotID
	notes := []string{}
	if previous.WindowHandle != "" && current.WindowHandle != "" &&
		previous.WindowHandle != current.WindowHandle {
		current.previousHandle = previous.WindowHandle
		notes = append(notes, fmt.Sprintf(
			"ROOT WINDOW CHANGED: this snapshot is rooted at a different top-level "+
				"window than the previous model-visible snapshot for this app "+
				"(was titled %q; now titled %q). Window titles alone may not show "+
				"this — two windows of one app can carry the same title. Refs owned by "+
				"the previous window are stale; use the current rN/wN refs listed below.",
			previous.WindowTitle, current.WindowTitle))
		return notes
	}
	if previous.SnapshotID == "" || current.SnapshotID == "" {
		return notes
	}
	if normalizedAXScope(current.projection.Scope) == contract.AXScopeContent &&
		previous.contentRevision != "" && current.contentRevision != "" {
		current.comparedToPrevious = true
		current.identicalToPrevious =
			previous.contentRevision == current.contentRevision
		if !current.identicalToPrevious {
			return notes
		}
		gap := ""
		if !previous.capturedAt.IsZero() {
			gap = fmt.Sprintf(
				" %.1fs apart.",
				time.Since(previous.capturedAt).Seconds(),
			)
		}
		notes = append(notes, fmt.Sprintf(
			"CONTENT SCOPE UNCHANGED: the current content is identical to the previous "+
				"model-visible content for this window — same scoped "+
				"semantic tree and selected text.%s Browser/application chrome "+
				"outside this scope may have changed. This is not proof that a "+
				"visual, network, file, document, or application effect failed.",
			gap,
		))
		return notes
	}
	current.comparedToPrevious = true
	current.identicalToPrevious = previous.SnapshotID == current.SnapshotID
	if !current.identicalToPrevious {
		return notes
	}
	gap := ""
	if !previous.capturedAt.IsZero() {
		gap = fmt.Sprintf(" %.1fs apart.", time.Since(previous.capturedAt).Seconds())
	}
	// Report two tiers because the evidence differs in strength: an app that
	// reports busy is positive evidence for waiting, while its absence only
	// means "unknown" and never proves that the action failed.
	if current.Busy.busy() {
		notes = append(notes, fmt.Sprintf(
			"SNAPSHOT UNCHANGED, APP REPORTS BUSY: the current state is identical to the "+
				"previous model-visible state for this app "+
				"(same window, tree, focus and selection).%s "+
				"But the app is still working: the window carries an accessibility BUSY state "+
				"or exposes %d progress indicator(s). This is 'not finished yet', not "+
				"'nothing happened' and not 'information was trimmed away'. Wait and "+
				"call AX mode=observe again rather than repeating the action — repeating "+
				"it may apply it twice.",
			gap,
			current.Busy.ProgressElements))
		return notes
	}
	notes = append(notes, fmt.Sprintf(
		"SNAPSHOT UNCHANGED: the current state is identical to the previous one for this "+
			"app that was actually delivered to the model — same "+
			"window instance, same tree, same focus, same selection.%s "+
			"Nothing in the tree reports work in progress, but that is not proof your "+
			"last action failed: effects may not reach the accessibility tree. "+
			"Check the screenshot, or "+
			"wait for the specific element with an AX wait step before deciding to retry. "+
			"Note also what this line rules OUT: the content is not missing because a "+
			"budget trimmed it — this tree is the same one you already have.",
		gap))
	return notes
}

func observationPhaseAndPayload(note string) (string, string) {
	if !strings.HasPrefix(note, "[observation phase=") {
		return "", note
	}
	end := strings.Index(note, "] ")
	if end < 0 {
		return "", note
	}
	header := note[:end]
	phase := ""
	for _, field := range strings.Fields(strings.TrimPrefix(header, "[")) {
		if value, ok := strings.CutPrefix(field, "phase="); ok {
			phase = value
			break
		}
	}
	return phase, note[end+2:]
}

func diagnosticCategory(note string) string {
	_, note = observationPhaseAndPayload(note)
	for _, item := range []struct {
		prefix string
		key    string
	}{
		{"ROOT WINDOW CHANGED:", "root_window_changed"},
		{"CONTENT SCOPE UNCHANGED:", "content_scope_unchanged"},
		{"SNAPSHOT UNCHANGED, APP REPORTS BUSY:", "snapshot_busy"},
		{"SNAPSHOT UNCHANGED:", "snapshot_unchanged"},
		{"MODAL DIALOG:", "modal_dialog"},
		{"DIALOG IN FRONT:", "dialog_in_front"},
		{"CONTENT PLANE UNCERTAIN:", "content_plane_uncertain"},
		{"ACCESSIBILITY CONTENT UNCERTAIN:", "accessibility_content_uncertain"},
		{"ANOTHER APP IS IN FRONT:", "foreign_modal"},
		{"ANOTHER WINDOW MAY BE IN FRONT:", "foreign_window"},
	} {
		if strings.HasPrefix(note, item.prefix) {
			return item.key
		}
	}
	return ""
}

func (s *Service) dedupeDiagnosticNotes(app string, notes []string) []string {
	key := strings.ToLower(strings.TrimSpace(app))
	previous := s.lastDiagnostics[key]
	current := map[string]string{}
	out := make([]string, 0, len(notes))
	for _, note := range notes {
		category := diagnosticCategory(note)
		if category == "" {
			out = append(out, note)
			continue
		}
		current[category] = note
		repeated := previous != nil && previous[category] == note
		if previous != nil &&
			(category == "snapshot_unchanged" ||
				category == "snapshot_busy" ||
				category == "content_scope_unchanged") &&
			previous[category] != "" {
			// These notes include a changing elapsed-time sentence. The diagnostic
			// state is still the same even when that duration changes.
			repeated = true
		}
		if repeated {
			out = append(out, fmt.Sprintf(
				"[diagnostic %s] still present; full explanation unchanged from the "+
					"previous response.", category))
			continue
		}
		out = append(out, note)
	}
	if len(current) == 0 {
		delete(s.lastDiagnostics, key)
	} else {
		s.lastDiagnostics[key] = current
	}
	return out
}
