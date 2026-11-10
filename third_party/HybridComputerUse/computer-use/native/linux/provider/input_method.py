"""Unicode keyboard input with acknowledgements from the active input method."""

import re
import time

from .foundation import OperationFailure


def ibus_bindings():
    import gi
    gi.require_version("IBus", "1.0")
    from gi.repository import Gio, GLib, IBus
    return Gio, GLib, IBus


class InputMethodText:
    """Compose text without changing the X keymap or the clipboard.

    AT-SPI STRING reuses a temporary keycode for unmapped characters. Its
    return acknowledges the X server, not the application's keymap lookup.
    IBus Simple accepts Unicode hex input using ordinary ASCII keycodes.
    A preedit acknowledgement establishes that it consumed the prefix.
    Each hex digit receives a preedit acknowledgement before the next key.
    A matching CommitText completes each character before the next begins.
    """

    def __init__(self):
        self.emitted = False
        self.subscription = None
        self.rule = None
        self.preedit = None
        self.allowed_preedit = None
        self.preedit_error = False
        self.commits = []
        self.deadline = time.monotonic() + 20.0
        try:
            self.Gio, self.GLib, IBus = ibus_bindings()
            IBus.init()
            self.bus = IBus.Bus.new()
            if not self.bus.is_connected():
                raise RuntimeError("IBus is not connected")
            self.connection = self.bus.get_connection()
            self.context = self.property("CurrentInputContext")
            engine = self.property("GlobalEngine")
            if not (isinstance(self.context, str) and re.fullmatch(
                    r"/org/freedesktop/IBus/InputContext_\d+", self.context)):
                raise RuntimeError("there is no active IBus input context")
            if not (isinstance(engine, (tuple, list)) and len(engine) > 2
                    and str(engine[2]).startswith("xkb:")):
                raise RuntimeError("the active input method is not IBus Simple")
            self.engine_name = engine[2]
            self.subscription = self.connection.signal_subscribe(
                None, "org.freedesktop.IBus.InputContext", None,
                self.context, None, self.Gio.DBusSignalFlags.NO_MATCH_RULE,
                self.receive, None)
            rule = ("type='signal',interface='org.freedesktop.IBus.InputContext',"
                    "path='{}',eavesdrop='true'".format(self.context))
            self.match("AddMatch", rule)
            self.rule = rule
        except Exception as exc:
            try:
                self.close()
            except Exception:
                pass
            raise self.failure(str(exc)) from exc

    def call(self, destination, path, interface, method, parameters):
        return self.connection.call_sync(
            destination, path, interface, method, parameters, None,
            self.Gio.DBusCallFlags.NONE, 1000, None).unpack()

    def property(self, name):
        return self.call(
            "org.freedesktop.IBus", "/org/freedesktop/IBus",
            "org.freedesktop.DBus.Properties", "Get",
            self.GLib.Variant("(ss)", ("org.freedesktop.IBus", name)))[0]

    def match(self, method, rule):
        self.call("org.freedesktop.DBus", "/org/freedesktop/DBus",
                  "org.freedesktop.DBus", method,
                  self.GLib.Variant("(s)", (rule,)))

    def receive(self, connection, sender, path, interface, member, params, data):
        values = params.unpack()
        if member == "CommitText":
            # Two commits are already an error; retain bounded evidence only.
            if len(self.commits) < 2:
                self.commits.append(values[0][2])
        elif member in ("UpdatePreeditText", "UpdatePreeditTextWithMode"):
            self.preedit = values[0][2] if values[2] else None
            if (self.preedit is not None and self.allowed_preedit is not None
                    and self.preedit not in self.allowed_preedit):
                self.preedit_error = True

    def wait(self, ready):
        deadline = min(self.deadline, time.monotonic() + 1.0)
        context = self.GLib.MainContext.default()
        while not ready():
            if time.monotonic() >= deadline:
                raise RuntimeError("input method acknowledgement timed out")
            for _ in range(32):
                if not context.pending():
                    break
                context.iteration(False)
            if not ready():
                time.sleep(0.001)

    def require_context(self):
        if self.property("CurrentInputContext") != self.context:
            raise RuntimeError("input method focus changed during typing")
        engine = self.property("GlobalEngine")
        if not (isinstance(engine, (tuple, list)) and len(engine) > 2
                and engine[2] == self.engine_name):
            raise RuntimeError("input method engine changed during typing")
        if time.monotonic() >= self.deadline:
            raise RuntimeError("input method text deadline exceeded")

    def send(self, text, send_key, send_ascii):
        failure = None
        try:
            for char in text:
                self.require_context()
                self.preedit = None
                self.allowed_preedit = ("u",)
                self.preedit_error = False
                self.commits.clear()
                self.emitted = True
                send_key("CTRL+SHIFT+u")
                self.wait(lambda: self.preedit or self.commits or self.preedit_error)
                if self.commits:
                    raise RuntimeError("input method committed unexpected text")
                if self.preedit_error or self.preedit != "u":
                    raise RuntimeError("input method produced unexpected preedit text")
                expected = "u"
                for digit in format(ord(char), "x"):
                    self.require_context()
                    previous, expected = expected, expected + digit
                    self.allowed_preedit = (previous, expected)
                    send_ascii(digit)
                    self.wait(lambda: self.commits or self.preedit_error
                              or self.preedit not in (None, previous))
                    if self.commits:
                        raise RuntimeError("input method committed unexpected text")
                    if self.preedit_error or self.preedit != expected:
                        raise RuntimeError("input method produced unexpected preedit text")
                self.require_context()
                self.allowed_preedit = None
                send_key("ENTER")
                self.wait(lambda: bool(self.commits))
                if self.commits != [char]:
                    raise RuntimeError("input method committed unexpected text")
            self.require_context()
        except Exception as exc:
            failure = self.failure(str(exc))
        try:
            self.close()
        except Exception as exc:
            if failure is None:
                failure = self.failure("input method subscription cleanup failed: " + str(exc))
        if failure is not None:
            raise failure

    def failure(self, detail):
        return OperationFailure(
            "This keyboard text requires an active IBus Simple input context: "
            + detail + ". Enable the desktop input method before typing. "
            + ("Input may be partial; inspect the field before retrying."
               if self.emitted else "No input was emitted."),
            failure_code="input_emission_failed" if self.emitted else "unsupported_action",
            failure_phase="emission" if self.emitted else "precondition",
            input_events_emitted=self.emitted)

    def close(self):
        failure = None
        if self.subscription is not None:
            try:
                self.connection.signal_unsubscribe(self.subscription)
            except Exception as exc:
                failure = exc
            finally:
                self.subscription = None
        if self.rule is not None:
            try:
                self.match("RemoveMatch", self.rule)
            except Exception as exc:
                if failure is None:
                    failure = exc
            finally:
                self.rule = None
        if failure is not None:
            raise failure
