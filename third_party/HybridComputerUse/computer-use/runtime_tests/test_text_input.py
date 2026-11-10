#!/usr/bin/env python3
"""Linux provider editable text discovery, text insertion, caret, and set_value tests."""

from runtime_test_support import *  # noqa: F401,F403


class FindEditableTextTests(AtspiPatchedTestCase):
    def test_skips_node_that_lacks_editable_state(self):
        """The first EditableText node in tree order may be a hidden placeholder.

        It lacks the EDITABLE state, so text written to it is silently lost.
        """
        placeholder = FakeNode(
            states=(STATE.VISIBLE, STATE.ENABLED), text=FakeTextIface(honest=False)
        )
        real_editor = FakeNode(
            states=(STATE.FOCUSED, STATE.EDITABLE, STATE.SHOWING),
            text=FakeTextIface(),
        )
        window = FakeNode(children=(placeholder, real_editor))

        self.assertIs(runtime.find_editable_text(window), real_editor)

    def test_prefers_focused_over_merely_showing(self):
        showing = FakeNode(states=(STATE.EDITABLE, STATE.SHOWING), text=FakeTextIface())
        focused = FakeNode(
            states=(STATE.EDITABLE, STATE.SHOWING, STATE.FOCUSED), text=FakeTextIface()
        )
        window = FakeNode(children=(showing, focused))

        self.assertIs(runtime.find_editable_text(window), focused)

    def test_does_not_write_to_showing_but_unfocused_editor(self):
        offscreen = FakeNode(states=(STATE.EDITABLE,), text=FakeTextIface())
        showing = FakeNode(states=(STATE.EDITABLE, STATE.SHOWING), text=FakeTextIface())
        window = FakeNode(children=(offscreen, showing))

        self.assertIsNone(runtime.find_editable_text(window))

    def test_refuses_to_guess_between_multiple_showing_editors(self):
        first = FakeNode(states=(STATE.EDITABLE, STATE.SHOWING), text=FakeTextIface())
        second = FakeNode(states=(STATE.EDITABLE, STATE.SHOWING), text=FakeTextIface())
        window = FakeNode(children=(first, second))

        self.assertIsNone(runtime.find_editable_text(window))

    def test_returns_none_when_no_node_has_editable_state(self):
        window = FakeNode(
            children=(
                FakeNode(states=(STATE.VISIBLE,), text=FakeTextIface()),
                FakeNode(states=(STATE.ENABLED,), text=FakeTextIface()),
            )
        )

        self.assertIsNone(runtime.find_editable_text(window))

    def test_ignores_nodes_without_editable_text_iface(self):
        read_only = FakeNode(
            states=(STATE.EDITABLE, STATE.FOCUSED),
            text=FakeTextIface(),
            editable=False,
        )
        editor = FakeNode(
            states=(STATE.EDITABLE, STATE.SHOWING), text=FakeTextIface())
        window = FakeNode(children=(read_only, editor))

        self.assertIsNone(runtime.find_editable_text(window))

    def test_unique_showing_editor_is_not_treated_as_focused(self):
        editor = FakeNode(
            states=(STATE.EDITABLE, STATE.SHOWING), text=FakeTextIface())
        window = FakeNode(children=(FakeNode(states=(STATE.VISIBLE,)), editor))

        self.assertIsNone(runtime.find_editable_text(window))

    def test_finds_editor_nested_deep_in_the_tree(self):
        editor = FakeNode(
            states=(STATE.EDITABLE, STATE.FOCUSED, STATE.SHOWING), text=FakeTextIface()
        )
        window = FakeNode(
            children=(FakeNode(children=(FakeNode(children=(editor,)),)),)
        )

        self.assertIs(runtime.find_editable_text(window), editor)


def _write_into_window(window, text):
    """Write the way an untargeted type action does: into the focused editor."""
    outcome, _before, _after, _emitted = runtime.insert_text_outcome_on_node(
        runtime.find_editable_text(window), text
    )
    return outcome


