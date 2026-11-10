#!/usr/bin/env python3
"""Linux provider GUI input bounds, emission results, and accessibility coordinate fallback tests."""

from runtime_test_support import *  # noqa: F401,F403
from runtime_tests.gui_test_helpers import patch_gui_capture


class AccessibilityCoordinateFallbackTests(unittest.TestCase):
    def setUp(self):
        self.window = runtime.frame(100, 100, 800, 600)
        self.desktop = runtime.frame(0, 0, 1920, 1080)

    def test_uses_centre_of_visible_element_intersection(self):
        element = {"frame": runtime.frame(750, 550, 200, 200)}
        with mock.patch.object(
                runtime, "desktop_bounds", return_value=self.desktop):
            point = runtime.screen_point(self.window, element)

        self.assertEqual(point, (875.0, 675.0))

    def test_fails_closed_when_desktop_bounds_are_unavailable(self):
        element = {"frame": runtime.frame(10, 10, 100, 30)}
        with mock.patch.object(runtime, "desktop_bounds", return_value=None):
            with self.assertRaises(RuntimeError) as ctx:
                runtime.screen_point(self.window, element)

        self.assertIn("bounds are unavailable", str(ctx.exception))

    def test_rejects_element_below_the_window_instead_of_clicking_elsewhere(self):
        element = {"frame": runtime.frame(20, 6500, 100, 30)}
        with mock.patch.object(
                runtime, "desktop_bounds", return_value=self.desktop):
            with self.assertRaises(RuntimeError) as ctx:
                runtime.screen_point(self.window, element)

        self.assertIn("element_offscreen", str(ctx.exception))
        self.assertIn("scroll it into view", str(ctx.exception))

    def test_rejects_element_outside_the_desktop(self):
        element = {"frame": runtime.frame(-700, -500, 100, 30)}
        with mock.patch.object(
                runtime, "desktop_bounds", return_value=self.desktop):
            with self.assertRaises(RuntimeError) as ctx:
                runtime.screen_point(self.window, element)

        self.assertIn("element_offscreen", str(ctx.exception))


class InputBoundTests(unittest.TestCase):
    def test_click_count_has_a_hard_limit(self):
        with self.assertRaises(RuntimeError) as ctx:
            runtime.send_mouse_click(
                1, 2, "left", runtime.MAX_CLICK_COUNT + 1)

        self.assertIn("click_count", str(ctx.exception))

    def test_scroll_pages_has_a_hard_limit(self):
        with self.assertRaises(RuntimeError) as ctx:
            runtime.scroll_element(
                "down", runtime.MAX_SCROLL_PAGES + 1, point=(1, 2))

        self.assertIn("pages", str(ctx.exception))


