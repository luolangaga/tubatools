//go:build windows

package provider

import (
	"fmt"
	"io"
	"os/exec"
	"syscall"
	"unsafe"
)

const (
	jobObjectExtendedLimitInformation = 9
	jobObjectLimitKillOnJobClose      = 0x00002000
	processSetQuota                   = 0x0100
	processTerminate                  = 0x0001
)

var (
	kernel32DLL                      = syscall.NewLazyDLL("kernel32.dll")
	createJobObjectW                 = kernel32DLL.NewProc("CreateJobObjectW")
	setInformationJobObject          = kernel32DLL.NewProc("SetInformationJobObject")
	assignProcessToJobObject         = kernel32DLL.NewProc("AssignProcessToJobObject")
	openProcessForJob                = kernel32DLL.NewProc("OpenProcess")
	windowsProviderParentDeathBinder = bindWindowsProviderParentDeath
)

type windowsJobHandle syscall.Handle

type jobObjectBasicLimitInformation struct {
	PerProcessUserTimeLimit int64
	PerJobUserTimeLimit     int64
	LimitFlags              uint32
	MinimumWorkingSetSize   uintptr
	MaximumWorkingSetSize   uintptr
	ActiveProcessLimit      uint32
	Affinity                uintptr
	PriorityClass           uint32
	SchedulingClass         uint32
}

type ioCounters struct {
	ReadOperationCount  uint64
	WriteOperationCount uint64
	OtherOperationCount uint64
	ReadTransferCount   uint64
	WriteTransferCount  uint64
	OtherTransferCount  uint64
}

type jobObjectExtendedLimitInformationData struct {
	BasicLimitInformation jobObjectBasicLimitInformation
	IoInfo                ioCounters
	ProcessMemoryLimit    uintptr
	JobMemoryLimit        uintptr
	PeakProcessMemoryUsed uintptr
	PeakJobMemoryUsed     uintptr
}

func bindWindowsProviderParentDeath(cmd *exec.Cmd) (io.Closer, error) {
	if cmd == nil || cmd.Process == nil {
		return nil, fmt.Errorf("provider process is unavailable")
	}
	job, _, createErr := createJobObjectW.Call(0, 0)
	if job == 0 {
		return nil, windowsCallError("CreateJobObjectW", createErr)
	}
	handle := windowsJobHandle(job)
	closeOnError := true
	defer func() {
		if closeOnError {
			_ = handle.Close()
		}
	}()

	limits := jobObjectExtendedLimitInformationData{}
	limits.BasicLimitInformation.LimitFlags =
		jobObjectLimitKillOnJobClose
	ok, _, setErr := setInformationJobObject.Call(
		job,
		jobObjectExtendedLimitInformation,
		uintptr(unsafe.Pointer(&limits)),
		unsafe.Sizeof(limits),
	)
	if ok == 0 {
		return nil, windowsCallError(
			"SetInformationJobObject",
			setErr,
		)
	}

	process, _, openErr := openProcessForJob.Call(
		processSetQuota|processTerminate,
		0,
		uintptr(uint32(cmd.Process.Pid)),
	)
	if process == 0 {
		return nil, windowsCallError("OpenProcess", openErr)
	}
	defer syscall.CloseHandle(syscall.Handle(process))

	ok, _, assignErr := assignProcessToJobObject.Call(job, process)
	if ok == 0 {
		return nil, windowsCallError(
			"AssignProcessToJobObject",
			assignErr,
		)
	}
	closeOnError = false
	return handle, nil
}

func (handle windowsJobHandle) Close() error {
	return syscall.CloseHandle(syscall.Handle(handle))
}

func windowsCallError(operation string, callErr error) error {
	if callErr == nil || callErr == syscall.Errno(0) {
		return fmt.Errorf("%s failed", operation)
	}
	return fmt.Errorf("%s: %w", operation, callErr)
}
