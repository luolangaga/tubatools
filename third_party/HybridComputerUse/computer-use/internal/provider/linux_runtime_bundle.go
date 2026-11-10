//go:build linux

package provider

import runtimebundle "github.com/microsoft/Hybrid-Computer-Use/computer-use"

var linuxProviderPaths = []string{
	"native/linux/provider/foundation.py",
	"native/linux/provider/node_geometry.py",
	"native/linux/provider/node_visibility.py",
	"native/linux/provider/a11y_enable.py",
	"native/linux/provider/node_facts.py",
	"native/linux/provider/x11_windows.py",
	"native/linux/provider/desktop_layout.py",
	"native/linux/provider/x11_window_inventory.py",
	"native/linux/provider/window_scale.py",
	"native/linux/provider/atspi_inventory.py",
	"native/linux/provider/ax_node_properties.py",
	"native/linux/provider/ax_records.py",
	"native/linux/provider/ax_snapshot.py",
	"native/linux/provider/snapshot_children.py",
	"native/linux/provider/focus_probe.py",
	"native/linux/provider/ax_virtual_table.py",
	"native/linux/provider/find_walk.py",
	"native/linux/provider/action_evidence.py",
	"native/linux/provider/phase_timing.py",
	"native/linux/provider/app_resolver.py",
	"native/linux/provider/window_resolver.py",
	"native/linux/provider/native_windows.py",
	"native/linux/provider/window_diagnostics.py",
	"native/linux/provider/ref_continuity.py",
	"native/linux/provider/recovery.py",
	"native/linux/provider/element_resolver.py",
	"native/linux/provider/semantic_window_candidates.py",
	"native/linux/provider/semantic_scan.py",
	"native/linux/provider/gui_capture.py",
	"native/linux/provider/gui_dispatch.py",
	"native/linux/provider/snapshot.py",
	"native/linux/provider/actionability.py",
	"native/linux/provider/gui_input.py",
	"native/linux/provider/input_primitives.py",
	"native/linux/provider/input_method.py",
	"native/linux/provider/ax_actions.py",
	"native/linux/provider/ax_select.py",
	"native/linux/provider/ax_dropdown.py",
	"native/linux/provider/ax_dispatch.py",
	"native/linux/provider/ax_input_delivery.py",
	"native/linux/provider/ax_snapshot_diagnostics.py",
	"native/linux/provider/ax_target_notes.py",
	"native/linux/provider/ax_transition_evidence.py",
	"native/linux/provider/primitives.py",
	"native/linux/provider/tree_walk.py",
	"native/linux/provider/tree_reuse.py",
	"native/linux/provider/ui_events.py",
	"native/linux/provider/response_builder.py",
	"native/linux/provider/wire.py",
	"native/linux/provider/wire_request.py",
}

func linuxRuntimeBundle() map[string]string {
	paths := append([]string{
		"runtime.py",
		"native/__init__.py",
		"native/linux/__init__.py",
		"native/linux/provider/__init__.py",
	}, linuxProviderPaths...)
	bundle := make(map[string]string, len(paths))
	for _, path := range paths {
		data, err := runtimebundle.LinuxRuntimeFS.ReadFile(path)
		if err != nil {
			panic(err)
		}
		bundle[path] = string(data)
	}
	return bundle
}
