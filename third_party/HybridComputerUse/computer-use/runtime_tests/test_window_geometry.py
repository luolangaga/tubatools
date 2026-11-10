"""Missing provider frames must retain exact native window ownership."""

from runtime_test_support import *  # noqa: F401,F403
from native.linux.provider import x11_window_inventory


class MissingWindowGeometryTests(AtspiPatchedTestCase):
    def setUp(self):
        super().setUp()
        self.window = _TreeNode(role="frame", name="Document")
        self.app = _TreeNode(role="application", kids=(self.window,))
        self.app.get_process_id = lambda: 41
        self.native = {"id": "0x11", "title": "Document",
                       "x": 300, "y": 120, "width": 800, "height": 600}
        self.extents = self.patch("extents", return_value=None)
        self.patch("process_generation", return_value="process-one")
        self.handle = runtime.window_handle_for_pid(41, "0x11")
        self.bind_windows = runtime.strong_window_instance_handle_inventory
        self.binding = self.patch("strong_window_instance_handle_inventory",
                                  return_value=runtime.InventoryResult([(0, self.handle)]))
        self.native_read = self.patch("x11_window_inventory_for_pid",
                                      return_value=runtime.InventoryResult([self.native]))
        x11_window_inventory._x11_window_inventory_cache.clear()
        self.addCleanup(x11_window_inventory._x11_window_inventory_cache.clear)

    def patch(self, name, **kwargs):
        patcher = mock.patch.object(runtime, name, **kwargs)
        result = patcher.start()
        self.addCleanup(patcher.stop)
        return result

    def read(self):
        return runtime.window_bounds(self.app, 0, self.window)

    def test_missing_frame_uses_exact_bound_native_geometry(self):
        self.assertEqual(runtime.frame(300, 120, 800, 600), self.read())

    def test_provider_geometry_does_not_need_native_recovery(self):
        bounds = runtime.frame(5, 8, 100, 200)
        self.extents.return_value = bounds
        self.assertEqual(bounds, self.read())
        self.binding.assert_not_called()
        self.native_read.assert_not_called()

    def test_ambiguous_binding_cannot_guess_from_title(self):
        self.binding.return_value = runtime.InventoryResult([])
        self.assertIsNone(self.read())

    def test_two_indistinguishable_ax_roots_cannot_share_native_geometry(self):
        sibling = _TreeNode(role="frame", name="Document")
        self.app.children = [self.window, sibling]
        self.binding.side_effect = self.bind_windows
        self.assertIsNone(self.read())

    def test_native_geometry_never_comes_from_a_sibling_window(self):
        self.native_read.return_value = runtime.InventoryResult([
            dict(self.native, id="0x22", x=1000), self.native])
        self.assertEqual(300, self.read()["x"])
        self.native_read.return_value = runtime.InventoryResult([
            dict(self.native, id="0x22", x=1000)])
        self.assertIsNone(self.read())

    def test_missing_or_replaced_native_window_has_no_geometry(self):
        for windows in ([], [dict(self.native, id="0x22")]):
            with self.subTest(windows=windows):
                self.native_read.return_value = runtime.InventoryResult(windows)
                self.assertIsNone(self.read())

    def test_process_generation_change_does_not_reuse_geometry(self):
        self.patch("process_generation", return_value="process-two")
        self.assertIsNone(self.read())

    def test_duplicate_native_identity_has_no_geometry(self):
        self.native_read.return_value = runtime.InventoryResult([self.native, self.native])
        self.assertIsNone(self.read())

    def test_incomplete_identity_or_geometry_fails_before_input(self):
        for source in (self.binding, self.native_read):
            with self.subTest(source=source):
                original = source.return_value
                source.return_value = runtime.InventoryResult(complete=False)
                x11_window_inventory._x11_window_inventory_cache.clear()
                with self.assertRaises(runtime.OperationFailure) as raised:
                    self.read()
                self.assertEqual("observation_incomplete", raised.exception.failure_code)
                self.assertFalse(raised.exception.input_events_emitted)
                source.return_value = original

    def test_invalid_native_geometry_is_not_accepted(self):
        for values in ({"width": 0}, {"height": -1}, {"x": None},
                       {"width": runtime.MAX_SANE_EXTENT + 1}):
            with self.subTest(values=values):
                self.native_read.return_value = runtime.InventoryResult([
                    dict(self.native, **values)])
                self.assertIsNone(self.read())

