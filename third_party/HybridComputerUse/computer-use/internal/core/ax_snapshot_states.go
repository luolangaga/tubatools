package core

import (
	"sort"
	"strconv"
	"strings"
)

func explicitState(tokens map[string]bool, positive, negative string) TriState {
	if tokens[positive] {
		return TriTrue
	}
	if negative != "" && tokens[negative] {
		return TriFalse
	}
	return TriUnknown
}

func typedAXStates(record *ElementRecord) AXStates {
	tokens := map[string]bool{}
	for _, token := range recordStateTokens(record) {
		tokens[strings.ToLower(strings.TrimSpace(token))] = true
	}
	checked := CheckedUnknown
	switch {
	case tokens["indeterminate"] || tokens["mixed"]:
		checked = CheckedMixed
	case tokens["checked"]:
		checked = CheckedTrue
	case tokens["unchecked"]:
		checked = CheckedFalse
	}
	var level *int
	for token := range tokens {
		if strings.HasPrefix(token, "level=") {
			if parsed, err := strconv.Atoi(strings.TrimPrefix(token, "level=")); err == nil {
				level = &parsed
			}
		}
	}
	return AXStates{
		Active:   explicitState(tokens, "focused", ""),
		Checked:  checked,
		Expanded: explicitState(tokens, "expanded", "collapsed"),
		Selected: explicitState(tokens, "selected", "unselected"),
		Disabled: explicitState(tokens, "disabled", "enabled"),
		Pressed:  explicitState(tokens, "pressed", "not-pressed"),
		Invalid:  explicitState(tokens, "invalid", "valid"),
		Level:    level,
	}
}

func typedAXCapabilities(record *ElementRecord, role CanonicalRole) AXCapabilities {
	providerActions := typedProviderActions(record)
	capabilities := AXCapabilities{
		Activate:        ActivationNone,
		ProviderActions: providerActions,
	}
	actionSet := map[string]bool{}
	for _, action := range providerActions {
		actionSet[normalizedActionName(action)] = true
	}
	if hasMeaningfulWorkingClick(record) || actionSet["click"] ||
		actionSet["activate"] || actionSet["press"] || actionSet["toggle"] {
		capabilities.Activate = ActivationSemantic
	}
	if record != nil && record.SetValueSupported != nil {
		capabilities.SetValue = *record.SetValueSupported
	} else {
		roleImpliesSetValue := role == "slider" || role == "spinbutton"
		if record != nil && strings.HasPrefix(record.ProviderBusName, "uia3:") {
			roleImpliesSetValue = false
		}
		capabilities.SetValue = actionSet["setvalue"] || roleImpliesSetValue
	}
	capabilities.EditText = role == "textbox"
	capabilities.Scroll = actionSet["scroll"] || role == "scrollbar"
	return capabilities
}

func typedProviderActions(record *ElementRecord) []string {
	if record == nil {
		return nil
	}
	out := make([]string, 0, len(record.Actions))
	seen := map[string]bool{}
	for _, action := range record.Actions {
		normalized := normalizedActionName(action)
		if normalized == "" || normalized == "clickancestor" || seen[normalized] {
			continue
		}
		seen[normalized] = true
		out = append(out, strings.ToLower(strings.TrimSpace(action)))
	}
	sort.Strings(out)
	return out
}

func mergeTriState(first, second TriState) TriState {
	if first == second {
		return first
	}
	return TriUnknown
}

func mergeCheckedState(first, second CheckedState) CheckedState {
	if first == second {
		return first
	}
	return CheckedUnknown
}

func mergeBoolEvidence(first, second *bool) *bool {
	if first == nil {
		return second
	}
	if second == nil {
		return first
	}
	if *first != *second {
		return nil
	}
	return first
}
