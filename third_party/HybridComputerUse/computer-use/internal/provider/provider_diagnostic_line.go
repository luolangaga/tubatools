//go:build linux || windows || windows_sidecar_test

package provider

import (
	"bufio"
	"errors"
)

// readProviderDiagnosticLine reads one provider stderr line and keeps at most
// limit bytes of it. originalBytes counts the whole line without its newline,
// so an oversized line is drained and reported as truncated.
func readProviderDiagnosticLine(
	reader *bufio.Reader,
	limit int,
) (line []byte, originalBytes int, truncated bool, err error) {
	if limit <= 0 {
		limit = 1
	}
	for {
		fragment, readErr := reader.ReadSlice('\n')
		content := fragment
		if readErr == nil && len(content) > 0 &&
			content[len(content)-1] == '\n' {
			content = content[:len(content)-1]
		}
		originalBytes += len(content)
		if remaining := limit - len(line); remaining > 0 {
			if len(content) > remaining {
				line = append(line, content[:remaining]...)
				truncated = true
			} else {
				line = append(line, content...)
			}
		} else if len(content) > 0 {
			truncated = true
		}
		switch {
		case readErr == nil:
			return line, originalBytes, truncated, nil
		case errors.Is(readErr, bufio.ErrBufferFull):
			continue
		default:
			return line, originalBytes, truncated, readErr
		}
	}
}
