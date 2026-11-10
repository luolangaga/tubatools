#!/usr/bin/env python3
"""Windows of processes that export no accessibility tree."""

from runtime_test_support import *  # noqa: F401,F403


def _managed(*windows):
    return runtime.InventoryResult(list(windows), complete=True)


def _window(pid, window_id, title, x=0, y=0, width=800, height=600):
    return {"pid": pid, "id": window_id, "title": title, "x": x, "y": y,
            "width": width, "height": height}


class NativeOnlyWindowTests(AtspiPatchedTestCase):
    """A process that never registers on the bus still has managed windows.

    Without them in the listing, AX could not activate, close or wait for
    such a window by title (window_not_found, wait_timeout), although the
    window was on screen and GUI could operate it.
    """

    def setUp(self):
        super().setUp()
        for name, value in (
            ("x11_window_type", lambda window_id: (
                "DOCK" if window_id == "0x5" else "")),
            ("process_generation", lambda pid: "gen-{}".format(pid)),
            ("process_name", lambda pid, proc_root="/proc": "paint3d"),
        ):
            patcher = mock.patch.object(runtime, name, side_effect=value)
            patcher.start()
            self.addCleanup(patcher.stop)

    def test_only_on_screen_titled_application_windows_off_the_bus(self):
        windows = runtime.native_only_windows(
            [
                _window(30, "0x1", "Scene - Paint3D"),
                _window(21, "0x2", "Untitled - gedit"),
                _window(30, "0x3", ""),
                _window(30, "0x4", "Hidden", x=-900, y=-700),
                _window(30, "0x5", "Dock"),
                _window(30, "0x6", "Empty", width=0),
            ],
            accessible_coverage={21: {"handles": {
                runtime.window_handle_for_pid(21, "0x2")}, "complete": True}},
            active_window_id="0x1",
        )

        self.assertEqual(["Scene - Paint3D"], [w["title"] for w in windows])
        window = windows[0]
        self.assertEqual("paint3d", window["app"])
        self.assertTrue(window["active"])
        self.assertEqual(runtime.window_handle_for_pid(30, "0x1"), window["handle"])
        self.assertTrue(window["handle"].startswith("w2:"))

    def test_listing_gives_them_a_handle_and_no_tree_flag(self):
        window = _TreeNode(role="frame", name="Untitled - gedit")
        app = _TreeNode(role="application", name="gedit", kids=(window,))
        app.get_process_id = lambda: 21
        original_apps = runtime.iter_apps
        runtime.iter_apps = lambda: [app]
        self.addCleanup(setattr, runtime, "iter_apps", original_apps)
        managed = _managed(_window(21, "0x2", "Untitled - gedit"),
                           _window(30, "0x1", "Scene - Paint3D"))
        with mock.patch.object(runtime, "_x11_managed_windows",
                               return_value=managed), \
                mock.patch.object(runtime, "process_toolkit", return_value=""):
            inventory = runtime.list_apps_inventory()

        native = [app for app in inventory["apps"] if app["name"] == "paint3d"]
        self.assertEqual(1, len(native))
        listed = native[0]["windows"]
        self.assertEqual(["Scene - Paint3D"], [w["title"] for w in listed])
        self.assertTrue(listed[0]["noAccessibilityTree"])
        self.assertTrue(listed[0]["handle"].startswith("w2:"))
        self.assertTrue(listed[0]["inputTarget"])
        # The window itself says it has no tree; a toolkit without restart
        # advice adds no note.
        self.assertEqual([], inventory["notes"])

    def test_restart_advice_stays_for_toolkits_that_can_export_a_tree(self):
        with tempfile.TemporaryDirectory() as root:
            os.makedirs(os.path.join(root, "20"))
            with open(os.path.join(root, "20", "maps"), "w") as maps:
                maps.write("7f00-7f01 r-xp 0 08:01 1 /usr/lib/libQt5Core.so.5\n")
            notes = runtime.invisible_window_notes(
                [{"pid": 20, "title": "VLC media player"},
                 {"pid": 22, "title": "Scene - Paint3D"}],
                accessible_pids=set(), proc_root=root)

        self.assertEqual(1, len(notes))
        self.assertIn('"VLC media player" (pid 20, Qt)', notes[0])

    def test_registered_process_without_windows_still_lists_native_window(self):
        app = _TreeNode(role="application", name="paint3d", kids=())
        app.get_process_id = lambda: 30
        with mock.patch.object(runtime, "iter_apps", return_value=[app]), \
                mock.patch.object(runtime, "_x11_managed_windows", return_value=_managed(
                    _window(30, "0x1", "Scene - Paint3D"))), \
                mock.patch.object(runtime, "process_toolkit", return_value=""):
            inventory = runtime.list_apps_inventory()

        windows = [window for entry in inventory["apps"] for window in entry["windows"]]
        self.assertEqual(["Scene - Paint3D"], [window["title"] for window in windows])
        self.assertTrue(windows[0]["noAccessibilityTree"])
        self.assertEqual(runtime.window_handle_for_pid(30, "0x1"), windows[0]["handle"])

    def test_native_sibling_of_accessible_window_gets_its_own_handle(self):
        frame = _TreeNode(role="frame", name="Main")
        app = _TreeNode(role="application", name="paint3d", kids=(frame,))
        app.get_process_id = lambda: 30
        main_handle = runtime.window_handle_for_pid(30, "0x1")
        with mock.patch.object(runtime, "iter_apps", return_value=[app]), \
                mock.patch.object(runtime, "_x11_managed_windows", return_value=_managed(
                    _window(30, "0x1", "Main"), _window(30, "0x2", "Native dialog"))), \
                mock.patch.object(runtime, "strong_window_instance_handles", return_value={0: main_handle}), \
                mock.patch.object(runtime, "unmapped_window_indices", return_value=set()), \
                mock.patch.object(runtime, "process_toolkit", return_value=""):
            inventory = runtime.list_apps_inventory()

        windows = [window for entry in inventory["apps"] for window in entry["windows"]]
        self.assertEqual(["Main", "Native dialog"], [window["title"] for window in windows])
        self.assertEqual(2, len({window["handle"] for window in windows}))
        self.assertTrue(windows[1]["noAccessibilityTree"])
        # The listed native window is the only inventory entry for it; the
        # untracked-window note would repeat it with GUI-only advice.
        self.assertEqual([], [note for note in inventory["notes"] if "Native dialog" in note])

    def test_ambiguous_accessible_root_does_not_gain_a_second_native_alias(self):
        app = _TreeNode(role="application", name="paint3d", kids=(
            _TreeNode(role="frame", name="Untitled"),))
        app.get_process_id = lambda: 30
        with mock.patch.object(runtime, "iter_apps", return_value=[app]), \
                mock.patch.object(runtime, "_x11_managed_windows", return_value=_managed(
                    _window(30, "0x1", "Untitled"))), \
                mock.patch.object(runtime, "strong_window_instance_handles", return_value={}), \
                mock.patch.object(runtime, "process_toolkit", return_value=""):
            inventory = runtime.list_apps_inventory()

        windows = [window for entry in inventory["apps"] for window in entry["windows"]]
        self.assertEqual(1, len(windows))
        self.assertEqual("", windows[0]["handle"])
        self.assertTrue(inventory["inventory_issues"])
        self.assertFalse(windows[0].get("noAccessibilityTree"))

    def test_multiple_accessibility_roots_share_native_coverage(self):
        empty = _TreeNode(role="application", name="paint3d", kids=())
        full = _TreeNode(role="application", name="paint3d", kids=(
            _TreeNode(role="frame", name="Main"),))
        empty.get_process_id = full.get_process_id = lambda: 30
        coverage = {}
        handle = runtime.window_handle_for_pid(30, "0x1")
        runtime.record_window_coverage(coverage, empty, [], {})
        runtime.record_window_coverage(coverage, full, [(0, full.get_child_at_index(0))], {0: handle})
        windows = runtime.native_only_windows(_managed(_window(30, "0x1", "Main")), coverage)
        self.assertEqual([], list(windows))

    def test_incomplete_empty_root_is_not_evidence_of_absent_accessibility(self):
        app = _TreeNode(role="application", name="paint3d", kids=())
        app.get_process_id = lambda: 30
        coverage = {}
        runtime.record_window_coverage(coverage, app,
            runtime.InventoryResult([], complete=False, errors=["timeout"]), {})
        windows = runtime.native_only_windows(_managed(_window(30, "0x1", "Main")), coverage)
        self.assertEqual([], list(windows))
        self.assertFalse(windows.complete)


