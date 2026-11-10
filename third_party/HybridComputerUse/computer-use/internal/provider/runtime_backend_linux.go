//go:build linux

package provider

import (
	"os"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

type linuxRuntimeBackend struct{}

var _ core.PlatformBackend = linuxRuntimeBackend{}

func (backend linuxRuntimeBackend) ObserveApps(
	request core.ProviderObserveAppsRequest,
) (core.ProviderAccessibilityResult, error) {
	if err := core.ValidateProviderObserveAppsRequest(request); err != nil {
		return core.ProviderAccessibilityResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextLinuxProviderRequestID(), core.ProviderWireObserveApps,
	)
	wire.ObserveApps = &request
	response, err := runLinuxProvider(wire, backend)
	if response == nil || response.Accessibility == nil {
		return core.ProviderAccessibilityResult{}, err
	}
	return *response.Accessibility, err
}

func (backend linuxRuntimeBackend) Observe(
	request core.ProviderObserveRequest,
) (core.ProviderAccessibilityResult, error) {
	if err := core.ValidateProviderObserveRequest(request); err != nil {
		return core.ProviderAccessibilityResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextLinuxProviderRequestID(), core.ProviderWireObserve,
	)
	wire.Observe = &request
	response, err := runLinuxProvider(wire, backend)
	if response == nil || response.Accessibility == nil {
		return core.ProviderAccessibilityResult{}, err
	}
	return *response.Accessibility, err
}

func (backend linuxRuntimeBackend) ResolveAndAct(
	request core.ProviderActionRequest,
) (core.ProviderAccessibilityResult, error) {
	if err := core.ValidateProviderActionRequest(request); err != nil {
		return core.ProviderAccessibilityResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextLinuxProviderRequestID(), core.ProviderWireResolveAndAct,
	)
	wire.Action = &request
	response, err := runLinuxProvider(wire, backend)
	if response == nil || response.Accessibility == nil {
		return core.ProviderAccessibilityResult{}, err
	}
	return *response.Accessibility, err
}

func (backend linuxRuntimeBackend) ExecuteBatch(
	request core.ProviderDesktopRequest,
) (core.ProviderDesktopResult, error) {
	wire := core.NewProviderWireRequest(
		nextLinuxProviderRequestID(), core.ProviderWireDesktopBatch,
	)
	wire.Desktop = &request
	response, err := runLinuxProvider(wire, backend)
	if response == nil || response.Desktop == nil {
		return core.ProviderDesktopResult{}, err
	}
	return *response.Desktop, err
}

func (backend linuxRuntimeBackend) ListWindows(
	request core.ProviderWindowListRequest,
) (core.ProviderWindowResult, error) {
	if err := core.ValidateProviderWindowListRequest(request); err != nil {
		return core.ProviderWindowResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextLinuxProviderRequestID(), core.ProviderWireWindowList,
	)
	wire.WindowList = &request
	response, err := runLinuxProvider(wire, backend)
	if response == nil || response.Window == nil {
		return core.ProviderWindowResult{}, err
	}
	return *response.Window, err
}

func (backend linuxRuntimeBackend) ActOnWindow(
	request core.ProviderWindowActionRequest,
) (core.ProviderWindowResult, error) {
	if err := core.ValidateProviderWindowActionRequest(request); err != nil {
		return core.ProviderWindowResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextLinuxProviderRequestID(), core.ProviderWireWindowAction,
	)
	wire.WindowAction = &request
	response, err := runLinuxProvider(wire, backend)
	if response == nil || response.Window == nil {
		return core.ProviderWindowResult{}, err
	}
	return *response.Window, err
}

func (backend linuxRuntimeBackend) WindowForPID(pid int) (known, alive bool) {
	return linuxX11WindowForPID(pid, backend.RuntimeEnvironment(nil))
}

func (linuxRuntimeBackend) HostInfo() core.HostBackendInfo {
	return core.HostBackendInfo{
		Platform: "linux",
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

func (linuxRuntimeBackend) RuntimeEnvironment(base []string) []string {
	if base == nil {
		base = os.Environ()
	}
	return linuxRuntimeEnvironment(base)
}

func (linuxRuntimeBackend) ProcessState(pid int) string {
	return processPIDState(pid, "/proc")
}

func platformRuntimeBackend() core.PlatformBackend {
	return linuxRuntimeBackend{}
}

func platformShutdown() {
	shutdownLinuxProviderProcess()
}

func init() {
	core.DefaultRuntimeBackend = platformRuntimeBackend
	core.ShutdownPlatformRuntime = platformShutdown
}
