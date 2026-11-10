//go:build linux

package provider

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"time"
	"unicode/utf8"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

const linuxProviderDiagnosticLimit = 16 << 10
const linuxProviderDiagnosticLabelLimit = 256

func processPIDState(pid int, procRoot string) string {
	if pid <= 0 {
		return "missing"
	}
	dir := filepath.Join(procRoot, strconv.Itoa(pid))
	if _, err := os.Stat(dir); err != nil {
		return "missing"
	}
	data, err := os.ReadFile(filepath.Join(dir, "stat"))
	if err != nil {
		return "unknown"
	}
	text := strings.TrimSpace(string(data))
	closeParen := strings.LastIndex(text, ")")
	if closeParen < 0 {
		return "unknown"
	}
	rest := strings.TrimSpace(text[closeParen+1:])
	if rest == "" {
		return "unknown"
	}
	switch rest[0] {
	case 'Z':
		return "zombie"
	case 'X', 'x':
		return "exited"
	default:
		return "running"
	}
}

func linuxX11WindowForPID(
	pid int,
	environment []string,
) (known, alive bool) {
	ctx, cancel := context.WithTimeout(context.Background(), 750*time.Millisecond)
	defer cancel()
	cmd := exec.CommandContext(ctx, "wmctrl", "-lp")
	cmd.Env = environment
	output, err := cmd.Output()
	if err != nil || ctx.Err() != nil {
		return false, false
	}
	return true, wmctrlOutputHasPID(string(output), pid)
}

func wmctrlOutputHasPID(output string, pid int) bool {
	for _, line := range strings.Split(output, "\n") {
		fields := strings.Fields(line)
		if len(fields) < 3 {
			continue
		}
		value, err := strconv.Atoi(fields[2])
		if err == nil && value == pid {
			return true
		}
	}
	return false
}

// writeLinuxRuntimeDiagnostic keeps provider diagnostics on the operator side.
// Provider stderr can contain tracebacks, filesystem paths and implementation
// details, so it must never be copied into model-visible notes or errors.
func writeLinuxRuntimeDiagnostic(
	request core.ProviderWireRequest,
	stage,
	raw string,
) {
	writeLinuxProviderDiagnostic(map[string]any{
		"request_id": request.RequestID,
		"operation":  request.Operation,
		"stage":      stage,
		"diagnostic": raw,
	})
}

func writeLinuxRuntimeDiagnosticBytes(
	request core.ProviderWireRequest,
	stage string,
	raw []byte,
) {
	originalBytes := len(raw)
	sample := raw
	if len(sample) > linuxProviderDiagnosticLimit {
		sample = sample[:linuxProviderDiagnosticLimit]
	}
	writeLinuxProviderDiagnostic(map[string]any{
		"request_id":                request.RequestID,
		"operation":                 request.Operation,
		"stage":                     stage,
		"diagnostic":                string(sample),
		"diagnostic_original_bytes": originalBytes,
		"diagnostic_truncated":      originalBytes > len(sample),
	})
}

func writeLinuxProviderProcessDiagnostic(raw string) {
	text := strings.TrimSpace(raw)
	if text == "" {
		return
	}
	record := map[string]any{}
	if json.Unmarshal([]byte(text), &record) != nil ||
		record["kind"] != "linux_provider_diagnostic" {
		record = map[string]any{
			"request_id": "unknown",
			"operation":  "unknown",
			"stage":      "process_stderr",
			"diagnostic": text,
		}
	}
	writeLinuxProviderDiagnostic(record)
}

