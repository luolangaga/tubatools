#!/usr/bin/env python3
"""Event-driven dirty bit, tree reuse, and event-driven settle wait tests.

The fakes are deterministic: a fake clock advances only when code sleeps,
and fake events are delivered by pump() once the clock reaches their time.
"""

from runtime_test_support import *  # noqa: F401,F403


class _ScheduledMonitor(ui_events.EventMonitor):
    """EventMonitor whose pump() delivers scheduled events from a fake clock.

    ``schedule`` holds ``(time, bus)`` pairs. ``undrained`` makes the next
    pump report a backlog it could not drain.
    """

    def __init__(self, clock, schedule=(), registered_at=-10.0):
        super().__init__(clock=clock.monotonic, sleep=clock.sleep)
        self.fake_clock = clock
        self.schedule = sorted(schedule)
        self.active = True
        self.registered_at = registered_at
        self.undrained = False

    def pump(self, budget_seconds=ui_events.PUMP_BUDGET_SECONDS):
        if self.undrained:
            self.undrained = False
            self._global_generation += 1
            return False
        while self.schedule and self.schedule[0][0] <= self.fake_clock.now:
            _at, bus = self.schedule.pop(0)
            self.record(bus)
        return True


class EventMonitorTests(unittest.TestCase):
    def test_event_from_one_app_does_not_dirty_another(self):
        clock = _FakeClock()
        monitor = _ScheduledMonitor(clock)
        calc = monitor.mark(":1.26")
        gimp = monitor.mark(":1.77")
        monitor.record(":1.26")

        self.assertFalse(monitor.unchanged_since(":1.26", calc))
        self.assertTrue(monitor.unchanged_since(":1.77", gimp))

    def test_event_with_unknown_source_dirties_every_app(self):
        clock = _FakeClock()
        monitor = _ScheduledMonitor(clock)
        token = monitor.mark(":1.77")
        monitor.record(None)

        self.assertFalse(monitor.unchanged_since(":1.77", token))

    def test_undrained_backlog_dirties_every_app(self):
        clock = _FakeClock()
        monitor = _ScheduledMonitor(clock)
        token = monitor.mark(":1.77")
        monitor.undrained = True

        self.assertFalse(monitor.pump())
        self.assertFalse(monitor.unchanged_since(":1.77", token))

    def test_inactive_monitor_never_reports_unchanged(self):
        monitor = ui_events.EventMonitor()
        self.assertFalse(monitor.unchanged_since(":1.26", monitor.mark(":1.26")))

    def test_wait_for_event_returns_at_the_event(self):
        clock = _FakeClock()
        monitor = _ScheduledMonitor(clock, [(0.03, ":1.26")])

        self.assertTrue(monitor.wait_for_event(":1.26", 0.5))
        self.assertLess(clock.now, 0.04)

    def test_wait_for_event_times_out_without_events(self):
        clock = _FakeClock()
        monitor = _ScheduledMonitor(clock, [(0.03, ":1.77")])

        self.assertFalse(monitor.wait_for_event(":1.26", 0.2))
        self.assertAlmostEqual(clock.now, 0.2, places=6)


class _Probe:
    """Content probe stand-in whose answer the test controls."""

    def __init__(self, unchanged=True):
        self.answer = unchanged
        self.calls = 0

    def __len__(self):
        return 1

    def unchanged(self):
        self.calls += 1
        return self.answer


