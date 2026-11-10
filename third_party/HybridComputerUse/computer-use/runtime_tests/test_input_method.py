"""Input method acknowledgements, focus changes and partial-emission evidence."""

import contextlib
from types import SimpleNamespace

from runtime_test_support import *  # noqa: F401,F403


class InputMethodAcknowledgementTests(unittest.TestCase):
    def setUp(self):
        self.clock = 0.0
        self.pending = []
        self.events = []
        self.committed = []
        self.current_context = "/org/freedesktop/IBus/InputContext_7"
        self.commit_delay = 0.2
        self.commit_value = None
        self.acknowledge_preedit = True
        self.focus_on_preedit = None
        self.ascii = ""
        self.composer = object.__new__(runtime.InputMethodText)
        self.composer.emitted = False
        self.composer.preedit = False
        self.composer.allowed_preedit = None
        self.composer.preedit_error = False
        self.composer.commits = []
        self.composer.context = self.current_context
        self.composer.engine_name = "xkb:us::eng"
        self.engine = ("IBusEngineDesc", {}, "xkb:us::eng")
        self.composer.deadline = 20.0
        self.composer.subscription = None
        self.composer.rule = None
        self.composer.property = lambda name: (
            self.current_context if name == "CurrentInputContext" else self.engine)
        context = SimpleNamespace(
            pending=lambda: bool(self.pending and self.pending[0][0] <= self.clock),
            iteration=lambda block: self.pending.pop(0)[1]())
        self.composer.GLib = SimpleNamespace(
            MainContext=SimpleNamespace(default=lambda: context))
        self.time_patch = mock.patch.object(runtime.time, "monotonic",
                                           side_effect=lambda: self.clock)
        self.sleep_patch = mock.patch.object(runtime.time, "sleep",
                                            side_effect=self.advance)
        self.time_patch.start()
        self.sleep_patch.start()
        self.addCleanup(self.time_patch.stop)
        self.addCleanup(self.sleep_patch.stop)

    def advance(self, seconds):
        self.clock += seconds

    def signal(self, member, text, visible=True):
        if member == "CommitText":
            self.committed.append(text)
        text_value = ("IBusText", {}, text, ())
        values = ((text_value,) if member == "CommitText"
                  else (text_value, len(text), visible))
        self.composer.receive(None, None, self.current_context, None, member,
                              SimpleNamespace(unpack=lambda: values), None)

    def preedit(self):
        if self.focus_on_preedit:
            self.current_context = self.focus_on_preedit
        self.signal("UpdatePreeditText", "u")
        self.ascii = ""

    def key(self, key):
        self.events.append((self.clock, "key", key))
        if key == "CTRL+SHIFT+u" and self.acknowledge_preedit:
            self.pending.append((self.clock + 0.05, self.preedit))
        elif key == "ENTER":
            char = self.commit_value or chr(int(self.ascii, 16))
            self.pending.append((self.clock + self.commit_delay,
                                 lambda: self.signal("CommitText", char)))

    def text(self, text):
        self.ascii += text
        self.events.append((self.clock, "text", text))
        value = "u" + self.ascii
        self.pending.append((self.clock + 0.01,
                             lambda: self.signal("UpdatePreeditText", value)))

    def test_waits_for_delayed_commit_before_next_character(self):
        self.composer.send("中ü", self.key, self.text)
        prefixes = [t for t, kind, value in self.events
                    if value == "CTRL+SHIFT+u"]
        self.assertEqual(len(prefixes), 2)
        self.assertGreaterEqual(prefixes[1], 0.25)
        self.assertEqual([value for _, kind, value in self.events if kind == "text"],
                         list("4e2dfc"))

    def test_ascii_line_feed_and_tab_reach_input_method_as_literal_text(self):
        payload = "left\nright\tend"
        with mock.patch.object(runtime, "InputMethodText", return_value=self.composer), \
                mock.patch.object(runtime, "send_key", side_effect=self.key), \
                mock.patch.object(runtime, "emit_keyboard_event",
                                  side_effect=lambda code, text, kind, detail: self.text(text)):
            runtime.send_text(payload)

        self.assertEqual("".join(self.committed), payload)
        self.assertEqual([value for _, kind, value in self.events if kind == "key"],
                         [key for _ in payload for key in ("CTRL+SHIFT+u", "ENTER")])

    def test_missing_preedit_does_not_send_hex_or_enter(self):
        self.acknowledge_preedit = False
        with self.assertRaises(runtime.OperationFailure) as caught:
            self.composer.send("中文", self.key, self.text)
        self.assertTrue(caught.exception.input_events_emitted)
        self.assertEqual([value for _, _, value in self.events], ["CTRL+SHIFT+u"])

    def test_wrong_commit_stops_before_next_character(self):
        self.commit_value = "ü"
        with self.assertRaises(runtime.OperationFailure) as caught:
            self.composer.send("中文", self.key, self.text)
        self.assertTrue(caught.exception.input_events_emitted)
        self.assertEqual([value for _, _, value in self.events],
                         ["CTRL+SHIFT+u", "4", "e", "2", "d", "ENTER"])

    def test_focus_change_after_preedit_stops_before_digits(self):
        self.focus_on_preedit = "/org/freedesktop/IBus/InputContext_8"
        with self.assertRaises(runtime.OperationFailure):
            self.composer.send("中", self.key, self.text)
        self.assertEqual(len(self.events), 1)

    def test_whole_text_deadline_is_not_reset_per_character(self):
        self.composer.deadline = 0.3
        with self.assertRaises(runtime.OperationFailure):
            self.composer.send("中文", self.key, self.text)
        self.assertLess(self.clock, 0.31)
        self.assertEqual([value for _, kind, value in self.events if kind == "text"],
                         list("4e2d"))

    def test_each_hex_digit_waits_for_application_preedit(self):
        self.composer.send("中", self.key, self.text)
        emitted = [(t, value) for t, kind, value in self.events if kind == "text"]
        self.assertEqual([value for _, value in emitted], list("4e2d"))
        for (earlier, _), (later, _) in zip(emitted, emitted[1:]):
            self.assertGreaterEqual(later - earlier, 0.01 - 1e-9)

    def test_unexpected_hex_preedit_stops_before_next_digit_or_enter(self):
        def wrong_digit(text):
            self.events.append((self.clock, "text", text))
            self.pending.append((self.clock + 0.01,
                                 lambda: self.signal("UpdatePreeditText", "u8")))
        with self.assertRaises(runtime.OperationFailure) as caught:
            self.composer.send("中", self.key, wrong_digit)
        self.assertTrue(caught.exception.input_events_emitted)
        self.assertEqual([value for _, _, value in self.events],
                         ["CTRL+SHIFT+u", "4"])

    def test_subscriptions_are_removed_after_failed_input(self):
        self.composer.connection = mock.Mock()
        self.composer.subscription = 17
        self.composer.rule = "context rule"
        self.composer.match = mock.Mock()
        self.acknowledge_preedit = False
        with self.assertRaises(runtime.OperationFailure):
            self.composer.send("中", self.key, self.text)
        self.composer.connection.signal_unsubscribe.assert_called_once_with(17)
        self.composer.match.assert_called_once_with("RemoveMatch", "context rule")

    def test_cleanup_error_does_not_mask_partial_input_failure(self):
        self.acknowledge_preedit = False
        self.composer.close = mock.Mock(side_effect=RuntimeError("cleanup failed"))
        with self.assertRaises(runtime.OperationFailure) as caught:
            self.composer.send("中", self.key, self.text)
        self.assertIn("acknowledgement timed out", str(caught.exception))
        self.assertTrue(caught.exception.input_events_emitted)

    def test_engine_change_stops_before_input(self):
        self.engine = ("IBusEngineDesc", {}, "pinyin")
        with self.assertRaises(runtime.OperationFailure) as caught:
            self.composer.send("中", self.key, self.text)
        self.assertFalse(caught.exception.input_events_emitted)
        self.assertEqual(self.events, [])