func writeLinuxProviderDiagnostic(record map[string]any) {
	raw, _ := record["diagnostic"].(string)
	text := strings.TrimSpace(raw)
	if text == "" {
		return
	}
	originalBytes := linuxDiagnosticOriginalBytes(
		record["diagnostic_original_bytes"],
		len([]byte(raw)),
	)
	text, locallyTruncated := truncateLinuxDiagnostic(
		text,
		linuxProviderDiagnosticLimit,
	)
	truncated, _ := record["diagnostic_truncated"].(bool)
	truncated = truncated || locallyTruncated || originalBytes > len([]byte(raw))
	record["diagnostic"] = text
	record["diagnostic_original_bytes"] = originalBytes
	if truncated {
		record["diagnostic_truncated"] = true
	}
	requestID := linuxDiagnosticLabel(record["request_id"])
	operation := linuxDiagnosticLabel(record["operation"])
	stage := linuxDiagnosticLabel(record["stage"])
	record["request_id"] = requestID
	record["operation"] = operation
	record["stage"] = stage
	truncation := ""
	if truncated {
		truncation = fmt.Sprintf(
			" truncated=true original_bytes=%d",
			originalBytes,
		)
	}
	fmt.Fprintf(
		os.Stderr,
		"[computer-use linux-provider request=%s operation=%s stage=%s%s]\n%s\n",
		requestID,
		operation,
		stage,
		truncation,
		text,
	)

	path := core.TracePath()
	if path == "" {
		return
	}
	record["at"] = time.Now().Format(time.RFC3339Nano)
	record["kind"] = "linux_provider_diagnostic"
	line, err := json.Marshal(record)
	if err != nil {
		return
	}
	if dir := filepath.Dir(path); dir != "" && dir != "." {
		if os.MkdirAll(dir, 0o755) != nil {
			return
		}
	}
	file, err := os.OpenFile(path, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o644)
	if err != nil {
		return
	}
	defer file.Close()
	_, _ = file.Write(append(line, '\n'))
}

func linuxDiagnosticOriginalBytes(value any, fallback int) int {
	switch typed := value.(type) {
	case int:
		if typed >= 0 {
			return typed
		}
	case int64:
		if typed >= 0 {
			return int(typed)
		}
	case float64:
		if typed >= 0 {
			return int(typed)
		}
	}
	return fallback
}

func linuxDiagnosticLabel(value any) string {
	if value == nil {
		return "unknown"
	}
	text := strings.TrimSpace(fmt.Sprint(value))
	if text == "" {
		return "unknown"
	}
	text, _ = truncateLinuxDiagnostic(text, linuxProviderDiagnosticLabelLimit)
	return text
}

func truncateLinuxDiagnostic(text string, limit int) (string, bool) {
	if limit <= 0 {
		return "", text != ""
	}
	data := []byte(text)
	if len(data) <= limit {
		return text, false
	}
	data = data[:limit]
	for len(data) > 0 && !utf8.Valid(data) {
		data = data[:len(data)-1]
	}
	return string(data), true
}

func linuxRuntimeEnvironment(base []string) []string {
	uid := os.Getuid()
	return linuxRuntimeEnvironmentFrom(base, uid, desktopProcessEnvironments(uid))
}

func linuxRuntimeEnvironmentFrom(base []string, uid int, processEnvs []map[string]string) []string {
	env := envSliceToMap(base)
	runtimeDir := chooseRuntimeDir(env, processEnvs, uid)
	if runtimeDir != "" {
		env["XDG_RUNTIME_DIR"] = runtimeDir
	}

	if value := sessionBusAddress(env["DBUS_SESSION_BUS_ADDRESS"], runtimeDir, processEnvs); value != "" {
		env["DBUS_SESSION_BUS_ADDRESS"] = value
	}
	if value := waylandDisplay(env["WAYLAND_DISPLAY"], runtimeDir, processEnvs); value != "" {
		env["WAYLAND_DISPLAY"] = value
	}

	for _, key := range []string{
		"DISPLAY",
		"XAUTHORITY",
		"XDG_CURRENT_DESKTOP",
		"XDG_SESSION_DESKTOP",
		"XDG_SESSION_TYPE",
		"DESKTOP_SESSION",
		"GDK_BACKEND",
		"QT_QPA_PLATFORMTHEME",
		"AT_SPI_BUS_ADDRESS",
	} {
		if strings.TrimSpace(env[key]) == "" {
			if value := firstSessionValue(processEnvs, key); value != "" {
				env[key] = value
			}
		}
	}

	return envMapToSlice(base, env)
}

func chooseRuntimeDir(env map[string]string, processEnvs []map[string]string, uid int) string {
	candidates := []string{env["XDG_RUNTIME_DIR"]}
	for _, processEnv := range processEnvs {
		candidates = append(candidates, processEnv["XDG_RUNTIME_DIR"])
	}
	candidates = append(candidates, fmt.Sprintf("/run/user/%d", uid))

	seen := map[string]bool{}
	for _, candidate := range candidates {
		candidate = strings.TrimSpace(candidate)
		if candidate == "" {
			continue
		}
		candidate = filepath.Clean(candidate)
		if seen[candidate] {
			continue
		}
		seen[candidate] = true
		if validRuntimeDir(candidate, uid) {
			return candidate
		}
	}
	return ""
}

