//go:build linux

package core

import (
	"reflect"
	"strings"
	"testing"
)

func TestRuntimeCallsRouteByPlatformDomain(t *testing.T) {
	backend := &recordingPlatformBackend{
		accessibilityResponse: &ProviderAccessibilityResult{OK: true},
		desktopResponse:       &ProviderDesktopResult{OK: true},
		windowResponse:        &ProviderWindowResult{OK: true},
	}
	service := NewServiceWithBackend(backend)
	tests := []struct {
		name string
		call func() error
		want string
	}{
		{name: "observe_apps", want: "observe_apps", call: func() error {
			_, err := service.callProviderObserveApps(ProviderObserveAppsRequest{
				ProtocolVersion: ProviderProtocolVersion,
			})
			return err
		}},
		{name: "observe", want: "observe", call: func() error {
			_, err := service.callProviderObserve(ProviderObserveRequest{
				ProtocolVersion: ProviderProtocolVersion, App: "Editor",
			})
			return err
		}},
		{name: "resolve_and_act", want: "resolve_and_act", call: func() error {
			_, err := service.callProviderAction(ProviderActionRequest{
				ProtocolVersion: ProviderProtocolVersion,
				Intent:          AccessibilityClick,
				App:             "Editor",
				Target: &ProviderSemanticTarget{
					Selector: `button "Save"`,
					HasName:  true,
				},
				Click: &ProviderClickInput{},
			})
			return err
		}},
		{name: "desktop", want: "desktop_execute_batch", call: func() error {
			_, err := service.callProviderDesktop(ProviderDesktopRequest{
				ProtocolVersion: ProviderProtocolVersion,
			})
			return err
		}},
		{name: "window_activate", want: "window_activate", call: func() error {
			_, err := service.callProviderWindowAction(ProviderWindowActionRequest{
				ProtocolVersion: ProviderProtocolVersion,
				Action:          WindowActivate,
				App:             "Editor",
			})
			return err
		}},
		{name: "window_close", want: "window_close", call: func() error {
			_, err := service.callProviderWindowAction(ProviderWindowActionRequest{
				ProtocolVersion: ProviderProtocolVersion,
				Action:          WindowClose,
				App:             "Editor",
			})
			return err
		}},
	}
	for _, test := range tests {
		backend.calls = nil
		if err := test.call(); err != nil {
			t.Fatalf("%s call failed: %v", test.name, err)
		}
		if !reflect.DeepEqual(backend.calls, []string{test.want}) {
			t.Fatalf("%s routed to %v, want %s", test.name, backend.calls, test.want)
		}
	}
}

func TestAccessibilityOperationRoutesTypedWindowActions(t *testing.T) {
	backend := &recordingPlatformBackend{
		windowResponse: &ProviderWindowResult{
			OK: true,
			Mutation: &ProviderMutationFacts{
				InputEventsEmitted:  true,
				DesktopStateMutated: true,
			},
			ActionEvidence: &ProviderActionEvidence{
				InputEmitted: true,
			},
		},
	}
	service := NewServiceWithBackend(backend)
	for _, action := range []WindowOperation{WindowActivate, WindowClose} {
		backend.calls = nil
		result, err := service.callProviderAccessibilityOperation(
			AXServiceRequest{
				App:          "Editor",
				WindowAction: &action,
				SemanticTarget: &semanticTarget{
					Selector:    `window "Editor"`,
					Role:        "window",
					Name:        "Editor",
					HasName:     true,
					LocatorKind: AXLocatorProviderBound,
				},
			},
		)
		if err != nil {
			t.Fatalf("%s window action failed: %v", action, err)
		}
		if !reflect.DeepEqual(
			backend.calls,
			[]string{"window_" + string(action)},
		) {
			t.Fatalf("%s routed to %v", action, backend.calls)
		}
		if result == nil || !result.OK || !result.InputEventsEmitted ||
			!result.DesktopStateMutated || result.ActionEvidence == nil {
			t.Fatalf("%s result projection is incomplete: %#v", action, result)
		}
	}
}

func TestAccessibilityOperationRejectsConflictingTypedDomains(t *testing.T) {
	service := NewServiceWithBackend(&recordingPlatformBackend{})
	action := WindowActivate
	_, err := service.callProviderAccessibilityOperation(AXServiceRequest{
		Operation:    axServiceObserve,
		WindowAction: &action,
	})
	if err == nil || !strings.Contains(
		err.Error(),
		"exactly one typed operation domain",
	) {
		t.Fatalf("conflicting typed domains did not fail closed: %v", err)
	}
}

func TestRuntimeTimeoutDiagnosticUsesInjectedBackend(t *testing.T) {
	backend := &recordingPlatformBackend{
		processState: "running",
		windowKnown:  true,
		windowAlive:  true,
	}
	service := NewServiceWithBackend(backend)
	got := service.runtimeTimeoutDiagnostic(&AppSnapshot{
		App: AppDescriptor{PID: 42},
	})
	if !strings.Contains(got, "cached_pid_present=true") ||
		!strings.Contains(got, "window_for_cached_pid=true") {
		t.Fatalf("timeout diagnostic did not use backend facts: %q", got)
	}
}
