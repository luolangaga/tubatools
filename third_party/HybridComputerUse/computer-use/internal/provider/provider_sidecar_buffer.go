//go:build darwin || windows || darwin_sidecar_test || windows_sidecar_test

package provider

import (
	"strings"
	"sync"
)

// boundedProviderBuffer keeps the last limit bytes written to it. The macOS
// and Windows sidecar transports use it for provider stderr.
type boundedProviderBuffer struct {
	mu        sync.Mutex
	data      []byte
	limit     int
	truncated bool
}

func (buffer *boundedProviderBuffer) Write(data []byte) (int, error) {
	buffer.mu.Lock()
	defer buffer.mu.Unlock()
	originalLength := len(data)
	if buffer.limit <= 0 {
		buffer.truncated = buffer.truncated || originalLength > 0
		return originalLength, nil
	}
	if len(data) >= buffer.limit {
		buffer.data = append(buffer.data[:0], data[len(data)-buffer.limit:]...)
		buffer.truncated = true
		return originalLength, nil
	}
	if excess := len(buffer.data) + len(data) - buffer.limit; excess > 0 {
		copy(buffer.data, buffer.data[excess:])
		buffer.data = buffer.data[:len(buffer.data)-excess]
		buffer.truncated = true
	}
	buffer.data = append(buffer.data, data...)
	return originalLength, nil
}

func (buffer *boundedProviderBuffer) snapshot() ([]byte, bool) {
	buffer.mu.Lock()
	defer buffer.mu.Unlock()
	return append([]byte(nil), buffer.data...), buffer.truncated
}

// boundedProviderLabel trims a provider-supplied label for diagnostics.
func boundedProviderLabel(value string) string {
	const limit = 256
	value = strings.TrimSpace(value)
	if len(value) > limit {
		return value[:limit]
	}
	return value
}
