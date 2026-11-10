package core

import (
	"fmt"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestGUIRootPropertiesPreserveBranchConstraints(t *testing.T) {
	schema := contract.GUIInputSchema()
	properties := schema["properties"].(map[string]any)
	branchNames := map[string]bool{}
	for _, raw := range schema["oneOf"].([]any) {
		branch := raw.(map[string]any)
		if branch["additionalProperties"] != false {
			t.Fatal("GUI branches must reject unknown fields")
		}
		for name := range branch["properties"].(map[string]any) {
			branchNames[name] = true
		}
	}
	if len(properties) != len(branchNames) {
		t.Fatal("root properties must declare exactly the branch property names")
	}
	for name, value := range properties {
		declaration, ok := value.(map[string]any)
		if !branchNames[name] || !ok || len(declaration) != 0 {
			t.Fatalf("root property %q must leave all constraints to the branches", name)
		}
	}
}

// OpenCode v1.19.3 convertTool adds root properties (if absent) and
// additionalProperties:false. With properties only inside oneOf, that
// transformation rejects every GUI call before the MCP receives it.
func TestGUISchemaAfterOpenCodeConversion(t *testing.T) {
	original := contract.GUIInputSchema()
	delete(original, "properties")
	current := contract.GUIInputSchema()
	converted := contract.GUIInputSchema()
	converted["additionalProperties"] = false
	brokenOriginal := contract.GUIInputSchema()
	brokenOriginal["properties"] = map[string]any{}
	brokenOriginal["additionalProperties"] = false
	schemas, err := contract.CompileInputSchemas([]contract.ToolDefinition{
		{Name: "original", InputSchema: original},
		{Name: "current", InputSchema: current},
		{Name: "converted", InputSchema: converted},
		{Name: "codex", InputSchema: codexSanitize(jsonRoundTrip(current)).(map[string]any)},
		{Name: "brokenOriginal", InputSchema: brokenOriginal},
	})
	if err != nil {
		t.Fatal(err)
	}
	for _, tc := range []struct {
		name, args string
		valid      bool
	}{
		{"screenshot", `{"actions":[{"type":"screenshot"}]}`, true},
		{"click", `{"actions":[{"type":"click","x":1,"y":2}]}`, true},
		{"input batch", `{"actions":[{"type":"click","x":1,"y":2},{"type":"type","text":"hello"},{"type":"keypress","keys":["ENTER"]}]}`, true},
		{"missing actions", `{}`, false},
		{"empty actions", `{"actions":[]}`, false},
		{"wrong actions type", `{"actions":"screenshot"}`, false},
		{"unknown root field", `{"actions":[{"type":"screenshot"}],"wiat":1}`, false},
		{"unknown action field", `{"actions":[{"type":"click","x":1,"y":2,"wiat":1}]}`, false},
		{"missing coordinate", `{"actions":[{"type":"click","x":1}]}`, false},
		{"screenshot before input", `{"actions":[{"type":"screenshot"},{"type":"wait"}]}`, false},
		{"screenshot after input", `{"actions":[{"type":"wait"},{"type":"screenshot"}]}`, false},
		{"multiple screenshots", `{"actions":[{"type":"screenshot"},{"type":"screenshot"}]}`, false},
		{"screenshot settle", `{"actions":[{"type":"screenshot"}],"settle_ms":0}`, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			args := decodeTestArguments(t, tc.args)
			for _, name := range []string{"original", "current", "converted", "codex"} {
				if err := schemas[name].Validate(args); (err == nil) != tc.valid {
					t.Errorf("%s: accepted=%v want %v: %v", name, err == nil, tc.valid, err)
				}
			}
			if tc.valid && schemas["brokenOriginal"].Validate(args) == nil {
				t.Fatal("original OpenCode conversion should reproduce the rejection")
			}
		})
	}
	for _, count := range []int{0, 1, 2, contract.GUIMaxActions, contract.GUIMaxActions + 1} {
		for _, settle := range []any{nil, -1, 0, contract.GUIMaxSettleMS, contract.GUIMaxSettleMS + 1, 0.5, "0"} {
			t.Run(fmt.Sprintf("actions=%d/settle=%v", count, settle), func(t *testing.T) {
				actions := make([]any, count)
				for i := range actions {
					actions[i] = map[string]any{"type": "wait"}
				}
				args := map[string]any{"actions": actions}
				valid := count >= 1 && count <= contract.GUIMaxActions
				if settle != nil {
					args["settle_ms"] = settle
					n, ok := settle.(int)
					valid = valid && ok && n >= 0 && n <= contract.GUIMaxSettleMS
				}
				args = jsonRoundTrip(args).(map[string]any)
				for _, name := range []string{"original", "current", "converted", "codex"} {
					if err := schemas[name].Validate(args); (err == nil) != valid {
						t.Errorf("%s: accepted=%v want %v: %v", name, err == nil, valid, err)
					}
				}
			})
		}
	}
}
