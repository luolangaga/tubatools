package transport

import (
	"encoding/json"
	"strings"
	"testing"

	contractpkg "github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// The published contract items that a code entry point must reach: AX modes,
// act step types, wait step kinds, observe values and GUI action types. They
// are read from the published tool schemas, so a new item fails the CLI guard
// below and the SDK guard in sdk/python/tests/test_sdk.py until each entry
// point either supports it or lists it as reachable only through its generic
// JSON call. Error codes are covered by TestExitCodesFollowInputEmission and
// the SDK's test_errors_are_classes_named_by_code.

// axWaitSubjects are the wait step kinds; the decoder accepts exactly one per
// step. The schema has no enum for them, so publishedInventory checks that
// each is still a published step field.
var axWaitSubjects = []string{"window", "query", "ref", "text"}

// cliJSONOnly lists published items that the CLI sends only through
// act --json or gui --json. Every other item has a shorthand.
var cliJSONOnly = map[string]bool{"gui:drag": true, "gui:scroll": true, "gui:wait": true}

func publishedInventory(t *testing.T) map[string]bool {
	t.Helper()
	items := map[string]bool{}
	for _, tool := range contractpkg.AllToolDefinitions() {
		schema := roundTripJSON(t, tool.InputSchema).(map[string]any)
		switch tool.Name {
		case contractpkg.PublicToolAX:
			properties := schema["properties"].(map[string]any)
			step := properties["actions"].(map[string]any)["items"].(map[string]any)["properties"].(map[string]any)
			for prefix, enum := range map[string]any{
				"ax.mode:":    properties["mode"].(map[string]any)["enum"],
				"ax.observe:": properties["observe"].(map[string]any)["enum"],
				"ax.act:":     step["type"].(map[string]any)["enum"],
			} {
				for _, value := range enum.([]any) {
					items[prefix+value.(string)] = true
				}
			}
			for _, subject := range axWaitSubjects {
				if step[subject] == nil {
					t.Fatalf("wait subject %q is not a published AX step field", subject)
				}
				items["ax.wait:"+subject] = true
			}
		case contractpkg.PublicToolGUI:
			collectGUIActionTypes(schema, items)
		}
	}
	if len(items) == 0 {
		t.Fatal("no published items")
	}
	return items
}

// collectGUIActionTypes finds every {"type":{"const":X}} action branch.
func collectGUIActionTypes(node any, items map[string]bool) {
	switch value := node.(type) {
	case map[string]any:
		if kind, ok := value["type"].(map[string]any); ok {
			if name, ok := kind["const"].(string); ok {
				items["gui:"+name] = true
			}
		}
		for _, child := range value {
			collectGUIActionTypes(child, items)
		}
	case []any:
		for _, child := range value {
			collectGUIActionTypes(child, items)
		}
	}
}

// contractItems lists the published items one call uses.
func contractItems(tool string, args map[string]any) []string {
	actions, _ := args["actions"].([]any)
	items := []string{}
	if tool == contractpkg.PublicToolGUI {
		for _, raw := range actions {
			action, _ := raw.(map[string]any)
			items = append(items, "gui:"+action["type"].(string))
		}
		return items
	}
	items = append(items, "ax.mode:"+args["mode"].(string))
	if observe, ok := args["observe"].(string); ok {
		items = append(items, "ax.observe:"+observe)
	}
	for _, raw := range actions {
		action, _ := raw.(map[string]any)
		items = append(items, "ax.act:"+action["type"].(string))
		if action["type"] == "wait" {
			for _, subject := range axWaitSubjects {
				if _, present := action[subject]; present {
					items = append(items, "ax.wait:"+subject)
				}
			}
		}
	}
	return items
}

// Every published item has a CLI shorthand with a golden case, or is listed
// in cliJSONOnly; never both, and the list names only published items.
func TestCLIReachesEveryPublishedItem(t *testing.T) {
	shorthand := map[string]bool{}
	for _, golden := range cliGolden {
		if strings.Contains(golden.line, "--json") {
			continue
		}
		var args map[string]any
		if err := json.Unmarshal([]byte(golden.args), &args); err != nil {
			t.Fatal(err)
		}
		for _, item := range contractItems(golden.tool, args) {
			shorthand[item] = true
		}
	}
	published := publishedInventory(t)
	for item := range published {
		if shorthand[item] == cliJSONOnly[item] {
			t.Errorf("%s: needs exactly one of a shorthand golden case or a cliJSONOnly entry", item)
		}
	}
	for item := range cliJSONOnly {
		if !published[item] {
			t.Errorf("cliJSONOnly lists %s, which the contract does not publish", item)
		}
	}
}
