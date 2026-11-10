//go:build darwin || darwin_sidecar_test

package provider

import (
	"bytes"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"testing"
)

// These tests exercise packaging orchestration, not the Swift compiler or TCC.
func TestDarwinBuildPackagesVersionedAdjacentApp(t *testing.T) {
	for _, driver := range []string{"swiftc", "swiftpm"} {
		for _, arch := range []string{"amd64", "arm64"} {
			t.Run(driver+"/"+arch, func(t *testing.T) {
				root := t.TempDir()
				repo := filepath.Join(root, "source with spaces")
				runtime := filepath.Join(repo, "computer-use", "native", "macos")
				bin := filepath.Join(root, "bin")
				for _, path := range []string{runtime, bin} {
					if err := os.MkdirAll(path, 0o700); err != nil {
						t.Fatal(err)
					}
				}
				for _, name := range []string{"build.sh", "ProviderInfo.plist"} {
					data, err := os.ReadFile(filepath.Join("..", "..", "native", "macos", name))
					if err != nil {
						t.Fatal(err)
					}
					if err := os.WriteFile(filepath.Join(runtime, name), data, 0o700); err != nil {
						t.Fatal(err)
					}
				}
				if err := os.WriteFile(filepath.Join(repo, "VERSION"), []byte("2.3.4\n"), 0o600); err != nil {
					t.Fatal(err)
				}
				notices := make(map[string][]byte)
				for source, destination := range map[string]string{
					"LICENSE":                 "LICENSE",
					"THIRD_PARTY_NOTICES.md":  "THIRD_PARTY_NOTICES.md",
					"licenses/Apache-2.0.txt": "licenses/Apache-2.0.txt",
					"computer-use/vendor/golang.org/x/text/LICENSE": "licenses/golang.org/x/text/LICENSE",
					"computer-use/vendor/golang.org/x/text/PATENTS": "licenses/golang.org/x/text/PATENTS",
				} {
					data, err := os.ReadFile(filepath.Join("..", "..", "..", source))
					if err != nil {
						t.Fatal(err)
					}
					if len(bytes.TrimSpace(data)) == 0 {
						t.Fatalf("empty source notice %s", source)
					}
					fixture := filepath.Join(repo, source)
					if err := os.MkdirAll(filepath.Dir(fixture), 0o700); err != nil {
						t.Fatal(err)
					}
					if err := os.WriteFile(fixture, data, 0o600); err != nil {
						t.Fatal(err)
					}
					notices[destination] = data
				}
				mock := filepath.Join(bin, "mock")
				if err := os.WriteFile(mock, []byte(darwinBuildToolMock), 0o700); err != nil {
					t.Fatal(err)
				}
				for _, name := range []string{"swift", "swiftc", "go", "codesign", "xcrun", "plutil", "install_name_tool"} {
					if err := os.Symlink(mock, filepath.Join(bin, name)); err != nil {
						t.Fatal(err)
					}
				}
				log := filepath.Join(root, "tools.log")
				t.Setenv("PATH", bin+string(os.PathListSeparator)+os.Getenv("PATH"))
				t.Setenv("BUILD_TEST_LOG", log)
				t.Setenv("BUILD_TEST_BIN", filepath.Join(root, "swiftpm output"))
				t.Setenv("COMPUTER_USE_MACOS_BUILD_DRIVER", driver)
				t.Setenv("COMPUTER_USE_SWIFTC", filepath.Join(bin, "swiftc"))
				t.Setenv("COMPUTER_USE_MACOS_SDK", "/test/sdk")
				t.Setenv("COMPUTER_USE_BUILD_VERSION", "must-not-override-VERSION")
				t.Setenv("GOARCH", arch)
				if arch == "arm64" {
					t.Setenv("GOARCH", "")
				}
				output := filepath.Join(root, "relative output")
				command := exec.Command("bash", filepath.Join(runtime, "build.sh"), "relative output")
				if driver == "swiftpm" && arch == "arm64" {
					command = exec.Command("bash", filepath.Join(runtime, "build.sh"))
					output = filepath.Join(repo, "artifacts", "macos")
				}
				command.Dir = root
				if output, err := command.CombinedOutput(); err != nil {
					t.Fatalf("packaging failed: %v\n%s", err, output)
				}
				for _, name := range []string{
					"computer-use",
					"ComputerUseProvider.app/Contents/MacOS/computer-use-macos-provider",
					"ComputerUseProvider.app/Contents/Frameworks/libComputerUseProviderKit.dylib",
					"ComputerUseProvider.app/Contents/Info.plist",
				} {
					info, err := os.Stat(filepath.Join(output, name))
					if err != nil || !info.Mode().IsRegular() {
						t.Fatalf("missing packaged file %s: %v", name, err)
					}
				}
				for name, want := range notices {
					got, err := os.ReadFile(filepath.Join(output, name))
					if err != nil {
						t.Fatalf("missing packaged notice %s: %v", name, err)
					}
					if !bytes.Equal(got, want) {
						t.Errorf("packaged notice %s did not preserve the full source text", name)
					}
				}
				data, err := os.ReadFile(log)
				if err != nil {
					t.Fatal(err)
				}
				calls := string(data)
				swiftArch := arch
				if arch == "amd64" {
					swiftArch = "x86_64"
				}
				for _, want := range []string{
					"main.version=2.3.4", swiftArch + "-apple-macosx14.0",
					"plutil -replace CFBundleShortVersionString -string 2.3.4",
					"plutil -replace CFBundleVersion -string 2.3.4",
					"codesign --verify --deep --strict " + output + "/ComputerUseProvider.app",
					"go-env darwin " + arch + " 0 " + filepath.Join(repo, "computer-use"),
				} {
					if !strings.Contains(calls, want) {
						t.Errorf("missing %q in tool calls:\n%s", want, calls)
					}
				}
				for _, forbidden := range []string{"SHA256", "shasum", "legacyA11y", "must-not-override-VERSION"} {
					if strings.Contains(calls, forbidden) {
						t.Errorf("unexpected build argument %q", forbidden)
					}
				}
				if driver == "swiftpm" && !strings.Contains(calls, "install_name_tool -add_rpath @executable_path/../Frameworks") {
					t.Fatal("SwiftPM executable cannot locate its bundled library")
				}
			})
		}
	}
}

const darwinBuildToolMock = `#!/usr/bin/env bash
set -euo pipefail
name="$(basename "$0")"
printf '%s %s\n' "$name" "$*" >> "$BUILD_TEST_LOG"
case "$name" in
  xcrun) printf '/test/sdk\n' ;;
  swift)
    mkdir -p "$BUILD_TEST_BIN"
    printf 'provider\n' > "$BUILD_TEST_BIN/computer-use-macos-provider"
    printf 'library\n' > "$BUILD_TEST_BIN/libComputerUseProviderKit.dylib"
    if [[ " $* " == *' --show-bin-path '* ]]; then
      printf '%s\n' "$BUILD_TEST_BIN"
    fi
    ;;
  swiftc|go)
    if [[ "$name" == go ]]; then
      if [[ "$1" == env && "$2" == GOHOSTARCH ]]; then
        printf 'arm64\n'
        exit 0
      fi
      printf 'go-env %s %s %s %s\n' "$GOOS" "$GOARCH" "$CGO_ENABLED" "$PWD" >> "$BUILD_TEST_LOG"
    fi
    while [[ $# -gt 0 ]]; do
      if [[ "$1" == -o ]]; then
        shift
        mkdir -p "$(dirname "$1")"
        printf 'mock compiled output\n' > "$1"
        break
      fi
      shift
    done
    ;;
esac
`
