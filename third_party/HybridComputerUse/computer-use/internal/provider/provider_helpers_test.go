package provider

import "github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"

// Copies of lower-package test helpers for tests in this package.

// DecodeProviderWireRequest strictly decodes and validates a request the
// way the native providers do. The Go runtime only encodes requests, so this
// decoder exists for contract tests.
func providerDecodeProviderWireRequest(data []byte) (core.ProviderWireRequest, error) {
	var request core.ProviderWireRequest
	if err := core.DecodeProviderWireJSON(data, &request); err != nil {
		return core.ProviderWireRequest{}, err
	}
	if err := request.Validate(); err != nil {
		return core.ProviderWireRequest{}, err
	}
	return request, nil
}
