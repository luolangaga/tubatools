package core

import (
	"strings"
	"testing"
)

func TestUnchangedReferenceRequiresCurrentDeliveredRefs(t *testing.T) {
	s := NewService()
	delivered := deliveredDiffBaselineSnapshot("document", ` - window "Editor":`,
		`  - button "Save" [ref=r206]`)
	first := s.getAppStateResult(delivered, nil, false)
	if !strings.Contains(ResultText(first), "[ref=r206]") {
		t.Fatal("initial observation did not deliver ref")
	}
	reissued := deliveredDiffBaselineSnapshot("document", ` - window "Editor":`,
		`  - button "Save" [ref=r207]`)
	reissued.comparedToPrevious = true
	reissued.identicalToPrevious = true
	text := ResultText(s.getAppStateResult(reissued, nil, false))
	if strings.Contains(text, "Tree unchanged:") || !strings.Contains(text, "[ref=r207]") {
		t.Fatalf("new refs were hidden by unchanged provider state: %s", text)
	}
	full := ResultText(s.getAppStateResult(reissued, nil, true))
	if strings.Contains(full, "Tree unchanged:") || !strings.Contains(full, "[ref=r207]") {
		t.Fatalf("explicit full observation must always return tree: %s", full)
	}
}
