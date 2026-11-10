#!/usr/bin/env python3
"""Linux provider switches desktop accessibility on and explains apps it cannot see."""

from runtime_test_support import *  # noqa: F401,F403


class FakeSessionBus:
    """Session bus with an a11y status object and, optionally, Xfconf."""

    def __init__(self, enabled=False, xfconf=None, fail=()):
        self.enabled = enabled
        # None: no Xfconf on the bus; {}: Xfconf without Gtk/Modules.
        self.xfconf = xfconf
        self.fail = set(fail)
        self.writes = []

    def _maybe_fail(self, what):
        if what in self.fail:
            raise RuntimeError("{} failed".format(what))

    def has_owner(self, name):
        self._maybe_fail("has_owner")
        return name == "org.xfce.Xfconf" and self.xfconf is not None

    def get_property(self, name, path, interface, prop):
        self._maybe_fail("get")
        assert (name, path, interface, prop) == (
            "org.a11y.Bus", "/org/a11y/bus", "org.a11y.Status", "IsEnabled")
        return self.enabled

    def set_bool_property(self, name, path, interface, prop, value):
        self._maybe_fail("set")
        self.writes.append((interface, prop, value))
        self.enabled = value

    def xfconf_get_string(self, channel, prop):
        if (channel, prop) not in self.xfconf:
            raise RuntimeError(
                "GDBus.Error:org.xfce.Xfconf.Error.PropertyNotFound: no such")
        return self.xfconf[(channel, prop)]

    def xfconf_set_string(self, channel, prop, value):
        self.writes.append((channel, prop, value))
        self.xfconf[(channel, prop)] = value


class EnableSessionAccessibilityTests(unittest.TestCase):
    """Applications started by the user expose a tree without launch flags.

    In a plain session (no a11y-env.sh, toolkit-accessibility unset), Qt,
    Firefox, Chrome and Electron applications never registered on the AT-SPI
    bus, and GTK 2 applications registered only with GTK_MODULES set.
    """

    def test_turns_accessibility_on_in_a_plain_session(self):
        bus = FakeSessionBus(enabled=False, xfconf={})

        facts = runtime.enable_session_accessibility(bus)

        self.assertIn(("org.a11y.Status", "IsEnabled", True), bus.writes)
        self.assertIn(
            ("xsettings", "/Gtk/Modules", "gail:atk-bridge"), bus.writes)
        self.assertEqual(facts["errors"], [])

    def test_never_sets_screen_reader_enabled(self):
        """The launcher mirrors ScreenReaderEnabled to the key that starts Orca."""
        bus = FakeSessionBus(enabled=False)

        runtime.enable_session_accessibility(bus)

        self.assertNotIn(
            "ScreenReaderEnabled", [write[1] for write in bus.writes])

    def test_is_a_no_op_when_the_session_already_has_accessibility(self):
        """OSWorld guests export a11y-env.sh and set Gtk/Modules themselves."""
        bus = FakeSessionBus(
            enabled=True,
            xfconf={("xsettings", "/Gtk/Modules"): "canberra-gtk-module:gail:atk-bridge"},
        )

        facts = runtime.enable_session_accessibility(bus)

        self.assertEqual(bus.writes, [])
        self.assertEqual(facts, {"changed": [], "errors": []})

    def test_keeps_existing_gtk_modules(self):
        bus = FakeSessionBus(
            enabled=True,
            xfconf={("xsettings", "/Gtk/Modules"): "canberra-gtk-module:gail"},
        )

        runtime.enable_session_accessibility(bus)

        self.assertEqual(
            bus.xfconf[("xsettings", "/Gtk/Modules")],
            "canberra-gtk-module:gail:atk-bridge",
        )

    def test_leaves_xsettings_alone_without_xfconf(self):
        """GNOME's settings daemon derives Gtk/Modules from toolkit-accessibility."""
        bus = FakeSessionBus(enabled=False, xfconf=None)

        runtime.enable_session_accessibility(bus)

        self.assertEqual(bus.writes, [("org.a11y.Status", "IsEnabled", True)])

    def test_failures_are_reported_and_do_not_stop_the_other_switch(self):
        bus = FakeSessionBus(enabled=False, xfconf={}, fail={"get"})

        facts = runtime.enable_session_accessibility(bus)

        self.assertEqual(len(facts["errors"]), 1)
        self.assertIn("org.a11y.Status", facts["errors"][0])
        self.assertIn(
            ("xsettings", "/Gtk/Modules", "gail:atk-bridge"), bus.writes)


