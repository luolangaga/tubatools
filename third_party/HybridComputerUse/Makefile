SHELL := /bin/bash
VERSION := $(shell cat VERSION)

.PHONY: build-local build-macos build-windows test test-transports smoke

build-local:
	mkdir -p artifacts
	cd computer-use && CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -trimpath \
		-ldflags "-s -w -X main.version=$(VERSION)" \
		-o ../artifacts/computer-use ./cmd/computer-use

build-macos:
	./computer-use/native/macos/build.sh "$(CURDIR)/artifacts/macos"

build-windows:
	pwsh -NoProfile -File computer-use/native/windows/build.ps1 -OutputDirectory "$(CURDIR)/artifacts/windows"

test-transports:
	cd computer-use && go test -tags 'darwin_sidecar_test windows_sidecar_test' ./...

test:
	cd computer-use && test -z "$$(gofmt -l .)" && go vet ./... && go test ./...
	cd computer-use && python3 runtime_test.py
	python3 -m pytest -q tests
	python3 -m unittest discover -s scripts/images/osworld -p 'test_*.py'

smoke:
	python3 scripts/smoke-mcp.py artifacts/computer-use