class InsertTextTests(AtspiPatchedTestCase):
    def _window_with(self, iface):
        return FakeNode(
            children=(
                FakeNode(
                    states=(STATE.EDITABLE, STATE.FOCUSED, STATE.SHOWING), text=iface
                ),
            )
        )

    def test_rejects_buffer_that_reports_success_without_writing(self):
        """AT-SPI reports success even for the wrong control; read back the length."""
        liar = FakeTextIface(honest=False)
        window = self._window_with(liar)

        self.assertEqual(_write_into_window(window, "hello"), "no_change")
        self.assertEqual(liar.inserts, [(0, "hello")], "the write must actually be attempted")

    def test_accepts_buffer_that_actually_grows(self):
        iface = FakeTextIface()
        window = self._window_with(iface)

        self.assertEqual(_write_into_window(window, "hello"), "verified")
        self.assertEqual(iface.text, "hello")

    def test_appends_at_end_of_existing_content(self):
        iface = FakeTextIface(text="abc")
        window = self._window_with(iface)

        self.assertEqual(_write_into_window(window, "def"), "verified")
        self.assertEqual(iface.text, "abcdef")

    def test_reports_no_change_when_insert_itself_fails(self):
        iface = FakeTextIface(accept=False)
        window = self._window_with(iface)

        self.assertEqual(_write_into_window(window, "hello"), "no_change")

    def test_empty_payload_is_a_successful_noop(self):
        iface = FakeTextIface(text="abc")
        window = self._window_with(iface)

        self.assertEqual(_write_into_window(window, ""), "verified")
        self.assertEqual(iface.inserts, [], "empty text must not produce a write call")

    def test_reports_no_change_when_no_editable_target_exists(self):
        window = FakeNode(children=(FakeNode(states=(STATE.VISIBLE,)),))

        self.assertEqual(_write_into_window(window, "hello"), "no_change")

    def test_accepted_write_without_readback_is_unknown_not_fallback_safe(self):
        iface = FakeTextIface(text="abc", caret=3)
        node = FakeNode(
            states=(STATE.EDITABLE, STATE.FOCUSED, STATE.SHOWING),
            text=iface,
        )
        with mock.patch.object(
            runtime,
            "read_all_text",
            side_effect=["abc", "abc", None],
        ):
            outcome, before, after, emitted = (
                runtime.insert_text_outcome_on_node(node, "de")
            )

        self.assertEqual(outcome, "effect_unknown")
        self.assertEqual((before, after), (3, 3))
        self.assertTrue(emitted)
        self.assertEqual(iface.inserts, [(3, "de")])


class TextInsertionPointTests(AtspiPatchedTestCase):
    def test_uses_caret_offset(self):
        iface = FakeTextIface(text="abcdef", caret=3)

        self.assertEqual(runtime.text_insertion_point(iface), (3, None))

    def test_prefers_a_non_empty_selection_over_the_caret(self):
        iface = FakeTextIface(text="abcdef", caret=0, selection=(2, 5))

        self.assertEqual(runtime.text_insertion_point(iface), (2, (2, 5)))

    def test_ignores_a_collapsed_selection(self):
        iface = FakeTextIface(text="abcdef", caret=4, selection=(2, 2))

        self.assertEqual(runtime.text_insertion_point(iface), (4, None))

    def test_falls_back_to_appending_when_caret_is_unavailable(self):
        iface = FakeTextIface(text="abcdef", caret=None)

        self.assertEqual(runtime.text_insertion_point(iface), (6, None))

    def test_falls_back_to_appending_when_caret_is_out_of_range(self):
        iface = FakeTextIface(text="abc", caret=99)

        self.assertEqual(runtime.text_insertion_point(iface), (3, None))


class CaretInsertionTests(AtspiPatchedTestCase):
    def _window_with(self, iface):
        return FakeNode(
            children=(
                FakeNode(
                    states=(STATE.EDITABLE, STATE.FOCUSED, STATE.SHOWING), text=iface
                ),
            )
        )

    def test_inserts_at_the_caret_instead_of_the_end(self):
        """Appending at the end would ignore the caret the user or agent just placed."""
        iface = FakeTextIface(text="hello world", caret=5)

        self.assertEqual(
            _write_into_window(self._window_with(iface), " there"), "verified"
        )
        self.assertEqual(iface.text, "hello there world")

    def test_replaces_a_selection_the_way_typing_does(self):
        iface = FakeTextIface(text="hello world", caret=0, selection=(0, 5))

        self.assertEqual(
            _write_into_window(self._window_with(iface), "goodbye"), "verified"
        )
        self.assertEqual(iface.deletes, [(0, 5)])
        self.assertEqual(iface.text, "goodbye world")

    def test_failed_insert_after_deleting_a_selection_is_unknown(self):
        iface = FakeTextIface(
            text="hello world", caret=0, selection=(0, 5), accept=False)

        outcome, _before, _after, emitted = runtime.insert_text_outcome_on_node(
            runtime.find_editable_text(self._window_with(iface)), "goodbye"
        )

        self.assertEqual(outcome, "effect_unknown")
        self.assertTrue(emitted)
        self.assertEqual(iface.text, " world")

    def test_appends_when_the_control_has_no_caret(self):
        iface = FakeTextIface(text="abc", caret=None)

        self.assertEqual(
            _write_into_window(self._window_with(iface), "def"), "verified"
        )
        self.assertEqual(iface.text, "abcdef")

    def test_reports_character_counts(self):
        iface = FakeTextIface(text="abc", caret=3)

        self.assertEqual(
            runtime.insert_text_outcome_on_node(
                runtime.find_editable_text(self._window_with(iface)), "de"
            ),
            ("verified", 3, 5, True),
        )

    def test_reports_no_change_without_growth(self):
        iface = FakeTextIface(text="abc", caret=3, honest=False)

        outcome, before, after, _emitted = runtime.insert_text_outcome_on_node(
            runtime.find_editable_text(self._window_with(iface)), "de"
        )
        self.assertEqual(outcome, "no_change")
        self.assertEqual((before, after), (3, 3))

    def test_wrong_text_of_matching_length_is_unknown(self):
        iface = FakeTextIface(text="abc", caret=3)

        def corrupt(offset, payload):
            iface.inserts.append((offset, payload))
            iface.text = iface.text[:offset] + ("Z" * len(payload)) + iface.text[offset:]
            return True

        iface.insert = corrupt
        outcome, _before, _after, _emitted = runtime.insert_text_outcome_on_node(
            runtime.find_editable_text(self._window_with(iface)), "de"
        )

        self.assertEqual(outcome, "effect_unknown")
        self.assertEqual(iface.text, "abcZZ")