func sessionBusAddress(current, runtimeDir string, processEnvs []map[string]string) string {
	current = strings.TrimSpace(current)
	if runtimeDir != "" {
		busPath := filepath.Join(runtimeDir, "bus")
		if isSocket(busPath) && shouldUseRuntimeBus(current, runtimeDir) {
			return "unix:path=" + busPath
		}
	}
	if current != "" {
		return current
	}
	for _, processEnv := range processEnvs {
		value := strings.TrimSpace(processEnv["DBUS_SESSION_BUS_ADDRESS"])
		if value == "" {
			continue
		}
		if runtimeDir != "" {
			busPath := filepath.Join(runtimeDir, "bus")
			if isSocket(busPath) && strings.Contains(value, busPath) {
				return "unix:path=" + busPath
			}
		}
		return value
	}
	if runtimeDir != "" {
		busPath := filepath.Join(runtimeDir, "bus")
		if isSocket(busPath) {
			return "unix:path=" + busPath
		}
	}
	return ""
}

func shouldUseRuntimeBus(current, runtimeDir string) bool {
	current = strings.TrimSpace(current)
	if current == "" {
		return true
	}
	busPath := filepath.Join(runtimeDir, "bus")
	if strings.Contains(current, busPath) {
		return true
	}
	return strings.Contains(current, "/run/user/") && !strings.Contains(current, runtimeDir)
}

func waylandDisplay(current, runtimeDir string, processEnvs []map[string]string) string {
	if value := normalizeWaylandDisplay(current, runtimeDir); value != "" {
		return value
	}
	for _, processEnv := range processEnvs {
		if value := normalizeWaylandDisplay(processEnv["WAYLAND_DISPLAY"], runtimeDir); value != "" {
			return value
		}
	}
	if runtimeDir == "" {
		return ""
	}
	return firstWaylandSocket(runtimeDir)
}

func normalizeWaylandDisplay(value, runtimeDir string) string {
	value = strings.TrimSpace(value)
	if value == "" {
		return ""
	}
	if runtimeDir == "" {
		return value
	}
	if filepath.IsAbs(value) {
		if isSocket(value) {
			return value
		}
		return ""
	}
	if isSocket(filepath.Join(runtimeDir, value)) {
		return value
	}
	return ""
}

func firstWaylandSocket(runtimeDir string) string {
	for _, name := range []string{"wayland-0", "wayland-1"} {
		if isSocket(filepath.Join(runtimeDir, name)) {
			return name
		}
	}
	matches, err := filepath.Glob(filepath.Join(runtimeDir, "wayland-*"))
	if err != nil {
		return ""
	}
	sort.Strings(matches)
	for _, match := range matches {
		if strings.HasSuffix(match, ".lock") {
			continue
		}
		if isSocket(match) {
			return filepath.Base(match)
		}
	}
	return ""
}

func firstSessionValue(processEnvs []map[string]string, key string) string {
	for _, processEnv := range processEnvs {
		if value := strings.TrimSpace(processEnv[key]); value != "" {
			return value
		}
	}
	return ""
}

type rankedProcessEnv struct {
	env  map[string]string
	rank int
	pid  int
}

func desktopProcessEnvironments(uid int) []map[string]string {
	entries, err := os.ReadDir("/proc")
	if err != nil {
		return nil
	}

	var candidates []rankedProcessEnv
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		pid, err := strconv.Atoi(entry.Name())
		if err != nil {
			continue
		}
		procDir := filepath.Join("/proc", entry.Name())
		if !pathOwnedByUID(procDir, uid) {
			continue
		}
		rank := desktopProcessRank(processSearchText(procDir))
		if rank == 0 {
			continue
		}
		processEnv := readProcEnviron(procDir)
		if !hasSessionEnvSignal(processEnv) {
			continue
		}
		candidates = append(candidates, rankedProcessEnv{
			env:  processEnv,
			rank: rank + sessionEnvRank(processEnv),
			pid:  pid,
		})
	}

	sort.SliceStable(candidates, func(i, j int) bool {
		if candidates[i].rank != candidates[j].rank {
			return candidates[i].rank > candidates[j].rank
		}
		return candidates[i].pid < candidates[j].pid
	})

	results := make([]map[string]string, 0, len(candidates))
	for _, candidate := range candidates {
		results = append(results, candidate.env)
	}
	return results
}

