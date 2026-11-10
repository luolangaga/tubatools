#!/usr/bin/env python3
"""Provider tests for LibreOffice combo projection, transient items and select."""

from runtime_test_support import *  # noqa: F401,F403


class _Node(_TreeNode):
    """A tree node that knows its parent and exposes optional interfaces."""

    _paths = 0

    def __init__(self, role="panel", name="", parent=None, states=None,
                 x=10, y=10, w=20, h=10, **kwargs):
        super().__init__(role=role, name=name, x=x, y=y, w=w, h=h, **kwargs)
        if states is not None:
            self.states = set(states)
        self.parent = parent
        _Node._paths += 1
        self.path = "/org/a11y/atspi/accessible/{}".format(_Node._paths)
        self.app = mock.Mock(bus_name=":1.42")
        self.text_iface = None
        self.selection_iface = None
        self.table_iface = None
        self.relations = []
        for child in self.children:
            child.parent = self

    def add(self, *children):
        for child in children:
            child.parent = self
            self.children.append(child)
        return self

    def get_parent(self):
        return self.parent

    def get_index_in_parent(self):
        return self.parent.children.index(self) if self.parent else -1

    def get_relation_set(self):
        return self.relations

    def get_text_iface(self):
        return self.text_iface

    def get_selection_iface(self):
        return self.selection_iface

    def get_table_iface(self):
        return self.table_iface

    def get_process_id(self):
        return 4242


class ComboProjectionTests(AtspiPatchedTestCase):
    """LibreOffice gtk3 combos: a drawn toggle stands for a hidden combo box."""

    def _combo_group(self, panel_name="Line Spacing", combo_name=""):
        # LibreOffice draws only the toggle; the text and combo box report
        # neither SHOWING nor a frame (origin INT_MIN).
        text = _Node(role="text", states=(), x=-2147483648, y=-2147483648, w=1, h=1)
        combo = _Node(role="combo box", name=combo_name, states=(),
                      x=-2147483648, y=-2147483648, w=1, h=1)
        toggle = _Node(role="toggle button")
        inner = _Node(role="filler").add(text, combo, toggle)
        panel = _Node(role="panel", name=panel_name).add(_Node(role="filler").add(inner))
        return panel, combo, toggle

    def test_toggle_of_hidden_combo_renders_as_named_combo_box(self):
        panel, _combo, toggle = self._combo_group()
        record = runtime.record_for(toggle, 0, [0, 0, 0, 2], None)
        self.assertEqual(record["controlType"], "combo box")
        self.assertEqual(record["name"], "Line Spacing")
        self.assertEqual(record["nameSource"], "ancestor")

    def test_combo_label_prefers_the_combo_name(self):
        _panel, _combo, toggle = self._combo_group(combo_name="Page Style")
        record = runtime.record_for(toggle, 0, [0], None)
        self.assertEqual(record["name"], "Page Style")
        self.assertEqual(record["nameSource"], "provider")

    def test_live_identity_checks_use_the_projected_role(self):
        """A ref recorded as combo box must still match its live toggle."""
        _panel, _combo, toggle = self._combo_group()
        record = runtime.record_for(toggle, 0, [0], None)
        self.assertTrue(runtime.record_still_matches(toggle, record, None))
        self.assertEqual(runtime.effective_name(toggle), "Line Spacing")

    def test_plain_toggle_button_is_not_projected(self):
        toggle = _Node(role="toggle button")
        _Node(role="filler").add(_Node(role="push button"), toggle)
        record = runtime.record_for(toggle, 0, [0], None)
        self.assertEqual(record["controlType"], "toggle button")

    def test_combo_projection_survives_render_tree_pruning(self):
        """A combo inside layout panels survives render-tree pruning."""
        panel, _combo, _toggle = self._combo_group()
        window = _Node(role="dialog", name="Paragraph", x=0, y=0, w=200, h=200).add(panel)
        records, _lines, _ = runtime.render_tree(
            window, {"x": 0, "y": 0, "width": 200, "height": 200}, [0])
        combos = [r for r in records if r["controlType"] == "combo box"]
        self.assertEqual([r["name"] for r in combos], ["Line Spacing"])




