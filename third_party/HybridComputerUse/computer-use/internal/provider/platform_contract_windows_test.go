//go:build windows

package provider

import (
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func TestDefaultServiceUsesWindowsBackend(t *testing.T) {
	service := core.NewService()
	if _, ok := service.Backend.(windowsRuntimeBackend); !ok {
		t.Fatalf(
			"default backend = %T, want windowsRuntimeBackend",
			service.Backend,
		)
	}
}
