#!/usr/bin/env python3
"""Linux provider channel tagging, action notes, and snapshot diagnostic tests."""

from runtime_test_support import *  # noqa: F401,F403


class ChannelTaggingTests(AtspiPatchedTestCase):
    """Every action note carries a channel tag.

    The tags make the ratio of semantic to coordinate actions measurable and
    separate "the agent did not use accessibility" from "it tried and fell
    back after a failure"; the two cases need opposite fixes.
    """

    def setUp(self):
        super().setUp()
        self.window = FakeNode(states=(STATE.ACTIVE,))
        for name, value in (
            ("resolve_app", lambda q: FakeNode()),
            ("main_window", lambda a: (0, self.window)),
            ("extents", lambda n: runtime.frame(0, 0, 100, 100)),
            ("find_element_at_snapshot_path", lambda a, r: None),
            ("build_snapshot", lambda *a, **k: {"text": ""}),
            ("require_window_focus", lambda w, what: None),
            ("parse_key", lambda k: ([], k)),
            ("send_key", lambda k: None),
            ("send_text", lambda t: None),
            ("send_mouse_click", lambda *a: None),
            # Match scroll_element's signature: it takes an optional element
            # point and returns the route used, which the note text reports.
            ("scroll_element", lambda d, p, point=None: "keys"),
            ("time", _NoSleep()),
        ):
            original = getattr(runtime, name)
            setattr(runtime, name, value)
            self.addCleanup(setattr, runtime, name, original)

    def _notes(self, op):
        """Return only the action notes.

        The `[pixels]` note is a tree-independent effect check appended for
        every action type, so it stays out of assertions about the action.
        """
        return [n for n in perform_provider_operation(op).get("notes", [])
                if not n.startswith("[pixels]")]

    def test_semantic_and_synthesis_are_distinguishable(self):
        original = runtime.insert_text_outcome_on_node
        runtime.insert_text_outcome_on_node = (
            lambda root, text: ("verified", 0, 2, True)
        )
        self.addCleanup(
            setattr, runtime, "insert_text_outcome_on_node", original
        )

        semantic = self._notes({"intent": "type", "app": "x", "text": "hi"})
        self.assertIn("[semantic] ", semantic[0])

        runtime.insert_text_outcome_on_node = (
            lambda root, text: ("no_change", 0, 0, False)
        )
        synthesis = self._notes({"intent": "type", "app": "x", "text": "hi"})
        self.assertIn("[synthesis] ", synthesis[0])

    def test_every_action_path_carries_a_channel_tag(self):
        original = runtime.insert_text_outcome_on_node
        runtime.insert_text_outcome_on_node = (
            lambda root, text: ("no_change", 0, 0, False)
        )
        self.addCleanup(
            setattr, runtime, "insert_text_outcome_on_node", original
        )

        for op in (
            {"intent": "keypress", "app": "x", "key": "a"},
            {"intent": "type", "app": "x", "text": "hi"},
            {"intent": "scroll", "app": "x", "direction": "down", "pages": 1},
        ):
            notes = self._notes(op)
            self.assertTrue(notes, "{} produced no note".format(op["intent"]))
            # Each note is tagged on two orthogonal axes, addressing channel
            # and execution path; the agent needs both to choose a check.
            self.assertTrue(
                notes[0].startswith(("[a11y]", "[gui]", "[keyboard]")),
                "{} note lacks the addressing-channel label: {}".format(op["intent"], notes[0][:70]),
            )
            self.assertTrue(
                "[semantic] " in notes[0] or "[synthesis] " in notes[0],
                "{} note lacks the execution-path label: {}".format(op["intent"], notes[0][:70]),
            )


