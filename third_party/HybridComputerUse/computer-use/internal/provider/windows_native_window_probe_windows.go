//go:build windows

package provider

import (
	"syscall"
	"unsafe"
)

// EnumWindows documents SetLastError as the way to distinguish an early
// callback stop from enumeration failure. Application-defined error values
// avoid allocating a permanent syscall callback trampoline on each probe.
const windowsProbeFound = syscall.Errno(0x20000001)
const windowsProbeIncomplete = syscall.Errno(0x20000002)

var (
	windowsProbeUser32       = syscall.NewLazyDLL("user32.dll")
	windowsProbeEnumWindows  = windowsProbeUser32.NewProc("EnumWindows")
	windowsProbeIsVisible    = windowsProbeUser32.NewProc("IsWindowVisible")
	windowsProbeGetPID       = windowsProbeUser32.NewProc("GetWindowThreadProcessId")
	windowsProbeGetRect      = windowsProbeUser32.NewProc("GetWindowRect")
	windowsProbeSetLastError = kernel32DLL.NewProc("SetLastError")
	windowsProbeCallback     = syscall.NewCallback(func(window, requestedPID uintptr) uintptr {
		visible, _, _ := windowsProbeIsVisible.Call(window)
		if visible == 0 {
			return 1
		}
		var ownerPID uint32
		thread, _, _ := windowsProbeGetPID.Call(window, uintptr(unsafe.Pointer(&ownerPID)))
		if thread == 0 {
			windowsProbeSetLastError.Call(uintptr(windowsProbeIncomplete))
			return 0
		}
		if ownerPID != uint32(requestedPID) {
			return 1
		}
		var rect struct{ left, top, right, bottom int32 }
		ok, _, _ := windowsProbeGetRect.Call(window, uintptr(unsafe.Pointer(&rect)))
		if ok == 0 {
			windowsProbeSetLastError.Call(uintptr(windowsProbeIncomplete))
			return 0
		}
		if rect.right <= rect.left || rect.bottom <= rect.top {
			return 1
		}
		windowsProbeSetLastError.Call(uintptr(windowsProbeFound))
		return 0
	})
)

func probeWindowsNativeWindow(pid uint32) (known, alive bool) {
	// These Win32 queries do not initialize UIA, send window messages, or
	// wait for an application's accessibility provider to respond.
	complete, _, callErr := windowsProbeEnumWindows.Call(windowsProbeCallback, uintptr(pid))
	if complete != 0 {
		return true, false
	}
	if callErr == windowsProbeFound {
		return true, true
	}
	return false, false
}
