package core

import (
	"errors"
	"fmt"
	"strings"
)

// A rejection is an alternative to a domain result, before runtime dispatch.
// The explicit false pointer distinguishes proof from an omitted wire fact.
type ProviderRequestRejection struct {
	Code              string `json:"code"`
	Phase             string `json:"phase"`
	DispatchAttempted *bool  `json:"dispatch_attempted"`
	Message           string `json:"message"`
}

func (rejection ProviderRequestRejection) Validate() error {
	if rejection.Code != "invalid_arguments" ||
		rejection.Phase != "request_validation" ||
		rejection.DispatchAttempted == nil || *rejection.DispatchAttempted ||
		strings.TrimSpace(rejection.Message) == "" {
		return errors.New("provider request rejection requires complete pre-dispatch validation facts")
	}
	return nil
}

type RuntimeRequestRejectionError struct {
	Operation ProviderWireOperation    `json:"-"`
	Rejection ProviderRequestRejection `json:"-"`
}

func (err *RuntimeRequestRejectionError) Error() string {
	message := strings.Join(strings.Fields(err.Rejection.Message), " ")
	if len(message) > 512 {
		message = message[:512]
	}
	return fmt.Sprintf("provider rejected %s before runtime dispatch: %s", err.Operation, message)
}

func runtimeRequestRejectionResult(err error) (ToolCallResult, bool) {
	var rejection *RuntimeRequestRejectionError
	if !errors.As(err, &rejection) || rejection.Rejection.Validate() != nil {
		return ToolCallResult{}, false
	}
	result := TextResult(err.Error(), true).withEvidenceScope("error_only")
	result.StructuredContent["failure_code"] = "provider_request_rejected"
	result.StructuredContent["failure_phase"] = "request_validation"
	result.StructuredContent["failure_facts_complete"] = true
	result.StructuredContent["provider_request_rejected"] = true
	result.StructuredContent["input_events_emitted"] = false
	result.StructuredContent["partial_input_possible"] = false
	result.StructuredContent["mutation_unknown"] = false
	result.StructuredContent["preflight_only"] = false
	result.StructuredContent["retry_same_request"] = false
	return result, true
}
