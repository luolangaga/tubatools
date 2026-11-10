#!/usr/bin/env python3
"""Linux provider window selection, window focus, keyboard focus, and key syntax tests."""

from runtime_test_support import *  # noqa: F401,F403


class MainWindowSelectionTests(AtspiPatchedTestCase):
    """An open modal dialog is observed instead of the main window.

    With LibreOffice Writer's Format > Paragraph dialog open, the frame reports
    (SHOWING, VISIBLE) and the dialog (SHOWING, VISIBLE, MODAL); neither
    reports ACTIVE. Ranking by ACTIVE, then SHOWING, then child order picks
    the frame, whose tree contains no dialog node at all.
    """

    def _app(self, *windows):
        return FakeNode(children=windows)

    def test_visible_modal_dialog_wins_over_main_frame(self):
        frame = FakeNode(states=(STATE.SHOWING, STATE.VISIBLE))
        dialog = FakeNode(states=(STATE.SHOWING, STATE.VISIBLE, STATE.MODAL))
        self._patch_roles({id(frame): "frame", id(dialog): "dialog"})

        index, chosen = runtime.main_window(self._app(frame, dialog))

        self.assertIs(chosen, dialog)
        self.assertEqual(index, 1)

    def test_modal_dialog_wins_even_when_frame_claims_active(self):
        """A modal window blocks the others by definition, so it outranks ACTIVE."""
        frame = FakeNode(states=(STATE.SHOWING, STATE.VISIBLE, STATE.ACTIVE))
        dialog = FakeNode(states=(STATE.SHOWING, STATE.VISIBLE, STATE.MODAL))
        self._patch_roles({id(frame): "frame", id(dialog): "dialog"})

        _, chosen = runtime.main_window(self._app(frame, dialog))

        self.assertIs(chosen, dialog)

    def test_hidden_modal_dialog_does_not_win(self):
        """A closed modal dialog still attached to the tree does not take over."""
        frame = FakeNode(states=(STATE.SHOWING, STATE.VISIBLE, STATE.ACTIVE))
        stale = FakeNode(states=(STATE.MODAL,))
        self._patch_roles({id(frame): "frame", id(stale): "dialog"})

        _, chosen = runtime.main_window(self._app(frame, stale))

        self.assertIs(chosen, frame)

    def test_active_modal_wins_over_earlier_modal(self):
        """A combo box popup is a second modal window stacked on the dialog.

        In LibreOffice the Paragraph dialog is MODAL+SHOWING, while the line
        spacing popup is a separate top-level window that is also ACTIVE.
        Taking the first modal window would hide the popup's options.
        """
        dialog = FakeNode(states=(STATE.SHOWING, STATE.VISIBLE, STATE.MODAL))
        popup = FakeNode(
            states=(STATE.SHOWING, STATE.VISIBLE, STATE.MODAL, STATE.ACTIVE)
        )
        self._patch_roles({id(dialog): "dialog", id(popup): "window"})

        _, chosen = runtime.main_window(self._app(dialog, popup))

        self.assertIs(chosen, popup)

    def test_falls_back_to_active_then_showing(self):
        plain = FakeNode(states=(STATE.SHOWING,))
        active = FakeNode(states=(STATE.SHOWING, STATE.ACTIVE))
        self._patch_roles({id(plain): "frame", id(active): "frame"})

        _, chosen = runtime.main_window(self._app(plain, active))

        self.assertIs(chosen, active)

    def test_selection_metadata_reports_reason_and_all_candidates(self):
        frame = _NamedNode(
            name="Document", role="frame", states=(STATE.SHOWING,))
        dialog = _NamedNode(
            name="Confirm", role="dialog",
            states=(STATE.SHOWING, STATE.ACTIVE, STATE.MODAL))
        self._patch_roles({id(frame): "frame", id(dialog): "dialog"})

        index, chosen, selection = runtime.select_main_window(
            self._app(frame, dialog))

        self.assertEqual(index, 1)
        self.assertIs(chosen, dialog)
        self.assertEqual(selection["reason"], "active_modal")
        self.assertEqual(selection["candidateCount"], 2)
        self.assertEqual(
            [candidate["selected"] for candidate in selection["candidates"]],
            [False, True],
        )
        self.assertEqual(selection["candidates"][1]["title"], "Confirm")
        self.assertEqual(selection["candidates"][1]["role"], "dialog")

    def test_raises_when_no_window_exists(self):
        self._patch_roles({})
        with self.assertRaises(RuntimeError):
            runtime.main_window(FakeNode(children=()))

    def _patch_roles(self, mapping):
        patcher = mock.patch.object(
            FakeNode,
            "get_role_name",
            lambda node: mapping.get(id(node), "frame"),
        )
        patcher.start()
        self.addCleanup(patcher.stop)
        # The fakes have no component for the real extents() to query;
        # app_windows can classify them by role alone.
        original_ext = runtime.extents
        self.addCleanup(setattr, runtime, "extents", original_ext)
        runtime.extents = lambda node: None


