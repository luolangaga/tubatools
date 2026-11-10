package contract

import (
	"fmt"
	"github.com/santhosh-tekuri/jsonschema/v6"
	"sort"
	"strconv"
	"strings"
)

// explanation is one schema problem at an instance location.
type explanation struct {
	path    []string
	keyword string
	message string
	hint    string
	// accepted lists the fields an object declares; it is shown when no
	// corrected call could be built.
	accepted string
	// wrongBranch marks a const mismatch: the instance belongs to another
	// oneOf branch, so this branch's explanation is a poor choice.
	wrongBranch bool
}

func (e *explanation) text() string {
	switch {
	case e.hint != "":
		return e.message + "; use " + e.hint
	case e.accepted != "":
		return e.message + "; accepted fields: " + e.accepted
	default:
		return e.message
	}
}

type schemaExplainer struct {
	tool  string
	args  map[string]any
	rules toolArgumentRules
}

func (x *schemaExplainer) explainRoot(schema *jsonschema.Schema, args map[string]any) *explanation {
	var found *explanation
	if x.rules.before != nil {
		found = x.rules.before(args)
	}
	if found == nil {
		found = x.explain(schema, args, nil)
	}
	if found == nil {
		found = &explanation{message: x.tool + " arguments do not match the published input schema"}
	}
	if x.rules.refine != nil {
		x.rules.refine(args, found)
	}
	return found
}

// explain returns the first problem of value against schema, or nil when the
// value is valid. Object problems are reported in the order a caller fixes
// them: shape branch, unknown fields, missing fields, field values, then
// cross-field constraints.
func (x *schemaExplainer) explain(schema *jsonschema.Schema, value any, path []string) *explanation {
	if schema == nil || schema.Validate(value) == nil {
		return nil
	}
	if schema.Ref != nil {
		if found := x.explain(schema.Ref, value, path); found != nil {
			return found
		}
	}
	if schema.Types != nil && !typeAllowed(schema.Types.ToStrings(), value) {
		return x.typeMismatch(schema, value, path)
	}
	if schema.Const != nil && !jsonEqual(*schema.Const, value) {
		return valueMismatch(path, []any{*schema.Const}, value, true)
	}
	if schema.Enum != nil && !containsJSON(schema.Enum.Values, value) {
		return valueMismatch(path, schema.Enum.Values, value, false)
	}
	var found *explanation
	switch typed := value.(type) {
	case map[string]any:
		found = x.explainObject(schema, typed, path)
	case []any:
		found = x.explainArray(schema, typed, path)
	case string:
		found = explainString(schema, typed, path)
	default:
		found = explainNumber(schema, value, path)
	}
	if found != nil {
		return found
	}
	if found := x.explainCombinators(schema, value, path); found != nil {
		return found
	}
	return &explanation{
		path:    path,
		keyword: "schema",
		message: pathLabel(path, x.tool) + " does not match the published schema",
	}
}

func (x *schemaExplainer) explainObject(schema *jsonschema.Schema, object map[string]any, path []string) *explanation {
	if len(schema.OneOf) > 0 && ShapeBranches(schema.OneOf) {
		if found := x.explainShapeOneOf(schema, object, path); found != nil {
			return found
		}
	}
	label := x.objectLabel(schema, object, path)
	missing := []string{}
	for _, key := range schema.Required {
		if _, present := object[key]; !present {
			missing = append(missing, key)
		}
	}
	if closed, ok := schema.AdditionalProperties.(bool); ok && !closed {
		unknown := []string{}
		for key := range object {
			if _, declared := schema.Properties[key]; !declared {
				unknown = append(unknown, key)
			}
		}
		if len(unknown) > 0 {
			sort.Strings(unknown)
			quoted := make([]string, len(unknown))
			for index, key := range unknown {
				quoted[index] = strconv.Quote(key)
			}
			found := &explanation{
				path:     path,
				keyword:  "additionalProperties",
				message:  label + " does not accept fields: " + strings.Join(quoted, ", "),
				accepted: strings.Join(SortedKeys(schema.Properties), ", "),
			}
			if len(missing) > 0 {
				found.message += "; it requires " + joinWords(missing, "and")
				found.accepted = ""
			}
			return found
		}
	}
	// A missing identity counts as a missing field; both are reported
	// together because the caller fixes them in one retry.
	need, conflict := x.identityProblem(schema, object, path)
	if need != "" {
		missing = append(missing, need)
	}
	if len(missing) > 0 {
		message := label + " requires " + joinWords(missing, "and")
		if conflict != "" && need == "" {
			message += "; " + conflict
		}
		return &explanation{path: path, keyword: "required", message: message}
	}
	// Scalar fields are explained before nested ones: they are cheaper to
	// fix, and a nested problem is often a consequence of the same mistake.
	keys := SortedKeys(object)
	sort.SliceStable(keys, func(i, j int) bool {
		return !isContainer(object[keys[i]]) && isContainer(object[keys[j]])
	})
	for _, key := range keys {
		if property := schema.Properties[key]; property != nil {
			if found := x.explain(property, object[key], AppendPath(path, key)); found != nil {
				return found
			}
		}
	}
	if schema.MinProperties != nil && len(object) < *schema.MinProperties {
		return &explanation{
			path:    path,
			keyword: "minProperties",
			message: fmt.Sprintf("%s must contain at least %d field(s)", label, *schema.MinProperties),
		}
	}
	return nil
}

