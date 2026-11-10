package core

import (
	"bytes"
	"encoding/json"
	"fmt"
)

const focusPreparationUnknown = "request_attempted_outcome_unknown"
const pointerPreparationUnknown = "activation_attempted_outcome_unknown"

func validateProviderPreparationEnvelope(response ProviderWireResponse) error {
	type facts struct {
		mutation *ProviderMutationFacts
		failure  *ProviderFailure
	}
	var domains []facts
	if r := response.Accessibility; r != nil {
		domains = append(domains, facts{r.Mutation, r.Failure})
	}
	if r := response.Desktop; r != nil {
		domains = append(domains, facts{r.Mutation, r.Failure})
	}
	if r := response.Window; r != nil {
		domains = append(domains, facts{r.Mutation, r.Failure})
	}
	if r := response.OrderedInput; r != nil {
		domains = append(domains, facts{&r.Mutation, r.Failure})
	}
	for _, domain := range domains {
		m, f := domain.mutation, domain.failure
		pointerFailure := f != nil && f.Code == "pointer_preparation_failed"
		if (m == nil || (m.FocusPreparation == "" && m.PointerPreparation == "")) && !pointerFailure {
			continue
		}
		r := response.Accessibility
		if response.Operation != ProviderWireResolveAndAct || r == nil || r.Mutation != m {
			return fmt.Errorf("provider preparation requires a resolve_and_act accessibility response")
		}
		if m == nil {
			return fmt.Errorf("provider pointer_preparation tag is missing")
		}
		code := "focus_verification_failed"
		if m.PointerPreparation != "" || pointerFailure {
			code = "pointer_preparation_failed"
			if m.PointerPreparation != pointerPreparationUnknown || m.FocusPreparation != "" || m.DesktopStateMutated {
				return fmt.Errorf("provider pointer_preparation facts are invalid")
			}
		} else if m.FocusPreparation != focusPreparationUnknown {
			return fmt.Errorf("provider focus_preparation tag is invalid")
		}
		if r.OK || f == nil || f.Code != code || f.Phase != "emission" || f.FactsComplete ||
			!m.MutationUnknown || m.InputEventsEmitted || m.PartialInputPossible {
			return fmt.Errorf("provider preparation requires an incomplete %s emission failure with unknown preparation and no requested input", code)
		}
		if r.Snapshot != nil || r.RecoveryContext != nil ||
			(r.ActionEvidence != nil && (r.ActionEvidence.InputEmitted || r.ActionEvidence.ReadbackSampled)) {
			return fmt.Errorf("provider preparation cannot carry recovery, readback, or emitted input evidence")
		}
	}
	return nil
}

func validateProviderPreparationWire(data []byte) error {
	// Bool DTO fields collapse missing and null to false. Check raw presence so
	// uncertain preparation never becomes an explicit unchanged-desktop claim.
	var envelope map[string]json.RawMessage
	if err := json.Unmarshal(data, &envelope); err != nil {
		return err
	}
	for _, domain := range []string{"accessibility", "desktop", "window", "ordered_input"} {
		if len(envelope[domain]) == 0 {
			continue
		}
		var payload struct {
			OK       json.RawMessage            `json:"ok"`
			Mutation map[string]json.RawMessage `json:"mutation"`
			Failure  map[string]json.RawMessage `json:"failure"`
		}
		if err := json.Unmarshal(envelope[domain], &payload); err != nil {
			return err
		}
		for key, expected := range map[string]string{"focus_preparation": focusPreparationUnknown, "pointer_preparation": pointerPreparationUnknown} {
			raw, present := payload.Mutation[key]
			if !present {
				continue
			}
			var tag string
			if json.Unmarshal(raw, &tag) != nil || tag != expected {
				return fmt.Errorf("provider %s tag is invalid", key)
			}
			if !bytes.Equal(bytes.TrimSpace(payload.OK), []byte("false")) {
				return fmt.Errorf("provider preparation requires explicit ok=false")
			}
			for name, fact := range map[string]json.RawMessage{
				"facts_complete":         payload.Failure["facts_complete"],
				"input_events_emitted":   payload.Mutation["input_events_emitted"],
				"partial_input_possible": payload.Mutation["partial_input_possible"],
			} {
				if len(fact) > 0 && !bytes.Equal(bytes.TrimSpace(fact), []byte("false")) {
					return fmt.Errorf("provider preparation requires %s=false when present", name)
				}
			}
			if desktop, present := payload.Mutation["desktop_state_mutated"]; present &&
				(key == "pointer_preparation" || !bytes.Equal(bytes.TrimSpace(desktop), []byte("true"))) {
				return fmt.Errorf("provider %s has invalid desktop mutation evidence", key)
			}
		}
	}
	return nil
}
