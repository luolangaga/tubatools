//go:build windows_sidecar_test && !windows

package provider

func probeWindowsNativeWindow(uint32) (known, alive bool) {
	return false, false
}
