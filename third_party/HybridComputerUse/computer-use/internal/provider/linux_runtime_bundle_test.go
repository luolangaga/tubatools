//go:build linux

package provider

import (
	"os"
	"path/filepath"
	"sort"
	"strings"
	"testing"
)

func TestLinuxRuntimeBundleIsComplete(t *testing.T) {
	bundle := linuxRuntimeBundle()
	wantPaths := []string{
		"native/__init__.py",
		"native/linux/__init__.py",
		"native/linux/provider/__init__.py",
		"native/linux/provider/a11y_enable.py",
		"native/linux/provider/action_evidence.py",
		"native/linux/provider/actionability.py",
		"native/linux/provider/app_resolver.py",
		"native/linux/provider/atspi_inventory.py",
		"native/linux/provider/ax_actions.py",
		"native/linux/provider/ax_dispatch.py",
		"native/linux/provider/ax_dropdown.py",
		"native/linux/provider/ax_input_delivery.py",
		"native/linux/provider/ax_node_properties.py",
		"native/linux/provider/ax_records.py",
		"native/linux/provider/ax_select.py",
		"native/linux/provider/ax_snapshot.py",
		"native/linux/provider/ax_snapshot_diagnostics.py",
		"native/linux/provider/ax_target_notes.py",
		"native/linux/provider/ax_transition_evidence.py",
		"native/linux/provider/ax_virtual_table.py",
		"native/linux/provider/desktop_layout.py",
		"native/linux/provider/element_resolver.py",
		"native/linux/provider/find_walk.py",
		"native/linux/provider/focus_probe.py",
		"native/linux/provider/foundation.py",
		"native/linux/provider/gui_capture.py",
		"native/linux/provider/gui_dispatch.py",
		"native/linux/provider/gui_input.py",
		"native/linux/provider/input_method.py",
		"native/linux/provider/input_primitives.py",
		"native/linux/provider/native_windows.py",
		"native/linux/provider/node_facts.py",
		"native/linux/provider/node_geometry.py",
		"native/linux/provider/node_visibility.py",
		"native/linux/provider/phase_timing.py",
		"native/linux/provider/primitives.py",
		"native/linux/provider/recovery.py",
		"native/linux/provider/ref_continuity.py",
		"native/linux/provider/response_builder.py",
		"native/linux/provider/semantic_scan.py",
		"native/linux/provider/semantic_window_candidates.py",
		"native/linux/provider/snapshot.py",
		"native/linux/provider/snapshot_children.py",
		"native/linux/provider/tree_reuse.py",
		"native/linux/provider/tree_walk.py",
		"native/linux/provider/ui_events.py",
		"native/linux/provider/window_diagnostics.py",
		"native/linux/provider/window_resolver.py",
		"native/linux/provider/window_scale.py",
		"native/linux/provider/wire.py",
		"native/linux/provider/wire_request.py",
		"native/linux/provider/x11_window_inventory.py",
		"native/linux/provider/x11_windows.py",
		"runtime.py",
	}
	gotPaths := make([]string, 0, len(bundle))
	for path := range bundle {
		gotPaths = append(gotPaths, path)
	}
	sort.Strings(gotPaths)
	if strings.Join(gotPaths, "\n") != strings.Join(wantPaths, "\n") {
		t.Fatalf("Linux runtime bundle paths differ:\ngot:\n%s\nwant:\n%s",
			strings.Join(gotPaths, "\n"), strings.Join(wantPaths, "\n"))
	}
}

func TestLinuxRuntimeBundleIncludesEveryProviderModule(t *testing.T) {
	bundle := linuxRuntimeBundle()
	modules, err := filepath.Glob(filepath.Join("..", "..", "native", "linux", "provider", "*.py"))
	if err != nil || len(modules) == 0 {
		t.Fatalf("list provider modules: %v (%d found)", err, len(modules))
	}
	for _, module := range modules {
		path := "native/linux/provider/" + filepath.Base(module)
		if _, ok := bundle[path]; !ok {
			t.Errorf("%s is not in the Linux runtime bundle; the provider cannot import it", path)
		}
	}
}

func TestLinuxRuntimeEntrypointNamesEveryFragment(t *testing.T) {
	bundle := linuxRuntimeBundle()
	for _, name := range []string{
		"native.linux.provider.wire",
		"import main",
	} {
		if !strings.Contains(bundle["runtime.py"], name) {
			t.Fatalf("runtime.py does not reference %s", name)
		}
	}
	for _, forbidden := range []string{"ProviderFacade", "install_facade"} {
		if strings.Contains(bundle["runtime.py"], forbidden) ||
			strings.Contains(bundle["native/linux/provider/__init__.py"], forbidden) {
			t.Fatalf("Linux runtime still references retired facade %q", forbidden)
		}
	}
}

func TestLinuxBackendUsesProviderWireWithoutLegacyPlatformRoundTrip(
	t *testing.T,
) {
	for _, path := range []string{
		"runtime_backend_linux.go",
		"linux_runtime_executor.go",
	} {
		data, err := os.ReadFile(path)
		if err != nil {
			t.Fatal(err)
		}
		source := string(data)
		for _, forbidden := range []string{
			"legacyRequestFromProvider",
			"legacyResponseFromProvider",
			"runLinuxRuntime",
		} {
			if strings.Contains(source, forbidden) {
				t.Fatalf(
					"%s still uses retired Linux platform adapter %q",
					path,
					forbidden,
				)
			}
		}
	}
}
