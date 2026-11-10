package core

import (
	"strings"
)

func ontologyProviderRole(record *ElementRecord) string {
	if record == nil {
		return ""
	}
	// ControlType is the provider's stable role vocabulary. LocalizedControlType
	// is display text and cannot be the ontology lookup key.
	if role := strings.TrimSpace(record.ControlType); role != "" {
		return strings.ToLower(role)
	}
	return strings.ToLower(strings.TrimSpace(record.LocalizedControlType))
}

type RoleDescriptor struct {
	ModelRole       CanonicalRole
	ProviderAliases []string
	SemanticClass   string
}

var roleOntology = []RoleDescriptor{
	{CanonicalRole("alert"), []string{"alert"}, "boundary"},
	{CanonicalRole("application"), []string{"application"}, "boundary"},
	{CanonicalRole("article"), []string{"article"}, "boundary"},
	{CanonicalRole("button"), []string{"push button", "toggle button", "button"}, "control"},
	{CanonicalRole("checkbox"), []string{"check box"}, "control"},
	{CanonicalRole("combobox"), []string{"combo box"}, "control"},
	{CanonicalRole("dialog"), []string{"dialog"}, "boundary"},
	{CanonicalRole("document"), []string{"document web", "document text", "document frame", "document"}, "boundary"},
	{CanonicalRole("grid"), []string{"document spreadsheet"}, "collection"},
	{CanonicalRole("table"), []string{"table"}, "collection"},
	{CanonicalRole("cell"), []string{"table cell"}, "item"},
	{CanonicalRole("heading"), []string{"heading"}, "content"},
	{CanonicalRole("img"), []string{"image", "icon"}, "content"},
	{CanonicalRole("link"), []string{"link"}, "control"},
	{CanonicalRole("list"), []string{"list", "list box"}, "collection"},
	{CanonicalRole("listitem"), []string{"list item"}, "item"},
	{CanonicalRole("menu"), []string{"menu"}, "boundary"},
	{CanonicalRole("menubar"), []string{"menu bar"}, "boundary"},
	{CanonicalRole("menuitem"), []string{"menu item"}, "control"},
	{CanonicalRole("menuitemcheckbox"), []string{"check menu item"}, "control"},
	{CanonicalRole("menuitemradio"), []string{"radio menu item"}, "control"},
	{CanonicalRole("option"), []string{"option"}, "item"},
	{CanonicalRole("paragraph"), []string{"paragraph"}, "content"},
	{CanonicalRole("progressbar"), []string{"progress bar", "busy indicator"}, "status"},
	{CanonicalRole("radio"), []string{"radio button"}, "control"},
	{CanonicalRole("scrollbar"), []string{"scroll bar"}, "control"},
	{CanonicalRole("separator"), []string{"separator"}, "structure"},
	{CanonicalRole("slider"), []string{"slider"}, "control"},
	{CanonicalRole("spinbutton"), []string{"spin button"}, "control"},
	{CanonicalRole("status"), []string{"status bar", "notification"}, "status"},
	{CanonicalRole("tab"), []string{"page tab"}, "control"},
	{CanonicalRole("tablist"), []string{"page tab list"}, "collection"},
	{CanonicalRole("textbox"), []string{"entry", "text area", "password text"}, "control"},
	{RoleText, []string{"static", "text", "label", "caption"}, "text"},
	{CanonicalRole("toolbar"), []string{"tool bar"}, "boundary"},
	{CanonicalRole("tree"), []string{"tree"}, "collection"},
	{CanonicalRole("treegrid"), []string{"tree table"}, "collection"},
	{CanonicalRole("treeitem"), []string{"tree item"}, "item"},
	{CanonicalRole("window"), []string{"frame", "window"}, "boundary"},
	{CanonicalRole("group"), []string{"panel", "root pane", "filler", "section", "group", "viewport", "scroll pane", "layered pane", "split pane"}, "structure"},
}

var roleByProvider = func() map[string]RoleDescriptor {
	out := map[string]RoleDescriptor{}
	for _, descriptor := range roleOntology {
		out[strings.ToLower(strings.TrimSpace(string(descriptor.ModelRole)))] = descriptor
		for _, alias := range descriptor.ProviderAliases {
			out[strings.ToLower(strings.TrimSpace(alias))] = descriptor
		}
	}
	return out
}()

func canonicalRoleDescriptor(providerRole string, parent *AXNode) RoleDescriptor {
	provider := strings.ToLower(strings.TrimSpace(providerRole))
	if provider == "table cell" && parent != nil &&
		(parent.Role == "grid" || parent.Role == "treegrid") {
		return RoleDescriptor{ModelRole: "gridcell", SemanticClass: "item"}
	}
	if descriptor, ok := roleByProvider[provider]; ok {
		return descriptor
	}
	return RoleDescriptor{ModelRole: RoleGeneric, SemanticClass: "unknown"}
}

func providerAliasesForCanonicalRole(role string) []string {
	canonical := CanonicalRole(strings.ToLower(strings.TrimSpace(role)))
	candidates := []string{string(canonical)}
	for _, descriptor := range roleOntology {
		if descriptor.ModelRole != canonical {
			continue
		}
		candidates = append(candidates, descriptor.ProviderAliases...)
	}
	if canonical == "gridcell" {
		candidates = append(candidates, "table cell")
	}
	aliases := make([]string, 0, len(candidates))
	seen := map[string]bool{}
	for _, candidate := range candidates {
		candidate = strings.ToLower(strings.TrimSpace(candidate))
		if candidate == "" || seen[candidate] {
			continue
		}
		seen[candidate] = true
		aliases = append(aliases, candidate)
	}
	return aliases
}