class EditableTextRoleTests(AtspiPatchedTestCase):
    """AT-SPI role "text" names both static text and editable fields.

    GTK (GtkEntry, GtkTextView), Qt (QLineEdit) and LibreOffice dialog fields
    report role "text" with EDITABLE and EditableText; labels use "label". The
    model saw the gedit document and the LibreOffice Find field as static
    text: no ref in the tree, no value, and find role "textbox" missed them.
    """

    def _field(self, states, editable_iface=True, text="alpha"):
        node = _Node(role="text", states=states)
        node.text_iface = FakeTextIface(text)
        if not editable_iface:
            node.get_editable_text_iface = lambda: None
        else:
            node.get_editable_text_iface = lambda: node.text_iface
        return node

    def test_editable_single_line_text_is_an_entry(self):
        node = self._field((STATE.EDITABLE, STATE.SHOWING, STATE.VISIBLE,
                            STATE.SINGLE_LINE))
        _Node(role="filler").add(node)
        record = runtime.record_for(node, 0, [0], None)
        self.assertEqual(record["controlType"], "entry")
        self.assertEqual(runtime.effective_role(node), "entry")
        self.assertTrue(runtime.record_still_matches(node, record, None))

    def test_editable_multi_line_text_is_a_text_area(self):
        node = self._field((STATE.EDITABLE, STATE.SHOWING, STATE.VISIBLE,
                            STATE.MULTI_LINE))
        _Node(role="scroll pane").add(node)
        record = runtime.record_for(node, 0, [0], None)
        self.assertEqual(record["controlType"], "text area")

    def test_read_only_text_stays_text(self):
        node = self._field((STATE.SHOWING, STATE.VISIBLE))
        _Node(role="filler").add(node)
        self.assertEqual(runtime.record_for(node, 0, [0], None)["controlType"], "text")

    def test_editable_text_without_editable_interface_is_an_entry(self):
        # KTextEditor (Kate) reports EDITABLE and Text but no EditableText;
        # keyboard input still edits it.
        node = self._field((STATE.EDITABLE, STATE.SHOWING, STATE.VISIBLE),
                           editable_iface=False)
        _Node(role="filler").add(node)
        self.assertEqual(runtime.record_for(node, 0, [0], None)["controlType"], "entry")

    def test_editable_state_without_text_interface_stays_text(self):
        node = self._field((STATE.EDITABLE, STATE.SHOWING, STATE.VISIBLE),
                           editable_iface=False)
        node.text_iface = None
        _Node(role="filler").add(node)
        self.assertEqual(runtime.record_for(node, 0, [0], None)["controlType"], "text")

    def test_combo_editor_keeps_its_role(self):
        # The LibreOffice combo projection already exposes this editor through
        # the combo box (editTarget); a second textbox would duplicate it.
        editor = self._field((STATE.EDITABLE, STATE.SHOWING, STATE.VISIBLE))
        combo = _Node(role="combo box", states=())
        toggle = _Node(role="toggle button")
        _Node(role="filler").add(editor, combo, toggle)
        self.assertEqual(runtime.record_for(editor, 0, [0], None)["controlType"], "text")