func processSearchText(procDir string) string {
	var parts []string
	if data, err := os.ReadFile(filepath.Join(procDir, "comm")); err == nil {
		parts = append(parts, string(bytes.TrimSpace(data)))
	}
	if data, err := os.ReadFile(filepath.Join(procDir, "cmdline")); err == nil {
		data = bytes.Trim(data, "\x00")
		parts = append(parts, strings.ReplaceAll(string(data), "\x00", " "))
	}
	return strings.ToLower(strings.Join(parts, " "))
}

func desktopProcessRank(text string) int {
	patterns := []struct {
		needle string
		rank   int
	}{
		{"gnome-session", 100},
		{"gnome-shell", 95},
		{"plasmashell", 95},
		{"kwin_wayland", 95},
		{"kwin_x11", 95},
		{"startplasma", 95},
		{"cinnamon-session", 95},
		{"mate-session", 95},
		{"xfce4-session", 95},
		{"lxqt-session", 95},
		{"sway", 95},
		{"wayfire", 95},
		{"xorg", 80},
		{"xwayland", 75},
		{"gnome-terminal-server", 65},
		{"ptyxis", 65},
		{"kgx", 65},
		{"konsole", 65},
		{"xfce4-terminal", 65},
		{"alacritty", 65},
		{"wezterm", 65},
		{"kitty", 65},
		{"tilix", 65},
		{"codex", 50},
		{"dbus-daemon", 45},
		{"systemd --user", 40},
	}

	rank := 0
	for _, pattern := range patterns {
		if strings.Contains(text, pattern.needle) && pattern.rank > rank {
			rank = pattern.rank
		}
	}
	return rank
}

func sessionEnvRank(env map[string]string) int {
	rank := 0
	for _, key := range []string{"XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS"} {
		if strings.TrimSpace(env[key]) != "" {
			rank += 20
		}
	}
	for _, key := range []string{"DISPLAY", "WAYLAND_DISPLAY"} {
		if strings.TrimSpace(env[key]) != "" {
			rank += 10
		}
	}
	if strings.TrimSpace(env["XAUTHORITY"]) != "" {
		rank += 5
	}
	return rank
}

func hasSessionEnvSignal(env map[string]string) bool {
	for _, key := range []string{
		"XDG_RUNTIME_DIR",
		"DBUS_SESSION_BUS_ADDRESS",
		"DISPLAY",
		"WAYLAND_DISPLAY",
		"XAUTHORITY",
		"AT_SPI_BUS_ADDRESS",
	} {
		if strings.TrimSpace(env[key]) != "" {
			return true
		}
	}
	return false
}

func readProcEnviron(procDir string) map[string]string {
	data, err := os.ReadFile(filepath.Join(procDir, "environ"))
	if err != nil {
		return nil
	}
	return parseNullEnv(data)
}

func parseNullEnv(data []byte) map[string]string {
	env := map[string]string{}
	for _, entry := range bytes.Split(data, []byte{0}) {
		if len(entry) == 0 {
			continue
		}
		key, value, ok := strings.Cut(string(entry), "=")
		if ok && key != "" {
			env[key] = value
		}
	}
	return env
}

func envSliceToMap(items []string) map[string]string {
	env := map[string]string{}
	for _, item := range items {
		key, value, ok := strings.Cut(item, "=")
		if ok && key != "" {
			env[key] = value
		}
	}
	return env
}

func envMapToSlice(base []string, env map[string]string) []string {
	items := make([]string, 0, len(env))
	seen := map[string]bool{}
	for _, item := range base {
		key, _, ok := strings.Cut(item, "=")
		if !ok || key == "" {
			items = append(items, item)
			continue
		}
		if value, ok := env[key]; ok {
			items = append(items, key+"="+value)
			seen[key] = true
		}
	}

	var added []string
	for key := range env {
		if !seen[key] {
			added = append(added, key)
		}
	}
	sort.Strings(added)
	for _, key := range added {
		items = append(items, key+"="+env[key])
	}
	return items
}

func validRuntimeDir(path string, uid int) bool {
	info, err := os.Stat(path)
	if err != nil || !info.IsDir() {
		return false
	}
	return FileOwnedByUID(info, uid)
}

func pathOwnedByUID(path string, uid int) bool {
	info, err := os.Stat(path)
	if err != nil {
		return false
	}
	return FileOwnedByUID(info, uid)
}

func isSocket(path string) bool {
	info, err := os.Stat(path)
	return err == nil && info.Mode()&os.ModeSocket != 0
}
