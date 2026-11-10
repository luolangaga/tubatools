//go:build darwin || darwin_sidecar_test

package provider

import (
	"strconv"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func darwinTimeoutDesktopRequest(actions []core.ProviderDesktopAction, settleMS int) core.ProviderWireRequest {
	request := core.NewProviderWireRequest("timeout-budget", core.ProviderWireDesktopBatch)
	request.Desktop = &core.ProviderDesktopRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
		Actions:         actions,
		SettleMS:        settleMS,
	}
	return request
}

func TestDarwinGUIBudgetCoversNativePacing(t *testing.T) {
	t.Setenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS", "")
	for _, test := range []struct {
		name   string
		action core.ProviderDesktopAction
		pacing time.Duration
	}{
		// InputSimulation: paste command 100ms + consumption 500ms.
		{"paste", core.ProviderDesktopAction{Type: "type", Text: "a"}, 600 * time.Millisecond},
		// Drag: 27 posted mouse events * 30ms + 24 steps * 12ms + 360ms.
		{"drag", core.ProviderDesktopAction{Type: "drag", Path: []core.ProviderPoint{{X: 1, Y: 1}, {X: 10, Y: 10}}}, 1458 * time.Millisecond},
	} {
		t.Run(test.name, func(t *testing.T) {
			actions := make([]core.ProviderDesktopAction, contract.GUIMaxActions)
			for i := range actions {
				actions[i] = test.action
			}
			request := darwinTimeoutDesktopRequest(actions, contract.GUIMaxSettleMS)
			if err := request.Validate(); err != nil {
				t.Fatalf("timeout scenario must be a valid provider request: %v", err)
			}
			budget := darwinProviderRequestTimeout(request)
			required := time.Duration(len(actions))*test.pacing + time.Duration(contract.GUIMaxSettleMS)*time.Millisecond
			if required <= darwinProviderRuntimeTimeout() {
				t.Fatal("timeout scenario must exceed the default deadline")
			}
			if budget < required+darwinProviderRuntimeTimeout() || budget > 149*time.Second {
				t.Fatalf("budget %s does not cover native waits %s plus base reserve within the cap", budget, required)
			}
		})
	}
}

func TestDarwinGUIBudgetAccountsForWaitAndSettle(t *testing.T) {
	t.Setenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS", "")
	actions := make([]core.ProviderDesktopAction, contract.GUIMaxActions)
	for i := range actions {
		actions[i] = core.ProviderDesktopAction{Type: "type", Text: "a"}
	}
	baseline := darwinProviderRequestTimeout(darwinTimeoutDesktopRequest(actions, 0))
	for i := 0; i < contract.GUIMaxTotalWait; i++ {
		actions[i*2] = core.ProviderDesktopAction{Type: "wait"}
	}
	withWaits := darwinProviderRequestTimeout(darwinTimeoutDesktopRequest(actions, 0))
	withSettle := darwinProviderRequestTimeout(darwinTimeoutDesktopRequest(actions, contract.GUIMaxSettleMS))
	if withWaits-baseline != contract.GUIMaxTotalWait*time.Second {
		t.Fatalf("wait budget increment = %s", withWaits-baseline)
	}
	if withSettle-withWaits != time.Duration(contract.GUIMaxSettleMS)*time.Millisecond || withSettle > 149*time.Second {
		t.Fatalf("settle budget = %s, before settle = %s", withSettle, withWaits)
	}
}

func TestDarwinGUIBudgetPreservesExplicitTimeout(t *testing.T) {
	request := darwinTimeoutDesktopRequest([]core.ProviderDesktopAction{{Type: "wait"}}, contract.GUIMaxSettleMS)
	for _, seconds := range []int{1, 30, 300} {
		t.Run(strconv.Itoa(seconds), func(t *testing.T) {
			t.Setenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS", " "+strconv.Itoa(seconds)+" ")
			want := time.Duration(seconds) * time.Second
			if got := darwinProviderRequestTimeout(request); got != want {
				t.Fatalf("explicit timeout = %s, want %s", got, want)
			}
			if got := darwinProviderRuntimeTimeout(); got != want {
				t.Fatalf("non-GUI timeout = %s, want %s", got, want)
			}
			if got := darwinProviderStartupTimeout(); got != min(want, 10*time.Second) {
				t.Fatalf("startup timeout changed to %s", got)
			}
		})
	}
}

func TestDarwinGUIBudgetInvalidOverridesUseDefault(t *testing.T) {
	request := darwinTimeoutDesktopRequest([]core.ProviderDesktopAction{{Type: "type", Text: "a"}}, 0)
	t.Setenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS", "")
	want := darwinProviderRequestTimeout(request)
	for _, raw := range []string{" ", "0", "-1", "invalid", "1.5", "9223372037", "9223372036854775808"} {
		t.Run(raw, func(t *testing.T) {
			t.Setenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS", raw)
			if got := darwinProviderRequestTimeout(request); got != want {
				t.Fatalf("invalid override %q: got %s, want default %s", raw, got, want)
			}
		})
	}
}

func TestDarwinGUIBudgetIsBoundedForInvalidRequests(t *testing.T) {
	t.Setenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS", "")
	actions := make([]core.ProviderDesktopAction, 2*contract.GUIMaxActions)
	for i := range actions {
		actions[i] = core.ProviderDesktopAction{Type: "wait"}
	}
	request := darwinTimeoutDesktopRequest(actions, int(^uint(0)>>1))
	if got := darwinProviderRequestTimeout(request); got > 149*time.Second || got <= 0 {
		t.Fatalf("unbounded default timeout = %s", got)
	}
	request.Desktop.SettleMS = -1
	negative := darwinProviderRequestTimeout(request)
	request.Desktop.SettleMS = 0
	if got := darwinProviderRequestTimeout(request); got != negative {
		t.Fatalf("negative settle reduced timeout: negative %s, zero %s", negative, got)
	}
}

func TestDarwinGUIBudgetLeavesOtherOperationsUnchanged(t *testing.T) {
	t.Setenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS", "")
	for _, operation := range []core.ProviderWireOperation{
		core.ProviderWireObserveApps, core.ProviderWireObserve, core.ProviderWireResolveAndAct,
		core.ProviderWireOrderedInput, core.ProviderWireWindowList, core.ProviderWireWindowAction,
	} {
		request := core.NewProviderWireRequest("non-GUI", operation)
		if got := darwinProviderRequestTimeout(request); got != 30*time.Second {
			t.Errorf("%s timeout = %s", operation, got)
		}
	}
	for _, request := range []core.ProviderWireRequest{
		core.NewProviderWireRequest("no-payload", core.ProviderWireDesktopBatch),
		darwinTimeoutDesktopRequest(nil, 0),
	} {
		if got := darwinProviderRequestTimeout(request); got != 30*time.Second {
			t.Errorf("empty GUI timeout = %s", got)
		}
	}
}
