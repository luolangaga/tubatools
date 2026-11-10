"""Post-action snapshots stay with the action's logical window."""

from runtime_test_support import *  # noqa: F401,F403


class PostActionWindowTests(AtspiPatchedTestCase):
    def setUp(self):
        super().setUp()
        self.control = _TreeNode(role="spin button", name="Zoom")
        self.control.value = FakeValueIface(1)
        self.a = _TreeNode(role="frame", name="Document A", w=100, h=100,
                           kids=(self.control,))
        self.b = _TreeNode(role="frame", name="Document B", x=200, w=200, h=100)
        self.app = _TreeNode(role="application", name="Editor", kids=(self.a, self.b))
        self.app.get_process_id = lambda: 41
        self.windows = runtime.InventoryResult([(0, self.a), (1, self.b)])
        self.handles = {0: "w2:" + "a" * 32, 1: "w2:" + "b" * 32}
        self.preferred = (1, self.b)
        self.captures = []
        self.patch("app_windows", side_effect=lambda app: self.windows)
        self.patch("resolve_app_identity", return_value=self.app)
        self.patch("x11_preferred_window", side_effect=lambda windows: self.preferred)
        self.patch("strong_window_instance_handle_inventory", side_effect=lambda *args:
                   runtime.InventoryResult(list(self.handles.items())))
        self.patch("process_generation", return_value="gen-41")
        self.patch("provider_bus_name", return_value=":1.41")
        self.patch("wait_for_ui_to_settle", return_value=None)
        self.patch("focus_and_selection", return_value=("", ""))
        self.patch("top_level_window_inventory", return_value=[])
        self.patch("modal_diagnostic", return_value=[])
        self.patch("foreign_foreground_note", return_value=[])
        self.patch("x11_root_window_state", return_value={})
        self.patch("capture_window_pixels", side_effect=self.capture)
        self.pixel_diff = self.patch("persistent_pixel_change", return_value=None)

    def patch(self, name, **kwargs):
        patch = mock.patch.object(runtime, name, **kwargs)
        result = patch.start()
        self.addCleanup(patch.stop)
        return result

    def capture(self, bounds):
        self.captures.append(bounds)
        return object()

    def observe(self, handle=None):
        return ax_dispatch._observe_after_action(
            {"app": "Editor", "app_identity": {},
             "window_handle": handle or self.handles[0]},
            "resolve_and_act", {}, self.app, runtime.extents(self.a),
            None, [], {}, {}, {(0, "frame", "Document A"), (1, "frame", "Document B")},
            "before-pixels")

    def assert_window(self, result, index, window):
        self.assertEqual(result["snapshot"]["windowHandle"], self.handles[index])
        self.assertEqual(result["snapshot"]["windowTitle"], window.get_name())
        self.assertEqual(self.captures, [runtime.extents(window)] * 2)

    def test_background_value_write_observes_target_without_focusing(self):
        focus = self.patch("focus_target_element")
        ax_dispatch._perform_set_value({"value": "2"}, self.a, self.control,
                                       {"controlType": "spin button"}, [], {})
        self.assertEqual(self.control.value.current, 2)
        result = self.observe()
        self.assert_window(result, 0, self.a)
        self.assertFalse(result["externalDesktopTransition"])
        self.assertEqual(self.pixel_diff.call_args.args[0], "before-pixels")
        focus.assert_not_called()

    def test_dispatch_keeps_bound_window_across_two_background_writes(self):
        self.patch("resolve_operation_app", return_value=self.app)
        self.patch("_resolve_action_target", return_value=(
            self.control, {"controlType": "spin button"}, None,
            self.a, runtime.extents(self.a)))
        focus = self.patch("focus_target_element")
        for value in (2, 3):
            self.captures.clear()
            result = runtime.dispatch_provider_operation(
                {"app": "Editor", "app_identity": {},
                 "target": {"windowHandle": self.handles[0]},
                 "input": {"value": str(value)}},
                "resolve_and_act", "set_value")
            self.assertTrue(result["ok"])
            self.assertTrue(result["actionEvidence"]["readback_verified"])
            self.assertEqual(self.control.value.current, value)
            self.assertEqual(result["snapshot"]["windowHandle"], self.handles[0])
            self.assertEqual(self.captures, [runtime.extents(self.a)] * 3)
        self.assertEqual(self.control.value.set_calls, [2, 3])
        focus.assert_not_called()

    def test_missing_modal_geometry_does_not_reuse_original_region(self):
        self.b.states.update((STATE.MODAL, STATE.SHOWING, STATE.ACTIVE))
        extents = runtime.extents
        self.patch("extents", side_effect=lambda node: None if node is self.b else extents(node))
        self.patch("x11_window_inventory_for_pid", return_value=runtime.InventoryResult([]))
        result = self.observe()
        self.assertEqual(result["snapshot"]["windowHandle"], self.handles[1])
        self.assertEqual(self.captures, [None, None])

    def test_unreadable_native_geometry_does_not_reuse_original_region(self):
        self.b.states.update((STATE.MODAL, STATE.SHOWING, STATE.ACTIVE))
        extents = runtime.extents
        self.patch("extents", side_effect=lambda node: None if node is self.b else extents(node))
        self.patch("x11_window_inventory_for_pid", return_value=runtime.InventoryResult([], complete=False))
        with self.assertRaises(runtime.OperationFailure) as caught:
            self.observe()
        self.assertEqual(caught.exception.failure_code, "observation_incomplete")
        self.assertEqual(self.captures, [None])

    def test_foreground_change_between_capture_and_tree_does_not_reselect(self):
        selections = self.patch("select_window_by_handle", wraps=runtime.select_window_by_handle)
        original_capture = self.capture
        def capture(bounds):
            self.preferred = (1, self.b)
            return original_capture(bounds)
        self.patch("capture_window_pixels", side_effect=capture)
        self.preferred = (0, self.a)
        self.assert_window(self.observe(), 0, self.a)
        self.assertEqual(selections.call_count, 2)

    def test_new_visible_modal_is_observed(self):
        self.b.states.update((STATE.MODAL, STATE.SHOWING, STATE.ACTIVE))
        self.b.role = "dialog"
        self.assert_window(self.observe(), 1, self.b)
        self.assertIsNone(self.pixel_diff.call_args.args[0])

    def test_modal_without_showing_but_visible_is_observed(self):
        self.b.states.discard(STATE.SHOWING)
        self.b.states.update((STATE.MODAL, STATE.VISIBLE))
        self.assert_window(self.observe(), 1, self.b)

    def test_hidden_modal_does_not_steal_observation(self):
        self.b.states.difference_update((STATE.SHOWING, STATE.VISIBLE))
        self.b.states.add(STATE.MODAL)
        self.assert_window(self.observe(), 0, self.a)

    def test_disappeared_original_observes_remaining_window(self):
        original_handle = self.handles[0]
        self.windows = runtime.InventoryResult([(1, self.b)])
        del self.handles[0]
        self.assert_window(self.observe(original_handle), 1, self.b)
        self.assertIsNone(self.pixel_diff.call_args.args[0])

    def test_replacement_at_same_index_has_new_identity(self):
        original_handle = self.handles[0]
        self.handles[0] = "w2:" + "c" * 32
        self.preferred = (0, self.a)
        self.assert_window(self.observe(original_handle), 0, self.a)
        self.assertIsNone(self.pixel_diff.call_args.args[0])

    def test_incomplete_inventory_does_not_fall_back_to_sibling(self):
        self.windows = runtime.InventoryResult(list(self.windows), complete=False)
        with self.assertRaises(runtime.OperationFailure) as caught:
            self.observe()
        self.assertEqual(caught.exception.failure_code, "observation_incomplete")
        self.assertEqual(self.captures, [])

    def test_missing_all_windows_does_not_invent_a_snapshot(self):
        self.windows = runtime.InventoryResult([])
        with self.assertRaisesRegex(RuntimeError, "No top-level"):
            self.observe()
        self.assertEqual(self.captures, [])
