//go:build windows || windows_sidecar_test

package provider

import (
	"bytes"
	"encoding/csv"
	"io"
	"strconv"
	"strings"
)

const maxWindowsTasklistOutputBytes = 64 << 10

func windowsTasklistProcessState(output []byte, pid int) string {
	if pid <= 0 {
		return "missing"
	}
	if len(output) > maxWindowsTasklistOutputBytes {
		return "unknown"
	}
	reader := csv.NewReader(bytes.NewReader(output))
	reader.FieldsPerRecord = -1
	matched := false
	for {
		record, err := reader.Read()
		if err == io.EOF {
			if matched {
				return "running"
			}
			return "missing"
		}
		if err != nil {
			return "unknown"
		}
		// /FO CSV /NH emits five columns for a process. Localized notices
		// are not process rows and must never establish PID liveness.
		if len(record) != 5 {
			continue
		}
		rowPID, err := strconv.Atoi(strings.TrimSpace(record[1]))
		if err != nil || rowPID < 0 {
			return "unknown"
		}
		matched = matched || rowPID == pid
	}
}
