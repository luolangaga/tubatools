//go:build windows || windows_sidecar_test

package provider

var windowsNativeWindowProbe = probeWindowsNativeWindow

func windowsNativeWindowForPID(pid int) (known, alive bool) {
	if pid <= 0 {
		return true, false
	}
	if uint64(pid) > uint64(^uint32(0)) {
		return false, false
	}
	return windowsNativeWindowProbe(uint32(pid))
}