class ActionNotesTests(AtspiPatchedTestCase):
    """Action notes state which path ran and whether the result was confirmed."""

    def setUp(self):
        super().setUp()
        self.window = FakeNode(states=(STATE.ACTIVE,))
        self._patch("resolve_app", lambda query: FakeNode())
        self._patch("main_window", lambda app: (0, self.window))
        self._patch("extents", lambda node: runtime.frame(0, 0, 100, 100))
        self._patch("desktop_bounds", lambda: runtime.frame(0, 0, 1920, 1080))
        self._patch("find_element_at_snapshot_path", lambda app, record: None)
        self._patch("build_snapshot", lambda *a, **k: {"text": ""})
        self._patch("parse_key", lambda key: ([], key))
        self._patch("send_key", lambda key: None)
        self._patch("send_text", lambda text: None)
        self._patch("send_mouse_click", lambda *a: None)
        self._patch("coordinate_point_owner", lambda window, x, y: True)
        self._patch("time", _NoSleep())

    def _patch(self, name, value):
        original = getattr(runtime, name)
        setattr(runtime, name, value)
        self.addCleanup(setattr, runtime, name, original)

    def test_direct_write_is_reported_as_confirmed(self):
        self._patch(
            "insert_text_outcome_on_node",
            lambda root, text: ("verified", 3, 8, True),
        )

        notes = [n for n in perform_provider_operation(
            {"intent": "type", "app": "x", "text": "hello"}
        )["notes"] if not n.startswith("[pixels]")]

        self.assertEqual(len(notes), 1)
        self.assertIn("confirm it landed (3 -> 8 characters)", notes[0])
        self.assertNotIn("not verified", notes[0])
        # The confirmation covers the control, not the application: many
        # dialogs commit control state to the document only on OK/Apply.
        self.assertIn("may still require OK/Apply", notes[0])

    def test_synthesis_fallback_is_reported_as_unverified(self):
        self._patch(
            "insert_text_outcome_on_node",
            lambda root, text: ("no_change", 0, 0, False),
        )

        notes = perform_provider_operation(
            {"intent": "type", "app": "x", "text": "hello"}
        )["notes"]

        self.assertIn("fell back to", notes[0])
        self.assertIn("was not verified", notes[0])

    def test_delivery_only_action_skips_settle_and_snapshot(self):
        self._patch(
            "wait_for_ui_to_settle",
            mock.Mock(side_effect=AssertionError("settle must be skipped")),
        )
        self._patch(
            "build_snapshot",
            mock.Mock(side_effect=AssertionError("snapshot must be skipped")),
        )
        self._patch(
            "capture_window_pixels",
            mock.Mock(side_effect=AssertionError("no pixel note, no pixel sample")),
        )
        response = runtime.dispatch_provider_operation(
            {
                "app": "x",
                "input": {"key": "ENTER"},
                "observation": {"capture_snapshot": False},
            },
            operation_kind="resolve_and_act",
            action_intent="keypress",
        )
        self.assertTrue(response["ok"])
        self.assertTrue(response["observationSkipped"])
        self.assertTrue(response["actionEvidence"]["input_emitted"])
        self.assertNotIn("snapshot", response)

    def test_press_key_is_always_reported_as_unverified(self):
        notes = perform_provider_operation(
            {"intent": "keypress", "app": "x", "key": "ctrl+a"}
        )["notes"]

        self.assertIn("ctrl+a", notes[0])
        self.assertIn("was not verified", notes[0])

    def test_accessibility_click_is_reported_as_a_semantic_action(self):
        element = FakeNode()
        self._patch("find_element_at_snapshot_path", lambda app, record: element)
        self._patch("preferred_action_index", lambda node: 0)
        self._patch("do_action_by_index", lambda node, index: True)

        notes = perform_provider_operation(
            {"intent": "click", "app": "x", "click_method": "auto", "x": 1, "y": 1}
        )["notes"]

        self.assertIn("AT-SPI accessibility action", notes[0])
        self.assertNotIn("not verified", notes[0])

    def test_auto_click_reports_the_coordinate_fallback(self):
        element = FakeNode()
        self._patch("find_element_at_snapshot_path", lambda app, record: element)
        self._patch("preferred_action_index", lambda node: None)
        self._patch("do_action_by_index", lambda node, index: False)

        notes = perform_provider_operation(
            {"intent": "click", "app": "x", "click_method": "auto", "x": 1, "y": 1}
        )["notes"]

        self.assertIn("No usable AT-SPI action", notes[0])
        self.assertIn("was not verified", notes[0])

    def test_auto_click_fails_closed_for_menu_without_semantic_action(self):
        element = FakeNode()
        pointer_calls = []
        self._patch("find_element_at_snapshot_path", lambda app, record: element)
        self._patch("preferred_action_index", lambda node: None)
        self._patch("do_action_by_index", lambda node, index: False)
        self._patch("node_role", lambda node: "menu")
        self._patch("node_name", lambda node: "Format")
        self._patch(
            "send_mouse_click",
            lambda *args: pointer_calls.append(args),
        )

        with self.assertRaises(runtime.OperationFailure) as raised:
            perform_provider_operation(
                {
                    "intent": "click",
                    "app": "x",
                    "click_method": "auto",
                    "x": 1,
                    "y": 1,
                }
            )

        self.assertEqual(
            raised.exception.failure_code,
            "semantic_action_unavailable",
        )
        self.assertFalse(raised.exception.input_events_emitted)
        self.assertIn("automatic coordinate fallback is disabled", str(raised.exception))
        self.assertEqual(pointer_calls, [])

    def test_auto_multi_click_uses_real_pointer_events_not_one_semantic_action(self):
        element = FakeNode()
        self._patch("find_element_at_snapshot_path", lambda app, record: element)
        semantic_calls = []
        pointer_calls = []
        self._patch(
            "do_action_by_index",
            lambda node, index: semantic_calls.append((node, index)) or True)
        self._patch(
            "screen_point", lambda bounds, element=None, x=None, y=None: (10, 20))
        self._patch(
            "current_geometry",
            lambda element, record, bounds: (record, None))
        self._patch(
            "send_mouse_click",
            lambda x, y, button, count: pointer_calls.append(
                (x, y, button, count)))

        notes = perform_provider_operation({
            "intent": "click",
            "app": "x",
            "click_method": "auto",
            "click_count": 2,
            "element": {"frame": runtime.frame(0, 0, 10, 10)},
        })["notes"]

        self.assertEqual(semantic_calls, [])
        self.assertEqual(pointer_calls, [(10, 20, "left", 2)])
        self.assertTrue(any("2 coordinate click(s)" in note for note in notes))