class FocusWindowTests(AtspiPatchedTestCase):
    def test_active_window_needs_no_grab(self):
        component = FakeComponent(grabs=True)
        window = FakeNode(
            states=(STATE.ACTIVE,),
            children=(FakeNode(states=(STATE.FOCUSABLE,), component=component),),
        )

        self.assertTrue(runtime.focus_window(window))
        self.assertEqual(component.grab_calls, 0, "an already active window must not grab focus again")

    def test_grabs_focusable_child_to_activate_window(self):
        window = FakeNode()
        component = FakeComponent(window=window, grabs=True)
        window.children = [FakeNode(states=(STATE.FOCUSABLE,), component=component)]

        self.assertTrue(runtime.focus_window(window))
        self.assertEqual(component.grab_calls, 1)

    def test_prefers_the_previously_focused_widget(self):
        """An inactive window keeps FOCUSED on its last focused widget; try it first."""
        window = FakeNode()
        plain = FakeComponent(window=window, grabs=True)
        remembered = FakeComponent(window=window, grabs=True)
        window.children = [
            FakeNode(states=(STATE.FOCUSABLE,), component=plain),
            FakeNode(states=(STATE.FOCUSABLE, STATE.FOCUSED), component=remembered),
        ]

        self.assertTrue(runtime.focus_window(window))
        self.assertEqual(remembered.grab_calls, 1)
        self.assertEqual(plain.grab_calls, 0)

    def test_returns_false_when_nothing_can_take_focus(self):
        component = FakeComponent(grabs=False)
        window = FakeNode(
            children=(FakeNode(states=(STATE.FOCUSABLE,), component=component),)
        )

        self.assertFalse(runtime.focus_window(window))

    def test_skips_non_focusable_nodes(self):
        component = FakeComponent(grabs=True)
        window = FakeNode(children=(FakeNode(states=(STATE.VISIBLE,), component=component),))

        self.assertFalse(runtime.focus_window(window))
        self.assertEqual(component.grab_calls, 0)

    def test_caps_the_number_of_grab_attempts(self):
        """Each grab moves focus inside the app, so the whole tree is not tried."""
        components = [FakeComponent(grabs=False) for _ in range(20)]
        window = FakeNode(
            children=[
                FakeNode(states=(STATE.FOCUSABLE,), component=c) for c in components
            ]
        )

        self.assertFalse(runtime.focus_window(window))
        attempted = sum(1 for c in components if c.grab_calls)
        self.assertEqual(attempted, runtime.FOCUS_GRAB_CANDIDATES)

    def test_none_window_is_not_focusable(self):
        self.assertFalse(runtime.focus_window(None))

    def _native_window(self, window_id, active):
        """Give the window an X11 id and an active-window reader.

        ``active`` is a one-item list holding the current _NET_ACTIVE_WINDOW;
        the returned list records every activation request sent to the
        window manager, which moves ``active`` to the requested window.
        """
        requests = []

        def activate(requested):
            requests.append(requested)
            active[0] = requested
            return True

        for name, value in (
            ("x11_window_id", lambda window: window_id),
            ("x11_active_window_id", lambda: active[0]),
            ("request_window_activation", activate),
        ):
            patcher = mock.patch.object(input_primitives, name, value)
            patcher.start()
            self.addCleanup(patcher.stop)
        return requests

    def test_inactive_window_is_activated_through_the_window_manager(self):
        """Qt accepts grab_focus on every child without activating the window.

        Asking the window manager first activates it at once; no child grab
        (each of which only moves the app's internal focus) is needed.
        """
        component = FakeComponent(grabs=True)
        window = FakeNode(
            children=(FakeNode(states=(STATE.FOCUSABLE,), component=component),))
        requests = self._native_window("0x2a", ["0x10"])

        self.assertTrue(input_primitives.focus_window(window, timeout=0.2))
        self.assertEqual(requests, ["0x2a"])
        self.assertEqual(component.grab_calls, 0)

    def test_window_already_active_in_x11_needs_no_request(self):
        window = FakeNode()
        requests = self._native_window("0x2a", ["0x2a"])

        self.assertTrue(input_primitives.focus_window(window, timeout=0.2))
        self.assertEqual(requests, [])

    def test_another_window_of_the_same_app_does_not_count_as_focus(self):
        """Input goes to the X11 active window, not to any window of the pid."""
        window = FakeNode()
        requests = self._native_window("0x2a", ["0x2b"])
        patcher = mock.patch.object(
            input_primitives, "x11_holds_focus", lambda window: True)
        patcher.start()
        self.addCleanup(patcher.stop)

        self.assertTrue(input_primitives.focus_window(window, timeout=0.2))
        self.assertEqual(requests, ["0x2a"])

    def test_accepted_grab_that_leaves_the_window_inactive_stops_grabbing(self):
        """Without a native window, one accepted grab that does not activate
        the window shows the toolkit ignores activation; the remaining
        candidates are not each given the full timeout."""
        components = [FakeComponent(grabs=True) for _ in range(3)]
        window = FakeNode(
            children=[
                FakeNode(states=(STATE.FOCUSABLE,), component=c)
                for c in components
            ]
        )

        self.assertFalse(input_primitives.focus_window(window, timeout=0.05))
        self.assertEqual([c.grab_calls for c in components], [1, 0, 0])


