package core

import (
	"regexp"
	"strings"
	"testing"
	"unicode/utf8"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

const longCheckboxHelp = "Select to insert the sheet as a link instead as a copy. " +
	"The links can be updated to show the current contents."

func describedNode(id string, role CanonicalRole, class, name, description, ref string) *AXNode {
	return &AXNode{
		ID: id, Role: role, SemanticClass: class, Name: name, Ref: ref,
		SemanticFingerprint: "f1:" + id,
		Properties:          AXProperties{Description: description},
	}
}

func renderShortenedDescriptions(t *testing.T, actedRef string, nodes ...*AXNode) string {
	t.Helper()
	window := &AXNode{ID: "win", Role: "window", SemanticClass: "boundary", Name: "Doc",
		SemanticFingerprint: "f1:win", Children: nodes}
	for _, node := range nodes {
		node.Parent = window
	}
	snapshot := &AXSnapshot{
		SchemaVersion: "AXSnapshot", Roots: []*AXNode{window},
		CanonicalRoots: []*AXNode{window}, ActedRef: actedRef,
	}
	context := &DistillationContext{Snapshot: snapshot}
	roots, _, _, err := shortenNamedDescriptionsPlugin().Apply(context, snapshot.Roots)
	if err != nil {
		t.Fatal(err)
	}
	snapshot.Roots = roots
	lines, err := RenderAXSnapshotYAML(snapshot, AXRenderOptions{})
	if err != nil {
		t.Fatal(err)
	}
	return strings.Join(lines, "\n")
}

func TestNamedNodeLongDescriptionIsShortenedAtWordBoundary(t *testing.T) {
	text := renderShortenedDescriptions(t, "",
		describedNode("link", "checkbox", "control", "Link", longCheckboxHelp, "r5"),
	)
	want := `/description: Select to insert the sheet as a link instead as a copy. The links can be…`
	if !strings.Contains(text, want) {
		t.Fatalf("named checkbox help was not shortened at a word boundary:\n%s", text)
	}
	for _, line := range strings.Split(text, "\n") {
		if strings.Contains(line, "/description:") &&
			utf8.RuneCountInString(line) > maxNamedDescriptionRunes+40 {
			t.Fatalf("shortened description line is still long: %q", line)
		}
	}
}

func TestDescriptionsThatCarryInformationStayIntact(t *testing.T) {
	paragraph := "Paragraph: 0 High Interest Rate savings account offers a fixed rate for twelve months"
	text := renderShortenedDescriptions(t, "r7",
		describedNode("unnamed", "button", "control", "", longCheckboxHelp, "r6"),
		describedNode("acted", "checkbox", "control", "Acted", longCheckboxHelp, "r7"),
		describedNode("para", "paragraph", "content", "Body", paragraph, ""),
		describedNode("cell", "cell", "item", "A1", paragraph, "r8"),
		&AXNode{ID: "match", Role: "checkbox", SemanticClass: "control", Name: "Match",
			Ref: "r9", SemanticFingerprint: "f1:match", FindMatch: true,
			Properties: AXProperties{Description: longCheckboxHelp}},
	)
	if got := strings.Count(text, longCheckboxHelp); got != 3 {
		t.Fatalf("unnamed, acted and find-match descriptions must stay whole (got %d):\n%s", got, text)
	}
	if got := strings.Count(text, paragraph); got != 2 {
		t.Fatalf("document content descriptions must stay whole (got %d):\n%s", got, text)
	}
	if strings.Contains(text, "…") {
		t.Fatalf("no description should be shortened here:\n%s", text)
	}
}

func TestBoilerplateDescriptionIsDropped(t *testing.T) {
	text := renderShortenedDescriptions(t, "",
		describedNode("group", "group", "structure", "",
			"Please press enter to go into child control for more operations", "r47"),
	)
	if strings.Contains(text, "Please press enter") || !strings.Contains(text, "group [ref=r47]") {
		t.Fatalf("boilerplate description was not dropped cleanly:\n%s", text)
	}
}

func TestShortenAtWordKeepsShortTextAndCutsLongWord(t *testing.T) {
	if got, cut := shortenAtWord("Close Document", 80); cut || got != "Close Document" {
		t.Fatalf("short text changed: %q", got)
	}
	long := strings.Repeat("x", 120)
	got, cut := shortenAtWord(long, 80)
	if !cut || utf8.RuneCountInString(got) != 81 || !strings.HasSuffix(got, "…") {
		t.Fatalf("a single long word must be cut at the limit: %q", got)
	}
}

// The acted ref travels from the action request through snapshot preparation
// into the typed snapshot, so the acted node keeps its full description in the
// rendered post-action tree while its named siblings are shortened.
func TestActedRefKeepsFullDescriptionThroughSnapshotPreparation(t *testing.T) {
	service := NewService()
	build := func(request AXServiceRequest) string {
		snapshot := windowTransitionSnapshot(
			"w2:0123456789abcdef0123456789abcdef", "Document", true,
		)
		snapshot.Elements[2].Description = longCheckboxHelp
		snapshot.Elements[3].Description = longCheckboxHelp
		prepared, result := service.prepareProviderSnapshot(
			request,
			&ProviderAccessibilityResult{OK: true, Snapshot: snapshot},
		)
		if result.IsError {
			t.Fatalf("prepare failed: %#v", result)
		}
		return strings.Join(prepared.TreeLines, "\n")
	}
	observed := build(AXServiceRequest{Operation: axServiceObserve, TreeView: contract.TreeViewWorking})
	if strings.Contains(observed, longCheckboxHelp) {
		t.Fatalf("an observe must shorten named help text:\n%s", observed)
	}
	match := regexp.MustCompile(`button "OK" \[ref=(r\d+)\]`).FindStringSubmatch(observed)
	if match == nil {
		t.Fatalf("test setup: OK button has no ref:\n%s", observed)
	}
	acted := build(AXServiceRequest{
		TreeView:  contract.TreeViewWorking,
		Action:    axKeypressCommand{Key: "ENTER"},
		SourceRef: match[1],
	})
	if strings.Count(acted, longCheckboxHelp) != 1 || !strings.Contains(acted, "…") {
		t.Fatalf("only the acted button keeps its full description:\n%s", acted)
	}
}