class ItemNameFromContentTests(AtspiPatchedTestCase):
    """Unnamed rows take their name from their visible labels.

    GTK3 GtkListBoxRow (GNOME Settings sidebar, D-Feet bus list) and GTK tree
    rows expose no accessible name; the label is a descendant. The rows were
    rendered as "listitem [addressable=false]" with the text beside them, so
    the model could see "Mouse & Touchpad" but had no ref to click.
    """

    def _row(self, *labels, role="list item"):
        row = _Node(role=role)
        box = _Node(role="panel")
        row.add(box)
        for text, states in labels:
            box.add(_Node(role="label", name=text, states=states))
        _Node(role="list box").add(row)
        return row

    def test_unnamed_list_item_takes_its_visible_label(self):
        visible = (STATE.SHOWING, STATE.VISIBLE)
        hidden = ()
        row = self._row(("Control how you connect", hidden), ("Network", visible))
        record = runtime.record_for(row, 0, [0], None)
        self.assertEqual(record["name"], "Network")
        self.assertEqual(record["nameSource"], "content")
        self.assertEqual(runtime.effective_name(row), "Network")
        self.assertTrue(runtime.record_still_matches(row, record, None))

    def test_offscreen_row_still_named_from_visible_labels(self):
        row = self._row(("Mobile Network", (STATE.VISIBLE,)))
        self.assertEqual(runtime.effective_name(row), "Mobile Network")

    def test_several_labels_join_in_order(self):
        visible = (STATE.SHOWING, STATE.VISIBLE)
        row = self._row(("org.freedesktop.DBus", visible), ("pid: 220", visible),
                        role="tree item")
        self.assertEqual(runtime.effective_name(row), "org.freedesktop.DBus pid: 220")

    def test_own_name_wins(self):
        row = self._row(("Inner", (STATE.SHOWING, STATE.VISIBLE)))
        row.title = "Outer"
        record = runtime.record_for(row, 0, [0], None)
        self.assertEqual(record["name"], "Outer")
        self.assertEqual(record["nameSource"], "provider")

    def test_containers_are_not_named_from_content(self):
        panel = _Node(role="panel").add(
            _Node(role="label", name="Network", states=(STATE.SHOWING, STATE.VISIBLE)))
        self.assertEqual(runtime.effective_name(panel), "")



class _Span:
    def __init__(self, start, end):
        self.start_offset, self.end_offset = start, end


class _Text:
    """Text interface state of one paragraph."""

    def __init__(self, text):
        self.text = text
        self.selection = None
        self.caret = -1


class _TextApi:
    @staticmethod
    def get_character_count(iface):
        return len(iface.text)

    @staticmethod
    def get_text(iface, start, end):
        return iface.text[start:] if end == -1 else iface.text[start:end]

    @staticmethod
    def get_n_selections(iface):
        return 1 if iface.selection else 0

    @staticmethod
    def get_selection(iface, index):
        return _Span(*iface.selection)

    @staticmethod
    def add_selection(iface, start, end):
        iface.selection = (start, end)
        return True

    @staticmethod
    def set_selection(iface, index, start, end):
        iface.selection = (start, end)
        return True

    @staticmethod
    def set_caret_offset(iface, offset):
        iface.caret = offset
        return True