// identityProblem asks the tool rules to explain a failed top-level
// constraint oneOf. It returns empty strings when no rule applies.
func (x *schemaExplainer) identityProblem(schema *jsonschema.Schema, object map[string]any, path []string) (string, string) {
	if len(path) != 0 || x.rules.identity == nil || len(schema.OneOf) == 0 ||
		ShapeBranches(schema.OneOf) || oneOfHolds(schema.OneOf, object) {
		return "", ""
	}
	return x.rules.identity(x.args, countValid(schema.OneOf, object))
}

func (x *schemaExplainer) explainArray(schema *jsonschema.Schema, items []any, path []string) *explanation {
	label := pathLabel(path, x.tool)
	minimum, maximum := -1, -1
	if schema.MinItems != nil {
		minimum = *schema.MinItems
	}
	if schema.MaxItems != nil {
		maximum = *schema.MaxItems
	}
	if (minimum >= 0 && len(items) < minimum) || (maximum >= 0 && len(items) > maximum) {
		keyword := "maxItems"
		if minimum >= 0 && len(items) < minimum {
			keyword = "minItems"
		}
		return &explanation{
			path:    path,
			keyword: keyword,
			message: label + " must contain " + countRange(minimum, maximum, "item"),
		}
	}
	if itemSchema := schema.Items2020; itemSchema != nil {
		for index, item := range items {
			if found := x.explain(itemSchema, item, AppendPath(path, strconv.Itoa(index))); found != nil {
				return found
			}
		}
	}
	return nil
}

func explainString(schema *jsonschema.Schema, value string, path []string) *explanation {
	label := pathLabel(path, "")
	length := len([]rune(value))
	if schema.MinLength != nil && length < *schema.MinLength {
		message := label + " must not be empty"
		if *schema.MinLength > 1 {
			message = fmt.Sprintf("%s must have at least %d characters", label, *schema.MinLength)
		}
		return &explanation{path: path, keyword: "minLength", message: message}
	}
	if schema.MaxLength != nil && length > *schema.MaxLength {
		return &explanation{
			path:    path,
			keyword: "maxLength",
			message: fmt.Sprintf("%s accepts at most %d characters", label, *schema.MaxLength),
		}
	}
	if schema.Pattern != nil && !schema.Pattern.MatchString(value) {
		return &explanation{
			path:    path,
			keyword: "pattern",
			message: fmt.Sprintf("%s %s must be %s", label, strconv.Quote(value),
				patternDescription(schema.Pattern.String())),
		}
	}
	return nil
}

func explainNumber(schema *jsonschema.Schema, value any, path []string) *explanation {
	number, ok := jsonNumberRat(value)
	if !ok {
		return nil
	}
	low, high := "", ""
	failed := false
	if schema.Minimum != nil {
		low = "at least " + schema.Minimum.RatString()
		failed = failed || number.Cmp(schema.Minimum) < 0
	}
	if schema.ExclusiveMinimum != nil {
		low = "greater than " + schema.ExclusiveMinimum.RatString()
		failed = failed || number.Cmp(schema.ExclusiveMinimum) <= 0
	}
	if schema.Maximum != nil {
		high = "at most " + schema.Maximum.RatString()
		failed = failed || number.Cmp(schema.Maximum) > 0
	}
	if schema.ExclusiveMaximum != nil {
		high = "less than " + schema.ExclusiveMaximum.RatString()
		failed = failed || number.Cmp(schema.ExclusiveMaximum) >= 0
	}
	if !failed {
		return nil
	}
	bounds := low
	switch {
	case low == "":
		bounds = high
	case high != "" && strings.HasPrefix(low, "at least ") && strings.HasPrefix(high, "at most "):
		bounds = "between " + strings.TrimPrefix(low, "at least ") + " and " +
			strings.TrimPrefix(high, "at most ")
	case high != "":
		bounds = low + " and " + high
	}
	return &explanation{
		path:    path,
		keyword: "range",
		message: pathLabel(path, "") + " must be " + bounds,
	}
}

