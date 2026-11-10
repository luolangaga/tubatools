//go:build darwin || darwin_sidecar_test

package provider

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

type darwinProviderArtifact struct {
	bundlePath     string
	executablePath string
	libraryPath    string
	infoPlistPath  string
}

func darwinProviderStartupTimeout() time.Duration {
	timeout := darwinProviderRuntimeTimeout()
	if timeout > darwinProviderStartupLimit {
		return darwinProviderStartupLimit
	}
	return timeout
}

func darwinProviderExecutable() (darwinProviderArtifact, error) {
	if configured := strings.TrimSpace(
		os.Getenv("COMPUTER_USE_MACOS_PROVIDER"),
	); configured != "" {
		return darwinProviderArtifactFromPath(configured)
	}
	executable, err := os.Executable()
	if err != nil {
		return darwinProviderArtifact{}, err
	}
	return darwinProviderArtifactFromPath(filepath.Join(
		filepath.Dir(executable),
		darwinProviderAppName,
	))
}

func darwinProviderArtifactFromPath(
	path string,
) (darwinProviderArtifact, error) {
	cleanPath := filepath.Clean(path)
	if filepath.Ext(cleanPath) != ".app" {
		bundleRoot := filepath.Dir(filepath.Dir(filepath.Dir(cleanPath)))
		if filepath.Base(bundleRoot) != darwinProviderAppName ||
			filepath.Base(filepath.Dir(cleanPath)) != "MacOS" {
			return darwinProviderArtifact{}, fmt.Errorf(
				"macOS provider must be the %s app bundle",
				darwinProviderAppName,
			)
		}
		cleanPath = bundleRoot
	}
	if filepath.Base(cleanPath) != darwinProviderAppName {
		return darwinProviderArtifact{}, fmt.Errorf(
			"macOS provider app = %q, want %q",
			filepath.Base(cleanPath),
			darwinProviderAppName,
		)
	}
	return darwinProviderArtifact{
		bundlePath: cleanPath,
		executablePath: filepath.Join(
			cleanPath, "Contents", "MacOS", darwinProviderExecutableName,
		),
		libraryPath: filepath.Join(
			cleanPath, "Contents", "Frameworks", darwinProviderLibraryName,
		),
		infoPlistPath: filepath.Join(cleanPath, "Contents", "Info.plist"),
	}, nil
}

func verifyDarwinProvider(artifact darwinProviderArtifact) error {
	for _, item := range []struct {
		label string
		path  string
	}{
		{label: "executable", path: artifact.executablePath},
		{label: "library", path: artifact.libraryPath},
		{label: "Info.plist", path: artifact.infoPlistPath},
	} {
		info, err := os.Stat(item.path)
		if err != nil {
			return fmt.Errorf("stat macOS provider %s: %w", item.label, err)
		}
		if !info.Mode().IsRegular() {
			return fmt.Errorf("macOS provider %s must be a regular file", item.label)
		}
		if item.label == "executable" && info.Mode().Perm()&0o111 == 0 {
			return errors.New("macOS provider executable has no execute permission")
		}
	}
	return nil
}
