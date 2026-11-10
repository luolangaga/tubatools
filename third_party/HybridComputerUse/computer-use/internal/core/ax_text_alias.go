package core

// Normalize only the two text fields whose meaning is unambiguous for the
// action. Clone the map so validation never changes the recorded caller input.
func axNormalizeTextAlias(object map[string]any, actionType, path string) (map[string]any, *axArgError) {
	alias, field := "", ""
	switch actionType {
	case "type":
		alias, field = "value", "text"
	case "set_value":
		alias, field = "text", "value"
	default:
		return object, nil
	}
	value, present := object[alias]
	if !present {
		return object, nil
	}
	if _, both := object[field]; both {
		return nil, axInvalid(path+"."+alias, actionType+" accepts "+field+" or "+alias+", not both", "").
			withRule(axRuleFieldForType).withFix(axRemove(path + "." + alias))
	}
	if alias == "text" && value == "" {
		return nil, axInvalid(path+".text", `text must not be empty; to clear the element send "value":""`, "").
			withRule(axRuleFieldForType).withFix(axSet(path+".value", ""), axRemove(path+".text"))
	}
	renamed := make(map[string]any, len(object))
	for key, item := range object {
		if key != alias {
			renamed[key] = item
		}
	}
	renamed[field] = value
	return renamed, nil
}
