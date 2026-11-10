#!/usr/bin/env python3
"""Linux provider stable and dense indexing, name identity, snapshot grammar, and element reference tests."""

from runtime_test_support import *  # noqa: F401,F403


class StaleElementIndexTests(AtspiPatchedTestCase):
    """`runtimeId` is a positional path that points elsewhere once the tree changes."""

    def record(self, role, name, frame=None, path=(0, 1)):
        return {"runtimeId": list(path), "controlType": role, "name": name,
                "automationId": "", "frame": frame}

    def test_rejects_node_whose_role_and_name_changed(self):
        """A stale index that now resolves to a different control is rejected.

        In Nautilus, a path recorded for `menu item Rename…` while a context
        menu was open points at the toolbar's `toggle button Menu` once the
        menu closes, and nothing reports an error. `Move to Trash` sits next
        to Rename in the same menu, so silently acting on the wrong item is
        unacceptable.
        """
        other = _TreeNode(role="toggle button", name="Menu", x=715, y=23, w=26, h=46)

        self.assertFalse(
            runtime.record_still_matches(
                other, self.record("menu item", "Rename…"), None
            )
        )

    def test_accepts_unchanged_node(self):
        same = _TreeNode(role="menu item", name="Rename…", x=7, y=189, w=304, h=25)

        self.assertTrue(
            runtime.record_still_matches(
                same, self.record("menu item", "Rename…"), None
            )
        )

    def test_unnamed_element_is_matched_by_position(self):
        """Many dialog controls are unnamed, so position is their only identity clue."""
        node = _TreeNode(role="toggle button", x=10, y=20, w=30, h=40)
        frame = {"x": 10, "y": 20, "width": 30, "height": 40}
        moved = {"x": 400, "y": 20, "width": 30, "height": 40}

        self.assertTrue(
            runtime.record_still_matches(node, self.record("toggle button", "", frame), None)
        )
        self.assertFalse(
            runtime.record_still_matches(node, self.record("toggle button", "", moved), None)
        )

    def test_named_node_does_not_satisfy_unnamed_record(self):
        """A named node resolved for an unnamed record means the element changed."""
        named = _TreeNode(role="toggle button", name="Menu", x=10, y=20, w=30, h=40)
        frame = {"x": 10, "y": 20, "width": 30, "height": 40}

        self.assertFalse(
            runtime.record_still_matches(named, self.record("toggle button", "", frame), None)
        )

    def test_accessible_id_wins_when_present(self):
        """A stable toolkit id takes precedence over changes in name or position."""

        class WithID(_TreeNode):
            def get_accessible_id(self):
                return "save-button"

        node = WithID(role="push button", name="Save As", x=99, y=99, w=1, h=1)
        record = {"runtimeId": [0], "controlType": "push button", "name": "Save",
                  "automationId": "save-button", "frame": None}

        self.assertTrue(runtime.record_still_matches(node, record, None))

    def test_find_element_refuses_stale_path_end_to_end(self):
        """find_element_at_snapshot_path does not resolve a stale path to a different control.

        Unit tests of the matching predicate cannot show that callers use it,
        so this test exercises the whole lookup.
        """
        toolbar = _TreeNode(role="toggle button", name="Menu", x=715, y=23, w=26, h=46)
        window = _TreeNode(role="frame", name="Files", w=900, h=600, kids=(toolbar,))
        app = _TreeNode(role="application", name="Files", kids=(window,))

        stale = {"runtimeId": [0, 0], "controlType": "menu item",
                 "name": "Rename…", "automationId": "", "frame": None}

        # The path alone reaches the toolbar, which is not the recorded element.
        self.assertIs(runtime.resolve_path(app, [0, 0]), toolbar)
        self.assertIsNone(runtime.find_element_at_snapshot_path(app, stale))

    def test_find_element_still_resolves_valid_path(self):
        item = _TreeNode(role="menu item", name="Rename…", x=7, y=189, w=304, h=25)
        window = _TreeNode(role="frame", name="Files", w=900, h=600, kids=(item,))
        app = _TreeNode(role="application", name="Files", kids=(window,))

        record = {"runtimeId": [0, 0], "controlType": "menu item",
                  "name": "Rename…", "automationId": "", "frame": None}

        self.assertIs(runtime.find_element_at_snapshot_path(app, record), item)