func (x *schemaExplainer) explainCombinators(schema *jsonschema.Schema, value any, path []string) *explanation {
	object, isObject := value.(map[string]any)
	label := pathLabel(path, x.tool)
	if isObject {
		label = x.objectLabel(schema, object, path)
	}
	if len(schema.OneOf) > 0 && !ShapeBranches(schema.OneOf) && !oneOfHolds(schema.OneOf, value) {
		valid := countValid(schema.OneOf, value)
		if isObject {
			if _, message := x.identityProblem(schema, object, path); message != "" {
				return &explanation{path: path, keyword: "oneOf", message: message}
			}
		}
		keys := requiredAlternatives(schema.OneOf)
		message := label + " requires one of " + joinWords(keys, "or")
		if valid > 1 {
			message = label + " accepts only one of " + joinWords(keys, "or")
		}
		return &explanation{path: path, keyword: "oneOf", message: message}
	}
	if len(schema.AnyOf) > 0 && countValid(schema.AnyOf, value) == 0 {
		keys := []string{}
		single := true
		for _, branch := range schema.AnyOf {
			required := resolved(branch).Required
			if len(required) != 1 {
				single = false
				break
			}
			keys = append(keys, required[0])
		}
		if single && isObject {
			for index, key := range keys {
				if _, present := object[key]; present {
					return x.explain(schema.AnyOf[index], value, path)
				}
			}
			return &explanation{
				path:    path,
				keyword: "anyOf",
				message: label + " requires at least one of " + joinWords(keys, "or"),
			}
		}
		return x.explain(schema.AnyOf[0], value, path)
	}
	for _, branch := range schema.AllOf {
		if branch.Validate(value) == nil {
			continue
		}
		if not := resolved(branch).Not; not != nil && len(resolved(not).Required) > 1 {
			return &explanation{
				path:    path,
				keyword: "not",
				message: label + " accepts only one of " + joinWords(resolved(not).Required, "or"),
			}
		}
		return x.explain(branch, value, path)
	}
	if schema.Not != nil && schema.Not.Validate(value) == nil {
		not := resolved(schema.Not)
		if not.Const != nil {
			return &explanation{
				path:    path,
				keyword: "not",
				message: label + " must not be " + CompactJSON(*not.Const),
			}
		}
		return &explanation{
			path:    path,
			keyword: "not",
			message: label + " uses a combination of fields the schema forbids",
		}
	}
	if schema.If != nil && schema.If.Validate(value) == nil && schema.Then != nil {
		return x.explain(schema.Then, value, path)
	}
	return nil
}

// explainShapeOneOf picks the branch the caller meant and explains it. A
// branch is chosen by its const properties (mode, type); without them, by
// which branch's problem is not a const mismatch and declares the most of
// the caller's fields.
func (x *schemaExplainer) explainShapeOneOf(schema *jsonschema.Schema, object map[string]any, path []string) *explanation {
	branches := schema.OneOf
	valid := countValid(branches, object)
	if valid == 1 {
		return nil
	}
	if valid > 1 {
		keys := requiredAlternatives(branches)
		return &explanation{
			path:    path,
			keyword: "oneOf",
			message: pathLabel(path, x.tool) + " accepts exactly one of " + joinWords(keys, "or"),
		}
	}
	var found *explanation
	if discriminators := ConstProperties(branches); len(discriminators) > 0 {
		if missing := missingDiscriminator(branches, discriminators, object, path); missing != nil {
			found = missing
		} else if branch := SelectByDiscriminator(branches, discriminators, object); branch != nil {
			found = x.explain(branch, object, path)
		} else {
			found = discriminatorMismatch(branches, discriminators, object, path)
		}
	} else {
		found = x.closestBranch(branches, object, path)
	}
	if found != nil && found.hint == "" {
		if repaired, ok := repairObject(schema, object, x.rules.hintSynonyms); ok {
			if hint := OrderedJSON(repaired, constPropertyNames(branches)); len(hint) <= MaxArgumentHintBytes {
				found.hint = hint
			}
		}
	}
	return found
}

