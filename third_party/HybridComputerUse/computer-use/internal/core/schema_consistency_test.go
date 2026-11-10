package core

import (
	"bufio"
	"compress/gzip"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"math/big"
	"os"
	"sort"
	"strings"
	"testing"

	"github.com/santhosh-tekuri/jsonschema/v6"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// The consistency test proves that the published input schema and the
// runtime accept the same arguments. Samples are generated from the schema
// itself (one per branch, every enum value, every optional field) and then
// mutated field by field. For each sample the schema verdict is compared
// with dispatchValidated, which runs the tool parsing with the schema gate
// bypassed, against a fixture whose refs a1/w1/r1/f1 resolve.
//
// A runtime rejection of a schema-valid sample is allowed only for the
// declared cross-field rules below; each must carry its own error code.

var crossFieldRuleCodes = map[string]bool{
	contract.ArgumentRuleCloseNotLast:    true,
	contract.ArgumentRuleTextBudget:      true,
	contract.ArgumentRuleRefOwnsIdentity: true,
	contract.ArgumentRuleWaitBudget:      true,
	// The flat AX schema has no oneOf or if/then, so rules that depend on
	// the value of mode or of an action type are declared rules of
	// the AX decoder: which fields a mode or type accepts or requires,
	// whether a ref must be rN or wN, and exactly-one-of pairs whose
	// presence depends on the type. Rules the flat schema can express
	// (blank strings, app with window_ref) are in the schema instead.
	axRuleFieldForMode:    true,
	axRuleFieldForType:    true,
	axRuleRequiredForMode: true,
	axRuleRequiredForType: true,
	axRuleOneOf:           true,
	axRuleRefKind:         true,
	axRuleObserveNone:     true,
	axRuleFindFilterPair:  true,
}

// axConsistencySeeds are valid calls for each AX mode and action type. The
// flat AX schema has no branches, so seeds generated from it alone would
// cover only the fields every call shares.
var axConsistencySeeds = []string{
	`{"mode":"apps"}`,
	`{"mode":"observe","app":"a1"}`,
	`{"mode":"observe","window_ref":"w1","root":"r1","scope":"content","observe":"full"}`,
	`{"mode":"act","actions":[{"type":"wait","window":"Sheet","gone":true,"timeout_ms":1}]}`,
	`{"mode":"act","actions":[{"type":"wait","ref":"r1","value":"5","timeout_ms":1}]}`,
	`{"mode":"act","actions":[{"type":"wait","query":{"role":"button","name_contains":"Save","state":"focused"},"timeout_ms":1}]}`,
	`{"mode":"act","actions":[{"type":"wait","ref":"r1","state":"checked","timeout_ms":1}]}`,
	`{"mode":"act","window_ref":"w1","actions":[{"type":"wait","text":"Save","timeout_ms":1}]}`,
	`{"mode":"find","window_ref":"w1","limit":5,"scope":"window","query":{"role":"button","name_contains":"Save","text_equals":"Save","state":"focused"}}`,
	`{"mode":"find","cursor":"f1","limit":5}`,
	`{"mode":"act","observe":"none","actions":[{"type":"click","ref":"r1"},{"type":"wait","ref":"r1","gone":true,"timeout_ms":1}]}`,
	`{"mode":"act","window_ref":"w1","actions":[{"type":"keypress","keys":["CTRL","S"]},{"type":"type","text":"x"},{"type":"scroll","direction":"down","pages":2}]}`,
	`{"mode":"act","actions":[{"type":"set_value","ref":"r1","value":"x"},{"type":"type","ref":"r1","text":"y"},{"type":"keypress","ref":"r1","keys":["TAB"]},{"type":"scroll","ref":"r1","direction":"up"},{"type":"wait","window":"Sheet","timeout_ms":1}]}`,
	`{"mode":"act","actions":[{"type":"activate","ref":"w1"}]}`,
	`{"mode":"act","actions":[{"type":"close","ref":"w1"}]}`,
	`{"mode":"act","actions":[{"type":"activate","title":"Sheet"}]}`,
}

// liveStateRejections are runtime refusals that depend on desktop or cache
// state the probe fixture does not model (an index target needs a snapshot
// of that exact app; a cursor needs a full cached AX snapshot). They are not
// argument checks, so they count as acceptance of the arguments.
var liveStateRejections = []string{
	"cannot use target.index without a current accessibility snapshot",
	"AX find cursor has no recoverable snapshot",
	// The ref registry binds each ref to one app; whether a named app owns
	// r1 is registry state, not argument shape. Refs other than the
	// fixture's r1/w1/a1/f1 are simply unknown to the registry.
	"belongs to app",
	"unknown short AX",
	"is not a current",
	"unknown AX find cursor",
	"was never issued in this session",
}

type runtimeVerdict struct {
	accepted bool
	// liveState marks a refusal that depends on registry or snapshot state
	// rather than on the argument shape.
	liveState bool
	code      string
	text      string
}

// probeReached stops a call at its first provider request. Reaching the
// provider is what "the runtime accepted the arguments" means here; stopping
// there keeps polling assertions from waiting for their timeouts.
type probeReached struct{}

type probeBackend struct{ *recordingPlatformBackend }

func (probeBackend) ObserveApps(ProviderObserveAppsRequest) (ProviderAccessibilityResult, error) {
	panic(probeReached{})
}
func (probeBackend) Observe(ProviderObserveRequest) (ProviderAccessibilityResult, error) {
	panic(probeReached{})
}
func (probeBackend) ResolveAndAct(ProviderActionRequest) (ProviderAccessibilityResult, error) {
	panic(probeReached{})
}
func (probeBackend) ExecuteBatch(ProviderDesktopRequest) (ProviderDesktopResult, error) {
	panic(probeReached{})
}
func (probeBackend) ListWindows(ProviderWindowListRequest) (ProviderWindowResult, error) {
	panic(probeReached{})
}
func (probeBackend) ActOnWindow(ProviderWindowActionRequest) (ProviderWindowResult, error) {
	panic(probeReached{})
}

type consistencyProbe struct {
	service *Service
}

// consistencyApp owns the fixture refs a1, w1 and r1.
const consistencyApp = "libreoffice"

func newConsistencyProbe(t testing.TB) consistencyProbe {
	t.Helper()
	service, _, _, _ := sheetBatchService(&testing.T{})
	service.Backend = probeBackend{&recordingPlatformBackend{err: errors.New("unreachable")}}
	snapshot := editorFormSnapshot()
	if cursor := service.FindCursorRegistry.Create(snapshot, snapshot.Elements, 0, "probe"); cursor != "f1" {
		t.Fatalf("fixture cursor = %q, want f1", cursor)
	}
	// Index targets address the current snapshot of the named app.
	current := editorFormSnapshot()
	current.App.Name = consistencyApp
	service.rememberSnapshot(consistencyApp, current)
	return consistencyProbe{service: service}
}

func (p consistencyProbe) run(tool string, args map[string]any) (verdict runtimeVerdict) {
	defer func() {
		if recovered := recover(); recovered != nil {
			if _, reached := recovered.(probeReached); !reached {
				panic(recovered)
			}
			verdict = runtimeVerdict{accepted: true, text: "reached provider"}
		}
	}()
	result := p.service.dispatchValidated(tool, cloneJSON(args).(map[string]any))
	verdict = runtimeVerdict{accepted: !result.IsError, text: firstLine(ResultText(result))}
	for _, fragment := range liveStateRejections {
		if strings.Contains(verdict.text, fragment) {
			verdict.liveState = true
		}
	}
	if code, ok := result.StructuredContent["argument_error_code"].(string); ok {
		verdict.code = code
	}
	return verdict
}

func schemaAccepts(t testing.TB, tool string, args map[string]any) bool {
	t.Helper()
	schema, err := contract.CompiledInputSchema(tool)
	if err != nil {
		t.Fatal(err)
	}
	return schema.Validate(contract.JSONInstance(args)) == nil
}

type consistencySample struct {
	tool   string
	origin string
	args   map[string]any
}

type consistencyMismatch struct {
	sample  consistencySample
	schema  bool
	runtime runtimeVerdict
}

func checkConsistency(t testing.TB, sample consistencySample) *consistencyMismatch {
	t.Helper()
	schemaOK := schemaAccepts(t, sample.tool, sample.args)
	verdict := newConsistencyProbe(t).run(sample.tool, sample.args)
	switch {
	case schemaOK && verdict.accepted:
		return nil
	case !schemaOK && !verdict.accepted:
		return nil
	case schemaOK && crossFieldRuleCodes[verdict.code]:
		return nil
	case schemaOK && verdict.liveState:
		// The probe cannot tell whether a later shape check would have
		// passed, so this sample proves nothing either way.
		return nil
	case !schemaOK && sample.tool == contract.PublicToolGUI:
		// The Go GUI path decodes strictly and hands the action list to the
		// provider, which owns the remaining checks. The provider side is
		// compared with the schema by gui_contract_samples.json.
		return nil
	}
	return &consistencyMismatch{sample: sample, schema: schemaOK, runtime: verdict}
}

func TestPublishedSchemaMatchesRuntimeAcceptance(t *testing.T) {
	samples := consistencySamples(t)
	if len(samples) < 1000 {
		t.Fatalf("only %d consistency samples were generated", len(samples))
	}
	var mismatches []consistencyMismatch
	accepted := 0
	for _, sample := range samples {
		if schemaAccepts(t, sample.tool, sample.args) {
			accepted++
		}
		if mismatch := checkConsistency(t, sample); mismatch != nil {
			mismatches = append(mismatches, *mismatch)
		}
	}
	t.Logf("%d samples, %d schema-valid", len(samples), accepted)
	if accepted < len(samples)/10 {
		t.Fatalf("too few schema-valid samples (%d of %d)", accepted, len(samples))
	}
	if len(mismatches) == 0 {
		return
	}
	sort.Slice(mismatches, func(i, j int) bool {
		return mismatches[i].sample.origin < mismatches[j].sample.origin
	})
	var report strings.Builder
	seen := map[string]bool{}
	for _, mismatch := range mismatches {
		key := mismatch.sample.tool + " " + mismatch.sample.origin
		if seen[key] {
			continue
		}
		seen[key] = true
		fmt.Fprintf(&report, "\n%s %s\n    args=%s\n    schema=%v runtime=%v code=%q %q",
			mismatch.sample.tool, mismatch.sample.origin, contract.CompactJSON(mismatch.sample.args),
			mismatch.schema, mismatch.runtime.accepted, mismatch.runtime.code, mismatch.runtime.text)
		if len(seen) >= 400 {
			break
		}
	}
	t.Fatalf("%d schema/runtime mismatches (%d distinct origins):%s",
		len(mismatches), len(seen), report.String())
}

// consistencySamples builds the valid seeds from the schema and applies every
// mutation to every field of every seed.
func consistencySamples(t testing.TB) []consistencySample {
	t.Helper()
	var samples []consistencySample
	for _, tool := range []string{contract.PublicToolAX, contract.PublicToolGUI} {
		schema, err := contract.CompiledInputSchema(tool)
		if err != nil {
			t.Fatal(err)
		}
		seeds := generateInstances(schema, 0)
		if tool == contract.PublicToolAX {
			for _, raw := range axConsistencySeeds {
				seeds = append(seeds, decodeTestArguments(&testing.T{}, raw))
			}
		}
		valid := 0
		for index, seed := range seeds {
			object, ok := seed.(map[string]any)
			if !ok {
				continue
			}
			object = completeObject(schema, object)
			if object == nil {
				continue
			}
			valid++
			origin := fmt.Sprintf("seed%d", index)
			samples = append(samples, consistencySample{tool, origin, object})
			for _, mutation := range mutateInstance(schema, object) {
				samples = append(samples, consistencySample{tool, origin + mutation.origin, mutation.args})
			}
			// The full seed carries every optional field that keeps it
			// valid, so optional fields are mutated as well.
			accepts := func(candidate map[string]any) bool {
				return newConsistencyProbe(t).run(tool, candidate).accepted
			}
			if full := enrichInstance(schema, object, accepts); contract.CompactJSON(full) != contract.CompactJSON(object) {
				origin += "full"
				samples = append(samples, consistencySample{tool, origin, full})
				for _, mutation := range mutateInstance(schema, full) {
					samples = append(samples, consistencySample{tool, origin + mutation.origin, mutation.args})
				}
			}
		}
		if valid < 6 {
			t.Fatalf("%s produced only %d valid seeds", tool, valid)
		}
	}
	return samples
}

// generateInstances returns one instance per branch reachable through
// required fields: each oneOf/anyOf alternative and each enum value appear
// in at least one instance. Optional fields are left out; mutations add them.
func generateInstances(schema *jsonschema.Schema, depth int) []any {
	schema = followRef(schema)
	if schema == nil || depth > 8 {
		return []any{nil}
	}
	if len(schema.OneOf) > 0 && contract.ShapeBranches(schema.OneOf) {
		var out []any
		for _, branch := range schema.OneOf {
			out = append(out, generateInstances(branch, depth+1)...)
		}
		return out
	}
	if schema.Const != nil {
		return []any{*schema.Const}
	}
	if schema.Enum != nil {
		return append([]any(nil), schema.Enum.Values...)
	}
	types := []string{}
	if schema.Types != nil {
		types = schema.Types.ToStrings()
	}
	switch {
	case contract.ContainsString(types, "object") || len(schema.Properties) > 0:
		base := map[string]any{}
		variants := [][2]any{}
		required := append([]string(nil), schema.Required...)
		// An object that needs one of several fields (anyOf of required
		// sets, or minProperties) gets one variant per alternative.
		alternatives := []string{}
		for _, branch := range schema.AnyOf {
			if keys := followRef(branch).Required; len(keys) == 1 {
				alternatives = append(alternatives, keys[0])
			}
		}
		if len(alternatives) == 0 && schema.MinProperties != nil && *schema.MinProperties > 0 {
			alternatives = contract.SortedKeys(schema.Properties)
		}
		if len(alternatives) > 0 {
			required = append(required, alternatives[0])
			for _, key := range alternatives[1:] {
				if property := schema.Properties[key]; property != nil {
					variants = append(variants, [2]any{"swap:" + alternatives[0] + ":" + key,
						generateInstances(property, depth+1)[0]})
				}
			}
		}
		for _, key := range required {
			property := schema.Properties[key]
			values := generateInstances(property, depth+1)
			base[key] = values[0]
			for _, value := range values[1:] {
				variants = append(variants, [2]any{key, value})
			}
		}
		out := []any{base}
		for _, variant := range variants {
			clone := cloneJSON(base).(map[string]any)
			key := variant[0].(string)
			if swap := strings.Split(key, ":"); len(swap) == 3 && swap[0] == "swap" {
				delete(clone, swap[1])
				key = swap[2]
			}
			clone[key] = variant[1]
			out = append(out, clone)
		}
		return out
	case contract.ContainsString(types, "array"):
		count := 1
		if schema.MinItems != nil && *schema.MinItems > count {
			count = *schema.MinItems
		}
		items := generateInstances(schema.Items2020, depth+1)
		var out []any
		for _, item := range items {
			array := make([]any, count)
			for index := range array {
				array[index] = cloneJSON(item)
			}
			out = append(out, array)
		}
		return out
	case contract.ContainsString(types, "string"):
		return []any{stringFor(schema)}
	case contract.ContainsString(types, "integer"), contract.ContainsString(types, "number"):
		return []any{numberFor(schema)}
	case contract.ContainsString(types, "boolean"):
		return []any{true}
	}
	return []any{consistencyApp}
}

func followRef(schema *jsonschema.Schema) *jsonschema.Schema {
	for schema != nil && schema.Ref != nil && schema.Types == nil && len(schema.Properties) == 0 &&
		len(schema.OneOf) == 0 && schema.Const == nil && schema.Enum == nil {
		schema = schema.Ref
	}
	if schema != nil && schema.Ref != nil && len(schema.OneOf) == 0 && len(schema.Properties) == 0 {
		return schema.Ref
	}
	return schema
}

func stringFor(schema *jsonschema.Schema) string {
	if schema.Pattern != nil {
		switch pattern := schema.Pattern.String(); {
		case strings.HasPrefix(pattern, "^w"):
			return "w1"
		case strings.HasPrefix(pattern, "^a"):
			return "a1"
		case strings.HasPrefix(pattern, "^f"):
			return "f1"
		case strings.Contains(pattern, "r"):
			return "r1"
		}
	}
	return consistencyApp
}

func numberFor(schema *jsonschema.Schema) any {
	value := big.NewRat(1, 1)
	if schema.Minimum != nil && schema.Minimum.Cmp(value) > 0 {
		value = schema.Minimum
	}
	if schema.Maximum != nil && schema.Maximum.Cmp(value) < 0 {
		value = schema.Maximum
	}
	return json.Number(value.RatString())
}

// completeObject makes a generated seed schema-valid by adding optional
// fields one at a time; it returns nil when no single addition helps.
func completeObject(schema *jsonschema.Schema, object map[string]any) map[string]any {
	if schema.Validate(object) == nil {
		return object
	}
	branch := branchFor(schema, object)
	for _, key := range contract.SortedKeys(branch.Properties) {
		if _, present := object[key]; present {
			continue
		}
		for _, value := range generateInstances(branch.Properties[key], 1) {
			candidate := cloneJSON(object).(map[string]any)
			candidate[key] = value
			if schema.Validate(candidate) == nil {
				return candidate
			}
		}
	}
	return nil
}

// enrichInstance adds, at every object location, each optional field with
// the first generated value that keeps the whole instance schema-valid and
// runtime-accepted, so that a declared rule on one field does not hide the
// mutations of every other field.
func enrichInstance(root *jsonschema.Schema, instance map[string]any, accepts func(map[string]any) bool) map[string]any {
	current := cloneJSON(instance).(map[string]any)
	var visit func(schema *jsonschema.Schema, path []string)
	visit = func(schema *jsonschema.Schema, path []string) {
		schema = followRef(schema)
		var value any = current
		for _, segment := range path {
			value = childOf(value, segment)
		}
		switch typed := value.(type) {
		case map[string]any:
			branch := branchFor(schema, typed)
			for _, key := range contract.SortedKeys(branch.Properties) {
				if _, present := typed[key]; present {
					continue
				}
				for _, generated := range generateInstances(branch.Properties[key], 1) {
					candidate, ok := replaceAt(current, contract.AppendPath(path, key), generated)
					if ok && root.Validate(candidate) == nil && accepts(candidate) {
						current = candidate
						break
					}
				}
			}
			var object any = current
			for _, segment := range path {
				object = childOf(object, segment)
			}
			if fresh, ok := object.(map[string]any); ok {
				for _, key := range contract.SortedKeys(fresh) {
					if property := branch.Properties[key]; property != nil {
						visit(property, contract.AppendPath(path, key))
					}
				}
			}
		case []any:
			for index := range typed {
				visit(schema.Items2020, contract.AppendPath(path, fmt.Sprint(index)))
			}
		}
	}
	visit(root, nil)
	return current
}

// branchFor returns the object schema that governs instance: the oneOf
// branch selected by const fields, or the schema itself.
func branchFor(schema *jsonschema.Schema, instance map[string]any) *jsonschema.Schema {
	schema = followRef(schema)
	if len(schema.OneOf) > 0 && contract.ShapeBranches(schema.OneOf) {
		if branch := contract.SelectByDiscriminator(schema.OneOf, contract.ConstProperties(schema.OneOf), instance); branch != nil {
			return followRef(branch)
		}
		for _, branch := range schema.OneOf {
			if branch.Validate(instance) == nil {
				return followRef(branch)
			}
		}
		return followRef(schema.OneOf[0])
	}
	return schema
}

type mutation struct {
	origin string
	args   map[string]any
}

// mutateInstance applies each mutation at each location of a valid seed.
func mutateInstance(root *jsonschema.Schema, seed map[string]any) []mutation {
	var out []mutation
	var visit func(schema *jsonschema.Schema, value any, path []string)
	visit = func(schema *jsonschema.Schema, value any, path []string) {
		schema = followRef(schema)
		label := "/" + strings.Join(path, "/")
		for _, variant := range valueMutations(schema, value) {
			if args, ok := replaceAt(seed, path, variant.value); ok {
				out = append(out, mutation{label + ":" + variant.name, args})
			}
		}
		switch typed := value.(type) {
		case map[string]any:
			branch := branchFor(schema, typed)
			for _, key := range contract.SortedKeys(typed) {
				childPath := contract.AppendPath(path, key)
				if property := branch.Properties[key]; property != nil {
					visit(property, typed[key], childPath)
				}
				if args, ok := deleteAt(seed, childPath); ok {
					out = append(out, mutation{"/" + strings.Join(childPath, "/") + ":missing", args})
				}
			}
			for _, key := range contract.SortedKeys(branch.Properties) {
				if _, present := typed[key]; present {
					continue
				}
				for index, generated := range generateInstances(branch.Properties[key], 1) {
					if args, ok := replaceAt(seed, contract.AppendPath(path, key), generated); ok {
						out = append(out, mutation{fmt.Sprintf("%s/%s:add%d", label, key, index), args})
					}
				}
			}
			extra := cloneJSON(typed).(map[string]any)
			extra["bogus"] = json.Number("1")
			if args, ok := replaceAt(seed, path, extra); ok {
				out = append(out, mutation{label + ":unknown_field", args})
			}
			for from, to := range legacyKeyAliases {
				if inner, present := typed[from]; present {
					renamed := cloneJSON(typed).(map[string]any)
					delete(renamed, from)
					renamed[to] = inner
					if args, ok := replaceAt(seed, path, renamed); ok {
						out = append(out, mutation{label + ":alias_" + to, args})
					}
				}
			}
			for key, extraValue := range legacyExtraFields {
				if _, present := typed[key]; present || len(path) != 0 {
					continue
				}
				added := cloneJSON(typed).(map[string]any)
				added[key] = extraValue
				if args, ok := replaceAt(seed, path, added); ok {
					out = append(out, mutation{label + ":legacy_" + key, args})
				}
			}
		case []any:
			for index, item := range typed {
				visit(schema.Items2020, item, contract.AppendPath(path, fmt.Sprint(index)))
			}
			if len(typed) > 0 {
				longer := append(cloneJSON(typed).([]any), cloneJSON(typed[0]))
				if args, ok := replaceAt(seed, path, longer); ok {
					out = append(out, mutation{label + ":repeat_item", args})
				}
			}
		}
	}
	visit(root, seed, nil)
	return out
}

var legacyKeyAliases = map[string]string{
	"target":    "element",
	"keys":      "key",
	"assertion": "expect",
	"type":      "action",
}

var legacyExtraFields = map[string]any{
	"settle_ms": json.Number("800"),
	"expect":    map[string]any{"appears": "Save"},
	"limit":     json.Number("5"),
	"actions":   []any{},
	"app":       consistencyApp,
	"view":      "full",
}

type valueVariant struct {
	name  string
	value any
}

// valueMutations returns replacement values for one location: wrong types,
// spelling variants of strings and enum values, and numeric boundaries.
func valueMutations(schema *jsonschema.Schema, value any) []valueVariant {
	variants := []valueVariant{}
	if schema == nil {
		return variants
	}
	switch typed := value.(type) {
	case string:
		variants = append(variants,
			valueVariant{"number", json.Number("7")},
			valueVariant{"bool", true},
			valueVariant{"object", map[string]any{"value": typed}},
			valueVariant{"empty", ""},
			valueVariant{"blank", "  "},
			valueVariant{"lead_space", " " + typed},
			valueVariant{"trail_space", typed + " "},
			valueVariant{"upper", strings.ToUpper(typed)},
			valueVariant{"title", titleCase(typed)},
			valueVariant{"numeric_string", "3"},
		)
		for _, alias := range legacyValueAliases[typed] {
			variants = append(variants, valueVariant{"alias_" + alias, alias})
		}
	case json.Number:
		variants = append(variants,
			valueVariant{"string", typed.String()},
			valueVariant{"bool", true},
			valueVariant{"fraction", json.Number("1.5")},
			valueVariant{"float_integral", json.Number(typed.String() + ".0")},
			valueVariant{"zero", json.Number("0")},
			valueVariant{"negative", json.Number("-1")},
		)
		for _, bound := range []*big.Rat{schema.Minimum, schema.Maximum, schema.ExclusiveMinimum, schema.ExclusiveMaximum} {
			if bound == nil {
				continue
			}
			for _, delta := range []int64{-1, 0, 1} {
				number := new(big.Rat).Add(bound, big.NewRat(delta, 1))
				variants = append(variants, valueVariant{
					fmt.Sprintf("bound%+d(%s)", delta, bound.RatString()), json.Number(number.RatString()),
				})
			}
		}
	case bool:
		variants = append(variants,
			valueVariant{"string_true", "true"},
			valueVariant{"string_yes", "yes"},
			valueVariant{"number", json.Number("1")},
			valueVariant{"negated", !typed},
		)
	case map[string]any:
		variants = append(variants,
			valueVariant{"string", "r1"},
			valueVariant{"empty_object", map[string]any{}},
			valueVariant{"array", []any{typed}},
		)
	case []any:
		variants = append(variants,
			valueVariant{"empty_array", []any{}},
			valueVariant{"string", "x"},
		)
		if len(typed) > 0 {
			variants = append(variants, valueVariant{"bare_item", typed[0]})
		}
	}
	if schema.Enum != nil {
		for _, value := range schema.Enum.Values {
			variants = append(variants, valueVariant{"enum_" + fmt.Sprint(value), value})
		}
	}
	return variants
}

var legacyValueAliases = map[string][]string{
	"none":   {"notes"},
	"value":  {"value_equals", "value_contains"},
	"exists": {"EXISTS"},
	"r1":     {"7", "w1", "r??", "s1:payload"},
	"w1":     {"r1", "w0"},
	"click":  {"press", "Click"},
	"close":  {"Close"},
}

func titleCase(text string) string {
	if text == "" {
		return text
	}
	return strings.ToUpper(text[:1]) + text[1:]
}

func cloneJSON(value any) any {
	switch typed := value.(type) {
	case map[string]any:
		clone := make(map[string]any, len(typed))
		for key, item := range typed {
			clone[key] = cloneJSON(item)
		}
		return clone
	case []any:
		clone := make([]any, len(typed))
		for index, item := range typed {
			clone[index] = cloneJSON(item)
		}
		return clone
	default:
		return value
	}
}

func replaceAt(root map[string]any, path []string, value any) (map[string]any, bool) {
	if len(path) == 0 {
		object, ok := value.(map[string]any)
		return object, ok
	}
	clone := cloneJSON(root).(map[string]any)
	var parent any = clone
	for _, segment := range path[:len(path)-1] {
		parent = childOf(parent, segment)
		if parent == nil {
			return nil, false
		}
	}
	return clone, setChild(parent, path[len(path)-1], value)
}

func deleteAt(root map[string]any, path []string) (map[string]any, bool) {
	clone := cloneJSON(root).(map[string]any)
	var parent any = clone
	for _, segment := range path[:len(path)-1] {
		parent = childOf(parent, segment)
	}
	object, ok := parent.(map[string]any)
	if !ok {
		return nil, false
	}
	delete(object, path[len(path)-1])
	return clone, true
}

func childOf(parent any, segment string) any {
	switch typed := parent.(type) {
	case map[string]any:
		return typed[segment]
	case []any:
		var index int
		if _, err := fmt.Sscan(segment, &index); err == nil && index < len(typed) {
			return typed[index]
		}
	}
	return nil
}

func setChild(parent any, segment string, value any) bool {
	switch typed := parent.(type) {
	case map[string]any:
		typed[segment] = value
		return true
	case []any:
		var index int
		if _, err := fmt.Sscan(segment, &index); err == nil && index < len(typed) {
			typed[index] = value
			return true
		}
	}
	return false
}

var updateGUISamples = flag.Bool("update-gui-samples", false,
	"rewrite testdata/contract/gui_action_samples.json from the GUI schema")

// TestGUIActionSamplesMatchSchema keeps the provider-side GUI validators
// honest. It writes every generated GUI action sample with the schema
// verdict to a fixture; runtime_tests/test_gui.py replays the fixture
// through validate_gui_actions and asserts the same verdict, so the Go
// schema and the Python provider cannot drift apart unnoticed.
func TestGUIActionSamplesMatchSchema(t *testing.T) {
	type sample struct {
		Origin   string `json:"origin"`
		Action   any    `json:"action"`
		Accepted bool   `json:"accepted"`
	}
	root, err := contract.CompiledInputSchema(contract.PublicToolGUI)
	if err != nil {
		t.Fatal(err)
	}
	// The input-batch branch; its item schema is the oneOf of GUI actions.
	var itemSchema *jsonschema.Schema
	for _, branch := range followRef(root).OneOf {
		if items := followRef(branch).Properties["actions"]; items != nil &&
			followRef(items).MaxItems != nil && *followRef(items).MaxItems > 1 {
			itemSchema = followRef(items).Items2020
		}
	}
	if itemSchema == nil {
		t.Fatal("GUI input-batch item schema not found")
	}
	accepts := func(action map[string]any) bool {
		return schemaAccepts(t, contract.PublicToolGUI, map[string]any{"actions": []any{action}})
	}
	var samples []sample
	seen := map[string]bool{}
	add := func(origin string, action map[string]any) {
		key := contract.CompactJSON(action)
		if seen[key] {
			return
		}
		seen[key] = true
		samples = append(samples, sample{origin, action, accepts(action)})
	}
	for index, seed := range generateInstances(itemSchema, 0) {
		object, ok := seed.(map[string]any)
		if !ok {
			continue
		}
		object = completeObject(itemSchema, object)
		if object == nil {
			continue
		}
		origin := fmt.Sprintf("action%d", index)
		for _, base := range []map[string]any{
			object,
			enrichInstance(itemSchema, object, func(map[string]any) bool { return true }),
		} {
			add(origin, base)
			for _, mutation := range mutateInstance(itemSchema, base) {
				add(origin+mutation.origin, mutation.args)
			}
		}
	}
	sort.Slice(samples, func(i, j int) bool {
		return contract.CompactJSON(samples[i].Action) < contract.CompactJSON(samples[j].Action)
	})
	encoded, err := json.MarshalIndent(samples, "", " ")
	if err != nil {
		t.Fatal(err)
	}
	encoded = append(encoded, '\n')
	path := "../../testdata/contract/gui_action_samples.json"
	if *updateGUISamples {
		if err := os.WriteFile(path, encoded, 0o644); err != nil {
			t.Fatal(err)
		}
		return
	}
	want, err := os.ReadFile(path)
	if err != nil || string(want) != string(encoded) {
		t.Fatalf("%s is stale; rerun with -update-gui-samples (%v)", path, err)
	}
}

// historicalArgumentShapes returns AX arguments recorded in evaluation runs,
// with refs renumbered to the fixture's r1/w1/a1/f1
// and every free-text value replaced, so only the argument shapes remain.
func historicalArgumentShapes(t testing.TB) []map[string]any {
	t.Helper()
	file, err := os.Open("../../testdata/contract/ax_argument_shapes.jsonl.gz")
	if err != nil {
		t.Fatal(err)
	}
	defer file.Close()
	reader, err := gzip.NewReader(file)
	if err != nil {
		t.Fatal(err)
	}
	var shapes []map[string]any
	scanner := bufio.NewScanner(reader)
	scanner.Buffer(make([]byte, 1<<16), 1<<22)
	for scanner.Scan() {
		decoder := json.NewDecoder(strings.NewReader(scanner.Text()))
		decoder.UseNumber()
		var args map[string]any
		if decoder.Decode(&args) == nil {
			shapes = append(shapes, args)
		}
	}
	if len(shapes) < 1000 {
		t.Fatalf("only %d historical shapes", len(shapes))
	}
	return shapes
}

func TestHistoricalArgumentsMatchSchema(t *testing.T) {
	for index, args := range historicalArgumentShapes(t) {
		sample := consistencySample{contract.PublicToolAX, fmt.Sprintf("history%d", index), args}
		if mismatch := checkConsistency(t, sample); mismatch != nil {
			t.Fatalf("history shape %d: schema=%v runtime=%v %q\n%s", index,
				mismatch.schema, mismatch.runtime.accepted, mismatch.runtime.text,
				contract.CompactJSON(args))
		}
	}
}

// FuzzAXArgumentsMatchSchema mutates recorded argument shapes at the byte
// level. Any input that decodes to a JSON object must get the same verdict
// from the published schema and from the runtime path behind it.
func FuzzAXArgumentsMatchSchema(f *testing.F) {
	for _, args := range historicalArgumentShapes(f) {
		f.Add(contract.CompactJSON(args))
	}
	f.Fuzz(func(t *testing.T, raw string) {
		decoder := json.NewDecoder(strings.NewReader(raw))
		decoder.UseNumber()
		var args map[string]any
		if decoder.Decode(&args) != nil || args == nil {
			return
		}
		if decoder.More() {
			return
		}
		sample := consistencySample{contract.PublicToolAX, "fuzz", args}
		if mismatch := checkConsistency(t, sample); mismatch != nil {
			t.Fatalf("schema=%v runtime=%v %q\n%s", mismatch.schema,
				mismatch.runtime.accepted, mismatch.runtime.text, raw)
		}
	})
}

// The consistency probe calls dispatchValidated. The AX decoder must run on
// that path too, or the probe would not compare the schema with it.
func TestConsistencyProbeReachesTheAXDecoder(t *testing.T) {
	verdict := newConsistencyProbe(t).run(contract.PublicToolAX, map[string]any{
		"mode": "act", "actions": []any{map[string]any{"type": "click"}},
	})
	if verdict.accepted || verdict.code != axRuleRequiredForType {
		t.Fatalf("dispatchValidated did not run the AX decoder: %#v", verdict)
	}
}
