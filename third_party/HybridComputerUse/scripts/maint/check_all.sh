#!/usr/bin/env bash
# Run the HCU maintainability check list on the current tree.
#
# Usage: scripts/maint/check_all.sh [BASELINE_BINARY]
#
# With BASELINE_BINARY, the script also builds the current tree and compares
# MCP initialize/tools/list, help and schema bytes with that binary.
# Logs go to $LOG_DIR (default /tmp/hcu-maint-check); each step prints one
# summary line, and the log tail on failure. The script exits non-zero at the
# first failing step.
set -euo pipefail

repo=$(git rev-parse --show-toplevel)
export PATH="$HOME/.local/bin:$PATH"
log_dir=${LOG_DIR:-/tmp/hcu-maint-check}
mkdir -p "$log_dir"
osworld=${OSWORLD_V2_ROOT:-$repo/external/OSWorld-V2}
baseline_binary=${1:-}

step() {
	local name=$1
	shift
	local log="$log_dir/$name.log"
	if "$@" >"$log" 2>&1; then
		echo "ok   $name $(tail -1 "$log" | cut -c1-120)"
	else
		echo "FAIL $name (log $log)"
		tail -30 "$log"
		exit 1
	fi
}

gofmt_clean() {
	local files
	files=$(cd "$repo/computer-use" && find . -name '*.go' -not -path './vendor/*')
	# shellcheck disable=SC2086
	local unformatted
	unformatted=$(cd "$repo/computer-use" && gofmt -l $files)
	if [ -n "$unformatted" ]; then
		echo "$unformatted"
		return 1
	fi
	echo "gofmt clean"
}

cd "$repo/computer-use"
step gofmt gofmt_clean
# go vet type-checks every package and test of the target OS, so separate
# go build steps add nothing. The sidecar tags only add test transports, so
# the tagged run covers every test of the untagged one.
step go-vet go vet ./...
step go-vet-darwin env GOOS=darwin go vet ./...
step go-vet-windows env GOOS=windows go vet ./...
step go-test-sidecar go test -count=1 -tags 'darwin_sidecar_test windows_sidecar_test' ./...
step runtime-test /usr/bin/python3 runtime_test.py
cd "$repo"
step pytest env OSWORLD_V2_ROOT="$osworld" "$osworld/.venv/bin/python" -m pytest -q tests -p no:cacheprovider

if [ -n "$baseline_binary" ]; then
	candidate="$log_dir/computer-use.candidate"
	# Stamp the version like make build-local does, so a baseline built by
	# make reports the same serverInfo.version.
	(cd "$repo/computer-use" && CGO_ENABLED=0 go build -trimpath \
		-ldflags "-X main.version=$(cat "$repo/VERSION")" -o "$candidate" ./cmd/computer-use)
	step mcp-bytes python3 "$repo/scripts/maint/compare_mcp_bytes.py" "$baseline_binary" "$candidate"
fi