class SetElementValueTests(AtspiPatchedTestCase):
    def test_numeric_value_interface_precedes_editable_text_surface(self):
        text = FakeTextIface(text="0")
        value = FakeValueIface(0)
        node = FakeNode(
            states=(STATE.EDITABLE,), text=text, value=value)

        self.assertEqual(runtime.set_element_value_outcome(node, "-25"), "verified")
        self.assertEqual(value.current, -25.0)
        self.assertEqual(value.set_calls, [-25.0])
        self.assertEqual(text.set_calls, [])

    def test_unconfirmed_value_write_falls_back_to_editable_text(self):
        text = FakeTextIface(text="0")
        value = FakeValueIface(0, honest=False)
        node = FakeNode(
            states=(STATE.EDITABLE,), text=text, value=value)

        self.assertEqual(runtime.set_element_value_outcome(node, "-25"), "verified")
        self.assertEqual(value.current, 0.0)
        self.assertEqual(text.text, "-25")

    def test_value_interface_exception_falls_back_to_editable_text(self):
        text = FakeTextIface(text="0")
        value = FakeValueIface(0)
        node = FakeNode(states=(STATE.EDITABLE,), text=text, value=value)

        original = FakeAtspi.Value.set_current_value
        FakeAtspi.Value.set_current_value = staticmethod(
            lambda _iface, _value: (_ for _ in ()).throw(RuntimeError("dbus")))
        self.addCleanup(
            setattr, FakeAtspi.Value, "set_current_value", original)

        self.assertEqual(runtime.set_element_value_outcome(node, "-25"), "verified")
        self.assertEqual(text.text, "-25")

    def test_confirms_the_write_landed(self):
        iface = FakeTextIface(text="old")
        node = FakeNode(states=(STATE.EDITABLE,), text=iface)

        self.assertEqual(runtime.set_element_value_outcome(node, "new"), "verified")
        self.assertEqual(iface.text, "new")

    def test_rejects_a_control_that_reports_success_without_changing(self):
        """set_text_contents, like insert_text, returns True for the wrong control."""
        iface = FakeTextIface(text="old", honest=False)
        node = FakeNode(states=(STATE.EDITABLE,), text=iface)

        self.assertEqual(runtime.set_element_value_outcome(node, "new"), "no_change")

    def test_rejects_a_control_that_changes_to_a_different_value(self):
        """A change to some other value does not prove the requested value landed."""
        iface = FakeTextIface(text="old")

        def normalize(payload):
            iface.set_calls.append(payload)
            iface.text = payload.strip().upper()
            return True

        iface.set_contents = normalize
        node = FakeNode(states=(STATE.EDITABLE,), text=iface)

        self.assertEqual(
            runtime.set_element_value_outcome(node, " new "), "effect_unknown")
        self.assertEqual(iface.text, "NEW")

    def test_accepted_numeric_write_without_readback_is_unknown(self):
        value = FakeValueIface(0)
        node = FakeNode(value=value)
        with mock.patch.object(
            runtime.Atspi.Value,
            "get_current_value",
            side_effect=[0.0, None],
        ):
            self.assertEqual(
                runtime.set_element_value_outcome(node, "25"),
                "effect_unknown",
            )


class EmbeddedEditorValueTests(AtspiPatchedTestCase):
    """An editor whose text is only embedded-object characters shows its lines.

    Electron editors (Joplin's CodeMirror Markdown editor) expose the editor
    as an EDITABLE entry whose Text is a single U+FFFC; each line is an
    embedded child reached through Hypertext. The textbox therefore had no
    value, and read/effect lines could not show what was typed into it.
    """

    def _editor(self, lines, editable=True):
        children = [FakeNode(text=FakeTextIface(line), editable=False)
                    for line in lines]
        states = (STATE.EDITABLE, STATE.FOCUSED) if editable else ()
        editor = FakeNode(states=states,
                          text=FakeTextIface("￼" * len(lines)),
                          children=children)
        editor.links = children
        return editor

    def test_value_joins_the_embedded_lines(self):
        editor = self._editor(["会议 notes", "Body 第一行"])

        self.assertEqual(runtime.element_value(editor, role="entry"),
                         "会议 notes\nBody 第一行")

    def test_plain_text_is_unchanged(self):
        node = FakeNode(states=(STATE.EDITABLE,), text=FakeTextIface("hello"))

        self.assertEqual(runtime.element_value(node, role="entry"), "hello")

    def test_non_editable_containers_keep_their_text(self):
        """A web paragraph with a link also holds U+FFFC; it is not an editor."""
        paragraph = self._editor(["link text"], editable=False)

        self.assertEqual(runtime.element_value(paragraph, role="paragraph"), "")
