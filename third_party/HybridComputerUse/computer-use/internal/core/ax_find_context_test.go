package core

import "testing"

func TestFindSnapshotPreservesAppAndWindowOwnership(t *testing.T) {
	source := &AXSnapshot{
		SchemaVersion:              "AXSnapshot",
		AppRef:                     "a7",
		AppIdentity:                "libreoffice",
		WindowHandle:               "private-window",
		WindowRef:                  "w9",
		Revision:                   "c42",
		AcquisitionIncomplete:      true,
		IdentityCoverageIncomplete: true,
		RefInventoryComplete:       true,
	}
	node := &AXNode{ID: "0/1", Role: "cell", Name: "C2"}
	findSnapshot := newAXFindSnapshot(source, []*AXNode{node})
	if findSnapshot == nil ||
		findSnapshot.AppRef != "a7" ||
		findSnapshot.WindowRef != "w9" ||
		findSnapshot.WindowHandle != "private-window" ||
		!findSnapshot.AcquisitionIncomplete ||
		!findSnapshot.IdentityCoverageIncomplete ||
		findSnapshot.RefInventoryComplete {
		t.Fatalf("find snapshot lost ownership: %#v", findSnapshot)
	}
}