class TreeReuseCacheTests(unittest.TestCase):
    """A stored tree is reused only when every check passes."""

    BUS = ":1.26"
    KEY = ("bus", "/window", 0)
    PRINT = ("bounds", "title", 3, ())
    REFS = {"0.1": {"index": 4, "role": "push button", "name": "OK"}}

    def _cache(self, schedule=(), registered_at=-10.0, **kwargs):
        clock = _FakeClock()
        monitor = _ScheduledMonitor(clock, schedule, registered_at)
        # The application has delivered events before, so its events are
        # known to reach the listener.
        monitor.record(self.BUS)
        return clock, monitor, tree_reuse.TreeReuseCache(
            monitor, clock=clock.monotonic, **kwargs)

    def _store(self, cache, known=None, key=None, bus=None, size=1000,
               probe=None, tree="tree"):
        indexer = tree_reuse.RecordingIndexer(known or self.REFS)
        indexer.index_for([0, 1], "push button", "OK")
        indexer.index_for([0, 2], "push button", "Cancel")
        walk = cache.begin_walk(bus or self.BUS)
        cache.store(key or self.KEY, self.PRINT, walk, indexer.calls, tree,
                    size, probe)
        return walk

    def _lookup(self, cache, key=None, bus=None, refs=None):
        return cache.lookup(
            bus or self.BUS, key or self.KEY, self.PRINT, refs or self.REFS)

    def test_unchanged_window_reuses_the_tree(self):
        clock, _monitor, cache = self._cache()
        self._store(cache)
        clock.sleep(2.0)

        self.assertEqual(self._lookup(cache), "tree")

    def test_unchanged_window_is_reused_at_model_turn_gaps(self):
        """Model turns are about 8 s apart at the median and 70 s at p90."""
        for gap in (3.0, 10.0, 30.0, 70.0, 300.0):
            with self.subTest(gap=gap):
                clock, _monitor, cache = self._cache()
                self._store(cache, probe=_Probe())
                clock.sleep(gap)

                self.assertEqual(self._lookup(cache), "tree")
                self.assertEqual(cache.last_decision, "reused")

    def test_event_after_the_walk_began_forces_a_walk(self):
        clock, _monitor, cache = self._cache([(1.0, BUS_CALC)])
        self._store(cache)
        clock.sleep(2.0)

        self.assertIsNone(self._lookup(cache))

    def test_change_without_event_is_caught_by_the_fingerprint(self):
        """LibreOffice emits no event when its window is resized."""
        clock, _monitor, cache = self._cache()
        self._store(cache)
        clock.sleep(1.0)
        resized = ("other bounds",) + self.PRINT[1:]

        self.assertIsNone(cache.lookup(self.BUS, self.KEY, resized, self.REFS))

    def test_change_without_event_is_caught_by_the_content_probe(self):
        """Chrome scrolls and Calc recalculations emit no event."""
        clock, _monitor, cache = self._cache()
        probe = _Probe(unchanged=False)
        self._store(cache, probe=probe)
        clock.sleep(30.0)

        self.assertIsNone(self._lookup(cache))
        self.assertEqual(cache.last_decision, "content_changed")
        self.assertEqual(probe.calls, 1)
        # A changed entry can never be valid again, so it is gone.
        self.assertEqual(len(cache), 0)

    def test_event_emitted_while_the_probe_reads_forces_a_walk(self):
        clock, monitor, cache = self._cache()

        class EventDuringRead(_Probe):
            def unchanged(probe):
                monitor.schedule.append((clock.now, self.BUS))
                return True

        self._store(cache, probe=EventDuringRead())
        clock.sleep(5.0)

        self.assertIsNone(self._lookup(cache))
        self.assertEqual(cache.last_decision, "events_since_walk")

    def test_tree_older_than_the_backstop_is_not_reused(self):
        clock, _monitor, cache = self._cache()
        self._store(cache)
        clock.sleep(tree_reuse.SNAPSHOT_REUSE_MAX_AGE_SECONDS + 1)

        self.assertIsNone(self._lookup(cache))
        self.assertEqual(cache.last_decision, "too_old")

    def test_walk_inside_the_registration_grace_is_not_reused(self):
        clock, _monitor, cache = self._cache(registered_at=0.0)
        self._store(cache)
        clock.sleep(2.0)

        self.assertIsNone(self._lookup(cache))

    def test_app_that_never_emitted_an_event_is_not_trusted(self):
        clock = _FakeClock()
        monitor = _ScheduledMonitor(clock)
        cache = tree_reuse.TreeReuseCache(monitor, clock=clock.monotonic)
        self._store(cache)
        clock.sleep(1.0)

        self.assertIsNone(self._lookup(cache))

    def test_different_known_refs_force_a_walk(self):
        """Indices depend on the caller's refs; a mismatch must not be reused."""
        clock, _monitor, cache = self._cache()
        self._store(cache)
        clock.sleep(1.0)
        other = {"0.1": {"index": 9, "role": "push button", "name": "OK"}}

        self.assertIsNone(self._lookup(cache, refs=other))

    def test_walk_that_began_with_an_undrained_backlog_is_not_stored(self):
        clock, monitor, cache = self._cache()
        monitor.undrained = True
        self._store(cache)
        clock.sleep(1.0)

        self.assertIsNone(self._lookup(cache))

    def test_undrained_backlog_at_lookup_forces_a_walk(self):
        clock, monitor, cache = self._cache()
        self._store(cache)
        clock.sleep(1.0)
        monitor.undrained = True

        self.assertIsNone(self._lookup(cache))

    def test_inactive_monitor_stores_nothing(self):
        clock = _FakeClock()
        monitor = ui_events.EventMonitor(clock=clock.monotonic)
        cache = tree_reuse.TreeReuseCache(monitor, clock=clock.monotonic)
        cache.store(self.KEY, self.PRINT,
                    (self.BUS, monitor.mark(self.BUS), 0.0), [], "tree", 10)

        self.assertIsNone(self._lookup(cache))

    def test_walk_cut_by_time_or_read_errors_is_not_reusable(self):
        def omission(reason):
            return [{"kind": "provider_omission", "reason": reason}]

        self.assertTrue(runtime.walk_is_reusable(omission("filtered,budget")))
        self.assertFalse(runtime.walk_is_reusable(omission("filtered,time")))
        self.assertFalse(
            runtime.walk_is_reusable(omission("provider_probe_error")))