class NativeWindowActionTests(AtspiPatchedTestCase):
    def setUp(self):
        super().setUp()
        self.window = {"pid": 30, "id": "0x1", "title": "Scene - Paint3D",
                       "app": "paint3d", "handle": "w2:abc", "generation": "g",
                       "active": False}

    def test_window_action_without_bus_identity_resolves_the_native_window(self):
        operation = {"app": "paint3d", "window_handle": "w2:abc",
                     "target": {"selector": "window", "hasName": True,
                                "windowHandle": "w2:abc"}}
        with mock.patch.object(runtime, "_x11_managed_windows",
                               return_value=_managed()), \
                mock.patch.object(runtime, "iter_apps",
                                  return_value=runtime.InventoryResult([], complete=True)), \
                mock.patch.object(runtime, "x11_root_window_state",
                                  return_value={"windowIds": {"0x1"}, "activeWindow": None}), \
                mock.patch.object(runtime, "native_only_windows",
                                  return_value=[self.window]):
            self.assertIs(self.window, runtime.native_window_for_operation(operation))
            operation["target"]["providerBusName"] = ":1.5"
            self.assertIsNone(runtime.native_window_for_operation(operation))

    def test_activate_is_confirmed_by_the_window_manager(self):
        states = iter([{"windowIds": {"0x1"}, "activeWindow": "0x9"},
                       {"windowIds": {"0x1"}, "activeWindow": "0x1"}])
        with mock.patch.object(runtime, "_emit") as emit, \
                mock.patch.object(runtime, "x11_root_window_state",
                                  side_effect=lambda: next(states)), \
                mock.patch.object(runtime.time, "sleep"):
            response = runtime.perform_native_window_action("activate", self.window)

        emit.assert_called_once_with("activate", "0x1")
        self.assertTrue(response["ok"])
        self.assertTrue(response["input_events_emitted"])

    def test_registered_empty_root_resolves_exact_native_window_and_rejects_replacement(self):
        app = _TreeNode(role="application", name="paint3d", kids=())
        app.get_process_id = lambda: 30
        managed = _managed(_window(30, "0x1", "Scene - Paint3D"))
        with mock.patch.object(runtime, "iter_apps", return_value=[app]), \
                mock.patch.object(runtime, "_x11_managed_windows", return_value=managed), \
                mock.patch.object(runtime, "x11_window_type", return_value="NORMAL"), \
                mock.patch.object(runtime, "process_generation", return_value="first-generation"), \
                mock.patch.object(runtime, "x11_root_window_state", return_value={"activeWindow": "0x1"}):
            handle = runtime.window_handle_for_pid(30, "0x1")
            operation = {"window_handle": handle, "target": {"windowHandle": handle}}
            self.assertEqual("0x1", runtime.native_window_for_operation(operation)["id"])
            managed[0]["id"] = "0x2"
            self.assertIsNone(runtime.native_window_for_operation(operation))
            managed[0]["id"] = "0x1"
            with mock.patch.object(runtime, "process_generation", return_value="new-generation"):
                self.assertIsNone(runtime.native_window_for_operation(operation))

    def test_native_window_is_not_reused_after_accessibility_claims_it(self):
        app = _TreeNode(role="application", name="paint3d", kids=(
            _TreeNode(role="frame", name="Main"),))
        app.get_process_id = lambda: 30
        with mock.patch.object(runtime, "iter_apps", return_value=[app]), \
                mock.patch.object(runtime, "_x11_managed_windows", return_value=_managed(
                    _window(30, "0x1", "Main"))), \
                mock.patch.object(runtime, "x11_window_type", return_value="NORMAL"), \
                mock.patch.object(runtime, "process_generation", return_value="gen"), \
                mock.patch.object(runtime, "x11_root_window_state", return_value={"activeWindow": "0x1"}):
            handle = runtime.window_handle_for_pid(30, "0x1")
            with mock.patch.object(runtime, "strong_window_instance_handles", return_value={0: handle}):
                self.assertIsNone(runtime.native_window_for_operation({"window_handle": handle}))

    def test_dispatcher_rejects_native_ref_after_ax_claims_window_before_app_lookup(self):
        app = _TreeNode(role="application", name="paint3d", kids=(
            _TreeNode(role="frame", name="Main"),))
        app.get_process_id = lambda: 30
        with mock.patch.object(runtime, "iter_apps", return_value=[app]), \
                mock.patch.object(runtime, "_x11_managed_windows", return_value=_managed(
                    _window(30, "0x1", "Main"))), \
                mock.patch.object(runtime, "x11_window_type", return_value="NORMAL"), \
                mock.patch.object(runtime, "process_generation", return_value="gen"), \
                mock.patch.object(runtime, "x11_root_window_state", return_value={"activeWindow": "0x1"}), \
                mock.patch.object(runtime, "resolve_operation_app",
                    side_effect=AssertionError("A native ref must not fall back to app-name lookup")) as resolve, \
                mock.patch.object(runtime, "_emit") as native_emit, \
                mock.patch.object(runtime, "_emit_native_window_command") as ax_emit:
            handle = runtime.window_handle_for_pid(30, "0x1")
            operation = {"app": "paint3d", "action": "activate", "window_handle": handle,
                         "target": {"selector": 'window "Main"', "hasName": True,
                                    "name": "Main", "role": "window", "windowHandle": handle}}
            with mock.patch.object(runtime, "strong_window_instance_handles", return_value={0: handle}):
                with self.assertRaises(runtime.OperationFailure) as caught:
                    runtime.perform_provider_request("window_action", operation)
            self.assertIn(caught.exception.failure_code, {"invalid_target", "stale_window_ref"})
            self.assertEqual("precondition", caught.exception.failure_phase)
            self.assertFalse(caught.exception.input_events_emitted)
            resolve.assert_not_called()
            native_emit.assert_not_called()
            ax_emit.assert_not_called()

    def test_close_that_leaves_the_window_is_effect_unknown(self):
        with mock.patch.object(runtime, "_emit"), \
                mock.patch.object(runtime, "x11_root_window_state",
                                  return_value={"windowIds": {"0x1"},
                                                "activeWindow": "0x1"}), \
                mock.patch.object(runtime, "AX_WINDOW_ACTION_TIMEOUT_SECONDS", 0.0):
            with self.assertRaises(runtime.OperationFailure) as caught:
                runtime.perform_native_window_action("close", self.window)

        self.assertEqual("effect_unknown", caught.exception.failure_code)
        self.assertTrue(caught.exception.input_events_emitted)