class StableIndexTests(unittest.TestCase):
    """Indices survive across snapshots.

    If opening a dialog renumbered every element, a click with an index from
    the previous snapshot would silently hit a different control.
    """

    def test_same_element_keeps_its_number(self):
        known = {"0.1": {"index": 7, "role": "push button", "name": "Save"}}
        indexer = runtime.StableIndexer(known)
        self.assertEqual(indexer.index_for([0, 1], "push button", "Save"), 7)

    def test_role_or_name_change_invalidates_the_number(self):
        """A changed role or name gets a new index.

        The element is no longer the same thing.
        """
        known = {"0.1": {"index": 7, "role": "push button", "name": "Save"}}
        self.assertNotEqual(
            runtime.StableIndexer(known).index_for([0, 1], "push button", "Saved"), 7)
        self.assertNotEqual(
            runtime.StableIndexer(known).index_for([0, 1], "menu item", "Save"), 7)

    def test_full_name_fingerprint_change_invalidates_truncated_name(self):
        known = {
            "0.1": {
                "index": 7,
                "role": "push button",
                "name": "Manage settings...",
                "nameHash": "old-full-name",
            }
        }
        self.assertEqual(
            runtime.StableIndexer(known).index_for(
                [0, 1],
                "push button",
                "Manage settings...",
                "old-full-name",
            ),
            7,
        )
        self.assertNotEqual(
            runtime.StableIndexer(known).index_for(
                [0, 1],
                "push button",
                "Manage settings...",
                "different-full-name",
            ),
            7,
        )

    def test_inserting_an_element_does_not_shift_the_others(self):
        """With traversal order, one insertion would shift every later index."""
        known = {
            "0.0": {"index": 0, "role": "frame", "name": "W"},
            "0.1": {"index": 1, "role": "push button", "name": "A"},
            "0.2": {"index": 2, "role": "push button", "name": "B"},
        }
        indexer = runtime.StableIndexer(known)
        self.assertEqual(indexer.index_for([0, 0], "frame", "W"), 0)
        # A newly inserted element gets a fresh index instead of taking another's.
        fresh = indexer.index_for([0, 1, 5], "push button", "NEW")
        self.assertEqual(indexer.index_for([0, 1], "push button", "A"), 1)
        self.assertEqual(indexer.index_for([0, 2], "push button", "B"), 2)
        self.assertGreaterEqual(fresh, 3)

    def test_numbers_are_never_reused_within_one_snapshot(self):
        """Reusing an index within one snapshot would silently target the wrong node."""
        known = {"0.1": {"index": 3, "role": "text", "name": ""},
                 "0.2": {"index": 3, "role": "text", "name": ""}}
        indexer = runtime.StableIndexer(known)
        first = indexer.index_for([0, 1], "text", "")
        second = indexer.index_for([0, 2], "text", "")
        self.assertNotEqual(first, second)

    def test_without_prior_knowledge_it_numbers_from_zero(self):
        indexer = runtime.StableIndexer(None)
        self.assertEqual(indexer.index_for([0], "frame", "W"), 0)
        self.assertEqual(indexer.index_for([0, 0], "push button", "A"), 1)


