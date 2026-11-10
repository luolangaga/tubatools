"""Popup navigation keeps focus evidence without quadratic provider reads."""

from runtime_test_support import *  # noqa: F401,F403
from runtime_tests import test_select_boundaries as fixtures
from runtime_tests.test_capabilities import _Node


class GeckoDropdownCommitTests(AtspiPatchedTestCase):
    """Content selection and native popup activation are different operations."""

    _combo = fixtures.ComboOptionSelectTests._combo

    def setUp(self):
        super().setUp()
        patch = mock.patch.object(runtime, "time", fixtures._FakeClock())
        patch.start()
        self.addCleanup(patch.stop)

    def _gecko(self, opened=False, extra=False, disabled=False):
        combo, items = self._combo(item_action=False)
        frame = _Node(role="frame")
        _Node(role="application").add(frame)
        frame.add(combo)
        combo.get_attributes = lambda: {"tag": "select"}
        combo.app.get_toolkit_name.return_value = "Gecko"
        combo.get_application = lambda: combo.app
        popup = _Node(role="combo box", states={STATE.EXPANDABLE}, x=10, y=20)
        menu = _Node(role="menu", x=10, y=20, w=100, h=80)
        popup.add(menu)
        calls, changes = [], []
        for index, item in enumerate(items):
            item.states.update({STATE.ENABLED, STATE.SENSITIVE})
            row = _Node(role="menu item", name=item.title,
                        states={STATE.ENABLED, STATE.SENSITIVE})
            row.get_n_actions = lambda: 1
            row.get_action_name = lambda _i: "click"

            def click(_i, index=index):
                calls.append("click")
                for source in items:
                    source.states.discard(STATE.SELECTED)
                items[index].states.add(STATE.SELECTED)
                changes.append(items[index].title)
                popup.states.discard(STATE.EXPANDED)
                return True

            row.do_action = click
            menu.add(row)
        if disabled:
            menu.children[2].states.clear()
            items[2].states.clear()
        frame.add(popup)

        def open_list(_i):
            calls.append("open")
            popup.states.update({STATE.EXPANDED, STATE.SHOWING})
            if extra:
                other = _Node(role="combo box", states={STATE.EXPANDED, STATE.SHOWING})
                other.get_state_set = lambda: FakeStateSet(other.states)
                frame.add(other)
            return True

        def close_list(_i):
            calls.append("close")
            popup.states.discard(STATE.EXPANDED)
            return True

        combo.get_n_actions = lambda: 1
        combo.get_action_name = lambda _i: "open"
        combo.do_action = open_list
        popup.get_n_actions = lambda: 1
        popup.get_action_name = lambda _i: "close"
        popup.do_action = close_list
        popup.get_state_set = lambda: FakeStateSet(popup.states)
        if opened:
            popup.states.update({STATE.EXPANDED, STATE.SHOWING})
        return combo, items, popup, calls, changes

    def test_content_selection_never_substitutes_for_popup_commit(self):
        combo, items, _popup, calls, changes = self._gecko()
        runtime.perform_select({"text": "Seattle"}, None, combo, {}, None, [], {})
        self.assertEqual(changes, ["Seattle"])
        self.assertEqual(calls, ["open", "click"])
        self.assertIn(STATE.SELECTED, items[2].states)

    def test_open_source_popup_is_rebound_before_one_commit(self):
        combo, _items, _popup, calls, changes = self._gecko(opened=True)
        runtime.perform_select({"text": "Seattle"}, None, combo, {}, None, [], {})
        self.assertEqual(changes, ["Seattle"])
        self.assertEqual(calls, ["close", "open", "click"])

    def test_same_value_is_a_no_input_no_event_operation(self):
        combo, _items, _popup, calls, changes = self._gecko()
        runtime.perform_select({"text": "Beijing"}, None, combo, {}, None, [], {})
        self.assertEqual((calls, changes), ([], []))

    def test_ambiguous_popup_keeps_open_input_evidence_without_selecting(self):
        combo, _items, _popup, calls, changes = self._gecko(extra=True)
        with self.assertRaises(runtime.OperationFailure) as caught:
            runtime.perform_select({"text": "Seattle"}, None, combo, {}, None, [], {})
        self.assertTrue(caught.exception.input_events_emitted)
        self.assertEqual(changes, [])
        self.assertEqual(calls, ["open"])

    def test_disabled_target_is_rejected_before_opening(self):
        combo, _items, _popup, calls, changes = self._gecko(disabled=True)
        with self.assertRaises(runtime.OperationFailure) as caught:
            runtime.perform_select({"text": "Seattle"}, None, combo, {}, None, [], {})
        self.assertFalse(caught.exception.input_events_emitted)
        self.assertEqual((calls, changes), ([], []))

    def test_identical_options_in_another_control_do_not_receive_the_click(self):
        combo, _items, _popup, calls, changes = self._gecko()
        other, other_items = self._combo(item_action=False)
        other.get_attributes = combo.get_attributes
        other.get_application = combo.get_application
        combo.parent.add(other)
        runtime.perform_select({"text": "Seattle"}, None, combo, {}, None, [], {})
        self.assertEqual(changes, ["Seattle"])
        self.assertIn(STATE.SELECTED, other_items[0].states)
        self.assertEqual(calls, ["open", "click"])

    def test_popup_ref_resolves_only_one_anchored_source(self):
        combo, _items, popup, calls, changes = self._gecko(opened=True)
        popup.get_application = combo.get_application
        popup.get_attributes = lambda: {"tag": "menulist"}
        runtime.perform_select({"text": "Seattle"}, None, popup, {}, None, [], {})
        self.assertEqual(changes, ["Seattle"])
        self.assertEqual(calls, ["close", "open", "click"])

    def test_popup_ref_with_two_matching_anchors_is_rejected(self):
        combo, _items, popup, calls, changes = self._gecko(opened=True)
        popup.get_application = combo.get_application
        popup.get_attributes = lambda: {"tag": "menulist"}
        other, _other_items = self._combo()
        other.get_attributes = combo.get_attributes
        other.get_application = combo.get_application
        combo.parent.add(other)
        with self.assertRaises(runtime.OperationFailure) as caught:
            runtime.perform_select({"text": "Seattle"}, None, popup, {}, None, [], {})
        self.assertFalse(caught.exception.input_events_emitted)
        self.assertEqual((calls, changes), ([], []))

    def test_click_value_without_source_readback_is_not_success(self):
        combo, items, popup, calls, changes = self._gecko()
        original = popup.children[0].children[2].do_action

        def wrong_choice(index):
            original(index)
            items[2].states.discard(STATE.SELECTED)
            items[0].states.add(STATE.SELECTED)
            return True

        popup.children[0].children[2].do_action = wrong_choice
        with self.assertRaises(runtime.OperationFailure) as caught:
            runtime.perform_select({"text": "Seattle"}, None, combo, {}, None, [], {})
        self.assertTrue(caught.exception.input_events_emitted)
        self.assertEqual(calls, ["open", "click"])

    def test_other_frame_is_rejected_before_input(self):
        combo, _items, _popup, calls, changes = self._gecko()
        with self.assertRaises(runtime.OperationFailure):
            runtime.perform_select({"text": "Seattle"}, _Node(role="frame"),
                                   combo, {}, None, [], {})
        self.assertEqual((calls, changes), ([], []))

    def test_unrelated_open_popup_is_not_closed_or_selected(self):
        combo, _items, popup, calls, changes = self._gecko(opened=True)
        popup.children[0].children[1].title = "Unrelated"
        with self.assertRaises(runtime.OperationFailure) as caught:
            runtime.perform_select({"text": "Seattle"}, None, combo, {}, None, [], {})
        self.assertFalse(caught.exception.input_events_emitted)
        self.assertEqual((calls, changes), ([], []))

    def test_missing_or_incomplete_popup_never_falls_back_to_content_selection(self):
        for kind in ("missing", "unreadable", "different", "disabled", "no-click"):
            with self.subTest(kind=kind):
                combo, items, popup, calls, changes = self._gecko()
                if kind == "missing":
                    combo.do_action = lambda _i: calls.append("open") or True
                elif kind == "unreadable":
                    popup.get_child_count = lambda: None
                elif kind == "different":
                    popup.children[0].children[2].title = "Other"
                elif kind == "disabled":
                    popup.children[0].children[2].states.clear()
                else:
                    popup.children[0].children[2].get_action_name = lambda _i: "select"
                with self.assertRaises(runtime.OperationFailure) as caught:
                    runtime.perform_select({"text": "Seattle"}, None, combo, {}, None, [], {})
                self.assertTrue(caught.exception.input_events_emitted)
                self.assertEqual(changes, [])
                self.assertIn(STATE.SELECTED, items[0].states)
                self.assertEqual(calls, ["open"])

    def test_unreadable_popup_state_cannot_prove_there_is_only_one(self):
        combo, _items, popup, calls, changes = self._gecko()
        popup.get_state_set = lambda: None
        with self.assertRaises(runtime.OperationFailure) as caught:
            runtime.perform_select({"text": "Seattle"}, None, combo, {}, None, [], {})
        self.assertFalse(caught.exception.input_events_emitted)
        self.assertEqual((calls, changes), ([], []))

    def test_popup_reply_loss_does_not_retry(self):
        combo, items, popup, calls, changes = self._gecko()
        original = popup.children[0].children[2].do_action

        def lost_reply(index):
            original(index)
            raise RuntimeError("reply lost")

        popup.children[0].children[2].do_action = lost_reply
        with self.assertRaises(RuntimeError):
            runtime.perform_select({"text": "Seattle"}, None, combo, {}, None, [], {})
        self.assertEqual(changes, ["Seattle"])
        self.assertEqual(calls, ["open", "click"])

    def test_unreadable_state_after_click_does_not_prove_popup_closed(self):
        combo, _items, popup, calls, changes = self._gecko()
        original = popup.children[0].children[2].do_action

        def unreadable(index):
            original(index)
            popup.get_state_set = lambda: None
            return True

        popup.children[0].children[2].do_action = unreadable
        with self.assertRaises(runtime.OperationFailure) as caught:
            runtime.perform_select({"text": "Seattle"}, None, combo, {}, None, [], {})
        self.assertTrue(caught.exception.input_events_emitted)
        self.assertEqual(changes, ["Seattle"])
        self.assertEqual(calls, ["open", "click"])

    def test_popup_click_without_close_is_not_a_verified_commit(self):
        combo, _items, popup, calls, changes = self._gecko()
        original = popup.children[0].children[2].do_action

        def remains_open(index):
            original(index)
            popup.states.add(STATE.EXPANDED)
            return True

        popup.children[0].children[2].do_action = remains_open
        with self.assertRaises(runtime.OperationFailure) as caught:
            runtime.perform_select({"text": "Seattle"}, None, combo, {}, None, [], {})
        self.assertEqual(caught.exception.failure_code, "effect_unknown")
        self.assertEqual(changes, ["Seattle"])
        self.assertEqual(calls, ["open", "click"])


class PopupNavigationTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.DropdownCommitTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)

    def select(self, combo, wanted):
        runtime.perform_select({"text": wanted}, None, combo, {}, None, [], {})

    def test_long_lists_use_linear_focus_reads_in_both_directions(self):
        for count in (503, 600, runtime.MAX_DROPDOWN_OPTIONS - 2):
            names = ["Item %s" % index for index in range(count)]
            for start, target in ((0, count // 2), (0, count - 1),
                                  (count - 1, count // 2), (count - 1, 0)):
                with self.subTest(count=count, start=start, target=target):
                    combo, _items, state = self.fixture._qt_combo(
                        value=names[start], options=names)
                    reads = []
                    original = runtime.state_contains

                    def observed(node, flag):
                        if flag == STATE.FOCUSED:
                            reads.append(node)
                        return original(node, flag)

                    with mock.patch.object(runtime, "state_contains", observed):
                        self.select(combo, names[target])
                    self.assertEqual(combo.title, names[target])
                    self.assertEqual(state["keys"][-1], "Return")
                    self.assertLess(len(reads), 6 * count)

    def test_separators_and_disabled_rows_do_not_shift_the_target(self):
        names = ["First", "Disabled", "", "Target", "Last"]
        for start, wanted in (("First", "Target"), ("Last", "First")):
            with self.subTest(start=start):
                combo, _items, state = self.fixture._qt_combo(
                    value=start, options=names, disabled_indices=(1,))
                self.select(combo, wanted)
                self.assertEqual(combo.title, wanted)
                self.assertEqual(state["keys"][-1], "Return")

    def test_delayed_focus_does_not_pay_a_fixed_poll_period_per_arrow(self):
        for count in (600, 1998):
            with self.subTest(count=count):
                names = ["Item %s" % index for index in range(count)]
                combo, _items, state = self.fixture._qt_combo(
                    value=names[0], options=names)
                original_key = runtime.send_key
                original_state = runtime.state_contains
                clock = runtime.time
                pending = []

                def send(key):
                    if key in ("Down", "Up"):
                        self.assertFalse(pending)
                        pending.append((key, clock.monotonic() + 0.003))
                    else:
                        original_key(key)

                def observed(node, flag):
                    if pending and clock.monotonic() >= pending[0][1]:
                        key, _deadline = pending.pop()
                        original_key(key)
                    return original_state(node, flag)

                started = clock.monotonic()
                with mock.patch.object(runtime, "send_key", send), \
                        mock.patch.object(runtime, "state_contains", observed):
                    self.select(combo, names[-1])
                elapsed = clock.monotonic() - started
                self.assertEqual(combo.title, names[-1])
                self.assertEqual(len(state["keys"]), count)
                self.assertLess(elapsed, 0.02 * (count - 1))

    def test_focus_wait_backs_off_and_stops_at_its_existing_deadline(self):
        from native.linux.provider import ax_dropdown
        clock = runtime.time
        original_sleep = clock.sleep
        sleeps = []

        def sleep(seconds):
            sleeps.append(seconds)
            original_sleep(seconds)

        started = clock.monotonic()
        with mock.patch.object(clock, "sleep", sleep):
            self.assertFalse(ax_dropdown._wait_for_focus_advance(lambda: False))
        self.assertAlmostEqual(clock.monotonic() - started, 0.6)
        self.assertEqual(sleeps[:4], [0.001, 0.002, 0.004, 0.008])
        self.assertTrue(all(0 < delay <= 0.02 for delay in sleeps))
        self.assertLess(len(sleeps), 40)

    def test_disabled_target_is_not_crossed_repeatedly(self):
        combo, _items, state = self.fixture._qt_combo(
            value="First", options=["First", "Disabled", "Last"],
            disabled_indices=(1,))
        with self.assertRaises(runtime.OperationFailure):
            self.select(combo, "Disabled")
        self.assertEqual(combo.title, "First")
        self.assertEqual(state["keys"], ["Down", "Escape"])

    def test_unchanged_focus_cancels_without_committing(self):
        combo, _items, state = self.fixture._qt_combo()
        original = runtime.send_key

        def key(value):
            if value == "Down":
                state["keys"].append(value)
            else:
                original(value)

        with mock.patch.object(runtime, "send_key", key):
            with self.assertRaises(runtime.OperationFailure):
                self.select(combo, "Audio")
        self.assertEqual(combo.title, "Video")
        self.assertEqual(state["keys"], ["Down", "Escape"])

    def test_final_scan_rejects_two_focused_items(self):
        combo, items, state = self.fixture._qt_combo()
        original = runtime.send_key

        def key(value):
            original(value)
            if value == "Down":
                items[0].states.add(STATE.FOCUSED)

        with mock.patch.object(runtime, "send_key", key):
            with self.assertRaises(runtime.OperationFailure):
                self.select(combo, "Audio")
        self.assertEqual(combo.title, "Video")
        self.assertEqual(state["keys"], ["Down", "Escape"])

    def test_wrong_direction_focus_cancels_without_return(self):
        combo, items, state = self.fixture._qt_combo()
        original = runtime.send_key

        def key(value):
            if value == "Down":
                for item in items:
                    item.states.discard(STATE.FOCUSED)
                items[0].states.add(STATE.FOCUSED)
                state["focus"] = 0
                state["keys"].append(value)
            else:
                original(value)

        with mock.patch.object(runtime, "send_key", key):
            with self.assertRaises(runtime.OperationFailure):
                self.select(combo, "Audio")
        self.assertEqual(combo.title, "Video")
        self.assertEqual(state["keys"], ["Down", "Escape"])
