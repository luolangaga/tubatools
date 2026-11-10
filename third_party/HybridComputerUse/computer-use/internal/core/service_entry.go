package core

// callForCommandLine returns exactly what an MCP client without structured
// content receives, except that images are files: their paths replace the
// base64 data.
func (s *Service) CallForCommandLine(name string, args map[string]any) (ToolCallResult, error) {
	result, _, err := s.CallForText(name, args)
	return result, err
}

// callForText is callForCommandLine plus the public error code of the
// result ("" on success), for entry points that map it to an exit status.
func (s *Service) CallForText(name string, args map[string]any) (ToolCallResult, string, error) {
	result := prepareModelEvidence(name, s.callTool(name, args))
	code := ""
	if result.IsError {
		code = recordFromEvidence(name, result).Code
	}
	result, err := writeImagesToFiles(placeModelEvidence(name, result, false))
	return result, code, err
}
