package core

import (
	"encoding/json"
	"fmt"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func optionalSettleMS(args map[string]any) (int, error) {
	value, present := args["settle_ms"]
	if !present || value == nil {
		return contract.DefaultSettleMS, nil
	}
	invalid := fmt.Errorf("settle_ms must be a whole number of milliseconds between 0 and %d", contract.MaxSettleMS)
	var milliseconds int
	switch typed := value.(type) {
	case int:
		milliseconds = typed
	case float64:
		if !isWholeNumber(typed) || typed < 0 || typed > float64(contract.MaxSettleMS) {
			return 0, invalid
		}
		milliseconds = int(typed)
	case json.Number:
		integer, ok := jsonInteger(typed)
		if !ok || integer < 0 || integer > int64(contract.MaxSettleMS) {
			return 0, invalid
		}
		milliseconds = int(integer)
	default:
		return 0, invalid
	}
	if milliseconds < 0 || milliseconds > contract.MaxSettleMS {
		return 0, invalid
	}
	return milliseconds, nil
}