class DenseIndexTests(AtspiPatchedTestCase):
    """Pruned nodes do not consume an index before being dropped.

    This must be an AtspiPatchedTestCase: under a bare TestCase relative_frame
    returns None, so every node below the root is pruned as invisible and the
    fixture exercises no pruning at all.
    """

    def test_pruned_nodes_do_not_consume_numbers(self):
        """Indices stay dense when nodes are pruned.

        Assigning an index before the prune decision spends numbers on nodes
        that never reach the records, leaving gaps. Pruned nodes are also
        missing from the known map, so each snapshot would claim new numbers
        and indices would keep growing during a session. With N elements in
        the tree, indices must fall in 0..N-1.
        """
        # Unnamed panels, pruned by role, surround two named buttons. Geometry
        # is explicit because the first prune check requires a non-None frame.
        filler = [_TreeNode(role="panel", x=1, y=1, w=5, h=5)
                  for _ in range(8)]
        keep_a = _TreeNode(role="push button", name="Save", x=2, y=2, w=5, h=5)
        keep_b = _TreeNode(role="push button", name="Cancel", x=3, y=3, w=5, h=5)
        window = _TreeNode(role="frame", name="W", w=900, h=600,
                           kids=tuple(filler[:4]) + (keep_a,)
                                + tuple(filler[4:]) + (keep_b,))

        # Pass an indexer: without one the index falls back to len(records),
        # which is dense by construction and would prove nothing.
        records, _lines, _annotations = runtime.render_tree(
            window, None, [0], indexer=runtime.StableIndexer({}), prune=True)
        indices = sorted(r["index"] for r in records)
        self.assertEqual(indices, list(range(len(records))),
                         "indices must be dense, got {}".format(indices))


class TruncatedNameIdentityTests(AtspiPatchedTestCase):
    """Truncating a name with text_limit does not weaken element identity.

    Comparing the live full name with a record's truncated name fails once
    text_limit drops from 500 to 40, and matching silently falls back to the
    weakest role-plus-position check. text_limit is a per-request parameter,
    so comparisons cannot assume the default value.
    """

    LONG = "Manage — Settings and keyboard shortcuts, and more actions for the editor"

    def test_truncated_name_still_identifies_the_same_element(self):
        node = _TreeNode(role="push button", name=self.LONG)
        for limit in (500, 40, 10):
            record = runtime.record_for(node, 7, [0, 1], None, text_limit=limit)
            self.assertTrue(
                runtime.record_still_matches(node, record, None),
                "text_limit={} failed to recognize the same node".format(limit))

    def test_a_different_element_sharing_the_prefix_is_not_confused(self):
        """Two elements sharing a truncated prefix are not confused.

        Comparing only the truncated prefix would trade a silent mismatch for a
        worse silent false match. Truncated records therefore carry a
        fingerprint of the full name, and matching compares full names exactly.
        """
        node = _TreeNode(role="push button", name=self.LONG)
        sibling = _TreeNode(
            role="push button",
            name="Manage — Settings and keyboard shortcuts, and MORE actions here")
        for limit in (40, 10):
            record = runtime.record_for(node, 7, [0, 1], None, text_limit=limit)
            self.assertFalse(
                runtime.record_still_matches(sibling, record, None),
                "text_limit={} confused a different element sharing the prefix".format(limit))

    def test_short_truncation_margins_always_carry_a_fingerprint(self):
        """The ellipsis adds 3 characters, so length alone cannot reveal truncation."""
        prefix = "abcdefghij"
        for suffix in ("k", "kl", "klm"):
            node = _TreeNode(role="push button", name=prefix + suffix)
            sibling = _TreeNode(role="push button", name=prefix + "z" * len(suffix))
            record = runtime.record_for(node, 7, [0, 1], None, text_limit=10)

            self.assertEqual(record["name"], prefix + "...")
            self.assertIn("nameHash", record)
            self.assertTrue(runtime.record_still_matches(node, record, None))
            self.assertFalse(runtime.record_still_matches(sibling, record, None))

            known = {
                "0.1": {
                    "index": 7,
                    "role": "push button",
                    "name": record["name"],
                    "nameHash": record["nameHash"],
                }
            }
            sibling_record = runtime.record_for(
                sibling, 8, [0, 1], None, text_limit=10
            )
            self.assertNotEqual(
                runtime.StableIndexer(known).index_for(
                    [0, 1],
                    sibling_record["controlType"],
                    sibling_record["name"],
                    sibling_record["nameHash"],
                ),
                7,
            )

    def test_short_names_carry_no_fingerprint(self):
        """Only truncated records carry a name fingerprint."""
        node = _TreeNode(role="push button", name="Save")
        record = runtime.record_for(node, 0, [0], None, text_limit=500)
        self.assertNotIn("nameHash", record)