class ProviderStartupEnablesAccessibilityTests(unittest.TestCase):
    def test_provider_start_enables_accessibility_once(self):
        with mock.patch.object(
            runtime, "_provider_initialized", False
        ), mock.patch.object(
            runtime, "require_desktop_session"
        ), mock.patch.object(
            runtime.Atspi, "init"
        ), mock.patch.object(
            runtime.Atspi, "set_timeout"
        ), mock.patch.object(
            runtime.ui_events, "start"
        ), mock.patch.object(
            runtime, "enable_session_accessibility",
            return_value={"changed": [], "errors": []},
        ) as enable:
            runtime._initialize_provider({"request_id": "r1"})
            runtime._initialize_provider({"request_id": "r2"})

        enable.assert_called_once_with()

    def test_enable_failure_does_not_fail_provider_start(self):
        """A session without the a11y bus must still serve GUI and GTK3 apps."""
        with mock.patch.object(
            runtime, "_provider_initialized", False
        ), mock.patch.object(
            runtime, "require_desktop_session"
        ), mock.patch.object(
            runtime.Atspi, "init"
        ), mock.patch.object(
            runtime.Atspi, "set_timeout"
        ), mock.patch.object(
            runtime.ui_events, "start"
        ), mock.patch.object(
            runtime, "enable_session_accessibility",
            side_effect=RuntimeError("no session bus"),
        ):
            runtime._initialize_provider({"request_id": "r1"})
            self.assertTrue(runtime._provider_initialized)


class ChromiumTreeRequestTests(AtspiPatchedTestCase):
    """Chromium builds the tree below its frames only for a screen reader.

    Chrome and Electron apps started after IsEnabled expose only their
    application and frame until an AT calls GetAttributes.
    """

    def setUp(self):
        super().setUp()
        runtime._attribute_requested.clear()
        self.addCleanup(runtime._attribute_requested.clear)

    def _app(self, bus_name):
        app = _TreeNode(role="application", name="Browser",
                        kids=(_TreeNode(role="frame", name="Page"),))
        calls = []
        app.get_attributes = lambda: calls.append(bus_name) or {}
        return app, calls

    def test_listing_asks_each_application_for_attributes_once(self):
        app, calls = self._app(":1.7")
        root = _TreeNode(role="desktop", name="Desktop", kids=(app,))
        with mock.patch.object(runtime, "desktop", return_value=root), \
                mock.patch.object(runtime, "provider_bus_name",
                                  return_value=":1.7"):
            runtime.iter_apps()
            runtime.iter_apps()

        self.assertEqual(calls, [":1.7"])

    def test_restarted_application_is_asked_again(self):
        first, calls = self._app(":1.7")
        second, _ = self._app(":1.9")
        second.get_attributes = first.get_attributes
        names = iter([":1.7", ":1.9"])
        with mock.patch.object(runtime, "provider_bus_name",
                               side_effect=lambda _app: next(names)):
            runtime.request_full_tree(first)
            runtime.request_full_tree(second)

        self.assertEqual(len(calls), 2)

    def test_attribute_failure_does_not_break_the_listing(self):
        app = _TreeNode(role="application", name="Hung")

        def fail():
            raise RuntimeError("timeout")

        app.get_attributes = fail
        root = _TreeNode(role="desktop", name="Desktop", kids=(app,))
        with mock.patch.object(runtime, "desktop", return_value=root), \
                mock.patch.object(runtime, "provider_bus_name",
                                  return_value=":1.8"):
            inventory = runtime.iter_apps()

        self.assertEqual(list(inventory), [app])
        self.assertTrue(inventory.complete)


