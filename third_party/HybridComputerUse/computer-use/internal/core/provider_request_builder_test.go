package core

import (
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestProviderActionObservationCanSkipSnapshot(t *testing.T) {
	request := AXServiceRequest{
		Action:  axKeypressCommand{Key: "ENTER"},
		Observe: contract.ObserveNone,
	}
	options := providerObservationOptionsFromOperation(request)
	if options.CaptureSnapshot == nil || *options.CaptureSnapshot {
		t.Fatalf("observe=none action must disable provider snapshot capture: %#v",
			options)
	}

	request.Observe = contract.ObserveDiff
	options = providerObservationOptionsFromOperation(request)
	if options.CaptureSnapshot == nil || !*options.CaptureSnapshot {
		t.Fatalf("observe=diff action must request provider snapshot capture: %#v",
			options)
	}

	capture := true
	request.Observe = contract.ObserveNone
	request.CaptureSnapshot = &capture
	options = providerObservationOptionsFromOperation(request)
	if options.CaptureSnapshot == nil || !*options.CaptureSnapshot {
		t.Fatalf("internal continuity capture override was ignored: %#v", options)
	}

	windowAction := WindowOperation("activate")
	options = providerObservationOptionsFromOperation(AXServiceRequest{
		WindowAction: &windowAction,
		Observe:      contract.ObserveNone,
	})
	if options.CaptureSnapshot == nil || !*options.CaptureSnapshot {
		t.Fatalf("window actions must retain internal lifecycle observation: %#v",
			options)
	}

	options = providerObservationOptionsFromOperation(AXServiceRequest{
		Operation: axServiceObserve,
	})
	if options.CaptureSnapshot != nil {
		t.Fatalf("read operations should use provider default capture behavior: %#v",
			options)
	}
}
