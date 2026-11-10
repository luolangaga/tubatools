package core

import "strings"

func recordHasState(record *ElementRecord, state string) bool {
	if record == nil {
		return false
	}
	for _, reported := range strings.Fields(strings.Trim(record.States, "[] \t\r\n")) {
		if strings.EqualFold(reported, strings.TrimSpace(state)) {
			return true
		}
	}
	return false
}

// focusedRecord finds the focused node in the rendered tree of this snapshot.
//
// It deliberately ignores snapshot.FocusedSummary, which comes from a separate
// whole-window scan by the provider that is not subject to node-budget pruning
// and may belong to another window. Predicates are evaluated against Elements,
// so evidence must come from Elements too; mixing the sources produces
// self-contradicting evidence such as "nothing in the tree matches, focus is
// on X".
func focusedRecord(snapshot *AppSnapshot) *ElementRecord {
	if snapshot == nil {
		return nil
	}
	for index := range snapshot.Elements {
		if containsFold(snapshot.Elements[index].States, "focused") {
			return &snapshot.Elements[index]
		}
	}
	return nil
}