class InvisibleWindowNoteTests(AtspiPatchedTestCase):
    """A window whose process is not on the bus is named, with what to do."""

    def _proc(self, root, pid, libraries):
        os.makedirs(os.path.join(root, str(pid)))
        with open(os.path.join(root, str(pid), "maps"), "w",
                  encoding="utf-8") as maps:
            for library in libraries:
                maps.write(
                    "7f00-7f01 r-xp 00000000 08:01 1 {}\n".format(library))
            maps.write("7ffd-7ffe rw-p 00000000 00:00 0 [stack]\n")

    def test_toolkit_is_read_from_mapped_libraries(self):
        with tempfile.TemporaryDirectory() as root:
            self._proc(root, 10, ["/usr/lib/x86_64-linux-gnu/libQt5Core.so.5.15.3"])
            self._proc(root, 11, ["/opt/firefox/libxul.so",
                                  "/usr/lib/libgtk-3.so.0.2404.29"])
            self._proc(root, 12, ["/opt/app/chrome_100_percent.pak",
                                  "/usr/lib/libgtk-3.so.0.2404.29"])
            self._proc(root, 13, ["/usr/lib/libgtk-x11-2.0.so.0.2400.33"])
            self._proc(root, 14, ["/usr/lib/libgtk-3.so.0.2404.29"])
            self._proc(root, 15, ["/usr/lib/libc.so.6"])

            toolkits = [runtime.process_toolkit(pid, root)
                        for pid in range(10, 17)]

        self.assertEqual(
            toolkits, ["Qt", "Gecko", "Chromium", "GTK 2", "GTK", "", ""])

    def test_vendor_renamed_qt_and_java_are_recognized(self):
        # WPS Office ships Qt 5.12 renamed to libQt5CoreKso; GeoGebra runs
        # on its own Java runtime.
        with tempfile.TemporaryDirectory() as root:
            self._proc(root, 16, ["/opt/kingsoft/wps-office/office6/libQt5CoreKso.so.5.12.10",
                                  "/usr/lib/libgtk-3.so.0.2404.29"])
            self._proc(root, 17, ["/opt/geogebra/jre/lib/server/libjvm.so"])
            self.assertEqual("Qt", runtime.process_toolkit(16, root))
            self.assertEqual((5, 12), runtime.process_qt_version(16, root))
            self.assertEqual("Java", runtime.process_toolkit(17, root))

    def test_qt_before_5_15_needs_the_always_on_variable(self):
        # Qt before 5.15 reads only ScreenReaderEnabled, not IsEnabled.
        with tempfile.TemporaryDirectory() as root:
            self._proc(root, 30, ["/opt/kingsoft/wps-office/office6/libQt5CoreKso.so.5.12.10"])
            notes = runtime.invisible_window_notes(
                [{"pid": 30, "title": "Untitled - WPS Writer"}],
                accessible_pids=set(), proc_root=root)
        self.assertEqual(1, len(notes))
        self.assertIn("(pid 30, Qt 5.12)", notes[0])
        self.assertIn("QT_LINUX_ACCESSIBILITY_ALWAYS_ON=1", notes[0])

    def test_registration_through_another_toolkit_still_gets_advice(self):
        # WPS loads GTK for its Qt platform theme; GTK registers an empty
        # application while the Qt windows stay off the bus.
        with tempfile.TemporaryDirectory() as root:
            self._proc(root, 30, ["/opt/kingsoft/wps-office/office6/libQt5CoreKso.so.5.12.10",
                                  "/usr/lib/libgtk-3.so.0.2404.29"])
            window = [{"pid": 30, "title": "Untitled - WPS Writer"}]
            mismatched = runtime.invisible_window_notes(
                window, accessible_pids={30}, proc_root=root,
                windowless_toolkits={30: {"gtk"}})
            registered = runtime.invisible_window_notes(
                window, accessible_pids={30}, proc_root=root,
                windowless_toolkits={30: {"qt"}})
        self.assertEqual(1, len(mismatched))
        self.assertIn("QT_LINUX_ACCESSIBILITY_ALWAYS_ON=1", mismatched[0])
        self.assertEqual([], registered)

    def test_java_advice_names_the_atk_wrapper(self):
        with tempfile.TemporaryDirectory() as root:
            self._proc(root, 40, ["/opt/geogebra/jre/lib/server/libjvm.so"])
            notes = runtime.invisible_window_notes(
                [{"pid": 40, "title": "GeoGebra Classic 5"}],
                accessible_pids=set(), proc_root=root)
        self.assertEqual(1, len(notes))
        self.assertIn("(pid 40, Java)", notes[0])
        self.assertIn("org.GNOME.Accessibility.AtkWrapper", notes[0])

    def test_listing_gives_advice_for_a_windowless_registration(self):
        app = _TreeNode(role="application", name="wps", kids=())
        app.get_process_id = lambda: 30
        app.get_toolkit_name = lambda: "gtk"
        original_apps = runtime.iter_apps
        runtime.iter_apps = lambda: [app]
        self.addCleanup(setattr, runtime, "iter_apps", original_apps)
        managed = runtime.InventoryResult([
            {"pid": 30, "id": "0x1", "x": 0, "y": 0, "width": 900, "height": 700,
             "title": "Untitled - WPS Writer"},
        ])
        with mock.patch.object(runtime, "_x11_managed_windows", return_value=managed), \
                mock.patch.object(runtime, "process_toolkit", return_value="Qt"), \
                mock.patch.object(runtime, "process_qt_version", return_value=(5, 12)), \
                mock.patch.object(runtime, "process_generation", return_value="7"), \
                mock.patch.object(runtime, "correlation_root_window_state", return_value=None):
            notes = runtime.list_apps_inventory()["notes"]
        self.assertEqual(1, len(notes), notes)
        self.assertIn('"Untitled - WPS Writer" (pid 30, Qt 5.12)', notes[0])

    def test_names_windows_of_processes_missing_from_the_bus(self):
        with tempfile.TemporaryDirectory() as root:
            self._proc(root, 20, ["/usr/lib/libQt5Core.so.5"])
            self._proc(root, 21, ["/usr/lib/libgtk-3.so.0"])
            notes = runtime.invisible_window_notes(
                [
                    {"pid": 20, "title": "VLC media player"},
                    {"pid": 20, "title": "VLC preferences"},
                    {"pid": 21, "title": "Untitled - gedit"},
                    {"pid": 22, "title": ""},
                ],
                accessible_pids={21},
                proc_root=root,
            )

        self.assertEqual(len(notes), 1)
        self.assertIn('"VLC media player" (pid 20, Qt)', notes[0])
        self.assertIn("restart the application", notes[0])
        self.assertIn("only GUI", notes[0])

    def test_listing_reports_windows_of_processes_missing_from_the_bus(self):
        window = _TreeNode(role="frame", name="Untitled - gedit")
        app = _TreeNode(role="application", name="gedit", kids=(window,))
        app.get_process_id = lambda: 21
        original_apps = runtime.iter_apps
        runtime.iter_apps = lambda: [app]
        self.addCleanup(setattr, runtime, "iter_apps", original_apps)
        managed = runtime.InventoryResult([
            {"pid": 21, "id": "0x1", "x": 0, "y": 0, "width": 9, "height": 9,
             "title": "Untitled - gedit"},
            {"pid": 20, "id": "0x2", "x": 0, "y": 0, "width": 9, "height": 9,
             "title": "Welcome - Visual Studio Code"},
        ])
        with mock.patch.object(runtime, "_x11_managed_windows",
                               return_value=managed), \
                mock.patch.object(runtime, "process_toolkit",
                                  return_value="Chromium"):
            notes = runtime.list_apps_inventory()["notes"]

        self.assertEqual(len(notes), 1)
        self.assertIn('"Welcome - Visual Studio Code" (pid 20, Chromium)',
                      notes[0])
        self.assertIn("quit every window", notes[0])


