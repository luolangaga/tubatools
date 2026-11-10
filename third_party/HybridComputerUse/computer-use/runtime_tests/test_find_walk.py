#!/usr/bin/env python3
"""Linux provider AX find walk: query prefilter, candidate records, resume."""

from runtime_test_support import *  # noqa: F401,F403


def _table(rows):
    """A web-style table: named cells whose text leaf repeats the cell."""
    row_nodes = []
    for index in range(rows):
        name = "Item %d" % index
        cell = _TreeNode(role="table cell", name=name, y=index, kids=(
            _TreeNode(role="static", name=name, y=index),))
        value_cell = _TreeNode(role="table cell", name="Score %d" % index,
                               y=index, description="row %d" % index)
        button = _TreeNode(role="push button", name="Open %d" % index, y=index)
        row_nodes.append(_TreeNode(role="table row", y=index,
                                   kids=(cell, value_cell, button)))
    entry = _TreeNode(role="text", name="", y=rows)
    entry.states.add(STATE.EDITABLE)
    entry._text = FakeTextIface("Item 3 draft")
    table = _TreeNode(role="table", name="Results", w=100, h=100,
                      kids=tuple(row_nodes))
    return _TreeNode(role="frame", name="Window", w=100, h=100,
                     kids=(table, entry))


def _walk(root, known=None, find=None, **limits):
    limits = dict(ax_snapshot.FIND_WALK_LIMITS, **limits)
    return runtime.render_tree(
        root, None, [0], indexer=runtime.StableIndexer(known or {}),
        find=find, **limits)


def _refs(records):
    return {
        ".".join(str(part) for part in record["runtimeId"]): {
            "index": record["index"], "role": record["controlType"],
            "name": record["name"], "nameHash": record.get("nameHash", ""),
        }
        for record in records
    }


def _query(**fields):
    return find_walk.FindWalk(find_walk.FindFilter(**fields))


class FindWalkCandidateTests(AtspiPatchedTestCase):
    def test_match_record_equals_the_full_record(self):
        """A find-walk match is the record a full walk builds for the node.

        Refs come from an earlier full walk, as Go passes them, so the index
        is part of the comparison.
        """
        root = _table(40)
        full, _lines, _annotations = _walk(root)
        by_path = {tuple(record["runtimeId"]): record for record in full}
        for fields in ({"name": "Item 7", "name_exact": True},
                       {"name": "open 3"},
                       {"text": "row 12"},
                       {"text": "item 3 draft", "text_exact": True}):
            records, _lines, _annotations = _walk(
                root, known=_refs(full), find=_query(**fields))
            self.assertGreater(len(records), 1, fields)
            for record in records:
                self.assertEqual(
                    record, by_path[tuple(record["runtimeId"])], fields)

    def test_only_candidates_and_their_ancestors_get_records(self):
        root = _table(40)
        records, _lines, _annotations = _walk(
            root, find=_query(name="Item 7", name_exact=True))
        self.assertEqual(
            [(record["controlType"], record["name"]) for record in records],
            [("frame", "Window"), ("table", "Results"),
             ("table cell", "Item 7")])

    def test_query_without_name_or_text_keeps_every_record(self):
        root = _table(20)
        full, _lines, _annotations = _walk(root)
        find = _query()
        records, _lines, _annotations = _walk(
            root, known=_refs(full), find=find)
        self.assertEqual(records, full)
        self.assertFalse(find.candidates_only)

    def test_walk_that_left_out_records_says_so(self):
        find = _query(name="Item 3")
        _walk(_table(20), find=find)
        self.assertTrue(find.candidates_only)
        self.assertTrue(find_walk.FindWalk(resume_after=[0, 1]).candidates_only)

    def test_text_query_matches_value_description_and_name(self):
        root = _table(20)
        names = {}
        for text in ("row 5", "item 3 draft", "Open 9"):
            records, _lines, _annotations = _walk(root, find=_query(text=text))
            names[text] = [record["name"] for record in records[1:]
                           if record["controlType"] != "table"
                           and record["controlType"] != "table row"]
        self.assertEqual(names["row 5"], ["Score 5"])
        self.assertEqual(names["item 3 draft"], [""])
        self.assertEqual(names["Open 9"], ["Open 9"])

    def test_pruning_and_counts_match_the_full_walk(self):
        root = _table(30)
        _records, _lines, full_annotations = _walk(root)
        _records, _lines, find_annotations = _walk(
            root, find=_query(name="nothing here"))
        self.assertEqual(full_annotations, find_annotations)

    def test_non_candidates_get_no_full_record(self):
        root = _table(30)
        built = []
        original = ax_snapshot.record_for

        def counting(node, *args, **kwargs):
            built.append(node)
            return original(node, *args, **kwargs)

        with mock.patch.object(ax_snapshot, "record_for", counting), \
                mock.patch.object(find_walk, "record_for", counting):
            _walk(root, find=_query(name="Open 4", name_exact=True))
        # The root, the table above the match, the match, and the unnamed
        # entry, whose name needs its full record. Unnamed rows are pruned.
        self.assertEqual(len(built), 4)


