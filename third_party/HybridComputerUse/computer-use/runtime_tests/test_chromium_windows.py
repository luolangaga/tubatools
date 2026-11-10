#!/usr/bin/env python3
"""Linux provider window identity for Chromium and Electron windows and popups."""

from runtime_test_support import *  # noqa: F401,F403


class SameTitleWindowHandleTests(AtspiPatchedTestCase):
    """Hidden same-title windows must not take the handle of the shown one.

    Electron applications keep hidden BrowserWindows that AT-SPI still lists
    as frames. Joplin lists three frames named "Joplin" while the window
    manager manages one window: the 1536x842 main window, plus two hidden
    800x600 ones that wmctrl does not list. All three matched the managed
    window by title, the tie gave none of them a handle, and every node of
    Joplin became addressable=false (owner_identity_incomplete).
    """

    def _joplin(self):
        main = _TreeNode(role="frame", name="Joplin", x=192, y=132, w=1536, h=842)
        hidden_a = _TreeNode(role="frame", name="Joplin", x=560, y=253, w=800, h=600)
        hidden_b = _TreeNode(role="frame", name="Joplin", x=560, y=253, w=800, h=600)
        app = _TreeNode(role="application", name="joplin",
                        kids=(main, hidden_a, hidden_b))
        app.get_process_id = lambda: 4633
        return app

    def _managed(self, *windows):
        return runtime.InventoryResult(list(windows), complete=True)

    def test_the_frame_matching_the_managed_geometry_gets_the_handle(self):
        app = self._joplin()
        managed = self._managed({
            "pid": 4633, "id": "0x3200004", "x": 197, "y": 161,
            "width": 1536, "height": 842, "title": "Joplin"})
        with mock.patch.object(runtime, "_x11_managed_windows",
                               return_value=managed), \
                mock.patch.object(runtime, "process_generation",
                                  return_value="77"), \
                mock.patch.object(runtime, "correlation_root_window_state",
                                  return_value=None):
            handles = runtime.strong_window_instance_handles(app)

        self.assertEqual(list(handles), [0])
        self.assertTrue(handles[0].startswith("w2:"))

    def test_equally_close_frames_stay_unaddressable(self):
        """Two frames at the managed geometry cannot be told apart."""
        twin_a = _TreeNode(role="frame", name="Doc", x=0, y=0, w=800, h=600)
        twin_b = _TreeNode(role="frame", name="Doc", x=0, y=0, w=800, h=600)
        app = _TreeNode(role="application", name="editor", kids=(twin_a, twin_b))
        app.get_process_id = lambda: 9
        managed = self._managed({
            "pid": 9, "id": "0x1", "x": 0, "y": 0,
            "width": 800, "height": 600, "title": "Doc"})
        with mock.patch.object(runtime, "_x11_managed_windows",
                               return_value=managed), \
                mock.patch.object(runtime, "process_generation",
                                  return_value="77"), \
                mock.patch.object(runtime, "correlation_root_window_state",
                                  return_value=None):
            handles = runtime.strong_window_instance_handles(app)

        self.assertEqual(handles, {})


class OverrideRedirectPopupHandleTests(AtspiPatchedTestCase):
    """An untitled popup frame gets the handle of its override-redirect window.

    Chromium opens its app menu, context menus and <select> lists as
    override-redirect X11 windows, which wmctrl never lists, and AT-SPI
    shows each as an untitled frame. Without a native window the frame got
    no handle, so every menu item rendered addressable=false with
    owner_identity_incomplete and AX could not pick a menu item.
    """

    XWININFO_CHILDREN = (
        "\nxwininfo: Window id: 0x5c5 (the root window) (has no name)\n\n"
        "  Root window id: 0x5c5 (the root window) (has no name)\n"
        "  Parent window id: 0x0 (none)\n"
        "     3 children:\n"
        "     0x2000011 (has no name): ()  467x813+501+105  +501+105\n"
        "     0x2000004 \"Page - Google Chrome\": ()  945x1033+10+37  +10+37\n"
        "     0x3000001 (has no name): ()  10x10+10+10  +10+10\n"
    )

    def _xwininfo(self, viewable=True, override=True):
        return (
            "xwininfo: Window id: 0x2000011 (has no name)\n"
            "  Absolute upper-left X:  501\n  Absolute upper-left Y:  105\n"
            "  Width: 467\n  Height: 813\n"
            "  Map State: {}\n  Override Redirect State: {}\n".format(
                "IsViewable" if viewable else "IsUnMapped",
                "yes" if override else "no")
        )

    def _run(self, viewable=True, override=True, pid=389):
        def run(command, **_kwargs):
            if command[:2] == ["wmctrl", "-lpG"]:
                out = "0x02000004  0 389 10 37 945 1033 host Page - Google Chrome\n"
            elif command[:3] == ["xwininfo", "-root", "-children"]:
                out = self.XWININFO_CHILDREN
            elif command[:3] == ["xwininfo", "-id", "0x2000011"]:
                out = self._xwininfo(viewable, override)
            elif command[:3] == ["xprop", "-id", "0x2000011"]:
                out = "_NET_WM_PID(CARDINAL) = {}\n".format(pid)
            else:
                out = ""
            return subprocess.CompletedProcess(command, 0, out, "")
        return run

    def _chrome(self, popup_name=""):
        page = _TreeNode(role="frame", name="Page - Google Chrome",
                         x=10, y=37, w=945, h=1033)
        menu = _TreeNode(role="frame", name=popup_name, x=501, y=105, w=467, h=813)
        app = _TreeNode(role="application", name="Google Chrome", kids=(page, menu))
        app.get_process_id = lambda: 389
        return app

    def _handles(self, app, **run_options):
        with mock.patch.object(runtime.subprocess, "run",
                               side_effect=self._run(**run_options)), \
                mock.patch.object(runtime, "process_generation", return_value="77"), \
                mock.patch.object(runtime, "correlation_root_window_state",
                                  return_value=None):
            return runtime.strong_window_instance_handles(app)

    def test_untitled_popup_takes_the_override_redirect_window(self):
        handles = self._handles(self._chrome())

        self.assertEqual(sorted(handles), [0, 1])
        self.assertNotEqual(handles[0], handles[1])

    def test_unmapped_or_managed_windows_are_not_popups(self):
        self.assertEqual(sorted(self._handles(self._chrome(), viewable=False)), [0])
        self.assertEqual(sorted(self._handles(self._chrome(), override=False)), [0])

    def test_popup_of_another_process_is_not_taken(self):
        self.assertEqual(sorted(self._handles(self._chrome(), pid=999)), [0])

    def test_untitled_frames_of_desktop_components_do_not_scan(self):
        panel_frame = _TreeNode(role="frame", name="", x=0, y=0, w=1920, h=27)
        panel = _TreeNode(role="application", name="xfce4-panel", kids=(panel_frame,))
        panel.get_process_id = lambda: 85
        calls = []

        def run(command, **_kwargs):
            calls.append(command[0])
            return subprocess.CompletedProcess(command, 0, "", "")

        with mock.patch.object(runtime.subprocess, "run", side_effect=run), \
                mock.patch.object(runtime, "process_generation", return_value="77"), \
                mock.patch.object(runtime, "correlation_root_window_state",
                                  return_value=None):
            runtime.strong_window_instance_handles(panel)

        self.assertEqual(calls, ["wmctrl"])
