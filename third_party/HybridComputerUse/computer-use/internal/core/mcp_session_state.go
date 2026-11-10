package core

const (
	MCPProtocolLegacy     = "2025-03-26"
	MCPProtocolStructured = "2025-06-18"
)

// mcpTransportState is what the MCP adapter negotiates with its client. It
// rides on the service only so that one value carries a session to the
// adapter; core code does not read it.
type mcpTransportState struct {
	ProtocolVersion string `json:"-"`
}
