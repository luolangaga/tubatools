//go:build windows || windows_sidecar_test

package provider

import (
	"encoding/json"
	"errors"
	"os"
	"sync/atomic"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

var windowsProviderTraceSequence atomic.Uint64

// Traces contain only transport state. Request IDs, payloads, environment,
// executable paths and error messages must not enter this operator stream.
type windowsProviderTrace struct {
	sequence   uint64
	generation uint64
	operation  core.ProviderWireOperation
	started    time.Time
	deadline   time.Time
	budget     time.Duration
	callBudget time.Duration
}

type windowsProviderTraceEvent struct {
	Event                 string                     `json:"event"`
	Stage                 string                     `json:"stage"`
	Sequence              uint64                     `json:"sequence"`
	Generation            uint64                     `json:"worker_generation"`
	Operation             core.ProviderWireOperation `json:"operation"`
	UTC                   string                     `json:"utc"`
	DeadlineUTC           string                     `json:"deadline_utc"`
	ElapsedMS             float64                    `json:"elapsed_ms"`
	RemainingMS           float64                    `json:"remaining_ms"`
	BudgetMS              float64                    `json:"budget_ms"`
	CallBudgetMS          float64                    `json:"call_budget_ms,omitempty"`
	ErrorBudgetMS         float64                    `json:"error_budget_ms,omitempty"`
	Outcome               string                     `json:"outcome,omitempty"`
	Phase                 string                     `json:"phase,omitempty"`
	ResetRequired         bool                       `json:"provider_reset_required,omitempty"`
	AccessibilityOK       *bool                      `json:"accessibility_ok,omitempty"`
	NativeInternalFailure bool                       `json:"native_internal_failure,omitempty"`
}

func newWindowsProviderTrace(
	operation core.ProviderWireOperation, deadline time.Time, budget time.Duration,
) *windowsProviderTrace {
	if os.Getenv("COMPUTER_USE_WINDOWS_PROVIDER_TRACE") != "1" {
		return nil
	}
	return &windowsProviderTrace{
		sequence: windowsProviderTraceSequence.Add(1), operation: operation,
		started: deadline.Add(-budget), deadline: deadline, budget: budget,
	}
}

func (trace *windowsProviderTrace) forWorker(generation uint64) *windowsProviderTrace {
	if trace == nil {
		return nil
	}
	copy := *trace
	copy.generation = generation
	return &copy
}

func (trace *windowsProviderTrace) forCall(budget time.Duration) *windowsProviderTrace {
	if trace == nil {
		return nil
	}
	copy := *trace
	copy.callBudget = budget
	return &copy
}

func (trace *windowsProviderTrace) record(
	stage string, response *core.ProviderWireResponse, err error,
) {
	if trace == nil {
		return
	}
	now := time.Now()
	ms := func(duration time.Duration) float64 { return float64(duration) / float64(time.Millisecond) }
	event := windowsProviderTraceEvent{
		Event: "windows_provider_transport", Stage: stage,
		Sequence: trace.sequence, Generation: trace.generation, Operation: trace.operation,
		UTC: now.UTC().Format(time.RFC3339Nano), DeadlineUTC: trace.deadline.UTC().Format(time.RFC3339Nano),
		ElapsedMS: ms(now.Sub(trace.started)), RemainingMS: ms(trace.deadline.Sub(now)),
		BudgetMS: ms(trace.budget), CallBudgetMS: ms(trace.callBudget),
	}
	if response != nil {
		event.Outcome = "response"
		event.ResetRequired = response.ProviderResetRequired
		if response.Accessibility != nil {
			ok := response.Accessibility.OK
			event.AccessibilityOK = &ok
			event.NativeInternalFailure = response.Accessibility.Failure != nil &&
				response.Accessibility.Failure.Code == "provider_internal_error"
		}
	}
	if err != nil {
		event.Outcome = "error"
		var timeout *core.RuntimeTimeoutError
		var transport *core.RuntimeTransportError
		var rejection *core.RuntimeRequestRejectionError
		switch {
		case errors.As(err, &timeout):
			event.Outcome, event.Phase = "runtime_timeout", timeout.Phase
			event.ErrorBudgetMS = ms(timeout.Duration)
		case errors.As(err, &transport):
			event.Outcome, event.Phase = "transport_error", transport.Phase
		case errors.As(err, &rejection):
			event.Outcome = "request_rejection"
		}
	}
	payload, marshalErr := json.Marshal(event)
	if marshalErr == nil {
		windowsProviderDiagnostics.write(append(payload, '\n'))
	}
}
