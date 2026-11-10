package core

import "errors"

type unsupportedRuntimeBackend struct{}

var _ PlatformBackend = unsupportedRuntimeBackend{}

func unsupportedBackendError() error {
	return errors.New("no native desktop backend is linked for this platform")
}

func (unsupportedRuntimeBackend) ObserveApps(
	ProviderObserveAppsRequest,
) (ProviderAccessibilityResult, error) {
	return ProviderAccessibilityResult{}, unsupportedBackendError()
}

func (unsupportedRuntimeBackend) Observe(
	ProviderObserveRequest,
) (ProviderAccessibilityResult, error) {
	return ProviderAccessibilityResult{}, unsupportedBackendError()
}

func (unsupportedRuntimeBackend) ResolveAndAct(
	ProviderActionRequest,
) (ProviderAccessibilityResult, error) {
	return ProviderAccessibilityResult{}, unsupportedBackendError()
}

func (unsupportedRuntimeBackend) ExecuteBatch(
	ProviderDesktopRequest,
) (ProviderDesktopResult, error) {
	return ProviderDesktopResult{}, unsupportedBackendError()
}

func (unsupportedRuntimeBackend) ListWindows(
	ProviderWindowListRequest,
) (ProviderWindowResult, error) {
	return ProviderWindowResult{}, unsupportedBackendError()
}

func (unsupportedRuntimeBackend) ActOnWindow(
	ProviderWindowActionRequest,
) (ProviderWindowResult, error) {
	return ProviderWindowResult{}, unsupportedBackendError()
}

func (unsupportedRuntimeBackend) WindowForPID(int) (bool, bool) {
	return false, false
}

func (unsupportedRuntimeBackend) HostInfo() HostBackendInfo {
	return HostBackendInfo{Platform: "unsupported"}
}

func (unsupportedRuntimeBackend) RuntimeEnvironment(base []string) []string {
	return base
}

func (unsupportedRuntimeBackend) ProcessState(int) string {
	return "unknown"
}
