package provider

import (
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func TestDefaultServiceUsesPlatformBackend(t *testing.T) {
	service := core.NewService()
	if _, ok := service.Backend.(linuxRuntimeBackend); !ok {
		t.Fatalf("default backend = %T, want linuxRuntimeBackend", service.Backend)
	}
}