class WindowWithoutTreeNoteTests(AtspiPatchedTestCase):
    """A managed window that the application's tree does not contain is named.

    Electron shows native GTK message boxes ("A setting has changed that
    requires a restart", "The window terminated unexpectedly") as managed
    X11 windows of the same process, but they are not in the AT-SPI tree.
    AX apps listed only the main window, so the agent did not know a modal
    message box was blocking it.
    """

    def test_apps_names_a_managed_window_missing_from_the_tree(self):
        main = _TreeNode(role="frame", name="a.txt - Visual Studio Code",
                         x=240, y=90, w=1440, h=900)
        app = _TreeNode(role="application", name="code", kids=(main,))
        app.get_process_id = lambda: 2980
        original_apps = runtime.iter_apps
        runtime.iter_apps = lambda: [app]
        self.addCleanup(setattr, runtime, "iter_apps", original_apps)
        panel = _TreeNode(role="application", name="xfce4-panel",
                          kids=(_TreeNode(role="frame", name=""),))
        panel.get_process_id = lambda: 85
        runtime.iter_apps = lambda: [app, panel]
        managed = runtime.InventoryResult([
            {"pid": 85, "id": "0x600003", "x": 0, "y": 0, "width": 1920,
             "height": 27, "title": "xfce4-panel"},
            {"pid": 2980, "id": "0xe00004", "x": 240, "y": 90, "width": 1440,
             "height": 900, "title": "a.txt - Visual Studio Code"},
            {"pid": 2980, "id": "0x1e0008f", "x": 642, "y": 436, "width": 636,
             "height": 208, "title": "Visual Studio Code"},
            {"pid": 2980, "id": "0x1e00090", "x": -9999, "y": -9999,
             "width": 306, "height": 3, "title": "offscreen helper"},
            {"pid": 2980, "id": "0xe00005", "x": 240, "y": 90, "width": 1440,
             "height": 900, "title": "a.txt - Visual Studio Code  — Code"},
        ])
        with mock.patch.object(runtime, "_x11_managed_windows",
                               return_value=managed), \
                mock.patch.object(runtime, "process_generation",
                                  return_value="77"), \
                mock.patch.object(runtime, "correlation_root_window_state",
                                  return_value=None):
            result = runtime.list_apps_inventory()

        # The message box is listed once, with a native handle, so AX can
        # activate, close or wait for it; no note repeats it.
        self.assertEqual(result["notes"], [], result["notes"])
        native = [window for entry in result["apps"] for window in entry["windows"]
                  if window.get("noAccessibilityTree")]
        self.assertEqual(1, [window["title"] for window in native].count("Visual Studio Code"),
                         result["text"])