class SnapshotGrammarTests(unittest.TestCase):
    """Snapshot line grammar: `- role "name" [attr=value]`.

    Free text must be quoted:
    names can contain colons, such as `panel PageShape: Weekday in school` in
    LibreOffice Impress, so an unquoted
    `<idx> <role> <name> Description: <desc>` line cannot separate the name
    from the field delimiters.
    """

    def _line(self, **overrides):
        record = {
            "index": 7, "localizedControlType": "push button", "controlType": "push button",
            "name": "", "automationId": "", "value": "", "states": "",
            "description": "", "placeholder": "", "actions": [], "frame": None,
        }
        record.update(overrides)
        return runtime.render_element_line(record, 0)

    def test_name_containing_a_colon_stays_unambiguous(self):
        """This line is ambiguous unless the name is quoted."""
        line = self._line(name="PageShape: Weekday in school",
                          description="Slide")
        self.assertIn('"PageShape: Weekday in school"', line)
        self.assertIn('[desc="Slide"]', line)

    def test_quotes_inside_a_name_are_escaped(self):
        line = self._line(name='say "hi"')
        self.assertIn(r'"say \"hi\""', line)

    def test_geometry_is_compact(self):
        """Geometry shrinks from 45 characters to 14 without losing information.

        `Frame: {x: 687, y: 23, width: 64, height: 46}` becomes
        `{687,23,64,46}`. Geometry makes up 35-50% of a tree, so this saves
        about 30% of the whole tree.
        """
        line = self._line(frame={"x": 687.0, "y": 23.0, "width": 64.0, "height": 46.0})
        self.assertIn("{687,23,64,46}", line)
        self.assertNotIn("Frame:", line)

    def test_value_goes_last_after_a_colon(self):
        """Matches the aria snapshot form `- textbox: Enter your name`."""
        line = self._line(localizedControlType="text", value="baseline-marker",
                          states="[focused]")
        self.assertTrue(line.rstrip().endswith(': "baseline-marker"'), line)

    def test_empty_segments_are_omitted(self):
        """Empty segments leave no placeholder bytes on the line."""
        self.assertEqual(self._line(name="OK"), '\t7 push button "OK"')

    def test_index_stays_at_the_head(self):
        """The index leads the line so it is easy to extract as a reference."""
        line = self._line(name="OK").strip()
        self.assertTrue(line.startswith("7 "), line)


class ElementReferenceTests(unittest.TestCase):
    """Element references have one renderer whose output parses as a target.

    A separate unquoted `role + " " + name` rendering, such as
    `entry Address and search bar`, cannot be pasted back into `expect`: the
    bare string spans the role and name fields, so it never matches.
    """

    def test_named_element_is_quoted_like_a_tree_line(self):
        self.assertEqual(
            runtime.element_reference("entry", "Address and search bar"),
            'entry "Address and search bar"',
        )

    def test_unnamed_element_uses_the_field_form(self):
        """Unnamed elements render as `role="separator"`, which parses back.

        Prose such as `an unnamed separator` cannot be read back as a target.
        """
        self.assertEqual(runtime.element_reference("separator", ""), 'role="separator"')

    def test_quotes_inside_a_name_are_escaped(self):
        self.assertEqual(
            runtime.element_reference("entry", 'say "hi"'), r'entry "say \"hi\""'
        )

    def test_focus_summary_uses_the_shared_renderer(self):
        """Focus evidence uses the shared renderer so it reads back as a target."""
        node = object()
        with mock.patch.object(runtime, "iter_apps", return_value=[node]), \
                mock.patch.object(runtime, "node_pid", return_value=42), \
                mock.patch.object(runtime, "main_window", return_value=(None, node)), \
                mock.patch.object(runtime, "find_first", return_value=node), \
                mock.patch.object(runtime, "node_role", return_value="entry"), \
                mock.patch.object(runtime, "node_name",
                                  return_value="Address and search bar"), \
                mock.patch.object(runtime, "desktop", return_value=None):
            summary, _selected = runtime.focus_and_selection(42)
            self.assertEqual(summary, 'entry "Address and search bar"')