class ActionReplyTimeoutTests(AtspiPatchedTestCase):
    """A semantic action whose reply times out was still delivered.

    Qt runs a modal dialog's event loop inside the action handler
    (QDialog::exec), so the D-Bus reply to DoAction arrives only after the
    dialog closes. Okular's "Go to Page..." menu item opened its dialog,
    the call timed out, and the click fell back to a coordinate click that
    the new dialog blocked; the result said no input was emitted.
    """

    def setUp(self):
        super().setUp()
        self.window = FakeNode(states=(STATE.ACTIVE,))
        for name, value in (
            ("resolve_app", lambda q: FakeNode()),
            ("main_window", lambda a: (0, self.window)),
            ("extents", lambda n: runtime.frame(0, 0, 100, 100)),
            ("build_snapshot", lambda *a, **k: {"text": ""}),
            ("require_window_focus", lambda w, what: None),
            ("time", _NoSleep()),
        ):
            original = getattr(runtime, name)
            setattr(runtime, name, value)
            self.addCleanup(setattr, runtime, name, original)

    def _no_reply_node(self):
        from gi.repository import GLib

        node = FakeNode()
        calls = []

        def do_action(index):
            calls.append(index)
            raise GLib.Error.new_literal(
                GLib.quark_from_string("atspi_error"),
                "Did not receive a reply. Possible causes include: the remote "
                "application did not send a reply, the message bus security "
                "policy blocked the reply, the reply timeout expired, or the "
                "network connection was broken.",
                1,
            )

        node.do_action = do_action
        return node, calls

    def test_reply_timeout_counts_as_delivered_without_pointer_fallback(self):
        element, calls = self._no_reply_node()
        pointer = []
        for name, value in (
            ("find_element_at_snapshot_path", lambda a, r: element),
            ("send_mouse_click", lambda *args: pointer.append(args)),
            ("preferred_action_index", lambda node: 0),
        ):
            original = getattr(runtime, name)
            setattr(runtime, name, value)
            self.addCleanup(setattr, runtime, name, original)

        response = perform_provider_operation(
            {"intent": "click", "app": "x", "click_method": "auto",
             "x": 1, "y": 1})

        self.assertEqual(calls, [0])
        self.assertEqual(pointer, [])
        self.assertEqual(response["actionEvidence"]["route"], "semantic_action")
        self.assertTrue(response["actionEvidence"]["input_emitted"])
        self.assertTrue(any("did not reply" in note for note in response["notes"]))

    def test_other_action_errors_still_mean_not_invoked(self):
        element = FakeNode()

        def refused(index):
            raise RuntimeError("no such action")

        element.do_action = refused
        self.assertFalse(runtime.do_action_by_index(element, 0))


