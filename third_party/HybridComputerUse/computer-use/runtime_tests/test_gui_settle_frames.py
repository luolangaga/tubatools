#!/usr/bin/env python3
"""GUI batch cases for settle waits, visual change reports, coordinate frame guards, and structured failures."""

from runtime_test_support import *  # noqa: F401,F403
from runtime_tests.gui_test_helpers import (
    captured,
    patch_gui_capture,
)


class GUISettleAndFrameGuardCases:
    """Cases of GUIScreenCoordinateTests; mixed into it so test ids keep its name."""

    def test_gui_settle_requires_a_300ms_stable_run(self):
        frame = {
            "data": bytes([10, 20, 30]),
            "stride": 3,
            "channels": 3,
            "width": 1,
            "height": 1,
        }
        with mock.patch.object(
                runtime, "capture_window_pixels",
                side_effect=[frame, frame, frame, frame]):
            now = [0.0]

            def sleep(seconds):
                now[0] += seconds

            result = runtime.wait_for_gui_settle(
                self.desktop,
                800,
                clock=lambda: now[0],
                sleep=sleep,
            )

        self.assertTrue(result["stable"])
        self.assertGreaterEqual(result["stable_samples"], 4)
        self.assertGreaterEqual(result["stable_for_ms"], 300)
        self.assertEqual(result["polls"], 3)
        self.assertEqual(result["elapsed_ms"], 300)
        note = runtime.gui_settle_note(result)
        self.assertIn("consecutive full-desktop screenshots", note)
        self.assertIn("over 300ms", note)
        self.assertIn("does not verify any action effect", note)

    def test_gui_settle_requires_four_samples_even_when_polling_is_delayed(self):
        frame = {
            "data": bytes([10, 20, 30]),
            "stride": 3,
            "channels": 3,
            "width": 1,
            "height": 1,
        }
        now = [0.0]

        def delayed_sleep(_seconds):
            now[0] += 0.15

        with mock.patch.object(
                runtime, "capture_window_pixels",
                side_effect=[frame, frame, frame, frame]):
            result = runtime.wait_for_gui_settle(
                self.desktop,
                800,
                clock=lambda: now[0],
                sleep=delayed_sleep,
            )

        self.assertTrue(result["stable"])
        self.assertEqual(result["stable_samples"], 4)
        self.assertEqual(result["polls"], 3)
        self.assertEqual(result["elapsed_ms"], 450)

    def test_gui_settle_compares_every_pixel_without_colour_tolerance(self):
        first = {
            "data": bytes([0] * 27),
            "stride": 27,
            "channels": 3,
            "width": 9,
            "height": 1,
        }
        changed_unsampled_pixel = dict(
            first,
            data=bytes([0, 0, 0, 1, 0, 0] + [0] * 21),
        )
        samples = [
            first,
            first,
            changed_unsampled_pixel,
            changed_unsampled_pixel,
            changed_unsampled_pixel,
            changed_unsampled_pixel,
        ]
        now = [0.0]

        def sleep(seconds):
            now[0] += seconds

        with mock.patch.object(
                runtime, "capture_window_pixels", side_effect=samples):
            result = runtime.wait_for_gui_settle(
                self.desktop,
                1000,
                clock=lambda: now[0],
                sleep=sleep,
            )

        self.assertTrue(result["stable"])
        self.assertEqual(result["stable_samples"], 4)
        self.assertEqual(result["polls"], 5)
        self.assertEqual(result["elapsed_ms"], 500)

    def test_gui_settle_resets_the_stable_run_after_a_change(self):
        first = {
            "data": bytes([0, 0, 0]),
            "stride": 3,
            "channels": 3,
            "width": 1,
            "height": 1,
        }
        second = dict(first, data=bytes([255, 255, 255]))
        samples = [first, first, first, second, second, second, second]
        now = [0.0]

        def sleep(seconds):
            now[0] += seconds

        with mock.patch.object(
                runtime, "capture_window_pixels", side_effect=samples):
            result = runtime.wait_for_gui_settle(
                self.desktop,
                1000,
                clock=lambda: now[0],
                sleep=sleep,
            )

        self.assertTrue(result["stable"])
        self.assertEqual(result["stable_samples"], 4)
        self.assertGreaterEqual(result["stable_for_ms"], 300)
        self.assertEqual(result["polls"], 6)

    def test_gui_settle_timeout_is_visible_not_an_error(self):
        before = {
            "data": bytes([0, 0, 0]),
            "stride": 3,
            "channels": 3,
            "width": 1,
            "height": 1,
        }
        after = dict(before, data=bytes([255, 255, 255]))
        now = [0.0]

        def sleep(seconds):
            now[0] += seconds

        with mock.patch.object(
                runtime, "capture_window_pixels", side_effect=[before, after]):
            result = runtime.wait_for_gui_settle(
                self.desktop,
                100,
                clock=lambda: now[0],
                sleep=sleep,
            )

        self.assertFalse(result["stable"])
        self.assertTrue(result["timed_out"])
        self.assertIn(
            "quiescence was not established", runtime.gui_settle_note(result))

    def test_settle_ms_zero_is_reported_as_an_explicit_skip(self):
        result = runtime.wait_for_gui_settle(self.desktop, 0)

        self.assertTrue(result["skipped"])
        self.assertEqual(result["reason"], "settle_ms_zero")
        self.assertIn("settle_ms=0", runtime.gui_settle_note(result))

    def test_perform_gui_uses_builtin_settle_for_mutating_batch(self):
        pixels = {
            "data": bytes([0, 0, 0]),
            "stride": 3,
            "channels": 3,
            "width": 1,
            "height": 1,
        }
        settled = {
            "stable": True,
            "elapsed_ms": 300,
            "polls": 3,
            "stable_samples": 4,
            "stable_for_ms": 300,
            "pixels": pixels,
        }
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(runtime, "execute_gui_action"), \
                mock.patch.object(
                    runtime, "wait_for_gui_settle",
                    return_value=settled) as wait, \
                patch_gui_capture(
                    return_value={"frameId": "layout", "screenshotPngBase64": "png",
                                  "pixels": pixels}):
            response = runtime.perform_gui(
                [{"type": "click", "x": 1, "y": 2}],
                settle_ms=900,
            )

        wait.assert_called_once_with(self.desktop, 900)
        self.assertTrue(response["ok"])
        self.assertTrue(response["quiescent"])
        self.assertTrue(response["effect_unknown"])
        self.assertIn("pixel-identical over 300ms", response["text"])
        self.assertIn("effect_unknown=true", response["text"])

    def test_gui_reports_nonassertive_visual_change(self):
        before = {
            "data": bytes([0, 0, 0]),
            "stride": 3,
            "channels": 3,
            "width": 1,
            "height": 1,
        }
        after = dict(before, data=bytes([255, 255, 255]))
        settled = {
            "stable": True,
            "elapsed_ms": 300,
            "polls": 3,
            "stable_samples": 4,
            "stable_for_ms": 300,
            "pixels": after,
        }
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(runtime, "execute_gui_action"), \
                mock.patch.object(
                    runtime, "wait_for_gui_settle", return_value=settled), \
                patch_gui_capture(side_effect=[
                    {"frameId": "layout", "screenshotPngBase64": "png",
                     "pixels": before},
                    {"frameId": "layout", "screenshotPngBase64": "png",
                     "pixels": after}]):
            response = runtime.perform_gui(
                [{"type": "click", "x": 1, "y": 2}],
                settle_ms=900,
            )

        self.assertTrue(response["changed_since_before"])
        self.assertTrue(response["visual_change_observed"])
        self.assertTrue(response["guiResult"]["visual_change_observed"])
        self.assertIn("exact_full_frame", response["visual_change_method"])
        self.assertNotIn("visual_change_verified", response)
        self.assertNotIn("visual_change_verified", response["guiResult"])
        self.assertGreater(response["visual_change_percent"], 0)
        self.assertIn("visual_changed=true", response["text"])
        self.assertIn("non-assertive", response["text"])

    def test_gui_reports_nonassertive_no_visual_change(self):
        frame = {
            "data": bytes([10, 20, 30]),
            "stride": 3,
            "channels": 3,
            "width": 1,
            "height": 1,
        }
        settled = {
            "stable": True,
            "elapsed_ms": 300,
            "polls": 3,
            "stable_samples": 4,
            "stable_for_ms": 300,
            "pixels": frame,
        }
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(runtime, "execute_gui_action"), \
                mock.patch.object(
                    runtime, "wait_for_gui_settle", return_value=settled), \
                patch_gui_capture(
                    return_value={"frameId": "layout", "screenshotPngBase64": "png",
                                  "pixels": frame}):
            response = runtime.perform_gui(
                [{"type": "click", "x": 1, "y": 2}],
            )

        self.assertFalse(response["changed_since_before"])
        self.assertFalse(response["visual_change_observed"])
        self.assertEqual(
            response["visual_change_method"], "exact_full_frame_bytes")
        self.assertNotIn("visual_change_verified", response)
        self.assertIn("visual_changed=false", response["text"])

    def test_gui_observation_is_immediate_and_does_not_compare_frames(self):
        snapshot = {
            "frameId": "layout",
            "screenshotPngBase64": "png",
        }
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(runtime, "capture_window_pixels") as capture, \
                mock.patch.object(runtime, "execute_gui_action"), \
                mock.patch.object(
                    runtime, "wait_for_gui_settle") as wait, \
                patch_gui_capture(
                    return_value=snapshot):
            response = runtime.perform_gui([{"type": "screenshot"}])

        wait.assert_not_called()
        capture.assert_not_called()
        self.assertIsNone(response["changed_since_before"])
        self.assertIsNone(response["visual_change_observed"])
        self.assertIsNone(response["quiescent"])
        self.assertFalse(response["effect_unknown"])
        self.assertEqual(response["snapshot"], snapshot)

    def test_gui_visual_change_detects_changes_missed_by_sampled_region_stats(self):
        before = {
            "data": bytes([0] * 27),
            "stride": 27,
            "channels": 3,
            "width": 9,
            "height": 1,
        }
        after = dict(
            before,
            data=bytes([0, 0, 0, 1, 0, 0] + [0] * 21),
        )

        result = runtime.gui_visual_change(before, after)

        self.assertTrue(result["changed_since_before"])
        self.assertEqual(result["visual_change_percent"], 0.0)
        self.assertIn("sampled_rgb_stride", result["visual_change_method"])

    def test_runtime_rejects_frame_stale_against_live_desktop(self):
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                patch_gui_capture(
                    return_value={
                        "frameId": "current-frame",
                        "screenshotPngBase64": "png",
                    }), \
                mock.patch.object(runtime, "execute_gui_action") as execute:
            response = runtime.perform_gui(
                [{"type": "click", "x": 1, "y": 2}],
                frame_id="old-frame",
            )

        self.assertFalse(response["ok"])
        self.assertEqual(response["failureCode"], "stale_gui_frame")
        self.assertEqual(response["failurePhase"], "precondition")
        self.assertIn("layout changed", response["error"])
        self.assertEqual(response["dispatched"], 0)
        execute.assert_not_called()

    def test_keyboard_only_batch_ignores_coordinate_frame_guard(self):
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                patch_gui_capture(
                    return_value={
                        "frameId": "current-frame",
                        "screenshotPngBase64": "png",
                    }), \
                mock.patch.object(runtime, "execute_gui_action") as execute, \
                mock.patch.object(
                    runtime, "wait_for_gui_settle",
                    return_value={"stable": False, "elapsed_ms": 0, "polls": 0,
                                  "skipped": True}):
            response = runtime.perform_gui(
                [{"type": "type", "text": "hello"}],
                frame_id="old-coordinate-frame",
                settle_ms=0,
            )

        self.assertTrue(response["ok"])
        execute.assert_called_once()

    def test_runtime_stale_guard_uses_layout_frame_not_content_frame(self):
        layout = {
            "desktop": {"x": 0, "y": 0, "width": 1920, "height": 1080},
            "activeWindow": "0x20",
            "windows": [],
        }
        frame_id = runtime.desktop_layout_frame_id(layout)
        snapshots = [
            {
                "frameId": frame_id,
                "screenshotPngBase64": "png",
            },
            {
                "frameId": frame_id,
                "screenshotPngBase64": "png",
            },
        ]
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                patch_gui_capture(
                                  side_effect=snapshots), \
                mock.patch.object(
                    runtime, "desktop_layout_state", return_value=layout), \
                mock.patch.object(runtime, "execute_gui_action") as execute, \
                mock.patch.object(
                    runtime, "wait_for_gui_settle",
                    return_value={"stable": False, "elapsed_ms": 0, "polls": 0,
                                  "skipped": True}):
            response = runtime.perform_gui(
                [{"type": "click", "x": 1, "y": 2}],
                frame_id=frame_id,
            )

        self.assertTrue(response["ok"])
        execute.assert_called_once()

    def test_runtime_rechecks_frame_before_each_coordinate_action(self):
        layout_a = {
            "desktop": {"x": 0, "y": 0, "width": 1920, "height": 1080},
            "activeWindow": "0x10",
            "windows": [],
        }
        layout_b = dict(layout_a, activeWindow="0x20")
        frame_id = runtime.desktop_layout_frame_id(layout_a)
        current = {
            "frameId": frame_id,
            "screenshotPngBase64": "png",
        }
        executed = []

        def execute(action):
            executed.append(action["type"])

        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                patch_gui_capture( return_value=current), \
                mock.patch.object(
                    runtime, "desktop_layout_state",
                    side_effect=[layout_b]), \
                mock.patch.object(
                    runtime, "execute_gui_action", side_effect=execute):
            response = runtime.perform_gui(
                [
                    {"type": "click", "x": 1, "y": 2},
                    {"type": "wait"},
                    {"type": "click", "x": 3, "y": 4},
                ],
                frame_id=frame_id,
            )

        self.assertFalse(response["ok"])
        self.assertIn("changed during the batch", response["error"])
        self.assertEqual(response["failureCode"], "stale_gui_frame")
        self.assertEqual(executed, ["click", "wait"])
        self.assertIn("before action 3", response["error"])
        self.assertEqual(response["dispatched"], 2)
        self.assertTrue(response["input_events_emitted"])
        self.assertEqual(response["input_actions_dispatched"], 1)
        self.assertTrue(response["mutation_unknown"])

    def test_gui_failure_exports_structured_result_metadata(self):
        response = runtime.finalize_gui_response({
            "ok": False,
            "error": "stale",
            "dispatched": 2,
            "input_events_emitted": True,
            "input_actions_dispatched": 1,
            "effect_unknown": True,
            "quiescent": None,
        })

        self.assertEqual(response["guiResult"]["dispatched"], 2)
        self.assertTrue(response["guiResult"]["input_events_emitted"])
        self.assertEqual(response["guiResult"]["input_actions_dispatched"], 1)
        self.assertFalse(response["guiResult"]["partial_input_possible"])
        for removed in (
            "window_commands_dispatched",
            "desktop_state_mutated",
            "window_action_observations",
        ):
            self.assertNotIn(removed, response["guiResult"])

    def test_post_dispatch_capture_failure_preserves_mutation_accounting(self):
        initial = {"frameId": "layout", "screenshotPngBase64": "png"}
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                patch_gui_capture(
                    side_effect=[initial, RuntimeError("capture failed")]), \
                mock.patch.object(runtime, "capture_window_pixels", return_value=None), \
                mock.patch.object(runtime, "execute_gui_action"), \
                mock.patch.object(
                    runtime, "wait_for_gui_settle",
                    return_value={"stable": False, "elapsed_ms": 0, "polls": 0,
                                  "skipped": True}):
            response = runtime.perform_gui([
                {"type": "move", "x": 1, "y": 2},
            ], settle_ms=0)

        self.assertFalse(response["ok"])
        self.assertEqual(response["dispatched"], 1)
        self.assertTrue(response["input_events_emitted"])
        self.assertEqual(response["input_actions_dispatched"], 1)
        self.assertNotIn("desktop_state_mutated", response)
        self.assertIn("post-dispatch observation failed", response["error"])

    def test_action_failure_and_error_screenshot_failure_preserve_partial_input(self):
        initial = {"frameId": "layout", "screenshotPngBase64": "png"}
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                patch_gui_capture(
                    side_effect=[initial, RuntimeError("capture failed")]), \
                mock.patch.object(runtime, "capture_window_pixels", return_value=None), \
                mock.patch.object(
                    runtime, "execute_gui_action",
                    side_effect=runtime.InputEmissionError(
                        "button release", True)):
            response = runtime.perform_gui([
                {"type": "click", "x": 1, "y": 2},
            ])

        self.assertFalse(response["ok"])
        self.assertTrue(response["input_events_emitted"])
        self.assertTrue(response["partial_input_possible"])
        self.assertTrue(response["mutation_unknown"])
        self.assertEqual(response["snapshot"], initial)

    def _settled_click(self, settle_pixels, before, delivered, matches=True):
        settled = {
            "stable": True,
            "elapsed_ms": 300,
            "polls": 3,
            "stable_samples": 4,
            "stable_for_ms": 300,
            "pixels": settle_pixels,
        }
        captures = [
            captured({"frameId": "layout", "screenshotPngBase64": "png",
                      "pixels": before}),
            captured({"frameId": "layout", "screenshotPngBase64": "png-final",
                      "pixels": delivered}, matches_bounds=matches),
        ]
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(runtime, "capture_gui_snapshot",
                                  side_effect=captures), \
                mock.patch.object(runtime, "execute_gui_action"), \
                mock.patch.object(
                    runtime, "wait_for_gui_settle", return_value=settled):
            return runtime.perform_gui([{"type": "click", "x": 1, "y": 2}])

    def test_final_capture_change_clears_quiescent(self):
        stable = {"data": bytes([0, 0, 0]), "stride": 3, "channels": 3,
                  "width": 1, "height": 1}
        changed = dict(stable, data=bytes([1, 0, 0]))
        response = self._settled_click(stable, stable, changed)

        self.assertTrue(response["ok"])
        self.assertIsNone(response["quiescent"])
        self.assertIn("changed again", response["text"])
        # The before/after comparison describes the attached image itself.
        self.assertTrue(response["changed_since_before"])
        self.assertEqual(response["snapshot"]["screenshotPngBase64"], "png-final")

    def test_quiescent_requires_the_delivered_frame_to_equal_the_settle_sample(self):
        # Regression: the settle verdict used to be checked against two extra
        # raw samples taken around the final grab, never against the grab that
        # was encoded. A frame that changed only inside that grab still read as
        # quiescent=true next to an image of a different state.
        settled = {"data": bytes([0, 0, 0]), "stride": 3, "channels": 3,
                   "width": 1, "height": 1}
        same = dict(settled)
        repainted = dict(settled, data=bytes([0, 9, 0]))

        self.assertTrue(self._settled_click(settled, settled, same)["quiescent"])
        response = self._settled_click(settled, settled, repainted)
        self.assertIsNone(response["quiescent"])
        self.assertIsNone(response["guiResult"]["quiescent"])
        self.assertIn("changed again", response["text"])

    def test_final_frame_with_other_geometry_has_no_pixel_verdict(self):
        frame = {"data": bytes([0, 0, 0]), "stride": 3, "channels": 3,
                 "width": 1, "height": 1}
        response = self._settled_click(frame, frame, frame, matches=False)

        self.assertTrue(response["ok"])
        self.assertIsNone(response["quiescent"])
        self.assertIsNone(response["changed_since_before"])

    def test_gui_exception_still_has_structured_result(self):
        with mock.patch.object(
                runtime, "perform_gui", side_effect=RuntimeError("capture failed")):
            response = perform_provider_operation({
                "intent": "desktop_execute_batch",
                "actions": [],
            })

        self.assertFalse(response["ok"])
        self.assertEqual(response["error"], "capture failed")
        self.assertEqual(response["guiResult"]["dispatched"], 0)
        self.assertFalse(response["guiResult"]["input_events_emitted"])