class ChromiumWakeSettleTests(AtspiPatchedTestCase):
    """The first read after the wake must see the web document.

    Chromium inserts the renderer's document about 20 ms after the first
    GetAttributes. The first observe of a freshly started Chrome walked the
    tree in the same request and missed the page (only browser chrome and
    "ax_content=uncertain"); the next observe had it.
    """

    def setUp(self):
        super().setUp()
        runtime._attribute_requested.clear()
        self.addCleanup(runtime._attribute_requested.clear)

    def _listing(self, toolkit):
        app = _TreeNode(role="application", name="Browser",
                        kids=(_TreeNode(role="frame", name="Page"),))
        app.get_toolkit_name = lambda: toolkit
        app.get_attributes = lambda: {}
        root = _TreeNode(role="desktop", name="Desktop", kids=(app,))
        sleeps = []
        with mock.patch.object(runtime, "desktop", return_value=root), \
                mock.patch.object(runtime, "provider_bus_name", return_value=":1.5"), \
                mock.patch.object(runtime.time, "sleep", side_effect=sleeps.append):
            runtime.iter_apps()
            runtime.iter_apps()
        return sleeps

    def test_first_listing_waits_once_for_a_woken_chromium_application(self):
        self.assertEqual(self._listing("Chromium"),
                         [runtime.CHROMIUM_WAKE_SETTLE_SECONDS])

    def test_other_toolkits_do_not_wait(self):
        self.assertEqual(self._listing("gtk"), [])
