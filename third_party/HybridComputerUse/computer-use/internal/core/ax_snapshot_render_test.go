package core

import (
	"fmt"
	"reflect"
	"strings"
	"testing"
)

func TestRendererHidesPredictedRoutesAndCanonicalActionAliases(t *testing.T) {
	semantic := &AXNode{
		ID: "semantic", Role: "button", Name: "Save",
		SemanticFingerprint: "f1:semantic",
		Capabilities: AXCapabilities{
			Activate:        ActivationSemantic,
			Actions:         []string{"open-menu"},
			ProviderActions: []string{"click", "menu", "release"},
		},
	}
	fallback := &AXNode{
		ID: "fallback", Role: "button", Name: "Canvas control",
		SemanticFingerprint: "f1:fallback",
		Capabilities:        AXCapabilities{Activate: ActivationLiveGeometry},
	}
	snapshot := &AXSnapshot{
		SchemaVersion: "AXSnapshot",
		AppIdentity:   "test", WindowHandle: "window", Revision: "r1",
		Roots: []*AXNode{semantic, fallback},
	}
	lines, err := RenderAXSnapshotYAML(snapshot, AXRenderOptions{})
	if err != nil {
		t.Fatal(err)
	}
	text := strings.Join(lines, "\n")
	for _, forbidden := range []string{
		"/activate",
		"live_geometry_fallback",
		"/capabilities",
		"provider-actions",
	} {
		if strings.Contains(text, forbidden) {
			t.Fatalf("working projection leaked %q:\n%s", forbidden, text)
		}
	}
	full, err := RenderAXSnapshotYAML(snapshot, AXRenderOptions{})
	if err != nil {
		t.Fatal(err)
	}
	for _, forbidden := range []string{
		"/provider-role",
		"/provider-actions",
		"release",
	} {
		if strings.Contains(strings.Join(full, "\n"), forbidden) {
			t.Fatalf("model renderer leaked %q:\n%s",
				forbidden, strings.Join(full, "\n"))
		}
	}
}

func TestCanonicalActionOntologyHidesUnknownProviderVocabulary(t *testing.T) {
	record := &ElementRecord{
		Actions: []string{"click", "showContextMenu", "release"},
	}
	if got := typedProviderActions(record); !reflect.DeepEqual(
		got, []string{"click", "release", "showcontextmenu"},
	) {
		t.Fatalf("raw provider actions were not preserved: %#v", got)
	}
}

func TestRendererDeduplicatesFieldsAndPureIconText(t *testing.T) {
	node := &AXNode{
		ID: "button", Role: "button", Name: "Continue",
		SemanticFingerprint: "f1:button",
		Properties: AXProperties{
			Value:       " Continue ",
			Description: "Continue",
			Placeholder: "Continue",
		},
	}
	icon := &AXNode{
		ID: "icon", Role: "text", SemanticFingerprint: "f1:icon",
		Properties: AXProperties{Value: "\ueabc\u200b"},
	}
	snapshot := &AXSnapshot{
		SchemaVersion: "AXSnapshot",
		AppIdentity:   "test", WindowHandle: "window", Revision: "r1",
		Roots: []*AXNode{node, icon},
	}
	lines, err := RenderAXSnapshotYAML(snapshot, AXRenderOptions{})
	if err != nil {
		t.Fatal(err)
	}
	text := strings.Join(lines, "\n")
	for _, forbidden := range []string{
		"/value", "/description", "/placeholder", "\ueabc",
	} {
		if strings.Contains(text, forbidden) {
			t.Fatalf("working projection leaked %q:\n%s", forbidden, text)
		}
	}
	full, err := RenderAXSnapshotYAML(snapshot, AXRenderOptions{})
	if err != nil {
		t.Fatal(err)
	}
	fullText := strings.Join(full, "\n")
	for _, forbidden := range []string{
		`/value: " Continue "`,
		`/description: Continue`,
		`/placeholder: Continue`,
		"\ueabc",
		"/provider-role",
		"/provider-actions",
	} {
		if strings.Contains(fullText, forbidden) {
			t.Fatalf("model renderer leaked %q:\n%s", forbidden, fullText)
		}
	}
}

func TestRendererUsesOneSpaceHierarchy(t *testing.T) {
	window := &AXNode{ID: "window", Role: "window", Name: "Bills"}
	toolbar := &AXNode{
		ID: "toolbar", Role: "toolbar", Name: "Spaces Toolbar",
		Properties: AXProperties{Description: "Toggle spaces"},
		Parent:     window,
	}
	button := &AXNode{
		ID: "mail", Role: "button", Name: "Mail (Ctrl+1)", Ref: "r1",
		Parent: toolbar,
	}
	toolbar.Children = []*AXNode{button}
	window.Children = []*AXNode{toolbar}
	snapshot := &AXSnapshot{
		SchemaVersion: "AXSnapshot",
		AppIdentity:   "test", WindowHandle: "window", Revision: "r1",
		Roots: []*AXNode{window},
	}
	lines, err := RenderAXSnapshotYAML(snapshot, AXRenderOptions{})
	if err != nil {
		t.Fatal(err)
	}
	want := strings.Join([]string{
		"ax:",
		` - window "Bills":`,
		`  - toolbar "Spaces Toolbar":`,
		`   - /description: Toggle spaces`,
		`   - button "Mail (Ctrl+1)" [ref=r1]`,
	}, "\n")
	if got := strings.Join(lines, "\n"); got != want {
		t.Fatalf("AX tree mismatch:\n--- got ---\n%s\n--- want ---\n%s", got, want)
	}
}

