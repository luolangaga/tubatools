//go:build darwin

package provider

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"syscall"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

type darwinRuntimeBackend struct{}

var _ core.PlatformBackend = darwinRuntimeBackend{}
var _ core.ProviderOrderedInputBackend = darwinRuntimeBackend{}

func (darwinRuntimeBackend) ObserveApps(
	request core.ProviderObserveAppsRequest,
) (core.ProviderAccessibilityResult, error) {
	if err := core.ValidateProviderObserveAppsRequest(request); err != nil {
		return core.ProviderAccessibilityResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextDarwinProviderRequestID(), core.ProviderWireObserveApps,
	)
	wire.ObserveApps = &request
	response, err := runDarwinProvider(wire)
	if response == nil || response.Accessibility == nil {
		return core.ProviderAccessibilityResult{}, err
	}
	return *response.Accessibility, err
}

func (darwinRuntimeBackend) Observe(
	request core.ProviderObserveRequest,
) (core.ProviderAccessibilityResult, error) {
	if err := core.ValidateProviderObserveRequest(request); err != nil {
		return core.ProviderAccessibilityResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextDarwinProviderRequestID(), core.ProviderWireObserve,
	)
	wire.Observe = &request
	response, err := runDarwinProvider(wire)
	if response == nil || response.Accessibility == nil {
		return core.ProviderAccessibilityResult{}, err
	}
	return *response.Accessibility, err
}

func (darwinRuntimeBackend) ResolveAndAct(
	request core.ProviderActionRequest,
) (core.ProviderAccessibilityResult, error) {
	if err := core.ValidateProviderActionRequest(request); err != nil {
		return core.ProviderAccessibilityResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextDarwinProviderRequestID(), core.ProviderWireResolveAndAct,
	)
	wire.Action = &request
	if core.ProviderTargetDiagnosticEnabled(os.Getenv) {
		target, _ := json.Marshal(request.Target)
		fmt.Fprintf(
			os.Stderr,
			"[computer-use private-target-diagnostic request=%s intent=%s target=%s]\n",
			wire.RequestID,
			request.Intent,
			target,
		)
	}
	response, err := runDarwinProvider(wire)
	if response == nil || response.Accessibility == nil {
		return core.ProviderAccessibilityResult{}, err
	}
	return *response.Accessibility, err
}

func (darwinRuntimeBackend) ExecuteOrderedInput(
	request core.ProviderOrderedInputRequest,
) (core.ProviderOrderedInputResult, error) {
	if err := request.Validate(); err != nil {
		return core.ProviderOrderedInputResult{}, err
	}
	request.Diagnostics = core.ProviderDiagnosticContextFromLookup(os.Getenv)
	wire := core.NewProviderWireRequest(
		nextDarwinProviderRequestID(), core.ProviderWireOrderedInput,
	)
	wire.OrderedInput = &core.ProviderOrderedInputWireRequest{
		ProtocolVersion:             core.ProviderProtocolVersion,
		ProviderOrderedInputRequest: request,
	}
	response, err := runDarwinProvider(wire)
	if response == nil || response.OrderedInput == nil {
		return core.ProviderOrderedInputResult{}, err
	}
	return *response.OrderedInput, err
}

func (darwinRuntimeBackend) ExecuteBatch(
	request core.ProviderDesktopRequest,
) (core.ProviderDesktopResult, error) {
	wire := core.NewProviderWireRequest(
		nextDarwinProviderRequestID(), core.ProviderWireDesktopBatch,
	)
	wire.Desktop = &request
	response, err := runDarwinProvider(wire)
	if response == nil || response.Desktop == nil {
		return core.ProviderDesktopResult{}, err
	}
	return *response.Desktop, err
}

func (darwinRuntimeBackend) ListWindows(
	request core.ProviderWindowListRequest,
) (core.ProviderWindowResult, error) {
	if err := core.ValidateProviderWindowListRequest(request); err != nil {
		return core.ProviderWindowResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextDarwinProviderRequestID(), core.ProviderWireWindowList,
	)
	wire.WindowList = &request
	response, err := runDarwinProvider(wire)
	if response == nil || response.Window == nil {
		return core.ProviderWindowResult{}, err
	}
	return *response.Window, err
}

func (darwinRuntimeBackend) ActOnWindow(
	request core.ProviderWindowActionRequest,
) (core.ProviderWindowResult, error) {
	if err := core.ValidateProviderWindowActionRequest(request); err != nil {
		return core.ProviderWindowResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextDarwinProviderRequestID(), core.ProviderWireWindowAction,
	)
	wire.WindowAction = &request
	response, err := runDarwinProvider(wire)
	if response == nil || response.Window == nil {
		return core.ProviderWindowResult{}, err
	}
	return *response.Window, err
}

func (backend darwinRuntimeBackend) WindowForPID(
	pid int,
) (known, alive bool) {
	return darwinTimeoutWindowForPID(pid, backend.ProcessState(pid))
}

func (darwinRuntimeBackend) HostInfo() core.HostBackendInfo {
	return core.HostBackendInfo{
		Platform: "darwin",
		Capabilities: core.PlatformCapabilities{
			Accessibility:        true,
			DesktopCapture:       true,
			InputSynthesis:       true,
			WindowManagement:     true,
			HostSessionDiscovery: true,
			ResourceCorrelation:  true,
		},
	}
}

func (darwinRuntimeBackend) RuntimeEnvironment(base []string) []string {
	if base == nil {
		return os.Environ()
	}
	return append([]string(nil), base...)
}

func (darwinRuntimeBackend) ProcessState(pid int) string {
	if pid <= 0 {
		return "missing"
	}
	if err := syscall.Kill(pid, 0); err == nil {
		return "running"
	} else if errors.Is(err, syscall.ESRCH) {
		return "missing"
	}
	return "unknown"
}

func platformRuntimeBackend() core.PlatformBackend {
	return darwinRuntimeBackend{}
}

func platformShutdown() {
	shutdownDarwinProviderProcess()
}

func init() {
	core.DefaultRuntimeBackend = platformRuntimeBackend
	core.ShutdownPlatformRuntime = platformShutdown
}