class NativeWindowTypeTests(AtspiPatchedTestCase):
    def test_standard_type_is_not_a_substring_of_a_vendor_extension(self):
        result = subprocess.CompletedProcess([], 0,
            "_NET_WM_WINDOW_TYPE(ATOM) = _KDE_NET_WM_WINDOW_TYPE_OVERRIDE, "
            "_NET_WM_WINDOW_TYPE_NORMAL\n", "")
        with mock.patch.object(runtime.subprocess, "run", return_value=result):
            self.assertEqual("NORMAL", runtime.x11_window_type("0x1"))

    def test_standard_type_order_and_missing_type_are_preserved(self):
        for value, expected in (("_NET_WM_WINDOW_TYPE_DIALOG, _NET_WM_WINDOW_TYPE_NORMAL", "DIALOG"),
                                ("_NET_WM_WINDOW_TYPE_DOCK", "DOCK"),
                                ("_NET_WM_WINDOW_TYPE: not found.", "")):
            with self.subTest(value=value), mock.patch.object(runtime.subprocess, "run",
                    return_value=subprocess.CompletedProcess([], 0, value, "")):
                self.assertEqual(expected, runtime.x11_window_type("0x1"))

    def test_type_suffix_is_not_shortened_into_an_application_type(self):
        for atom in ("NORMAL2", "NORMAL_custom", "DIALOG_aux", "DIALOG9"):
            with self.subTest(atom=atom), mock.patch.object(runtime.subprocess, "run",
                    return_value=subprocess.CompletedProcess([], 0,
                        "_NET_WM_WINDOW_TYPE(ATOM) = _NET_WM_WINDOW_TYPE_" + atom, "")):
                parsed = runtime.x11_window_type("0x1")
                self.assertEqual(atom, parsed)
                self.assertNotIn(parsed, runtime.APPLICATION_WINDOW_TYPES)