class SnapshotDiagnosticsTests(AtspiPatchedTestCase):
    """An app whose accessibility tree holds only its window frame is flagged.

    Chrome started without --force-renderer-accessibility registers with
    AT-SPI and exposes its window title, yet the tree has a single element.
    Without a note the agent cannot tell an empty UI from one it cannot see.
    """

    def test_flags_app_that_exposes_only_its_window_frame(self):
        records = [
            {
                "controlType": "frame",
                "name": "about:blank - Google Chrome",
                "actions": ["doDefault", "showContextMenu"],
            }
        ]

        notes = runtime.snapshot_diagnostics(records)

        self.assertEqual(len(notes), 1)
        self.assertIn("ACCESSIBILITY CONTENT UNCERTAIN", notes[0])
        self.assertIn("does NOT prove the visible window is empty", notes[0])
        self.assertIn("not", notes[0].lower())
        self.assertNotIn("need --force-renderer-accessibility", notes[0])

    def test_stays_silent_when_the_tree_has_real_content(self):
        records = [
            {"controlType": "frame", "name": "Untitled - gedit", "actions": []},
            {"controlType": "push button", "name": "Save", "actions": ["click"]},
        ]

        self.assertEqual(runtime.snapshot_diagnostics(records), [])

    def test_a_named_non_container_alone_counts_as_content(self):
        records = [
            {"controlType": "frame", "name": "Doc", "actions": []},
            {"controlType": "label", "name": "hello", "actions": []},
        ]

        self.assertEqual(runtime.snapshot_diagnostics(records), [])

    def test_containers_without_content_do_not_count(self):
        """Unnamed containers without actions amount to an empty shell."""
        records = [
            {"controlType": "frame", "name": "App", "actions": ["doDefault"]},
            {"controlType": "panel", "name": "", "actions": []},
            {"controlType": "filler", "name": "", "actions": []},
        ]

        self.assertEqual(len(runtime.snapshot_diagnostics(records)), 1)

    def test_empty_record_set_is_not_diagnosed(self):
        """No records at all means no window was read, which is out of scope here."""
        self.assertEqual(runtime.snapshot_diagnostics([]), [])

    def test_get_app_state_returns_the_diagnostic_as_notes(self):
        shell = {
            "elements": [
                {"controlType": "frame", "name": "W", "actions": ["doDefault"]}
            ]
        }
        original = runtime.build_snapshot
        runtime.build_snapshot = lambda *a, **k: shell
        self.addCleanup(setattr, runtime, "build_snapshot", original)

        response = perform_provider_operation({"intent": "observe", "app": "x"})

        self.assertTrue(response["ok"])
        self.assertEqual(len(response["notes"]), 1)
        self.assertIn("ACCESSIBILITY CONTENT UNCERTAIN", response["notes"][0])


class ContentHostDiagnosticTests(unittest.TestCase):
    def test_generic_text_table_or_list_does_not_clear_host_uncertainty(self):
        for generic_role in ("text", "table", "list"):
            with self.subTest(role=generic_role):
                records = [
                    {
                        "runtimeId": [0],
                        "controlType": "frame",
                        "name": "Workspace",
                    },
                    {
                        "runtimeId": [0, 0],
                        "controlType": "page tab list",
                        "name": "Tabs",
                    },
                    {
                        "runtimeId": [0, 1],
                        "controlType": generic_role,
                        "name": "Sidebar",
                    },
                ]

                notes = runtime.content_host_diagnostic(records)

                self.assertEqual(len(notes), 1)
                self.assertIn("CONTENT PLANE UNCERTAIN", notes[0])
                self.assertIn(
                    "Ordinary text/table/list nodes elsewhere", notes[0])
                self.assertIn("not proof", notes[0])
                self.assertNotIn("CONTENT PLANE MISSING", notes[0])
                self.assertIn(
                    "does not expose per-node rectangles", notes[0])
                self.assertNotIn("Frame values shown here", notes[0])

    def test_distinctive_content_root_clears_the_diagnostic(self):
        records = [
            {"runtimeId": [0], "controlType": "frame", "name": "Workspace"},
            {
                "runtimeId": [0, 0],
                "controlType": "page tab list",
                "name": "Tabs",
            },
            {
                "runtimeId": [0, 1],
                "controlType": "document web",
                "name": "Page",
            },
        ]

        self.assertEqual(runtime.content_host_diagnostic(records), [])

    def test_empty_embedded_container_is_only_an_uncertain_signal(self):
        records = [
            {"runtimeId": [0], "controlType": "frame", "name": "Workspace"},
            {
                "runtimeId": [0, 0],
                "controlType": "viewport",
                "name": "",
            },
        ]

        notes = runtime.content_host_diagnostic(records)

        self.assertEqual(len(notes), 1)
        self.assertIn("CONTENT PLANE UNCERTAIN", notes[0])
        self.assertIn("uncertain diagnostic", notes[0])
        self.assertIn("renderer does not export accessibility", notes[0])
        self.assertNotIn("The content is on screen", notes[0])