class FindFilterFoldTests(unittest.TestCase):
    def test_prefilter_accepts_the_pairs_go_matches(self):
        query = find_walk.FindFilter
        cases = [
            ("Save As", "save as", False),
            ("İstanbul", "istanbul", False),
            ("ΟΔΟΣ", "οδοσ", True),
            ("K", "k", True),
            ("  Report  ", "report", True),
            ("Straße", "STRASSE", True),
        ]
        for name, needle, exact in cases:
            self.assertTrue(
                query(name=needle, name_exact=exact).matches(
                    name, str, str, str),
                (name, needle))
        self.assertFalse(query(name="Item 2").matches(
            "Item 3", str, str, str))
        self.assertFalse(query(name="Item 2", name_exact=True).matches(
            "Item 20", str, str, str))


class FindWalkResumeTests(AtspiPatchedTestCase):
    def test_resumed_walks_cover_the_window_once(self):
        root = _table(60)
        full, _lines, _annotations = _walk(root)
        expected = [record["name"] for record in full
                    if record["name"].startswith("Item 1")]
        found = []
        resume = None
        for _attempt in range(40):
            find = find_walk.FindWalk(
                find_walk.FindFilter(name="Item 1"), resume)
            records, _lines, _annotations = _walk(
                root, find=find, max_tree_nodes=25)
            found += [record["name"] for record in records
                      if record["name"].startswith("Item 1")]
            if find.stop_path is None:
                break
            self.assertTrue(resume is None or find.stop_path > resume)
            resume = find.stop_path
        self.assertIsNone(find.stop_path)
        self.assertEqual(found, expected)

    def test_walk_stopped_by_the_deadline_resumes_where_it_stopped(self):
        root = _table(30)
        expected = [record["name"] for record in _walk(root)[0]
                    if record["name"].startswith("Open")]
        clock = [0.0]

        def tick():
            clock[0] += 1.0
            return clock[0]

        found, resume = [], None
        with mock.patch.object(ax_snapshot.time, "monotonic", tick):
            for _attempt in range(40):
                find = find_walk.FindWalk(
                    find_walk.FindFilter(name="Open"), resume)
                records, _lines, annotations = _walk(
                    root, find=find, deadline_seconds=40)
                found += [record["name"] for record in records
                          if record["name"].startswith("Open")]
                if find.stop_path is None:
                    break
                self.assertIn("time", annotations[0]["reason"])
                resume = find.stop_path
        self.assertIsNotNone(resume)
        self.assertEqual(found, expected)

    def test_walk_within_its_caps_reports_no_stop(self):
        find = _query(name="Item 1")
        _walk(_table(10), find=find)
        self.assertIsNone(find.stop_path)

    def test_resume_relation(self):
        find = find_walk.FindWalk(resume_after=[0, 2, 5])
        self.assertEqual(find.relation([0]), "ancestor")
        self.assertEqual(find.relation([0, 2]), "ancestor")
        self.assertEqual(find.relation([0, 1, 9]), "searched")
        self.assertEqual(find.relation([0, 2, 4, 7]), "searched")
        self.assertEqual(find.relation([0, 2, 5]), "new")
        self.assertEqual(find.relation([0, 2, 5, 0]), "new")
        self.assertEqual(find.relation([0, 3]), "new")


class FindWalkWireTests(unittest.TestCase):
    def _validate(self, observation):
        wire_request._validate_observation(observation, "observation")

    def test_find_fields_need_find_walk(self):
        for observation in ({"find_query": {"name": "x"}},
                            {"find_resume_after": [0, 1]}):
            with self.assertRaises(wire_request.ProviderRequestError):
                self._validate(observation)

    def test_find_fields_are_typed(self):
        for observation in (
            {"find_walk": True, "find_query": {"name": 3}},
            {"find_walk": True, "find_query": {"role": "button"}},
            {"find_walk": True, "find_query": {"name_exact": "yes"}},
            {"find_walk": True, "find_resume_after": []},
            {"find_walk": True, "find_resume_after": [0, -1]},
            {"find_walk": True, "find_resume_after": [0, True]},
        ):
            with self.assertRaises(wire_request.ProviderRequestError):
                self._validate(observation)
        self._validate({"find_walk": True,
                        "find_query": {"name": "x", "name_exact": True,
                                       "text": "", "text_exact": False},
                        "find_resume_after": [0, 3, 1]})

    def test_observe_passes_the_find_fields(self):
        seen = {}

        def build(*args, **kwargs):
            seen.update(kwargs)
            return {"elements": [], "modalNotes": []}

        with mock.patch.object(ax_dispatch, "build_snapshot", build):
            response = ax_dispatch._observe_operation(
                {"app": "Editor"},
                {"find_walk": True, "find_query": {"name": "x"},
                 "find_resume_after": [0, 4]})
        self.assertEqual(seen["find_query"], {"name": "x"})
        self.assertEqual(seen["find_resume_after"], [0, 4])
        self.assertNotIn("notes", response)
