package core

import (
	"testing"
)

func numericControlSnapshot(role, value, description string) *AppSnapshot {
	return &AppSnapshot{
		App: AppDescriptor{Name: "Controls"}, WindowHandle: "window-1",
		SnapshotID: "sample", publicAppRef: "a1", publicWindowRef: "w1",
		Elements: []ElementRecord{{
			Index: 1, RuntimeID: []int{0, 1}, ControlType: role,
			Value: value, Description: description,
			ProviderBusName: ":1.42", ProviderObjectPath: "/control/1",
			ProcessGeneration: "epoch1", ProviderWindowBusName: ":1.42",
			ProviderWindowObjectPath: "/window/1", ProviderWindowProcessGeneration: "epoch1",
		}},
	}
}

func assignNumericControlSnapshot(t *testing.T, registry *RefRegistry, source *AppSnapshot) *AXSnapshot {
	t.Helper()
	snapshot, err := buildAXSnapshot(source)
	if err != nil {
		t.Fatal(err)
	}
	registry.AssignSnapshotRefs(snapshot)
	return snapshot
}

func TestNumericControlRefSurvivesValueDescriptionChanges(t *testing.T) {
	for _, role := range []string{"slider", "spin button", "scroll bar"} {
		t.Run(role, func(t *testing.T) {
			registry := NewRefRegistry(32)
			source := numericControlSnapshot(role, "74.0", "74%")
			before := assignNumericControlSnapshot(t, registry, source)
			ref := before.ByIndex[1].Ref
			if ref == "" {
				t.Fatal("numeric control received no ref")
			}
			entry, err := registry.Resolve(ref, "")
			if err != nil {
				t.Fatal(err)
			}
			target, err := semanticTargetFromRefEntry(entry)
			if err != nil {
				t.Fatal(err)
			}
			wire := providerTargetFromOperation(target)
			if wire.LocatorKind != AXLocatorProviderBound || len(wire.Predicates) != 0 || wire.HasName ||
				wire.ProviderObjectPath != "/control/1" || wire.ProcessGeneration != "epoch1" ||
				wire.ProviderWindowObjectPath != "/window/1" {
				t.Fatalf("action/read target lost exact identity or retained changing state: %#v", wire)
			}
			for _, value := range []string{"50", "80", "0"} {
				source.Elements[0].Value, source.Elements[0].Description = value, value+"%"
				after := assignNumericControlSnapshot(t, registry, source)
				node := after.ByIndex[1]
				if node.Ref != ref {
					t.Fatalf("value change replaced ref %s with %s", ref, node.Ref)
				}
				if node.Properties.Value != value || node.Properties.Description != value+"%" {
					t.Fatalf("live value disappeared from observation: %#v", node.Properties)
				}
			}
		})
	}
}

func TestNumericControlRefsKeepDistinctProviderObjects(t *testing.T) {
	registry := NewRefRegistry(32)
	source := numericControlSnapshot("slider", "50", "50%")
	other := source.Elements[0]
	other.Index, other.RuntimeID, other.ProviderObjectPath = 2, []int{0, 2}, "/control/2"
	source.Elements = append(source.Elements, other)
	snapshot := assignNumericControlSnapshot(t, registry, source)
	first, second := snapshot.ByIndex[1].Ref, snapshot.ByIndex[2].Ref
	if first == "" || second == "" || first == second {
		t.Fatalf("identical values collapsed distinct objects: %q %q", first, second)
	}
	source.Elements[0].Description = "60%"
	after := assignNumericControlSnapshot(t, registry, source)
	if after.ByIndex[1].Ref != first || after.ByIndex[2].Ref != second {
		t.Fatal("changing one control rebound either ref")
	}
}

func TestNumericControlRefsRejectIdentityChanges(t *testing.T) {
	changes := map[string]func(*AppSnapshot){
		"object":  func(s *AppSnapshot) { s.Elements[0].ProviderObjectPath = "/replacement" },
		"process": func(s *AppSnapshot) { s.Elements[0].ProcessGeneration = "epoch2" },
		"bus":     func(s *AppSnapshot) { s.Elements[0].ProviderBusName = ":1.43" },
		"window":  func(s *AppSnapshot) { s.WindowHandle = "window-2" },
		"owner":   func(s *AppSnapshot) { s.Elements[0].ProviderWindowObjectPath = "/window/2" },
		"role":    func(s *AppSnapshot) { s.Elements[0].ControlType = "spin button" },
		"name":    func(s *AppSnapshot) { s.Elements[0].Name = "Replacement" },
	}
	for name, change := range changes {
		t.Run(name, func(t *testing.T) {
			registry := NewRefRegistry(32)
			source := numericControlSnapshot("slider", "50", "50%")
			before := assignNumericControlSnapshot(t, registry, source)
			ref := before.ByIndex[1].Ref
			change(source)
			after := assignNumericControlSnapshot(t, registry, source)
			if after.ByIndex[1].Ref == ref {
				t.Fatal("replacement reused the prior control ref")
			}
		})
	}
}

func TestNumericControlBindingLeavesOtherLocatorsUnchanged(t *testing.T) {
	for _, test := range []struct {
		name, role, label, provider string
	}{
		{"named slider", "slider", "Volume", "complete"},
		{"description button", "button", "", "complete"},
		{"incomplete slider", "slider", "", ""},
	} {
		t.Run(test.name, func(t *testing.T) {
			record := &ElementRecord{Name: test.label, Description: "Description"}
			selector := semanticDiagnosticSelector(CanonicalRole(test.role), record)
			kind, actual := axLocatorForRecord(record, CanonicalRole(test.role), selector, test.provider)
			if kind != AXLocatorSemantic || actual != selector {
				t.Fatalf("unrelated locator changed: %s %q", kind, actual)
			}
		})
	}
}