class TreeReuseEvictionTests(unittest.TestCase):
    """LRU order within a byte budget, and proactive drops."""

    BUS = TreeReuseCacheTests.BUS
    OTHER_BUS = ":1.77"
    PRINT = TreeReuseCacheTests.PRINT

    def _cache(self, schedule=(), **kwargs):
        clock = _FakeClock()
        monitor = _ScheduledMonitor(clock, schedule)
        monitor.record(self.BUS)
        monitor.record(self.OTHER_BUS)
        return clock, monitor, tree_reuse.TreeReuseCache(
            monitor, clock=clock.monotonic, **kwargs)

    def _store(self, cache, key, size, bus=None):
        walk = cache.begin_walk(bus or self.BUS)
        cache.store(key, self.PRINT, walk, [], key, size)

    def _lookup(self, cache, key, bus=None):
        return cache.lookup(bus or self.BUS, key, self.PRINT, {})

    def test_budget_evicts_the_least_recently_used_entry(self):
        clock, _monitor, cache = self._cache(max_bytes=3000)
        for key in ("a", "b", "c"):
            self._store(cache, (key,), 1000)
        clock.sleep(1.0)
        # Using "a" makes "b" the least recently used entry.
        self.assertEqual(self._lookup(cache, ("a",)), ("a",))

        self._store(cache, ("d",), 1000)

        self.assertIsNone(self._lookup(cache, ("b",)))
        for key in ("a", "c", "d"):
            self.assertEqual(self._lookup(cache, (key,)), (key,))
        self.assertLessEqual(cache.size_bytes, 3000)

    def test_budget_counts_bytes_not_entries(self):
        _clock, _monitor, cache = self._cache(max_bytes=3000)
        self._store(cache, ("small1",), 200)
        self._store(cache, ("small2",), 200)
        self._store(cache, ("big",), 2700)

        # Evicting the oldest small entry is enough to fit.
        self.assertIsNone(self._lookup(cache, ("small1",)))
        self.assertEqual(self._lookup(cache, ("small2",)), ("small2",))
        self.assertEqual(cache.size_bytes, 2900)
        self._store(cache, ("huge",), 5000)
        # An entry larger than the whole budget is not stored and evicts
        # nothing.
        self.assertEqual(len(cache), 2)

    def test_default_budget_keeps_many_windows(self):
        _clock, _monitor, cache = self._cache()
        for index in range(200):
            self._store(cache, (index,), 100 * 1024)

        self.assertEqual(len(cache), 200)

    def test_event_drops_the_entries_of_that_application_only(self):
        clock, _monitor, cache = self._cache([(1.0, self.BUS)])
        self._store(cache, ("calc",), 1000)
        self._store(cache, ("chrome",), 1000, bus=self.OTHER_BUS)
        clock.sleep(2.0)

        # Any lookup pumps the monitor and drops every entry that can
        # never be valid again, not only the one asked for.
        self.assertEqual(
            self._lookup(cache, ("chrome",), bus=self.OTHER_BUS), ("chrome",))
        self.assertEqual(len(cache), 1)
        self.assertEqual(cache.size_bytes, 1000)

    def test_walk_drops_entries_invalidated_by_events(self):
        clock, _monitor, cache = self._cache([(1.0, self.BUS)])
        self._store(cache, ("calc",), 1000)
        clock.sleep(2.0)

        self._store(cache, ("chrome",), 1000, bus=self.OTHER_BUS)

        self.assertEqual(len(cache), 1)

    def test_application_exit_drops_its_entries(self):
        """The registry, not the application, announces that it left."""
        clock, monitor, cache = self._cache()
        self._store(cache, ("calc",), 1000)
        self._store(cache, ("chrome",), 1000, bus=self.OTHER_BUS)

        class _App:
            bus_name = None

        class _Node:
            def __init__(self, bus):
                self.app = _App()
                self.app.bus_name = bus

        class _Event:
            type = "object:children-changed:remove"
            source = _Node("org.a11y.atspi.Registry")
            any_data = _Node(self.BUS)

        monitor._on_event(_Event())
        clock.sleep(1.0)

        self.assertIsNone(self._lookup(cache, ("calc",)))
        self.assertEqual(len(cache), 1)

    def test_window_close_drops_its_entries(self):
        """Closing a window emits window:destroy from its application."""
        clock, monitor, cache = self._cache()
        self._store(cache, ("dialog",), 1000)

        class _Source:
            class app:
                bus_name = TreeReuseCacheTests.BUS

        class _Event:
            type = "window:destroy"
            source = _Source()
            any_data = None

        monitor._on_event(_Event())
        cache.prune()

        self.assertEqual(len(cache), 0)
        self.assertEqual(cache.size_bytes, 0)

    def test_old_entries_are_dropped(self):
        clock, _monitor, cache = self._cache()
        self._store(cache, ("calc",), 1000)
        clock.sleep(tree_reuse.SNAPSHOT_REUSE_MAX_AGE_SECONDS + 1)

        self._store(cache, ("chrome",), 1000, bus=self.OTHER_BUS)

        self.assertEqual(len(cache), 1)