class SelectTests(AtspiPatchedTestCase):
    def setUp(self):
        super().setUp()
        patcher = mock.patch.object(runtime.Atspi, "Text", _TextApi, create=True)
        patcher.start()
        self.addCleanup(patcher.stop)
        self.notes = []
        self.evidence = {}

    def _paragraphs(self, *texts):
        doc = _Node(role="document text")
        paragraphs = []
        for text in texts:
            node = _Node(role="paragraph")
            node.text_iface = _Text(text)
            paragraphs.append(node)
        doc.add(*paragraphs)
        return doc, paragraphs

    def test_substring_select_reports_the_read_back_span(self):
        _doc, (para,) = self._paragraphs("Alpha beta gamma")
        runtime.perform_select({"text": "beta"}, None, para, {}, None,
                               self.notes, self.evidence)
        self.assertEqual(para.text_iface.selection, (6, 10))
        self.assertEqual(self.notes, ['[select] selected 6..10 "beta"'])
        self.assertTrue(self.evidence["readback_verified"])

    def test_shifted_selection_is_reported_as_read_back(self):
        """A shifted provider selection must not claim the requested effect."""
        _doc, (para,) = self._paragraphs("First bullet line")

        class ShiftingApi(_TextApi):
            @staticmethod
            def add_selection(iface, start, end):
                iface.selection = (max(start, 1), end)
                return True

        with mock.patch.object(runtime.Atspi, "Text", ShiftingApi), \
                mock.patch.object(runtime, "time", _FakeClock()):
            with self.assertRaises(runtime.OperationFailure) as caught:
                runtime.perform_select({"text": "First"}, None, para, {}, None,
                                       self.notes, self.evidence)
        self.assertIn("read back [(1, 5)]", str(caught.exception))
        self.assertTrue(caught.exception.input_events_emitted)
        self.assertFalse(self.evidence.get("readback_verified", False))

    def test_ambiguous_substring_is_rejected_before_input(self):
        _doc, (para,) = self._paragraphs("a b a")
        with self.assertRaises(runtime.OperationFailure) as caught:
            runtime.perform_select({"text": "a"}, None, para, {}, None,
                                   self.notes, self.evidence)
        self.assertEqual(caught.exception.failure_code, "invalid_argument")
        self.assertIsNone(para.text_iface.selection)

    def test_paragraph_range_extends_with_keys_until_read_back(self):
        """AT-SPI cannot select across paragraphs; Writer extends by keys."""
        _doc, paragraphs = self._paragraphs("one", "two", "three")
        presses = []

        def fake_key(key):
            presses.append(key)
            covered = paragraphs[len(presses) - 1]
            covered.text_iface.selection = (0, len(covered.text_iface.text))

        with mock.patch.object(runtime, "send_key", fake_key), \
                mock.patch.object(runtime, "require_window_focus", lambda *a: None):
            runtime.perform_select({}, object(), paragraphs[0], {}, paragraphs[1],
                                   self.notes, self.evidence)
        self.assertEqual(presses, ["ctrl+shift+Down", "ctrl+shift+Down"])
        self.assertEqual(paragraphs[0].text_iface.caret, 0)
        self.assertIn("selected 6 chars in 2 elements", self.notes[0])
        self.assertEqual(self.evidence["route"], "keyboard_synthesis")

    def test_paragraph_range_that_never_reaches_the_end_fails(self):
        _doc, paragraphs = self._paragraphs("one", "two")
        clock = _FakeClock()
        with mock.patch.object(runtime, "send_key", lambda key: None), \
                mock.patch.object(runtime, "require_window_focus", lambda *a: None), \
                mock.patch.object(runtime, "time", clock):
            with self.assertRaises(runtime.OperationFailure) as caught:
                runtime.perform_select({}, object(), paragraphs[0], {},
                                       paragraphs[1], self.notes, self.evidence)
        self.assertEqual(caught.exception.failure_code, "effect_unknown")
        self.assertTrue(caught.exception.input_events_emitted)

    def test_range_end_outside_the_container_is_rejected(self):
        _doc, (first,) = self._paragraphs("one")
        _other, (second,) = self._paragraphs("two")
        with self.assertRaises(runtime.OperationFailure) as caught:
            runtime.perform_select({}, object(), first, {}, second,
                                   self.notes, self.evidence)
        self.assertEqual(caught.exception.failure_code, "invalid_argument")
        self.assertFalse(caught.exception.input_events_emitted)

    def test_cell_range_selects_the_rectangle_and_reads_it_back(self):
        cells = {}
        table = _Node(role="table", states=(STATE.MANAGES_DESCENDANTS,))
        for row in range(3):
            for col in range(3):
                cell = _Node(role="table cell", name="{}{}".format("ABC"[col], row + 1))
                cells[(row, col)] = cell
                table.add(cell)
        table.table_iface = table
        table.selection_iface = table
        selected = []

        class TableApi:
            @staticmethod
            def get_row_at_index(t, index):
                return index // 3

            @staticmethod
            def get_column_at_index(t, index):
                return index % 3

            @staticmethod
            def get_index_at(t, row, col):
                return row * 3 + col

            @staticmethod
            def get_accessible_at(t, row, col):
                return cells[(row, col)]

        class SelectionApi:
            @staticmethod
            def clear_selection(container):
                selected.clear()
                return True

            @staticmethod
            def select_child(container, index):
                selected.append(index)
                return True

            @staticmethod
            def get_n_selected_children(container):
                return len(selected)

            @staticmethod
            def get_selected_child(container, index):
                return container.children[selected[index]]

        with mock.patch.object(runtime.Atspi, "Table", TableApi, create=True), \
                mock.patch.object(runtime.Atspi, "Selection", SelectionApi, create=True):
            runtime.perform_select({}, None, cells[(1, 0)], {}, cells[(2, 1)],
                                   self.notes, self.evidence)
        self.assertEqual(sorted(selected), [3, 4, 6, 7])
        self.assertEqual(self.notes, ["[select] selected 4 cells A2:B3"])