class TextRouteTests(unittest.TestCase):
    """send_text uses IBus Simple when it is active and key events otherwise."""

    KEYCODES = {"Return": 36, "Tab": 23}

    def setUp(self):
        self.events = []
        self.sleeps = []
        for patcher in (
                mock.patch.object(runtime, "keycode",
                                  side_effect=self.KEYCODES.__getitem__),
                mock.patch.object(runtime.time, "sleep",
                                  side_effect=self.sleeps.append)):
            patcher.start()
            self.addCleanup(patcher.stop)

    def generate(self, keyval, keystring, synth_type):
        self.events.append((keyval, keystring, synth_type))
        return True

    def without_input_method(self, generate=None):
        stack = contextlib.ExitStack()
        stack.enter_context(mock.patch.object(
            runtime, "ibus_bindings", side_effect=ValueError("no IBus typelib")))
        stack.enter_context(mock.patch.object(
            runtime.Atspi, "generate_keyboard_event",
            side_effect=generate or self.generate, create=True))
        return stack

    def test_input_method_composes_the_whole_text_when_active(self):
        with mock.patch.object(runtime, "InputMethodText") as composer, \
                mock.patch.object(runtime, "emit_keyboard_event") as keyboard:
            note = runtime.send_text("a\tb 你好\n")
        self.assertEqual(composer.return_value.send.call_args.args[0], "a\tb 你好\n")
        keyboard.assert_not_called()
        self.assertIn("input method", note)

    def test_plain_ascii_does_not_open_the_input_method(self):
        with mock.patch.object(runtime, "InputMethodText") as composer, \
                self.without_input_method():
            note = runtime.send_text("plain text")
        composer.assert_not_called()
        self.assertIsNone(note)
        self.assertEqual([text for _, text, _ in self.events], ["plain text"])

    def test_without_input_method_text_is_one_ordered_key_event_stream(self):
        STRING = runtime.Atspi.KeySynthType.STRING
        PRESSRELEASE = runtime.Atspi.KeySynthType.PRESSRELEASE
        with self.without_input_method():
            note = runtime.send_text("ab Grü\nx\t你!")
        # libatspi rebinds a spare keycode for every non-ASCII character, and
        # a burst reuses it before the application reads the previous
        # mapping ("Grüße 你好" -> "Gr好好e 好好"); each one is paced.
        self.assertEqual(self.events, [
            (0, "ab Gr", STRING), (0, "ü", STRING), (36, None, PRESSRELEASE),
            (0, "x", STRING), (23, None, PRESSRELEASE), (0, "你", STRING),
            (0, "!", STRING)])
        self.assertEqual(len(self.sleeps), 2)
        self.assertTrue(all(0.03 <= delay <= 0.05 for delay in self.sleeps))
        self.assertIn("key events", note)

    def test_inactive_input_method_context_uses_key_events(self):
        unavailable = runtime.OperationFailure(
            "the active input method is not IBus Simple", "unsupported_action",
            "precondition", input_events_emitted=False)
        with mock.patch.object(runtime, "InputMethodText", side_effect=unavailable), \
                self.without_input_method():
            runtime.send_text("中")
        self.assertEqual([text for _, text, _ in self.events], ["中"])

    def test_character_without_key_event_form_fails_before_input(self):
        for payload in ("first\0second\n", "中\ud800"):
            with self.subTest(payload=payload), self.without_input_method():
                with self.assertRaises(runtime.OperationFailure) as caught:
                    runtime.send_text(payload)
                self.assertEqual(caught.exception.failure_code, "unsupported_action")
                self.assertFalse(caught.exception.input_events_emitted)
        self.assertEqual(self.events, [])

    def test_input_method_failure_after_emission_does_not_retry_with_key_events(self):
        partial = runtime.OperationFailure(
            "input method acknowledgement timed out", "input_emission_failed",
            "emission", input_events_emitted=True)
        with mock.patch.object(runtime, "InputMethodText") as composer, \
                self.without_input_method():
            composer.return_value.send.side_effect = partial
            with self.assertRaises(runtime.OperationFailure) as caught:
                runtime.send_text("中文")
        self.assertIs(caught.exception, partial)
        self.assertEqual(self.events, [])

    def test_failed_control_key_reports_earlier_text_as_emitted(self):
        def generate(keyval, keystring, synth_type):
            self.events.append(keystring)
            return keystring is not None
        with self.without_input_method(generate):
            with self.assertRaises(runtime.InputEmissionError) as caught:
                runtime.send_text("first\nsecond")
        self.assertTrue(caught.exception.events_emitted)
        self.assertEqual(self.events, ["first", None])
