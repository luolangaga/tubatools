package core

// PlatformCapabilities describes mechanisms implemented by an operating-system
// backend. Runtime permission and provider availability remain call results,
// not static capability claims.
type PlatformCapabilities struct {
	Accessibility        bool
	DesktopCapture       bool
	InputSynthesis       bool
	WindowManagement     bool
	HostSessionDiscovery bool
	ResourceCorrelation  bool
}

type HostBackendInfo struct {
	Platform     string
	Capabilities PlatformCapabilities
}

type AccessibilityOperation string

const (
	AccessibilityResolveTarget AccessibilityOperation = "resolve_target"
	AccessibilityClick         AccessibilityOperation = "click"
	AccessibilityScroll        AccessibilityOperation = "scroll"
	AccessibilityType          AccessibilityOperation = "type"
	AccessibilityKeypress      AccessibilityOperation = "keypress"
	AccessibilitySetValue      AccessibilityOperation = "set_value"
	AccessibilitySelect        AccessibilityOperation = "select"
)

type WindowOperation string

const (
	WindowList     WindowOperation = "list_app_windows"
	WindowActivate WindowOperation = "activate"
	WindowClose    WindowOperation = "close"
)

// AccessibilityBackend owns provider acquisition and provider-native semantic
// operations. Canonicalization, refs, stale policy, projection and model
// rendering remain in the common Go service.
type AccessibilityBackend interface {
	ObserveApps(ProviderObserveAppsRequest) (ProviderAccessibilityResult, error)
	Observe(ProviderObserveRequest) (ProviderAccessibilityResult, error)
	ResolveAndAct(ProviderActionRequest) (ProviderAccessibilityResult, error)
}

// DesktopBackend owns full-desktop capture and global pointer/keyboard input.
// GUI provenance, stale policy and AX cache invalidation remain in the common
// service.
type DesktopBackend interface {
	ExecuteBatch(ProviderDesktopRequest) (ProviderDesktopResult, error)
}

// WindowBackend owns exact top-level window inventory and commands. Native
// window handles remain private backend evidence.
type WindowBackend interface {
	ListWindows(ProviderWindowListRequest) (ProviderWindowResult, error)
	ActOnWindow(ProviderWindowActionRequest) (ProviderWindowResult, error)
	WindowForPID(pid int) (known, alive bool)
}

// HostBackend owns interactive-session discovery and process liveness facts.
type HostBackend interface {
	HostInfo() HostBackendInfo
	RuntimeEnvironment(base []string) []string
	ProcessState(pid int) string
}

// PlatformBackend is the private operating-system boundary below the stable AX
// and GUI model contract.
type PlatformBackend interface {
	AccessibilityBackend
	DesktopBackend
	WindowBackend
	HostBackend
}