class InputEmissionResultTests(unittest.TestCase):
    def gui_response(self, actions):
        snapshot = {"frameId": "layout", "screenshotPngBase64": "png"}
        with mock.patch.object(runtime, "desktop_bounds", return_value=runtime.frame(0, 0, 1920, 1080)), \
                patch_gui_capture(return_value=snapshot), \
                mock.patch.object(runtime, "capture_window_pixels", return_value=None), \
                mock.patch.object(runtime.time, "sleep"):
            return runtime.perform_gui(actions, frame_id="layout", settle_ms=0)

    def test_double_click_keeps_input_history_at_every_failed_event(self):
        sequence = ["abs", "b1p", "b1r", "abs", "b1p", "b1r"]
        for failure in (False, RuntimeError("mouse transport lost")):
            for failed_at in range(len(sequence)):
                with self.subTest(failure=failure, failed_at=failed_at):
                    events = []

                    def generate(_x, _y, event):
                        events.append(event)
                        if len(events) == failed_at + 1:
                            if isinstance(failure, Exception):
                                raise failure
                            return failure
                        return True

                    with mock.patch.object(runtime.Atspi, "generate_mouse_event", side_effect=generate), \
                            mock.patch.object(runtime, "send_text") as later_input:
                        response = self.gui_response([
                            {"type": "double_click", "x": 1, "y": 2},
                            {"type": "type", "text": "must not run"},
                        ])
                    release_needed = failed_at in (2, 5) or (
                        isinstance(failure, Exception) and failed_at in (1, 4))
                    self.assertEqual(events, sequence[:failed_at + 1] + (["b1r"] if release_needed else []))
                    self.assertFalse(response["ok"])
                    self.assertEqual(response["dispatched"], 0)
                    self.assertEqual(response["input_actions_dispatched"], 0)
                    self.assertEqual(response["input_events_emitted"], failed_at > 0)
                    uncertain = failed_at > 0 or isinstance(failure, Exception)
                    for field in ("partial_input_possible", "effect_unknown", "mutation_unknown"):
                        self.assertEqual(response[field], uncertain, field)
                    self.assertEqual(response["snapshot"]["screenshotPngBase64"], "png")
                    later_input.assert_not_called()

    def test_held_modifiers_keep_callback_input_failure_and_release_all_keys(self):
        for error in (runtime.InputEmissionError("pointer", False),
                      runtime.OperationFailure("pointer refused", "input_failed", "emit"),
                      RuntimeError("pointer transport lost")):
            with self.subTest(error=error):
                events = []

                def generate(code, _text, synth):
                    events.append((code, synth))
                    # Failure while releasing one key must not mask the callback.
                    return synth != runtime.Atspi.KeySynthType.RELEASE or code != 2

                with mock.patch.object(runtime, "keycode", side_effect=[1, 2]), \
                        mock.patch.object(runtime.Atspi, "generate_keyboard_event", side_effect=generate):
                    with self.assertRaises(type(error)) as caught:
                        runtime.with_held_mouse_modifiers(["CTRL", "SHIFT"], mock.Mock(side_effect=error))
                self.assertIs(caught.exception, error)
                self.assertTrue(error.events_emitted)
                self.assertEqual(events, [(1, runtime.Atspi.KeySynthType.PRESS),
                                          (2, runtime.Atspi.KeySynthType.PRESS),
                                          (2, runtime.Atspi.KeySynthType.RELEASE),
                                          (1, runtime.Atspi.KeySynthType.RELEASE)])

    def test_modifier_press_and_release_failures_keep_whole_action_facts(self):
        for failure in (False, RuntimeError("keyboard transport lost")):
            for failed_at in range(4):
                with self.subTest(failure=failure, failed_at=failed_at):
                    events = []
                    mouse_events = []

                    def keyboard(code, _text, synth):
                        events.append((code, synth))
                        if len(events) == failed_at + 1:
                            if isinstance(failure, Exception):
                                raise failure
                            return False
                        return True

                    with mock.patch.object(runtime, "keycode", return_value=1), \
                            mock.patch.object(runtime.Atspi, "generate_keyboard_event", side_effect=keyboard), \
                            mock.patch.object(runtime.Atspi, "generate_mouse_event", side_effect=lambda *_args: mouse_events.append(_args) or True):
                        response = self.gui_response([
                            {"type": "double_click", "x": 1, "y": 2, "keys": ["CTRL", "SHIFT"]},
                        ])
                    self.assertFalse(response["ok"])
                    self.assertEqual(response["dispatched"], 0)
                    expected_emitted = failed_at > 0 or isinstance(failure, Exception)
                    self.assertEqual(response["input_events_emitted"], expected_emitted)
                    self.assertEqual(response["partial_input_possible"], expected_emitted)
                    self.assertEqual(len(mouse_events), 0 if failed_at < 2 else 6)
                    releases = [event for event in events if event[1] == runtime.Atspi.KeySynthType.RELEASE]
                    expected_releases = 2 if failed_at >= 2 else failed_at + int(isinstance(failure, Exception))
                    self.assertEqual(len(releases), expected_releases)

    def test_gui_modifier_input_is_not_lost_when_pointer_is_refused(self):
        with mock.patch.object(runtime, "keycode", return_value=1), \
                mock.patch.object(runtime.Atspi, "generate_keyboard_event", return_value=True) as keyboard, \
                mock.patch.object(runtime.Atspi, "generate_mouse_event", return_value=False):
            response = self.gui_response([
                {"type": "click", "x": 1, "y": 2, "keys": ["CTRL"]},
            ])
        self.assertFalse(response["ok"])
        self.assertTrue(response["input_events_emitted"])
        self.assertTrue(response["partial_input_possible"])
        self.assertEqual(keyboard.call_count, 2)

    def test_unknown_modifier_press_with_refused_cleanup_stays_unknown(self):
        with mock.patch.object(runtime, "keycode", return_value=1), \
                mock.patch.object(runtime.Atspi, "generate_keyboard_event", side_effect=[RuntimeError("press transport lost"), False]) as keyboard, \
                mock.patch.object(runtime.Atspi, "generate_mouse_event") as mouse:
            response = self.gui_response([
                {"type": "click", "x": 1, "y": 2, "keys": ["CTRL"]},
            ])
        self.assertFalse(response["input_events_emitted"])
        self.assertTrue(response["partial_input_possible"])
        self.assertTrue(response["mutation_unknown"])
        self.assertIn("press transport lost", response["error"])
        self.assertEqual(keyboard.call_count, 2)
        mouse.assert_not_called()

    def test_cleanup_exception_does_not_replace_the_failed_mouse_release(self):
        with mock.patch.object(runtime.Atspi, "generate_mouse_event", side_effect=[True, True, False, RuntimeError("cleanup transport lost")]):
            response = self.gui_response([{"type": "click", "x": 1, "y": 2}])
        self.assertTrue(response["input_events_emitted"])
        self.assertIn("mouse event 'b1r'", response["error"])
        self.assertNotIn("cleanup transport lost", response["error"])

    def test_successful_modified_pointer_action_releases_keys_in_reverse_order(self):
        events = []
        with mock.patch.object(runtime, "keycode", side_effect=[1, 2]), \
                mock.patch.object(runtime.Atspi, "generate_keyboard_event", side_effect=lambda code, _text, synth: events.append((code, synth)) or True):
            runtime.with_held_mouse_modifiers(["CTRL", "SHIFT"], lambda: events.append("pointer"))
        self.assertEqual(events, [(1, runtime.Atspi.KeySynthType.PRESS),
                                  (2, runtime.Atspi.KeySynthType.PRESS), "pointer",
                                  (2, runtime.Atspi.KeySynthType.RELEASE),
                                  (1, runtime.Atspi.KeySynthType.RELEASE)])

    def test_double_click_success_keeps_order_and_counts_one_complete_action(self):
        events = []
        with mock.patch.object(runtime.Atspi, "generate_mouse_event", side_effect=lambda _x, _y, event: events.append(event) or True):
            response = self.gui_response([{"type": "double_click", "x": 1, "y": 2}])
        self.assertEqual(events, ["abs", "b1p", "b1r", "abs", "b1p", "b1r"])
        self.assertTrue(response["ok"])
        self.assertEqual(response["dispatched"], 1)
        self.assertTrue(response["input_events_emitted"])

    def test_pointer_exception_keeps_accepted_events_and_releases_drag_press(self):
        for action, expected in [
                ({"type": "drag", "path": [{"x": 1, "y": 2}, {"x": 3, "y": 4}]}, ["abs", "b1p", "b1r"]),
                ({"type": "scroll", "x": 1, "y": 2, "scroll_y": 100}, ["abs", "b5c"])]:
            with self.subTest(action=action):
                events = []

                def generate(_x, _y, event):
                    events.append(event)
                    if len(events) == 2:
                        raise RuntimeError("pointer transport lost")
                    return True

                with mock.patch.object(runtime.Atspi, "generate_mouse_event", side_effect=generate):
                    response = self.gui_response([action])
                self.assertEqual(events, expected)
                self.assertTrue(response["input_events_emitted"])
                self.assertTrue(response["partial_input_possible"])
                self.assertIn("pointer transport lost", response["error"])

    def test_mouse_false_return_is_an_emission_failure(self):
        with mock.patch.object(
                runtime.Atspi, "generate_mouse_event",
                return_value=False, create=True):
            with self.assertRaises(RuntimeError) as caught:
                runtime.send_mouse_click(1, 2, "left", 1)

        self.assertIn("input emission failed", str(caught.exception))

    def test_keyboard_false_return_is_an_emission_failure(self):
        with mock.patch.object(
                runtime.Atspi, "generate_keyboard_event",
                return_value=False, create=True):
            with self.assertRaises(RuntimeError) as caught:
                runtime.send_text("hello")

        self.assertIn("input emission failed", str(caught.exception))

    def test_mixed_text_uses_one_input_method_stream(self):
        # Mixing native X events with input-method commits can reorder text.
        with mock.patch.object(runtime, "InputMethodText") as composer, \
                mock.patch.object(runtime, "emit_keyboard_event") as keyboard:
            runtime.send_text("ab Grüße 你好!")

        self.assertEqual(composer.return_value.send.call_args.args[0],
                         "ab Grüße 你好!")
        keyboard.assert_not_called()

    def test_literal_unicode_key_uses_acknowledged_input(self):
        with mock.patch.object(runtime, "InputMethodText") as composer, \
                mock.patch.object(runtime, "emit_keyboard_event") as keyboard:
            runtime.send_key("中")
        self.assertEqual(composer.return_value.send.call_args.args[0], "中")
        keyboard.assert_not_called()

    def test_long_non_ascii_text_is_rejected_before_input(self):
        events = []
        with mock.patch.object(
                runtime.Atspi, "generate_keyboard_event",
                side_effect=lambda *args: events.append(args) or True,
                create=True):
            with self.assertRaises(runtime.OperationFailure) as caught:
                runtime.send_text("你" * 501)
        self.assertEqual(events, [])
        self.assertFalse(caught.exception.input_events_emitted)
        self.assertIn("no input was emitted", str(caught.exception))

    def test_gui_batch_rejects_long_non_ascii_text_before_input(self):
        with self.assertRaises(RuntimeError) as caught:
            runtime.validate_gui_action({"type": "type", "text": "好" * 501})
        self.assertIn("non-ASCII", str(caught.exception))
        runtime.validate_gui_action({"type": "type", "text": "好" * 500})

    def test_ascii_text_is_one_event(self):
        events = []
        with mock.patch.object(
                runtime.Atspi, "generate_keyboard_event",
                side_effect=lambda *args: events.append(args) or True,
                create=True):
            runtime.send_text("hello world")
        self.assertEqual(len(events), 1)

    def test_all_pressed_modifiers_are_released_after_one_release_fails(self):
        events = []

        def generate(keyval, _keystring, synth_type):
            events.append((keyval, synth_type))
            if synth_type == runtime.Atspi.KeySynthType.RELEASE and keyval == 2:
                return False
            return True

        with mock.patch.object(runtime, "parse_key", return_value=([1, 2], "S")), \
                mock.patch.object(runtime, "keycode", return_value=3), \
                mock.patch.object(
                    runtime.Atspi, "generate_keyboard_event",
                    side_effect=generate, create=True):
            with self.assertRaises(runtime.InputEmissionError):
                runtime.send_key("ctrl+shift+s")

        releases = [
            keyval for keyval, synth_type in events
            if synth_type == runtime.Atspi.KeySynthType.RELEASE
        ]
        self.assertEqual(releases, [2, 1])

    def test_mouse_release_is_retried_after_an_explicit_failure(self):
        results = iter([True, True, False, True])
        with mock.patch.object(
                runtime.Atspi, "generate_mouse_event",
                side_effect=lambda *_args: next(results), create=True) as generate:
            with self.assertRaises(runtime.InputEmissionError):
                runtime.send_mouse_click(1, 2, "left", 1)

        self.assertEqual(generate.call_count, 4)