class RequireWindowFocusTests(AtspiPatchedTestCase):
    def test_passes_through_when_window_is_active(self):
        window = FakeNode(states=(STATE.ACTIVE,))

        runtime.require_window_focus(window, "press_key")  # must not raise

    def test_raises_instead_of_synthesizing_into_the_wrong_window(self):
        component = FakeComponent(grabs=False)
        window = FakeNode(
            children=(FakeNode(states=(STATE.FOCUSABLE,), component=component),)
        )

        with self.assertRaises(RuntimeError) as ctx:
            runtime.require_window_focus(window, "press_key")
        message = str(ctx.exception)
        self.assertIn("press_key", message)
        self.assertIn("foreground", message)


class TargetedKeyboardFocusTests(AtspiPatchedTestCase):
    def test_already_focused_target_needs_no_grab(self):
        element = FakeNode(states=(STATE.FOCUSED, STATE.EDITABLE))
        window = FakeNode(states=(STATE.ACTIVE,), children=(element,))

        runtime.focus_target_element(
            window, element,
            {"runtimeId": [0], "controlType": "", "name": ""},
            "targeted type_text",
        )

    def test_grab_focus_must_land_on_the_exact_target(self):
        component = FakeComponent()
        element = FakeNode(
            states=(STATE.EDITABLE,), component=component)
        component.grab_focus = lambda: element.states.add(STATE.FOCUSED) or True
        window = FakeNode(states=(STATE.ACTIVE,), children=(element,))

        runtime.focus_target_element(
            window, element,
            {"runtimeId": [0], "controlType": "", "name": ""},
            "targeted press_key",
        )
        self.assertIn(STATE.FOCUSED, element.states)

    def test_focus_failure_emits_no_keyboard_input(self):
        component = FakeComponent(grabs=False)
        element = FakeNode(
            states=(STATE.EDITABLE,), component=component)
        window = FakeNode(states=(STATE.ACTIVE,), children=(element,))

        with self.assertRaises(RuntimeError) as caught:
            runtime.focus_target_element(
                window, element,
                {"runtimeId": [0], "controlType": "", "name": ""},
                "targeted type_text",
            )
        self.assertIn("no keyboard input was emitted", str(caught.exception))
        # A classified precondition failure, so the caller sees why (an
        # unclassified exception reached the model as "input_failed").
        self.assertIsInstance(caught.exception, runtime.OperationFailure)
        self.assertEqual(caught.exception.failure_code, "target_not_focusable")
        self.assertEqual(caught.exception.failure_phase, "precondition")
        self.assertFalse(caught.exception.input_events_emitted)
        self.assertEqual(caught.exception.failure_subject, "element")

    def test_focus_that_never_lands_is_a_classified_failure(self):
        component = FakeComponent()
        component.grab_focus = lambda: True
        element = FakeNode(states=(STATE.EDITABLE,), component=component)
        window = FakeNode(states=(STATE.ACTIVE,), children=(element,))
        with self.assertRaises(runtime.OperationFailure) as caught:
            runtime.focus_target_element(
                window, element,
                {"runtimeId": [0], "controlType": "", "name": ""},
                "targeted set_value keyboard fallback", timeout=0.05,
            )
        self.assertEqual(caught.exception.failure_code, "target_not_focusable")
        self.assertIn("did not report focus", str(caught.exception))

    def test_cell_without_focus_report_names_the_cursor_route(self):
        """A table cell that never reports focus gets a cell-specific hint.

        Spreadsheet cells accept grab_focus and move the cell cursor, but the
        cell objects never report FOCUSED, so the target cannot be verified.
        The failure names the routes that work instead of a generic "target a
        focusable child", which does not exist for a cell.
        """
        component = FakeComponent()
        component.grab_focus = lambda: True
        cell = _NamedNode(name="B2", role="table cell", component=component)
        window = FakeNode(states=(STATE.ACTIVE,), children=(cell,))
        with self.assertRaises(runtime.OperationFailure) as caught:
            runtime.focus_target_element(
                window, cell,
                {"runtimeId": [0], "controlType": "table cell", "name": "B2"},
                "targeted type_text", timeout=0.05,
            )
        message = str(caught.exception)
        self.assertEqual(caught.exception.failure_code, "target_not_focusable")
        self.assertFalse(caught.exception.input_events_emitted)
        self.assertIn("no keyboard input was emitted", message)
        self.assertIn("Move the cell cursor there with keypress", message)
        self.assertIn("type without a ref", message)
        self.assertIn("set_value", message)
        self.assertNotIn("focusable child", message)

    def test_same_name_focused_sibling_does_not_satisfy_target_focus(self):
        target = _NamedNode(
            name="Search", role="entry",
            component=FakeComponent(grabs=False))
        sibling = _NamedNode(
            name="Search", role="entry",
            states=(STATE.FOCUSED,))
        window = FakeNode(states=(STATE.ACTIVE,), children=(target, sibling))

        with self.assertRaises(RuntimeError):
            runtime.focus_target_element(
                window, target,
                {"runtimeId": [0], "controlType": "entry", "name": "Search"},
                "targeted type_text",
            )

    def test_exact_target_resolution_never_rebinds_to_same_name_sibling(self):
        replacement = _NamedNode(name="Other", role="entry")
        same_name = _NamedNode(name="Search", role="entry")
        window = _NamedNode(
            name="Window", role="frame",
            states=(STATE.ACTIVE, STATE.SHOWING),
            children=(replacement, same_name),
            component=FakeComponent(extents=FakeExtents(0, 0, 100, 100)),
        )
        app = _NamedNode(name="App", role="application", children=(window,))
        record = {
            "runtimeId": [0, 0],
            "controlType": "entry",
            "name": "Search",
        }

        self.assertIsNone(runtime.find_element_at_snapshot_path(app, record))

    def test_targeted_type_text_uses_the_resolved_node(self):
        target = FakeNode(states=(STATE.EDITABLE, STATE.FOCUSED))
        other = FakeNode(states=(STATE.EDITABLE, STATE.FOCUSED))
        window = FakeNode(states=(STATE.ACTIVE,), children=(other, target))
        observed = []

        with mock.patch.object(runtime, "resolve_app", return_value=FakeNode()), \
                mock.patch.object(runtime, "main_window", return_value=(0, window)), \
                mock.patch.object(runtime, "extents",
                                  return_value=runtime.frame(0, 0, 100, 100)), \
                mock.patch.object(
                    runtime, "find_element_at_snapshot_path", return_value=target), \
                mock.patch.object(runtime, "current_geometry",
                                  side_effect=lambda e, r, b: (r, None)), \
                mock.patch.object(runtime, "focus_target_element"), \
                mock.patch.object(
                    runtime, "insert_text_outcome_on_node",
                    side_effect=lambda node, text: observed.append(
                        (node, text)) or ("verified", 0, len(text), True)), \
                mock.patch.object(runtime, "wait_for_ui_to_settle",
                                  return_value=None), \
                mock.patch.object(runtime, "build_snapshot",
                                  return_value={"text": "", "elements": []}), \
                mock.patch.object(runtime, "capture_window_pixels",
                                  return_value=None):
            response = perform_provider_operation({
                "intent": "type",
                "app": "x",
                "text": "hello",
                "element": {"runtimeId": [0, 1], "controlType": "", "name": ""},
            })

        self.assertTrue(response["ok"])
        self.assertEqual(observed, [(target, "hello")])

    def test_keyboard_fallback_reverifies_target_focus(self):
        target = FakeNode(states=(STATE.EDITABLE, STATE.FOCUSED))
        window = FakeNode(states=(STATE.ACTIVE,), children=(target,))
        focus_checks = []
        sent = []

        def check_focus(*_args):
            focus_checks.append(True)
            if len(focus_checks) == 2:
                raise RuntimeError("focus moved")

        with mock.patch.object(runtime, "resolve_app", return_value=FakeNode()), \
                mock.patch.object(runtime, "main_window", return_value=(0, window)), \
                mock.patch.object(runtime, "extents",
                                  return_value=runtime.frame(0, 0, 100, 100)), \
                mock.patch.object(
                    runtime, "find_element_at_snapshot_path", return_value=target), \
                mock.patch.object(runtime, "current_geometry",
                                  side_effect=lambda e, r, b: (r, None)), \
                mock.patch.object(
                    runtime, "focus_target_element", side_effect=check_focus), \
                mock.patch.object(
                    runtime, "insert_text_outcome_on_node",
                    return_value=("no_change", 0, 0, False)), \
                mock.patch.object(
                    runtime, "send_text",
                    side_effect=lambda text: sent.append(text)), \
                mock.patch.object(runtime, "capture_window_pixels",
                                  return_value=None):
            with self.assertRaises(RuntimeError):
                perform_provider_operation({
                    "intent": "type",
                    "app": "x",
                    "text": "hello",
                    "element": {
                        "runtimeId": [0, 0],
                        "controlType": "",
                        "name": "",
                    },
                })

        self.assertEqual(len(focus_checks), 2)
        self.assertEqual(sent, [])


