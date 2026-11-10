package contract

import (
	"bytes"
	"encoding/json"
	"fmt"
	"sync"

	"github.com/santhosh-tekuri/jsonschema/v6"
)

// The published input schema of each public tool is the contract for its
// arguments. validateToolArguments enforces it before any request reaches a
// tool service, so input that tools/list rejects is never acted on and
// input it accepts is never refused for static shape reasons. Rules that
// depend on live state (ref ownership, window continuity) or that span
// several fields without a schema keyword (close must be last, text
// budgets) stay in the services.

const (
	ArgumentErrorInvalid = "invalid_argument"

	// MaxArgumentHintBytes bounds the corrected-call example appended to an
	// error, so a hint never repeats a long text argument back to the model.
	MaxArgumentHintBytes = 300
)

// ArgumentError reports the first problem found in tool arguments. Message
// is model-visible. Path is the JSON Pointer of the offending value and Code
// classifies the rejection; both stay internal.
type ArgumentError struct {
	Code    string
	Path    string
	Message string
}

func (e *ArgumentError) Error() string { return e.Message }

var publicInputSchemas struct {
	once    sync.Once
	schemas map[string]*jsonschema.Schema
	err     error
}

// CompiledInputSchema returns the compiled published input schema of a
// public tool, or nil for a name without one. All schemas are compiled once,
// on first use.
func CompiledInputSchema(tool string) (*jsonschema.Schema, error) {
	publicInputSchemas.once.Do(func() {
		publicInputSchemas.schemas, publicInputSchemas.err =
			CompileInputSchemas(AllToolDefinitions())
	})
	if publicInputSchemas.err != nil {
		return nil, publicInputSchemas.err
	}
	return publicInputSchemas.schemas[tool], nil
}

func CompileInputSchemas(tools []ToolDefinition) (map[string]*jsonschema.Schema, error) {
	compiler := jsonschema.NewCompiler()
	compiler.DefaultDraft(jsonschema.Draft2020)
	compiled := make(map[string]*jsonschema.Schema, len(tools))
	for _, tool := range tools {
		encoded, err := json.Marshal(tool.InputSchema)
		if err != nil {
			return nil, fmt.Errorf("encode %s input schema: %w", tool.Name, err)
		}
		document, err := jsonschema.UnmarshalJSON(bytes.NewReader(encoded))
		if err != nil {
			return nil, fmt.Errorf("decode %s input schema: %w", tool.Name, err)
		}
		location := "schema://computer-use/" + tool.Name
		if err := compiler.AddResource(location, document); err != nil {
			return nil, fmt.Errorf("register %s input schema: %w", tool.Name, err)
		}
		schema, err := compiler.Compile(location)
		if err != nil {
			return nil, fmt.Errorf("compile %s input schema: %w", tool.Name, err)
		}
		compiled[tool.Name] = schema
	}
	return compiled, nil
}

// ValidateToolArguments checks arguments against the published input schema
// of a public tool. It returns nil for valid arguments and for a name that
// has no published schema; the dispatcher rejects unknown names itself.
func ValidateToolArguments(tool string, args map[string]any) *ArgumentError {
	schema, err := CompiledInputSchema(tool)
	if err != nil {
		// A schema that does not compile is a build defect. Fail closed: no
		// call may run unvalidated.
		return &ArgumentError{
			Code:    ArgumentErrorInvalid,
			Message: tool + " input schema is unavailable: " + err.Error(),
		}
	}
	if schema == nil {
		return nil
	}
	instance := JSONInstance(args)
	if schema.Validate(instance) == nil {
		return nil
	}
	object, _ := instance.(map[string]any)
	explainer := schemaExplainer{tool: tool, args: object, rules: argumentRules[tool]}
	found := explainer.explainRoot(schema, object)
	return &ArgumentError{
		Code:    ArgumentErrorInvalid,
		Path:    jsonPointer(found.path),
		Message: found.text(),
	}
}

// JSONInstance returns args in the representation the validator accepts.
// Arguments decoded from JSON already have it; values built in Go (typed
// slices, structs) are normalized through a JSON round trip.
func JSONInstance(args map[string]any) any {
	if isJSONValue(args) {
		return args
	}
	encoded, err := json.Marshal(args)
	if err != nil {
		return args
	}
	decoded, err := jsonschema.UnmarshalJSON(bytes.NewReader(encoded))
	if err != nil {
		return args
	}
	return decoded
}

func isJSONValue(value any) bool {
	switch typed := value.(type) {
	case nil, bool, string, json.Number, float64, int:
		return true
	case map[string]any:
		for _, item := range typed {
			if !isJSONValue(item) {
				return false
			}
		}
		return true
	case []any:
		for _, item := range typed {
			if !isJSONValue(item) {
				return false
			}
		}
		return true
	default:
		return false
	}
}

// toolArgumentRules words errors that a schema keyword alone cannot explain
// well. Each hook runs only after the schema has rejected the arguments, so
// it can change the message but never what is accepted.
type toolArgumentRules struct {
	// before explains cross-branch problems ahead of the generic walk.
	before func(args map[string]any) *explanation
	// identity explains a failed top-level oneOf whose branches are
	// constraints rather than shapes, such as "exactly one identity field".
	// valid counts the matching branches. need is a noun phrase that can
	// follow "requires" ("one of app, app_ref, or window_ref"), or "" when
	// the problem is a conflict; message is the complete sentence.
	identity func(args map[string]any, valid int) (need, message string)
	// refine may rewrite the generic explanation.
	refine func(args map[string]any, found *explanation)
	// hintSynonyms maps verbs callers write to the published const value
	// they mean ("press" to "click"). They are used only to build a
	// corrected-call hint and never make an argument acceptable.
	hintSynonyms map[string]string
}

var argumentRules = map[string]toolArgumentRules{}