class _Sheet(_TreeNode):
    """A self-managed table like a Calc sheet: cells only by coordinate."""

    def __init__(self, cells, **kwargs):
        super().__init__(role="table", name="Sheet1", **kwargs)
        self.states.add(STATE.MANAGES_DESCENDANTS)
        self.cells = cells

    def get_child_count(self):
        return 2 ** 31 - 1

    def get_table_iface(self):
        return self


class ContentProbeTests(AtspiPatchedTestCase):
    """A real walk records what can change silently; a re-read catches it."""

    def setUp(self):
        super().setUp()
        self.clock = _FakeClock()
        self.monitor = _ScheduledMonitor(self.clock)
        self.monitor.record(TreeReuseCacheTests.BUS)
        self.cache = tree_reuse.TreeReuseCache(
            self.monitor, clock=self.clock.monotonic)

        def cell(text):
            node = _TreeNode(role="table cell", name="")
            node._text = FakeTextIface(text)
            return node

        self.cells = {(0, 0): cell("0.25"), (0, 1): cell("")}
        self.sheet = _Sheet(self.cells, x=0, y=40, w=200, h=100)
        self.button = _TreeNode(role="push button", name="OK", x=0, y=0, w=50,
                                h=20)
        self.window = _TreeNode(role="frame", name="Calc", w=400, h=300,
                                kids=(self.button, self.sheet))
        table_api = type("FakeAtspiTable", (), {
            "get_accessible_at": staticmethod(
                lambda table, row, col: table.cells.get((row, col))),
        })
        for patcher in (
            mock.patch.object(runtime.Atspi, "Table", table_api, create=True),
            mock.patch.object(runtime, "visible_cell_range",
                              return_value=(0, 0, 0, 1)),
        ):
            patcher.start()
            self.addCleanup(patcher.stop)

    def _walk_and_store(self):
        probe = tree_reuse.ContentProbe(runtime.DEFAULT_TEXT_LIMIT)
        walk = self.cache.begin_walk(TreeReuseCacheTests.BUS)
        indexer = tree_reuse.RecordingIndexer({})
        records, lines, annotations = runtime.render_tree(
            self.window, runtime.frame(0, 0, 400, 300), [0], prune=True,
            indexer=indexer, content_probe=probe)
        self.assertIn("0.25", "\n".join(lines))
        self.cache.store(TreeReuseCacheTests.KEY, TreeReuseCacheTests.PRINT,
                         walk, indexer.calls, (records, lines), 1000, probe)
        self.clock.sleep(30.0)

    def _lookup(self):
        return self.cache.lookup(
            TreeReuseCacheTests.BUS, TreeReuseCacheTests.KEY,
            TreeReuseCacheTests.PRINT, {})

    def test_unchanged_window_is_reused(self):
        self._walk_and_store()

        self.assertIsNotNone(self._lookup())

    def test_silent_cell_value_change_is_never_served(self):
        """A Calc recalculation changes visible cells without any event."""
        self._walk_and_store()
        self.cells[(0, 0)]._text.text = "0.75"

        self.assertIsNone(self._lookup())
        self.assertEqual(self.cache.last_decision, "content_changed")

    def test_silently_filled_empty_cell_is_never_served(self):
        self._walk_and_store()
        self.cells[(0, 1)]._text.text = "hello"

        self.assertIsNone(self._lookup())

    def test_silently_scrolled_viewport_is_never_served(self):
        self._walk_and_store()

        with mock.patch.object(runtime, "visible_cell_range",
                               return_value=(5, 5, 0, 1)):
            self.assertIsNone(self._lookup())

    def test_silent_scroll_that_moves_a_node_is_never_served(self):
        """Chrome scrolls a page without any event; frames change."""
        self._walk_and_store()
        self.button.component.extents = FakeExtents(0, -600, 50, 20)

        self.assertIsNone(self._lookup())

    def test_silent_state_change_is_never_served(self):
        """Scrolled-out web content loses SHOWING without an event."""
        self._walk_and_store()
        self.button.states.discard(STATE.SHOWING)

        self.assertIsNone(self._lookup())