func (x *schemaExplainer) closestBranch(branches []*jsonschema.Schema, object map[string]any, path []string) *explanation {
	var best *explanation
	bestScore := -1 << 30
	for _, branch := range branches {
		found := x.explain(branch, object, path)
		if found == nil {
			continue
		}
		properties := resolved(branch).Properties
		score := 0
		for key := range object {
			if _, declared := properties[key]; declared {
				score += 4
			}
		}
		if found.wrongBranch {
			score -= 1000
		}
		score += len(properties)
		if score > bestScore {
			best, bestScore = found, score
		}
	}
	return best
}

func (x *schemaExplainer) typeMismatch(schema *jsonschema.Schema, value any, path []string) *explanation {
	expected := schema.Types.ToStrings()
	names := typeNames(expected)
	if example := objectExample(schema); example != "" {
		names = "an object like " + example
	}
	return &explanation{
		path:    path,
		keyword: "type",
		message: fmt.Sprintf("%s must be %s, not %s",
			pathLabel(path, x.tool), names, jsonTypeArticle(value)),
	}
}

// objectLabel names an object in messages: its path followed by the const
// values that selected its branch, or mode="act" style at the top level.
func (x *schemaExplainer) objectLabel(schema *jsonschema.Schema, object map[string]any, path []string) string {
	consts := []string{}
	for _, key := range SortedKeys(schema.Properties) {
		property := resolved(schema.Properties[key])
		if property.Const == nil {
			continue
		}
		if value, present := object[key]; present && jsonEqual(*property.Const, value) {
			if len(path) == 0 {
				consts = append(consts, key+"="+CompactJSON(value))
			} else {
				consts = append(consts, fmt.Sprint(value))
			}
		}
	}
	if len(consts) == 0 {
		return pathLabel(path, x.tool)
	}
	if len(path) == 0 {
		return strings.Join(consts, " with ")
	}
	return pathLabel(path, x.tool) + " " + strings.Join(consts, " ")
}

func missingDiscriminator(branches []*jsonschema.Schema, discriminators []string, object map[string]any, path []string) *explanation {
	for _, key := range discriminators {
		values := constValues(branches, key)
		if constBranchCount(branches, key) < len(branches) {
			continue
		}
		if _, present := object[key]; present {
			continue
		}
		return &explanation{
			path:    AppendPath(path, key),
			keyword: "required",
			message: pathLabel(AppendPath(path, key), "") + " is required and must be " +
				joinWords(stringValues(values), "or"),
		}
	}
	return nil
}

func discriminatorMismatch(branches []*jsonschema.Schema, discriminators []string, object map[string]any, path []string) *explanation {
	for _, key := range discriminators {
		values := constValues(branches, key)
		value, present := object[key]
		if !present || containsJSON(values, value) {
			continue
		}
		found := valueMismatch(AppendPath(path, key), values, value, false)
		found.wrongBranch = true
		return found
	}
	return &explanation{
		path:        path,
		keyword:     "oneOf",
		message:     pathLabel(path, "") + " combines fields from different forms",
		wrongBranch: true,
	}
}

func valueMismatch(path []string, allowed []any, value any, single bool) *explanation {
	label := pathLabel(path, "")
	found := &explanation{path: path, keyword: "enum", wrongBranch: single}
	text, isString := value.(string)
	switch {
	case single:
		found.keyword = "const"
		found.message = label + " must be " + CompactJSON(allowed[0])
	case isString:
		found.message = label + " " + strconv.Quote(text) + " is unsupported; it must be " +
			joinWords(stringValues(allowed), "or")
	default:
		found.message = label + " must be one of " + joinWords(stringValues(allowed), "or")
	}
	if isString {
		for _, candidate := range allowed {
			exact, ok := candidate.(string)
			if ok && exact != text && strings.EqualFold(strings.TrimSpace(text), exact) {
				found.message += " (values are exact and case-sensitive: " + strconv.Quote(exact) + ")"
				break
			}
		}
	}
	return found
}
