//go:build windows

package provider

import (
	"context"
	"fmt"
	"os"
	"os/exec"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

type windowsRuntimeBackend struct{}

var _ core.PlatformBackend = windowsRuntimeBackend{}

func (windowsRuntimeBackend) ObserveApps(
	request core.ProviderObserveAppsRequest,
) (core.ProviderAccessibilityResult, error) {
	if err := core.ValidateProviderObserveAppsRequest(request); err != nil {
		return core.ProviderAccessibilityResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextWindowsProviderRequestID(), core.ProviderWireObserveApps,
	)
	wire.ObserveApps = &request
	response, err := runWindowsProvider(wire)
	if response == nil || response.Accessibility == nil {
		return core.ProviderAccessibilityResult{}, err
	}
	return *response.Accessibility, err
}

func (windowsRuntimeBackend) Observe(
	request core.ProviderObserveRequest,
) (core.ProviderAccessibilityResult, error) {
	if err := core.ValidateProviderObserveRequest(request); err != nil {
		return core.ProviderAccessibilityResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextWindowsProviderRequestID(), core.ProviderWireObserve,
	)
	wire.Observe = &request
	response, err := runWindowsProvider(wire)
	if response == nil || response.Accessibility == nil {
		return core.ProviderAccessibilityResult{}, err
	}
	return *response.Accessibility, err
}

func (windowsRuntimeBackend) ResolveAndAct(
	request core.ProviderActionRequest,
) (core.ProviderAccessibilityResult, error) {
	if err := core.ValidateProviderActionRequest(request); err != nil {
		return core.ProviderAccessibilityResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextWindowsProviderRequestID(), core.ProviderWireResolveAndAct,
	)
	wire.Action = &request
	response, err := runWindowsProvider(wire)
	if response == nil || response.Accessibility == nil {
		return core.ProviderAccessibilityResult{}, err
	}
	return *response.Accessibility, err
}

func (windowsRuntimeBackend) ExecuteBatch(
	request core.ProviderDesktopRequest,
) (core.ProviderDesktopResult, error) {
	wire := core.NewProviderWireRequest(
		nextWindowsProviderRequestID(), core.ProviderWireDesktopBatch,
	)
	wire.Desktop = &request
	response, err := runWindowsProvider(wire)
	if response == nil || response.Desktop == nil {
		return core.ProviderDesktopResult{}, err
	}
	return *response.Desktop, err
}

func (windowsRuntimeBackend) ListWindows(
	request core.ProviderWindowListRequest,
) (core.ProviderWindowResult, error) {
	if err := core.ValidateProviderWindowListRequest(request); err != nil {
		return core.ProviderWindowResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextWindowsProviderRequestID(), core.ProviderWireWindowList,
	)
	wire.WindowList = &request
	response, err := runWindowsProvider(wire)
	if response == nil || response.Window == nil {
		return core.ProviderWindowResult{}, err
	}
	return *response.Window, err
}

func (windowsRuntimeBackend) ActOnWindow(
	request core.ProviderWindowActionRequest,
) (core.ProviderWindowResult, error) {
	if err := core.ValidateProviderWindowActionRequest(request); err != nil {
		return core.ProviderWindowResult{}, err
	}
	wire := core.NewProviderWireRequest(
		nextWindowsProviderRequestID(), core.ProviderWireWindowAction,
	)
	wire.WindowAction = &request
	response, err := runWindowsProvider(wire)
	if response == nil || response.Window == nil {
		return core.ProviderWindowResult{}, err
	}
	return *response.Window, err
}

func (windowsRuntimeBackend) WindowForPID(pid int) (known, alive bool) {
	return windowsNativeWindowForPID(pid)
}

func (windowsRuntimeBackend) HostInfo() core.HostBackendInfo {
	return core.HostBackendInfo{
		Platform: "windows",
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

func (windowsRuntimeBackend) RuntimeEnvironment(base []string) []string {
	if base == nil {
		return os.Environ()
	}
	return append([]string(nil), base...)
}

func (windowsRuntimeBackend) ProcessState(pid int) string {
	if pid <= 0 {
		return "missing"
	}
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	cmd := exec.CommandContext(
		ctx,
		"tasklist.exe",
		"/FI",
		fmt.Sprintf("PID eq %d", pid),
		"/FO",
		"CSV",
		"/NH",
	)
	output := &boundedProviderBuffer{limit: maxWindowsTasklistOutputBytes}
	configureWindowsBackgroundCommand(cmd)
	cmd.Stdout = output
	cmd.WaitDelay = 100 * time.Millisecond
	err := cmd.Run()
	if err != nil || ctx.Err() != nil {
		return "unknown"
	}
	data, truncated := output.snapshot()
	if truncated {
		return "unknown"
	}
	return windowsTasklistProcessState(data, pid)
}

func platformRuntimeBackend() core.PlatformBackend {
	return windowsRuntimeBackend{}
}

func platformShutdown() {
	shutdownWindowsProviderProcess()
}

func init() {
	core.DefaultRuntimeBackend = platformRuntimeBackend
	core.ShutdownPlatformRuntime = platformShutdown
}
