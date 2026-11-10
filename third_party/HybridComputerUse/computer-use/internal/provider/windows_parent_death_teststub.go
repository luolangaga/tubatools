//go:build windows_sidecar_test && !windows

package provider

import (
	"io"
	"os/exec"
)

var windowsProviderParentDeathBinder = func(*exec.Cmd) (io.Closer, error) {
	return testStubCloser{}, nil
}

// testStubCloser stands in for the Windows job object handle.
type testStubCloser struct{}

func (testStubCloser) Close() error { return nil }