func TestRendererEscapesOnlyUserText(t *testing.T) {
	node := &AXNode{
		ID: "button", Role: "button", Name: "Save: \"draft\"\nnow", Ref: "r7",
		Properties: AXProperties{
			Description: "Line one\nLine two",
		},
	}
	snapshot := &AXSnapshot{
		SchemaVersion: "AXSnapshot",
		AppIdentity:   "test", WindowHandle: "window", Revision: "r1",
		Roots: []*AXNode{node},
	}
	lines, err := RenderAXSnapshotYAML(snapshot, AXRenderOptions{})
	if err != nil {
		t.Fatal(err)
	}
	text := strings.Join(lines, "\n")
	if strings.Contains(text, `- "button \`) {
		t.Fatalf("entire semantic node label was quoted again:\n%s", text)
	}
	for _, expected := range []string{
		`button "Save: \"draft\"\nnow" [ref=r7]:`,
		`/description: "Line one\nLine two"`,
	} {
		if !strings.Contains(text, expected) {
			t.Fatalf("escaped user text missing %q:\n%s", expected, text)
		}
	}
}

func TestCohortCompressionSuppressesOnlyRepeatedNonStateValues(t *testing.T) {
	parent := &AXNode{ID: "toolbar", Role: "toolbar", Name: "Tools"}
	for index := 0; index < 7; index++ {
		value := "0.0"
		if index == 6 {
			value = "exception"
		}
		child := &AXNode{
			ID:   fmt.Sprintf("button-%d", index),
			Role: "button",
			Name: fmt.Sprintf("Action %d", index),
			Ref:  fmt.Sprintf("r%d", index+1),
			Properties: AXProperties{
				Value: value,
			},
			Capabilities: AXCapabilities{Activate: ActivationSemantic},
			Parent:       parent,
		}
		parent.Children = append(parent.Children, child)
	}
	stateful := &AXNode{
		ID:   "slider",
		Role: "slider",
		Name: "Zoom",
		Ref:  "r8",
		Properties: AXProperties{
			Value: "0.0",
		},
		Capabilities: AXCapabilities{SetValue: true},
		Parent:       parent,
	}
	parent.Children = append(parent.Children, stateful)
	snapshot := &AXSnapshot{
		SchemaVersion:  "AXSnapshot",
		AppIdentity:    "test",
		WindowHandle:   "window",
		Revision:       "revision",
		CanonicalRoots: []*AXNode{parent},
		Roots:          cloneAXForest([]*AXNode{parent}, nil),
		ByID:           map[string]*AXNode{},
		ByIndex:        map[int]*AXNode{},
	}
	if err := distillAXSnapshot(snapshot); err != nil {
		t.Fatal(err)
	}
	for index := 0; index < 6; index++ {
		if snapshot.Roots[0].Children[index].Properties.Value != "" {
			t.Fatalf("repeated cohort value %d was not suppressed", index)
		}
	}
	if got := snapshot.Roots[0].Children[6].Properties.Value; got != "exception" {
		t.Fatalf("cohort exception was suppressed: %q", got)
	}
	if got := snapshot.Roots[0].Children[7].Properties.Value; got != "0.0" {
		t.Fatalf("decision-bearing value was suppressed: %q", got)
	}
	foundAudit := false
	for _, report := range snapshot.Distillation {
		if report.Name == "suppress_redundant_cohort_properties" {
			foundAudit = report.SuppressedProperties == 6
		}
	}
	if !foundAudit {
		t.Fatalf("cohort suppression was not audited: %#v", snapshot.Distillation)
	}
	if got := parent.Children[0].Properties.Value; got != "0.0" {
		t.Fatalf("canonical cohort value was mutated: %q", got)
	}
}

func FuzzYAMLScalarRoundTripSurface(f *testing.F) {
	for _, seed := range []string{
		"", "null", "a: b", "# heading", "line\nbreak",
		`quoted "value"`, "中文", "- item", "true",
	} {
		f.Add(seed)
	}
	f.Fuzz(func(t *testing.T, value string) {
		rendered := yamlScalar(value)
		if strings.ContainsRune(rendered, '\x00') {
			t.Fatalf("YAML scalar leaked NUL: %q", rendered)
		}
		if !strings.HasPrefix(rendered, `"`) &&
			(strings.Contains(value, ": ") ||
				strings.HasPrefix(value, "#") ||
				strings.ContainsAny(value, "\r\n\t")) {
			t.Fatalf("unsafe scalar was not quoted: value=%q rendered=%q", value, rendered)
		}
	})
}