BUS_CALC = TreeReuseCacheTests.BUS


class _BusApp(_ScriptedApp):
    class _App:
        bus_name = ":1.26"

    app = _App()


class EventSettleTests(AtspiPatchedTestCase):
    """Settle wait driven by application events instead of a fixed sleep."""

    def _wait(self, app, before, clock, schedule):
        monitor = _ScheduledMonitor(clock, schedule)
        return runtime.wait_for_ui_to_settle(
            app, before, clock=clock.monotonic, sleep=clock.sleep,
            monitor=monitor)

    def _app(self, clock, timeline=None):
        main = _WindowNode("frame", "Calc", active=True)
        return _BusApp(clock, timeline or [(0.0, [main])])

    def test_app_without_events_waits_the_old_minimum(self):
        clock = _FakeClock()
        app = self._app(clock)
        before = runtime.window_identity_set(app)

        self.assertIsNone(self._wait(app, before, clock, []))
        self.assertAlmostEqual(clock.now, runtime.SETTLE_MIN_SECONDS, places=3)

    def test_short_burst_ends_the_wait_at_the_floor(self):
        clock = _FakeClock()
        app = self._app(clock)
        before = runtime.window_identity_set(app)
        burst = [(0.005, ":1.26"), (0.006, ":1.26"), (0.010, ":1.26")]

        self.assertIsNone(self._wait(app, before, clock, burst))
        self.assertGreaterEqual(clock.now, runtime.SETTLE_EVENT_FLOOR_SECONDS)
        self.assertLess(clock.now, runtime.SETTLE_MIN_SECONDS)

    def test_continuing_events_are_capped_at_the_old_minimum(self):
        clock = _FakeClock()
        app = self._app(clock)
        before = runtime.window_identity_set(app)
        stream = [(0.01 * step, ":1.26") for step in range(1, 40)]

        self.assertIsNone(self._wait(app, before, clock, stream))
        self.assertLess(
            clock.now,
            runtime.SETTLE_MIN_SECONDS + ui_events.WAIT_STEP_SECONDS + 1e-6)

    def test_other_apps_events_do_not_shorten_the_wait(self):
        clock = _FakeClock()
        app = self._app(clock)
        before = runtime.window_identity_set(app)

        self.assertIsNone(
            self._wait(app, before, clock, [(0.005, ":1.77")]))
        self.assertAlmostEqual(clock.now, runtime.SETTLE_MIN_SECONDS, places=3)

    def test_new_window_still_waits_for_focus(self):
        """The window-change guarantee is unchanged by the event wait."""
        clock = _FakeClock()
        main_active = _WindowNode("frame", "Calc", active=True)
        main_idle = _WindowNode("frame", "Calc")
        dialog_idle = _WindowNode("dialog", "Format Cells")
        dialog_active = _WindowNode("dialog", "Format Cells", active=True)
        app = self._app(clock, [
            (0.0, [main_active]),
            (0.030, [main_active, dialog_idle]),
            (0.300, [main_idle, dialog_active]),
        ])
        before = runtime.window_identity_set(app)
        burst = [(0.010 + 0.005 * step, ":1.26") for step in range(5)]

        self.assertIsNone(self._wait(app, before, clock, burst))
        self.assertGreaterEqual(clock.now, 0.300)
        _, chosen = runtime.main_window(app)
        self.assertEqual(runtime.node_name(chosen), "Format Cells")

    def test_window_that_never_takes_focus_still_reports_the_timeout(self):
        clock = _FakeClock()
        main = _WindowNode("frame", "Calc", active=True)
        tooltip = _WindowNode("window", "")
        app = self._app(clock, [(0.0, [main]), (0.02, [main, tooltip])])
        before = runtime.window_identity_set(app)

        note = self._wait(app, before, clock, [(0.01, ":1.26")])

        self.assertIsNotNone(note)
        self.assertIn("target ref", note)
        self.assertLess(
            clock.now,
            runtime.SETTLE_MIN_SECONDS
            + runtime.SETTLE_FOCUS_TIMEOUT_SECONDS + 0.05)


if __name__ == "__main__":
    unittest.main()
