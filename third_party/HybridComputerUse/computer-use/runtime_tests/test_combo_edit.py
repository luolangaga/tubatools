"""Composite combo edits bind the editor, while activation keeps the toggle."""

from runtime_test_support import *  # noqa: F401,F403
from runtime_tests.test_capabilities import _Node
from types import SimpleNamespace


class ComboEditTests(AtspiPatchedTestCase):
    def setUp(self):
        super().setUp()
        # Fake nodes must not depend on whether their PID exists on the host.
        mock.patch.object(runtime, "process_generation", return_value="fixture-generation").start()
        self.focus = mock.patch.object(runtime, "focus_target_element").start()
        self.key = mock.patch.object(runtime, "send_key").start()
        self.text = mock.patch.object(runtime, "send_text").start()
        self.addCleanup(mock.patch.stopall)

    def group(self, editable=True, iface=None):
        editor = _Node(role="text", states=(STATE.EDITABLE, STATE.SHOWING, STATE.VISIBLE) if editable else ())
        editor.text_iface = iface or FakeTextIface("old", caret=3)
        editor.get_editable_text_iface = lambda: editor.text_iface
        editor.get_value_iface = lambda: None
        combo = _Node(role="combo box", name="Find", states=())
        toggle = _Node(role="toggle button")
        parent = _Node(role="filler").add(editor, combo, toggle)
        record = runtime.record_for(toggle, 7, [0, 2], None)
        return parent, editor, toggle, record

    def invoke(self, intent, toggle, record, value="new"):
        evidence = {}
        method = runtime._perform_type if intent == "type" else runtime._perform_set_value
        method({"text": value, "value": value}, None, toggle, record, [], evidence)
        return evidence

    def test_set_value_writes_and_reads_editor_without_toggle_input(self):
        _parent, editor, toggle, record = self.group()
        self.assertTrue(record["setValue"])
        self.assertEqual(record["value"], "old")
        self.assertEqual(record["providerObjectPath"], toggle.path)
        self.assertEqual(record["editTarget"]["providerObjectPath"], editor.path)
        evidence = self.invoke("set_value", toggle, record)
        self.assertEqual(editor.text_iface.text, "new")
        self.assertTrue(evidence["readback_verified"])
        self.focus.assert_not_called()
        self.key.assert_not_called()
        self.text.assert_not_called()

    def test_type_inserts_at_editor_caret_and_reads_back(self):
        _parent, editor, toggle, record = self.group()
        evidence = self.invoke("type", toggle, record, "!")
        self.assertEqual(editor.text_iface.text, "old!")
        self.assertTrue(evidence["readback_verified"])
        self.focus.assert_not_called()
        self.text.assert_not_called()

    def test_unicode_insertion_uses_byte_length_and_character_offset(self):
        calls = []
        def insert_bytes(iface, offset, payload, length):
            calls.append((offset, length))
            delivered = payload.encode("utf-8")[:length].decode("utf-8", errors="ignore")
            return iface.insert(offset, delivered)
        for route in ("combo", "entry", "focused"):
            for payload in ("targeted type 字", "🙂é字", "e\u0301"):
                with self.subTest(route=route, payload=payload):
                    iface = FakeTextIface("前後", caret=1)
                    if route == "combo":
                        _parent, _editor, node, record = self.group(iface=iface)
                    else:
                        node = FakeNode(states=(STATE.EDITABLE,), text=iface)
                        record = {"controlType": "entry"} if route == "entry" else None
                    evidence = {}
                    with mock.patch.object(runtime.Atspi.EditableText, "insert_text", side_effect=insert_bytes), \
                            mock.patch.object(runtime, "find_editable_text", return_value=node):
                        runtime._perform_type(
                            {"text": payload}, None, node, record, [], evidence)
                    self.assertEqual(iface.text, "前" + payload + "後")
                    self.assertEqual(calls[-1], (1, len(payload.encode("utf-8"))))
                    self.assertTrue(evidence["readback_verified"])
        self.text.assert_not_called()

    def test_readonly_combo_never_receives_text(self):
        for intent in ("set_value", "type"):
            with self.subTest(intent=intent):
                _parent, editor, toggle, record = self.group(editable=False)
                self.assertFalse(record["setValue"])
                self.assertNotIn("editTarget", record)
                with self.assertRaises(runtime.OperationFailure) as caught:
                    self.invoke(intent, toggle, record)
                self.assertFalse(caught.exception.input_events_emitted)
                self.assertEqual(editor.text_iface.set_calls, [])
                self.assertEqual(editor.text_iface.inserts, [])
        self.focus.assert_not_called()
        self.key.assert_not_called()

    def test_unmapped_editable_placeholder_is_not_a_combo_editor(self):
        _parent, editor, toggle, _record = self.group()
        editor.states.discard(STATE.SHOWING)
        editor.states.discard(STATE.VISIBLE)
        record = runtime.record_for(toggle, 7, [0, 2], None)
        self.assertFalse(record["setValue"])
        self.assertNotIn("editTarget", record)
        for intent in ("set_value", "type"):
            with self.subTest(intent=intent):
                with self.assertRaises(runtime.OperationFailure) as caught:
                    self.invoke(intent, toggle, record)
                self.assertFalse(caught.exception.input_events_emitted)
        self.assertEqual(editor.text_iface.set_calls, [])
        self.assertEqual(editor.text_iface.inserts, [])
        self.assertEqual(record["providerObjectPath"], toggle.path)
        self.focus.assert_not_called()
        self.key.assert_not_called()
        self.text.assert_not_called()

    def test_missing_ambiguous_and_replaced_partner_fail_before_input(self):
        for change in ("missing", "ambiguous", "replaced", "readonly", "hidden", "defunct", "other_parent"):
            for intent in ("set_value", "type"):
                with self.subTest(change=change, intent=intent):
                    parent, editor, toggle, record = self.group()
                    if change == "missing":
                        parent.children.remove(editor)
                    elif change == "ambiguous":
                        parent.add(_Node(role="text", states=(STATE.EDITABLE,)))
                    elif change == "replaced":
                        editor.path += "-replacement"
                    elif change == "readonly":
                        editor.states.clear()
                    elif change == "hidden":
                        editor.states.discard(STATE.SHOWING)
                    elif change == "defunct":
                        editor.states.add(STATE.DEFUNCT)
                    else:
                        editor.parent = _Node(role="filler")
                    with self.assertRaises(runtime.OperationFailure) as caught:
                        self.invoke(intent, toggle, record)
                    self.assertFalse(caught.exception.input_events_emitted)
                    self.assertEqual(editor.text_iface.text, "old")
        self.focus.assert_not_called()
        self.key.assert_not_called()
        self.text.assert_not_called()

    def test_old_ref_cannot_bind_fresh_replacement_record(self):
        _parent, _editor, toggle, record = self.group()
        expected = dict(record["editTarget"])
        expected["providerObjectPath"] += "-old"
        with self.assertRaises(runtime.OperationFailure) as caught:
            runtime._live_node_for_semantic_match(
                None, {"editTarget": expected},
                SimpleNamespace(allow_provider_rebind=False), record, None)
        self.assertFalse(caught.exception.input_events_emitted)
        self.assertEqual(caught.exception.failure_code, "stale_element_ref")

    def test_partial_write_reported_false_is_unknown_without_retry(self):
        _parent, editor, toggle, record = self.group()
        def partial(payload):
            editor.text_iface.text = payload[:1]
            return False
        editor.text_iface.set_contents = partial
        with self.assertRaises(runtime.OperationFailure) as caught:
            self.invoke("set_value", toggle, record)
        self.assertEqual(caught.exception.failure_code, "effect_unknown")
        self.assertTrue(caught.exception.input_events_emitted)
        self.assertEqual(editor.text_iface.text, "n")
        self.focus.assert_not_called()
        self.text.assert_not_called()

    def test_unreadable_write_is_unknown_without_retry(self):
        _parent, _editor, toggle, record = self.group()
        with mock.patch.object(runtime, "read_all_text", side_effect=["old", None]):
            with self.assertRaises(runtime.OperationFailure) as caught:
                self.invoke("set_value", toggle, record)
        self.assertEqual(caught.exception.failure_code, "effect_unknown")
        self.assertTrue(caught.exception.input_events_emitted)
        self.text.assert_not_called()

    def test_partial_insertion_reported_false_is_unknown_without_retry(self):
        _parent, editor, toggle, record = self.group()
        def partial(offset, payload):
            editor.text_iface.text += payload[:1]
            return False
        editor.text_iface.insert = partial
        with self.assertRaises(runtime.OperationFailure) as caught:
            self.invoke("type", toggle, record)
        self.assertEqual(caught.exception.failure_code, "effect_unknown")
        self.assertTrue(caught.exception.input_events_emitted)
        self.assertEqual(editor.text_iface.text, "oldn")
        self.text.assert_not_called()

    def test_private_wire_fixture_and_incomplete_identity(self):
        fixture = Path(__file__).resolve().parents[1] / "testdata/provider-wire/combo-edit.request.json"
        request = json.loads(fixture.read_text())
        wire.validate_request(request)
        del request["action"]["target"]["editTarget"]["processGeneration"]
        with self.assertRaises(wire.ProviderRequestError):
            wire.validate_request(request)

    def test_partial_selection_delete_then_failed_insert_never_retries(self):
        iface = FakeTextIface("abc", selection=(0, 2), accept=False)
        _parent, editor, toggle, record = self.group(iface=iface)
        with self.assertRaises(runtime.OperationFailure) as caught:
            self.invoke("type", toggle, record)
        self.assertEqual(caught.exception.failure_code, "effect_unknown")
        self.assertTrue(caught.exception.input_events_emitted)
        self.assertEqual(editor.text_iface.text, "c")
        self.text.assert_not_called()

    def test_plain_entry_retains_semantic_set_and_targeted_type(self):
        iface = FakeTextIface("old", caret=3)
        node = FakeNode(states=(STATE.EDITABLE,), text=iface)
        record = {"controlType": "entry"}
        self.invoke("set_value", node, record)
        self.invoke("type", node, record, "!")
        self.assertEqual(iface.text, "new!")
        self.focus.assert_called_once()

    def test_plain_toggle_activation_still_resolves_to_toggle(self):
        _parent, _editor, toggle, record = self.group()
        self.assertTrue(runtime.record_still_matches(toggle, record, None))
        self.assertEqual(record["providerObjectPath"], toggle.path)