class HiddenWindowListingTests(AtspiPatchedTestCase):
    """AT-SPI windows that cannot be on screen leave the window listings."""

    def _patch_inventory(self, apps, handles, root_state):
        original_apps = runtime.iter_apps
        runtime.iter_apps = lambda: apps
        self.addCleanup(setattr, runtime, "iter_apps", original_apps)
        for patcher in (
            mock.patch.object(runtime, "strong_window_instance_handles",
                              side_effect=lambda app, windows: handles.get(id(app), {})),
            mock.patch.object(runtime, "correlation_root_window_state", return_value=root_state),
            mock.patch.object(runtime, "opaque_window_instance_handle_for_id",
                              side_effect=lambda app, window_id: "w2:" + str(window_id)),
            mock.patch.object(runtime, "is_infrastructure_window",
                              side_effect=lambda window: False),
            mock.patch.object(runtime, "provider_bus_name", return_value=":1.9"),
            mock.patch.object(runtime, "process_generation", return_value="gen"),
        ):
            patcher.start()
            self.addCleanup(patcher.stop)

    def _native(self, *window_ids):
        patcher = mock.patch.object(
            runtime, "x11_window_inventory_for_pid",
            return_value=runtime.InventoryResult(
                [{"id": window_id} for window_id in window_ids], complete=True))
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_hidden_windows_beside_a_bound_one_are_not_listed(self):
        """Hidden toolkit windows do not appear as "(not addressable)" noise.

        Electron keeps unmapped BrowserWindows with the main window's title
        that AT-SPI reports SHOWING. The process's only managed window is
        bound to the main frame, so the others cannot be on screen.
        """
        frames = tuple(_TreeNode(role="frame", name="Notes") for _ in range(3))
        app = _TreeNode(role="application", name="notes", kids=frames)
        app.get_process_id = lambda: 101
        self._patch_inventory([app], {id(app): {0: "w2:0xa"}},
                              {"windowIds": {"0xa"}, "activeWindow": "0xa"})
        self._native("0xa")

        windows = runtime.list_apps_inventory()["apps"][0]["windows"]

        self.assertEqual(["w2:0xa"], [w["handle"] for w in windows])

    def test_unbound_window_stays_while_a_native_window_is_unclaimed(self):
        frames = (_TreeNode(role="frame", name="Main"),
                  _TreeNode(role="dialog", name="Export"))
        app = _TreeNode(role="application", name="paint", kids=frames)
        app.get_process_id = lambda: 101
        self._patch_inventory([app], {id(app): {0: "w2:0xa"}},
                              {"windowIds": {"0xa", "0xb"}, "activeWindow": "0xa"})
        self._native("0xa", "0xb")

        windows = runtime.list_apps_inventory()["apps"][0]["windows"]

        self.assertEqual(["w2:0xa", ""], [w["handle"] for w in windows])

    def test_process_without_managed_windows_keeps_unbound_windows(self):
        frames = (_TreeNode(role="frame", name="Main"),
                  _TreeNode(role="frame", name="Other"))
        app = _TreeNode(role="application", name="remote", kids=frames)
        app.get_process_id = lambda: 101
        self._patch_inventory([app], {id(app): {0: "w2:0xa"}},
                              {"windowIds": {"0xa"}, "activeWindow": None})
        self._native()

        windows = runtime.list_apps_inventory()["apps"][0]["windows"]

        self.assertEqual(2, len(windows))