class KeySyntaxTests(unittest.TestCase):
    def test_accepts_common_gtk_and_x11_key_spellings(self):
        self.assertEqual(
            runtime.normalize_key_expression("<Control><Shift>s"),
            "Control+Shift+s",
        )
        self.assertEqual(runtime.MODIFIER_KEYS["control_l"], "Control_L")
        self.assertEqual(runtime.MODIFIER_KEYS["ctrl_l"], "Control_L")
        self.assertEqual(runtime.KEY_ALIASES["arrowdown"], "Down")
        self.assertEqual(runtime.KEY_ALIASES["arrowup"], "Up")
        self.assertEqual(runtime.KEY_ALIASES["pageup"], "Page_Up")
        self.assertEqual(runtime.KEY_ALIASES["pagedown"], "Page_Down")
        self.assertEqual(runtime.KEY_ALIASES["slash"], "slash")
        self.assertEqual(runtime.KEY_ALIASES["forward_slash"], "slash")


class KeyResolutionTests(unittest.TestCase):
    """Keys resolve to hardware keycodes before any input is sent."""

    def setUp(self):
        if runtime.Gdk is None:
            self.skipTest("Gdk is unavailable")

    def test_punctuation_main_key_with_modifier(self):
        # Gdk.keyval_from_name(",") returns VoidSymbol, which is truthy, so
        # CTRL+, (VS Code settings) failed with "No hardware keycode".
        for key, keysym_name in (
            ("CTRL+,", "comma"),
            ("ctrl+.", "period"),
            ("ctrl+/", "slash"),
        ):
            with self.subTest(key=key):
                self.assertEqual(
                    runtime.keysym(key.split("+")[-1]),
                    runtime.Gdk.keyval_from_name(keysym_name),
                )

    def test_named_keys_in_any_case(self):
        for key, expected in (
            ("INSERT", "Insert"),
            ("caps_lock", "Caps_Lock"),
            ("MENU", "Menu"),
            ("f4", "F4"),
        ):
            with self.subTest(key=key):
                self.assertEqual(
                    runtime.keysym(key), runtime.Gdk.keyval_from_name(expected)
                )

    def test_lowercase_keysym_names_in_any_case(self):
        # X keysym names for punctuation are lowercase ("comma", "plus").
        # Callers write them as on a key legend or in upper case, as for the
        # named keys above; "CTRL+COMMA" failed with "Unsupported key: COMMA".
        for key, expected in (
            ("COMMA", "comma"),
            ("Plus", "plus"),
            ("MINUS", "minus"),
            ("PERIOD", "period"),
            ("EQUAL", "equal"),
            ("BRACKETLEFT", "bracketleft"),
        ):
            with self.subTest(key=key):
                self.assertEqual(
                    runtime.keysym(key), runtime.Gdk.keyval_from_name(expected)
                )

    def test_bare_modifier_is_a_key(self):
        # keycode needs a display keymap; the name it receives is the point.
        with mock.patch.object(runtime, "keycode", side_effect=lambda name: 1):
            _codes, normalized = runtime.parse_key("SHIFT")
        self.assertEqual(normalized, "Shift_L")

    def test_unknown_key_still_fails(self):
        self.assertEqual(runtime.keysym("NoSuchKeyName"), 0)
        with self.assertRaises(RuntimeError):
            runtime.keycode("NoSuchKeyName")