class _RecreatingList(_Node):
    """A Qt item view: every child query returns a new accessible object.

    Qt's AT-SPI bridge wraps each item-view cell (and each list item of a
    combo popup) in a fresh QAccessibleInterface per query, so the object
    path of the same cell changes on every read. The cells are TRANSIENT
    children of a MANAGES_DESCENDANTS container.
    """

    def __init__(self, names, **kwargs):
        super().__init__(role="tree", states=(
            STATE.SHOWING, STATE.VISIBLE, STATE.MANAGES_DESCENDANTS), **kwargs)
        self.names = list(names)

    def get_child_count(self):
        return len(self.names)

    def get_child_at_index(self, index):
        cell = _Node(role="table cell", name=self.names[index], states=(
            STATE.SHOWING, STATE.VISIBLE, STATE.TRANSIENT))
        cell.parent = self
        cell.get_index_in_parent = lambda: index
        return cell


class TransientItemIdentityTests(AtspiPatchedTestCase):
    """Refs to Qt item-view cells failed as stale right after observe or find."""

    def setUp(self):
        super().setUp()
        self.view = _RecreatingList(["Logger", "Network Sync", "syslog"])
        self.window = _Node(role="dialog", name="Preferences").add(self.view)
        patcher = mock.patch.object(
            runtime, "process_generation", return_value="99123")
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_recreated_item_keeps_one_provider_identity(self):
        first = runtime.provider_identity_path(self.view.get_child_at_index(1))
        second = runtime.provider_identity_path(self.view.get_child_at_index(1))
        self.assertEqual(first, second)
        self.assertNotEqual(
            first, runtime.provider_identity_path(self.view.get_child_at_index(2)))

    def test_ref_to_recreated_cell_resolves_by_its_identity(self):
        cell = self.view.get_child_at_index(1)
        record = runtime.record_for(cell, 0, [0, 0, 1], None)
        found = runtime.scan_provider_identity(
            [(0, self.window)], record["providerBusName"],
            record["providerObjectPath"], record["processGeneration"],
            max_nodes=100, deadline=runtime.time.monotonic() + 5)
        self.assertEqual(len(found), 1)
        self.assertEqual(runtime.node_name(found[0][0]), "Network Sync")

    def test_runtime_path_check_accepts_the_recreated_cell(self):
        app = _Node(role="application", name="vlc").add(self.window)
        cell = self.view.get_child_at_index(2)
        record = runtime.record_for(cell, 0, [0, 0, 2], None)
        target = {
            "role": "table cell", "name": "syslog", "hasName": True,
            "providerBusName": record["providerBusName"],
            "providerObjectPath": record["providerObjectPath"],
            "processGeneration": record["processGeneration"],
        }
        found = runtime.resolve_provider_runtime_path(
            app, [0, 0, 2], target, {0})
        self.assertEqual(len(found), 1)

    def test_persistent_objects_keep_their_object_path(self):
        """GTK cells are TRANSIENT too but keep their objects; nothing changes."""
        button = _Node(role="push button", name="OK")
        _Node(role="panel").add(button)
        self.assertEqual(runtime.provider_identity_path(button), button.path)
        list_view = _Node(role="list", states=(
            STATE.SHOWING, STATE.VISIBLE, STATE.MANAGES_DESCENDANTS))
        plain = _Node(role="list item", name="a")
        list_view.add(plain)
        self.assertEqual(runtime.provider_identity_path(plain), plain.path)


if __name__ == "__main__":
    unittest.main()