if __name__ == "__main__":
    unittest.main()


class BlockingModalTests(AtspiPatchedTestCase):
    """A modal dialog of the same process keeps the input.

    MuseScore 4 opens its "Getting started" dialog modal and transient for
    the main window. Activating the main window then sent the command, the
    window manager kept focus in the dialog, and the result was a timeout
    reported as effect_unknown.
    """

    XPROP = {
        "0x2": "WM_TRANSIENT_FOR(WINDOW): window id # 0x1\n"
               "_NET_WM_STATE(ATOM) = _NET_WM_STATE_MODAL, _NET_WM_STATE_FOCUSED\n",
        "0x3": "WM_TRANSIENT_FOR(WINDOW): window id # 0x1\n"
               "_NET_WM_STATE(ATOM) = _NET_WM_STATE_SKIP_TASKBAR\n",
        "0x4": "WM_TRANSIENT_FOR(WINDOW): window id # 0x1\n"
               "_NET_WM_STATE(ATOM) = _NET_WM_STATE_MODAL\n",
        "0x5": "WM_TRANSIENT_FOR(WINDOW): window id # 0x1\n"
               "_NET_WM_STATE(ATOM) = _NET_WM_STATE_MODAL, _NET_WM_STATE_HIDDEN\n",
    }

    def setUp(self):
        super().setUp()
        managed = runtime.InventoryResult([
            {"pid": 30, "id": "0x1", "title": "Score", "x": 0, "y": 0, "width": 800, "height": 600},
            {"pid": 30, "id": "0x2", "title": "Getting started", "x": 100, "y": 100, "width": 400, "height": 300},
            {"pid": 30, "id": "0x3", "title": "Palette", "x": 100, "y": 100, "width": 100, "height": 100},
            {"pid": 31, "id": "0x4", "title": "Other app modal", "x": 0, "y": 0, "width": 100, "height": 100},
            {"pid": 30, "id": "0x5", "title": "Minimized modal", "x": 0, "y": 0, "width": 100, "height": 100},
        ], complete=True)
        for name, value in (
            ("_x11_managed_windows", lambda: managed),
            ("_xprop_text", lambda window_id, *atoms: self.XPROP.get(window_id, "")),
        ):
            patcher = mock.patch.object(runtime, name, side_effect=value)
            patcher.start()
            self.addCleanup(patcher.stop)

    def test_only_a_visible_modal_transient_of_the_same_process_blocks(self):
        blocking = runtime.modal_transient_windows("0x1", 30)
        self.assertEqual(["Getting started"], [window["title"] for window in blocking])

    def test_native_activate_names_the_modal_and_emits_nothing(self):
        window = {"pid": 30, "id": "0x1", "title": "Score", "app": "score",
                  "handle": "h", "generation": "g", "active": False}
        with mock.patch.object(runtime, "_emit") as emit:
            with self.assertRaises(runtime.OperationFailure) as raised:
                runtime.perform_native_window_action("activate", window)
        emit.assert_not_called()
        self.assertEqual("window_blocked", raised.exception.failure_code)
        self.assertFalse(raised.exception.input_events_emitted)
        self.assertIn('"Getting started"', str(raised.exception))