class FocusDiagnosticTests(AtspiPatchedTestCase):
    def test_names_the_window_that_actually_holds_focus(self):
        other = _TreeNode(role="frame", name="Terminal", w=100, h=100)
        other.states.add(STATE.ACTIVE)
        app = _TreeNode(role="application", name="gnome-terminal", kids=(other,))
        original = runtime.iter_apps
        runtime.iter_apps = lambda: [app]
        self.addCleanup(setattr, runtime, "iter_apps", original)

        target = _TreeNode(role="frame", name="Editor", w=100, h=100)
        with self.assertRaises(RuntimeError) as caught:
            runtime.require_window_focus(target, "press_key")

        self.assertIn("Terminal", str(caught.exception))
        self.assertIn("gnome-terminal", str(caught.exception))

    def test_no_accessible_window_active_points_at_the_screenshot_channel(self):
        """Focus held by something invisible to the accessibility tree is reported.

        After settings.json changes, VS Code can show a native "A setting has
        changed that requires a restart to take effect." dialog. It belongs to
        the same process and locks the app, yet does not exist in AT-SPI. The
        agent sees a normal tree while every action is rejected for no visible
        reason; only a screenshot can explain it.
        """
        original = runtime.iter_apps
        runtime.iter_apps = lambda: []
        self.addCleanup(setattr, runtime, "iter_apps", original)

        target = _TreeNode(role="frame", name="Editor", w=100, h=100)
        with self.assertRaises(RuntimeError) as caught:
            runtime.require_window_focus(target, "press_key")

        message = str(caught.exception)
        self.assertIn("cannot see", message)
        self.assertIn("gui", message)

    def test_diagnostic_never_turns_a_clear_error_into_a_crash(self):
        """If the diagnostic enumeration fails, the original error is still raised."""

        def boom():
            raise RuntimeError("a11y bus went away")

        original = runtime.iter_apps
        runtime.iter_apps = boom
        self.addCleanup(setattr, runtime, "iter_apps", original)

        target = _TreeNode(role="frame", name="Editor", w=100, h=100)
        with self.assertRaises(RuntimeError) as caught:
            runtime.require_window_focus(target, "press_key")

        self.assertIn("Refusing to synthesize", str(caught.exception))
