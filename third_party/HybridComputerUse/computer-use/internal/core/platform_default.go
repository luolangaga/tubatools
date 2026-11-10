package core

// DefaultRuntimeBackend returns the backend of a service created without
// one, and shutdownPlatformRuntime stops what that backend started. A
// platform backend file installs both in init(). Where no platform backend
// is linked (an unsupported GOOS, or tests of packages below the provider),
// a service gets the unsupported backend.
var (
	DefaultRuntimeBackend   = func() PlatformBackend { return unsupportedRuntimeBackend{} }
	ShutdownPlatformRuntime = func() {}
)
