"""Clicking an editor must focus it instead of invoking its submit action."""

from runtime_test_support import *  # noqa: F401,F403


class EditableClickTests(AtspiPatchedTestCase):
    def editor(self, action="activate"):
        node = FakeNode(states=(STATE.EDITABLE,), text=FakeTextIface("old"))
        node.get_action_iface = lambda: node if action is not None else None
        node.get_n_actions = lambda: 1 if action is not None else 0
        node.get_action_name = lambda index: action
        node.get_action_description = lambda index: ""
        return node

    def invoke(self, node, record, evidence):
        return runtime._auto_click(
            {}, object(), {}, node, record, 1, [], evidence)

    def test_editable_activate_is_not_used_as_a_click(self):
        node = self.editor()
        record = {"controlType": "entry"}
        with mock.patch.object(runtime, "node_role", return_value="entry"), \
                mock.patch.object(runtime, "_global_click") as pointer, \
                mock.patch.object(runtime, "focus_target_element") as focus, \
                mock.patch.object(runtime, "do_action_by_index") as activate:
            evidence = {}
            self.assertIs(self.invoke(node, record, evidence), record)
        focus.assert_called_once()
        pointer.assert_not_called()
        self.assertTrue(evidence["target_focus_verified"])
        activate.assert_not_called()

    def test_unconfirmed_focus_reports_possible_focus_change(self):
        node = self.editor()
        failure = runtime.OperationFailure(
            "focus stayed on the outer row", failure_code="target_not_focusable",
            failure_phase="precondition", input_events_emitted=False)
        with mock.patch.object(runtime, "node_role", return_value="text"), \
                mock.patch.object(runtime, "focus_target_element", side_effect=failure):
            with self.assertRaises(runtime.OperationFailure) as caught:
                self.invoke(node, {}, {})
        self.assertTrue(caught.exception.input_events_emitted)
        self.assertEqual(caught.exception.failure_code, "effect_unknown")
        self.assertEqual(caught.exception.failure_phase, "readback")

    def test_missing_geometry_does_not_prevent_semantic_editor_focus(self):
        node = self.editor()
        with mock.patch.object(runtime, "node_role", return_value="text"), \
                mock.patch.object(runtime, "_global_click", side_effect=RuntimeError("no geometry")) as pointer, \
                mock.patch.object(runtime, "focus_target_element") as focus, \
                mock.patch.object(runtime, "do_action_by_index") as activate:
            self.invoke(node, {}, {})
        focus.assert_called_once()
        pointer.assert_not_called()
        activate.assert_not_called()

    def test_plain_focus_exception_reports_possible_focus_change(self):
        node = self.editor()
        with mock.patch.object(runtime, "node_role", return_value="entry"), \
                mock.patch.object(runtime, "focus_target_element", side_effect=RuntimeError("window lost focus")):
            with self.assertRaises(runtime.OperationFailure) as caught:
                self.invoke(node, {}, {})
        self.assertTrue(caught.exception.input_events_emitted)
        self.assertEqual(caught.exception.failure_code, "effect_unknown")

    def test_button_retains_semantic_activation(self):
        node = FakeNode()
        with mock.patch.object(runtime, "node_role", return_value="push button"), \
                mock.patch.object(runtime, "is_dropdown_item", return_value=False), \
                mock.patch.object(runtime, "action_only_sets_state", return_value=False), \
                mock.patch.object(runtime, "preferred_action_index", return_value=0), \
                mock.patch.object(runtime, "do_action_by_index", return_value=True) as activate, \
                mock.patch.object(runtime, "_global_click") as pointer:
            evidence = {}
            self.invoke(node, {}, evidence)
        activate.assert_called_once_with(node, 0)
        pointer.assert_not_called()
        self.assertEqual(evidence["route"], "semantic_action")

    def test_editable_click_retains_its_native_click_action(self):
        node = self.editor("click")
        with mock.patch.object(runtime, "node_role", return_value="entry"), \
                mock.patch.object(runtime, "focus_target_element") as focus, \
                mock.patch.object(runtime, "do_action_by_index", return_value=True) as click:
            evidence = {}
            self.invoke(node, {}, evidence)
        click.assert_called_once_with(node, 0)
        focus.assert_not_called()
        self.assertEqual(evidence["route"], "semantic_action")
        self.assertNotIn("target_focus_verified", evidence)

    def test_editor_without_action_keeps_coordinate_safety_checks(self):
        node = self.editor(None)
        failure = runtime.OperationFailure(
            "target geometry unavailable", failure_code="observation_incomplete",
            failure_phase="precondition", input_events_emitted=False)
        with mock.patch.object(runtime, "node_role", return_value="entry"), \
                mock.patch.object(runtime, "focus_target_element") as focus, \
                mock.patch.object(runtime, "require_stable_coordinate_geometry", side_effect=failure) as geometry:
            with self.assertRaises(runtime.OperationFailure) as caught:
                self.invoke(node, {}, {})
        geometry.assert_called_once()
        focus.assert_not_called()
        self.assertFalse(caught.exception.input_events_emitted)
