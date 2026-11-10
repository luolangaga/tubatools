"""hcu_sdk: drive the desktop from code through the computer-use daemon.

This module is a thin client. The computer-use core (Go) is the only
implementation of the contract: it validates arguments, resolves refs,
checks windows before input and renders results. This module sends calls
and returns the core's records. Helpers reject ambiguous Python arguments;
the core validates the desktop request.

    import hcu_sdk as hcu
    d = hcu.connect()                       # starts the daemon if needed
    w = d.apps().windows[0].ref             # "w1"
    for el in d.find_all(w, role="button"):
        print(el.ref, el.name)
    d.click("r12", wait={"window": "Saved"})  # click, then a wait step

Every method returns a Record: a dict with attribute access, exactly the
JSON the core returned. Errors raise HcuError; its subclass is named after
the public code (stale_ref -> hcu.StaleRef), and the list of codes comes
from the daemon's schema, not from this file.

Async: AsyncDesktop has the same methods as coroutines, over any async
transport, for example the tool functions of a programmatic tool calling
container: hcu.AsyncDesktop(hcu.ptc_transport(hcu_ax, hcu_gui)).

The package is named hcu_sdk, not hcu, because the repository's harness
package is already called hcu; import it as hcu in scripts.

Standard library only; Python 3.8 or later.
"""

import asyncio
import json
import os
import shutil
import socket as _socket
import subprocess
import threading
import time
import uuid

__all__ = [
    "AsyncDesktop", "CallCancelled", "Desktop", "HcuError", "Record", "SocketTransport",
    "TransportClosed", "TransportEffectUnknown", "connect", "error_class", "ptc_transport",
    "zoom_point",
]

PROTOCOL = 1


class Record(dict):
    """A JSON object from the core with attribute access: r.ok, r.matches."""

    def __getattr__(self, name):
        if name.startswith("__"):
            raise AttributeError(name)
        return self.get(name)

    @staticmethod
    def wrap(value):
        if isinstance(value, dict):
            return Record({key: Record.wrap(item) for key, item in value.items()})
        if isinstance(value, list):
            return [Record.wrap(item) for item in value]
        return value


class HcuError(Exception):
    """A call the core answered with ok:false.

    code, message, hint and path are the core's; input_emitted, completed
    and requested say how far an act got. str() is the message with the
    hint, which is a corrected call.
    """

    code = None

    def __init__(self, record):
        self.record = record
        self.code = record.get("code")
        self.message = record.get("message") or ""
        self.hint = record.get("hint") or ""
        self.path = record.get("path") or ""
        self.input_emitted = record.get("input_emitted", False)
        self.completed = record.get("completed")
        self.requested = record.get("requested")
        text = "%s: %s" % (self.code, self.message)
        if self.path:
            text += " (at %s)" % self.path
        if self.hint:
            text += "; hint: %s" % self.hint
        super().__init__(text)


_error_classes = {}


def _class_name(code):
    return "".join(part.capitalize() for part in code.split("_"))


def error_class(code):
    """The HcuError subclass for a public error code, created on first use."""
    cls = _error_classes.get(code)
    if cls is None:
        cls = type(_class_name(code), (HcuError,), {"code": code, "__module__": __name__})
        _error_classes[code] = cls
    return cls


def __getattr__(name):
    # `except hcu.StaleRef` works before any connection: the class is made
    # from its name, and raising uses the same factory, so the two agree.
    # A test checks that every code in the daemon's schema maps to a name.
    if name[:1].isupper() and name.isalnum():
        code = "".join("_" + c.lower() if c.isupper() else c for c in name).lstrip("_")
        return error_class(code)
    raise AttributeError(name)


def _raise_for(record):
    if not record.get("ok", False):
        raise error_class(record.get("code") or "runtime_error")(record)
    return record


# ---------------------------------------------------------------------------
# Transports: callables (method, params) -> result dict.
# ---------------------------------------------------------------------------


def default_socket():
    """The daemon socket for this desktop, the same path computer-use uses."""
    if os.name == "nt":
        from ._windows_socket import default_socket as windows_default_socket
        return windows_default_socket()
    if os.environ.get("HCU_SOCKET"):
        return os.environ["HCU_SOCKET"]
    runtime = os.environ.get("XDG_RUNTIME_DIR")
    base = os.path.join(runtime, "hcu") if runtime else os.path.join(
        os.environ.get("TMPDIR", "/tmp"), "hcu-%d" % os.getuid())
    display = os.environ.get("WAYLAND_DISPLAY") or os.environ.get("DISPLAY") or ""
    name = "".join(c if c.isalnum() or c == "-" else "_" for c in display.lstrip(":")) or "default"
    return os.path.join(base, "desktop-" + name + ".sock")


class TransportClosed(Exception):
    """The connection dropped; the call may or may not have run.

    Custom transports may set retry_safe only when they guarantee that
    retrying these same params cannot repeat input, including after restart.
    """

    def __init__(self, message="connection closed", retry_safe=False):
        super().__init__(message)
        self.retry_safe = retry_safe


class TransportEffectUnknown(RuntimeError):
    """No reliable result exists; inspect current state before new input."""

    code = "effect_unknown"
    input_emitted = None

    def __init__(self, message, request_id=None):
        super().__init__(message)
        self.request_id = request_id


class DaemonUnavailable(ConnectionError):
    """Connection or daemon startup failed before the request was sent."""


class SocketTransport:
    """Line JSON-RPC over the daemon's Unix socket.

    The daemon is started with `computer-use daemon start` when no daemon
    answers; set HCU_BIN to the binary when it is not on PATH.
    """

    def __init__(self, path=None, session=None, timeout=120.0, start=True):
        self.path = path or default_socket()
        self.session = session or os.environ.get("HCU_SESSION") or "default"
        self.timeout = timeout
        self.start = start
        self.hello = None
        self._sock = None
        self._buffer = b""
        self._next_id = 0
        # AsyncDesktop runs synchronous calls in worker threads. A connection
        # owns one request/reply exchange, including its initial handshake.
        self._lock = threading.RLock()

    def _connect(self):
        try:
            self._open()
        except OSError as error:
            if not self.start:
                raise DaemonUnavailable(
                    "cannot connect to daemon at %r; auto-start is disabled; "
                    "run computer-use serve --socket PATH: %s" % (self.path, error)) from error
            binary = os.environ.get("HCU_BIN") or shutil.which("computer-use")
            if not binary:
                raise DaemonUnavailable(
                    "cannot connect to daemon at %r; computer-use was not found on PATH; "
                    "build/install the native CLI and provider, then set HCU_BIN to the "
                    "computer-use executable (installing Python packages does not build it)"
                    % self.path) from error
            options = {"creationflags": subprocess.CREATE_NO_WINDOW} if os.name == "nt" else {}
            try:
                result = subprocess.run(
                    [binary, "daemon", "start", "--socket", self.path],
                    stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
                    timeout=20, **options)
                if result.returncode:
                    detail = result.stderr.decode("utf-8", "replace").strip()
                    raise DaemonUnavailable(
                        "daemon start failed for %r (exit %s): %s" %
                        (self.path, result.returncode, detail))
                self._open()
            except subprocess.TimeoutExpired as error:
                raise DaemonUnavailable(
                    "daemon start timed out after 20s for %r; inspect the daemon log "
                    "before starting again" % self.path) from error
            except OSError as error:
                raise DaemonUnavailable(
                    "cannot start or connect to daemon at %r using HCU_BIN/PATH executable %r: %s"
                    % (self.path, binary, error)) from error
        params = {"session": self.session}
        display = os.environ.get("WAYLAND_DISPLAY") or os.environ.get("DISPLAY")
        if display:
            params["display"] = display
        try:
            hello = self._exchange("hello", params)
            if type(hello.get("protocol")) is not int or hello["protocol"] != PROTOCOL:
                raise RuntimeError("daemon: unsupported protocol in hello")
            session = hello.get("session")
            if not isinstance(session, str) or not session or (
                    self.session.strip() and session != self.session.strip()):
                raise RuntimeError("daemon: unexpected session in hello")
            if "instance_id" in hello and (
                    not isinstance(hello["instance_id"], str) or not hello["instance_id"]):
                raise RuntimeError("daemon: invalid instance_id in hello")
            self.hello = hello
        except BaseException:
            self.close()
            raise

    def _open(self):
        if os.name == "nt":
            from ._windows_socket import connect as windows_connect
            self._sock = windows_connect(self.path, self.timeout)
            self._buffer = b""
            return
        sock = _socket.socket(_socket.AF_UNIX, _socket.SOCK_STREAM)
        try:
            sock.settimeout(self.timeout)
            sock.connect(self.path)
        except BaseException:
            sock.close()
            raise
        self._sock, self._buffer = sock, b""

    def close(self):
        with self._lock:
            sock, self._sock = self._sock, None
            self._buffer = b""
            self.hello = None
            if sock is not None:
                sock.close()

    def _exchange(self, method, params):
        self._next_id += 1
        message = json.dumps({"jsonrpc": "2.0", "id": self._next_id,
                              "method": method, "params": params}) + "\n"
        try:
            self._sock.sendall(message.encode())
            while b"\n" not in self._buffer:
                chunk = self._sock.recv(1 << 16)
                if not chunk:
                    raise TransportClosed("daemon closed the connection")
                self._buffer += chunk
        except (OSError, TransportClosed) as error:
            self.close()
            raise TransportClosed(str(error)) from error
        line, self._buffer = self._buffer.split(b"\n", 1)
        try:
            response = json.loads(line)
            if (not isinstance(response, dict) or response.get("jsonrpc") != "2.0"
                    or type(response.get("id")) is not int
                    or response["id"] != self._next_id
                    or ("result" in response) == ("error" in response)):
                raise ValueError("unexpected response envelope or id")
            if "error" in response:
                error = response["error"]
                if (not isinstance(error, dict) or type(error.get("code")) is not int
                        or not isinstance(error.get("message"), str)):
                    raise ValueError("invalid error response")
            elif not isinstance(response["result"], dict):
                raise ValueError("result must be an object")
        except (ValueError, UnicodeError) as error:
            self.close()
            # The reply cannot establish what happened. Do not classify a
            # protocol failure as a dropped connection and silently retry it.
            raise RuntimeError("daemon: invalid response: %s" % error) from error
        if "error" in response:
            if response["error"]["code"] in (-32011, -32012):
                raise TransportEffectUnknown(response["error"]["message"], params.get("request_id"))
            raise RuntimeError("daemon: %s" % response["error"]["message"])
        return response["result"]

    def __call__(self, method, params):
        with self._lock:
            if self._sock is None:
                try:
                    self._connect()
                except Exception as error:
                    if params.get("replay_only") and _may_emit_input(
                            params.get("tool"), params.get("args", {})):
                        raise TransportEffectUnknown(
                            "cannot reconnect to recover the previous input result; "
                            "input may have run: %s" % error,
                            params.get("request_id")) from error
                    if isinstance(error, TransportClosed):
                        # The request has not been sent; handshake loss is safe to retry.
                        error.retry_safe = True
                    raise
            mutation = method == "call" and _may_emit_input(params.get("tool"), params.get("args", {}))
            if mutation:
                instance = self.hello.get("instance_id")
                bound = params.get("daemon_instance")
                if bound and bound != instance:
                    raise TransportEffectUnknown(
                        "daemon restarted; the previous input may have run; automatic replay stopped",
                        params.get("request_id"))
                if instance:
                    # This dict belongs to one Desktop.call. Its binding survives
                    # close/reconnect and is checked under the same lock as send.
                    params["daemon_instance"] = instance
            try:
                return self._exchange(method, params)
            except TransportClosed as error:
                request_id = params.get("request_id")
                error.retry_safe = not mutation or bool(
                    params.get("daemon_instance") and isinstance(request_id, str) and request_id.strip())
                if mutation and error.retry_safe:
                    # An evicted result cannot turn a retry into fresh input.
                    params["replay_only"] = True
                raise


def ptc_transport(ax, gui):
    """An async transport over the tool functions of a programmatic tool
    calling container, e.g. ptc_transport(hcu_ax, hcu_gui). Each function
    takes the tool arguments plus the call options and returns the records
    JSON string that the harness forwarder produced."""

    async def transport(method, params):
        if method == "schema":
            raise RuntimeError("schema is not available through tool functions")
        function = ax if params["tool"] == "AX" else gui
        arguments = dict(params["args"])
        for option in ("request_id", "since"):
            if params.get(option):
                arguments[option] = params[option]
        return json.loads(await function(arguments))

    return transport


# ---------------------------------------------------------------------------
# Argument builders: the only convenience layer. Each returns (tool, args)
# exactly as the contract spells them; the core checks them.
# ---------------------------------------------------------------------------


def _target(args, target):
    if target is None:
        return args
    if isinstance(target, str) and target[:1] == "w" and target[1:].isdigit():
        args["window_ref"] = target
    else:
        args["app"] = str(target)
    return args


def _wait_step(step):
    """A wait step from its fields: {"window": "Save"} -> {"type": "wait", ...}."""
    return dict(step, type="wait")


def _act(action, window_ref=None, wait=None, observe=None):
    args = {"mode": "act", "actions": [action]}
    if wait is not None:
        args["actions"].append(_wait_step(wait))
    if window_ref:
        args["window_ref"] = window_ref
    if observe is not None:
        args["observe"] = observe
    return args


def _with_ref(action, ref):
    if ref is not None:
        action["ref"] = ref
    return action


def _keys(chord):
    if isinstance(chord, (list, tuple)):
        return list(chord)
    return [part.strip().upper() for part in chord.split("+")]


_FIND_FIELDS = {"name": "name_equals", "name_has": "name_contains",
                "text": "text_equals", "text_has": "text_contains"}


def _discovery_args(mode, target, window_ref, app):
    if sum(value is not None for value in (target, window_ref, app)) > 1:
        raise TypeError("use exactly one of target, window_ref, or app")
    args = _target({"mode": mode}, target)
    for key, value in (("window_ref", window_ref), ("app", app)):
        if value is not None:
            args[key] = value
    return args


class _Builders:
    """Methods shared by Desktop and AsyncDesktop. Each one builds the
    arguments of one call and hands them to self.call, which is a plain
    function on Desktop and a coroutine on AsyncDesktop."""

    def apps(self):
        return self.call("AX", {"mode": "apps"})

    def observe(self, target=None, root=None, scope=None, observe=None, since=None,
                *, window_ref=None, app=None):
        args = _discovery_args("observe", target, window_ref, app)
        for key, value in (("root", root), ("scope", scope), ("observe", observe)):
            if value is not None:
                args[key] = value
        return self.call("AX", args, since=since)

    def wait(self, step, window_ref=None, observe="none"):
        """Wait without input: an act with one wait step, for example
        d.wait({"window": "Save"}) or d.wait({"ref": "r3", "state": "checked"}).
        A step that does not hold raises hcu.WaitTimeout. {"settle": True}
        needs an input step before it: pass it as wait= of an input helper,
        e.g. d.click("r4", wait={"settle": True})."""
        args = {"mode": "act", "actions": [_wait_step(step)], "observe": observe}
        if window_ref:
            args["window_ref"] = window_ref
        return self.call("AX", args)

    def find(self, target=None, limit=None, cursor=None, scope=None,
             *, window_ref=None, app=None, **query):
        args = _discovery_args("find", target, window_ref, app)
        if cursor is not None:
            if any(value is not None for value in (target, window_ref, app, scope)) or query:
                raise TypeError("cursor cannot be combined with a target, scope, or query")
            args["cursor"] = cursor
        else:
            fields = {}
            for key, value in query.items():
                canonical = _FIND_FIELDS.get(key, key)
                if canonical in fields:
                    raise TypeError("multiple values for find query field %s" % canonical)
                fields[canonical] = value
            args["query"] = fields
            if scope is not None:
                args["scope"] = scope
        if limit is not None:
            args["limit"] = limit
        return self.call("AX", args)

    def read(self, refs):
        return self.call("AX", {"mode": "read", "refs": list(refs)})

    def act(self, actions, window_ref=None, observe=None, request_id=None):
        """Ordered steps: actions and {"type": "wait", ...} steps."""
        args = {"mode": "act", "actions": list(actions)}
        if window_ref:
            args["window_ref"] = window_ref
        if observe is not None:
            args["observe"] = observe
        return self.call("AX", args, request_id=request_id)

    # Single-action helpers take window_ref, wait (one wait step that runs
    # after the action), observe and request_id.

    def click(self, ref, request_id=None, **options):
        return self.call("AX", _act({"type": "click", "ref": ref}, **options), request_id=request_id)

    def type(self, text, ref=None, request_id=None, **options):
        action = _with_ref({"type": "type", "text": text}, ref)
        return self.call("AX", _act(action, **options), request_id=request_id)

    def key(self, chord, ref=None, request_id=None, **options):
        action = _with_ref({"type": "keypress", "keys": _keys(chord)}, ref)
        return self.call("AX", _act(action, **options), request_id=request_id)

    def set_value(self, ref, value, request_id=None, **options):
        action = {"type": "set_value", "ref": ref, "value": value}
        return self.call("AX", _act(action, **options), request_id=request_id)

    def select(self, ref, text=None, to_ref=None, request_id=None, **options):
        """Select ref, the substring text of ref, or the range ref..to_ref."""
        action = {"type": "select", "ref": ref}
        if text is not None:
            action["text"] = text
        if to_ref is not None:
            action["to_ref"] = to_ref
        return self.call("AX", _act(action, **options), request_id=request_id)

    def scroll(self, ref, direction, pages=None, request_id=None, **options):
        action = {"type": "scroll", "ref": ref, "direction": direction}
        if pages is not None:
            action["pages"] = pages
        return self.call("AX", _act(action, **options), request_id=request_id)

    def activate(self, window, request_id=None, **options):
        action = _window_action("activate", window)
        return self.call("AX", _act(action, **options), request_id=request_id)

    def close_window(self, window, request_id=None, **options):
        action = _window_action("close", window)
        return self.call("AX", _act(action, **options), request_id=request_id)

    def screenshot(self):
        return self.call("GUI", {"actions": [{"type": "screenshot"}]})

    def zoom(self, region):
        return self.call("GUI", {"actions": [{"type": "zoom", "region": list(region)}]})

    def gui(self, actions, settle_ms=None, request_id=None):
        args = {"actions": list(actions)}
        if settle_ms is not None:
            args["settle_ms"] = settle_ms
        return self.call("GUI", args, request_id=request_id)

    def click_zoom(self, record, u, v, request_id=None, **click):
        x, y = zoom_point(record, u, v)
        return self.gui([dict(click, type="click", x=x, y=y)], request_id=request_id)


def _window_action(kind, window):
    if isinstance(window, str) and window[:1] == "w" and window[1:].isdigit():
        return {"type": kind, "ref": window}
    return {"type": kind, "title": window}


def zoom_point(record, u, v):
    """Convert a zoom-image pixel (u, v) to the GUI frame pixel (x, y)."""
    zoom = (record.get("evidence") or {}).get("zoom")
    if not zoom:
        raise ValueError("record is not a GUI zoom result")
    return zoom["region"][0] + u / zoom["scale_x"], zoom["region"][1] + v / zoom["scale_y"]


# GUI actions that only look (the observation branch of the GUI schema):
# they send no input, so they take no request_id and are safe to retry.
_GUI_LOOK = ("screenshot", "zoom")


def _wants_request_id(tool, args):
    if tool == "GUI":
        return any(action.get("type") not in _GUI_LOOK for action in args.get("actions", []))
    return args.get("mode") == "act"


def _may_emit_input(tool, args):
    if tool == "GUI":
        return any(action.get("type") not in _GUI_LOOK + ("wait",)
                   for action in args.get("actions", []))
    return args.get("mode") == "act" and any(
        action.get("type") != "wait" for action in args.get("actions", []))


def _check_retry(error, tool, args, params):
    if _may_emit_input(tool, args) and not error.retry_safe:
        raise TransportEffectUnknown(
            "connection lost; input may have run; transport cannot guarantee safe replay",
            params.get("request_id")) from error


def _call_params(tool, args, request_id, since):
    params = {"tool": tool, "args": args, "format": "records"}
    if request_id is None and _wants_request_id(tool, args):
        request_id = uuid.uuid4().hex
    if request_id:
        params["request_id"] = request_id
    if since:
        params["since"] = since
    return params


class Desktop(_Builders):
    """A synchronous session on the daemon."""

    def __init__(self, transport=None, retries=2):
        self.transport = transport or SocketTransport()
        self.retries = retries

    def call(self, tool, args, request_id=None, since=None):
        """Run one public call and return its Record; raise HcuError on ok:false."""
        params = _call_params(tool, args, request_id, since)
        for attempt in range(self.retries + 1):
            try:
                return _raise_for(Record.wrap(self.transport("call", params)))
            except TransportClosed as error:
                _check_retry(error, tool, args, params)
                if attempt == self.retries:
                    raise
                time.sleep(0.2 * (attempt + 1))

    def schema(self):
        return Record.wrap(self.transport("schema", {}))

    def find_all(self, target=None, page_size=50, **query):
        """Every match of a find, following next_cursor page by page."""
        page = self.find(target, limit=page_size, **query)
        while True:
            for element in page.matches or []:
                yield element
            cursor = (page.page or {}).get("next_cursor")
            if not cursor:
                return
            page = self.find(cursor=cursor, limit=page_size)

    def close(self):
        if hasattr(self.transport, "close"):
            self.transport.close()

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()


class CallCancelled(asyncio.CancelledError):
    """A cancelled SDK call; input_emitted is None when its effect is unknown."""

    code = "cancelled"

    def __init__(self, request_id, input_emitted, record=None, transport_error=None):
        super().__init__("call cancelled; input effect %s" % (
            "unknown" if input_emitted is None else "reported: %s" % input_emitted))
        self.request_id = request_id
        self.input_emitted = input_emitted
        self.record = Record.wrap(record) if record is not None else None
        self.transport_error = transport_error


class AsyncDesktop(_Builders):
    """The same methods as coroutines, over an async transport (or a sync
    one, which then runs in a worker thread)."""

    def __init__(self, transport=None, retries=2):
        self.transport = transport or SocketTransport()
        self.retries = retries
        self._loop = None
        self._send_lock = None

    def _gate(self):
        loop = asyncio.get_running_loop()
        if self._loop is not loop:
            if self._loop is not None and not self._loop.is_closed():
                raise RuntimeError("AsyncDesktop cannot be shared across live event loops")
            self._loop = loop
            self._send_lock = asyncio.Lock()
        return self._send_lock

    async def _send(self, method, params):
        gate = self._gate()
        try:
            await gate.acquire()
        except asyncio.CancelledError as error:
            # No worker or async transport has been submitted for this call.
            raise CallCancelled(params.get("request_id"), False) from error
        try:
            cancel_requested = threading.Event()
            not_sent = object()

            def send_sync():
                if isinstance(self.transport, SocketTransport):
                    # Other Desktop instances may share this socket. Check
                    # cancellation after its queue, before starting the call.
                    with self.transport._lock:
                        if cancel_requested.is_set():
                            return not_sent
                        return self.transport(method, params)
                if cancel_requested.is_set():
                    return not_sent
                return self.transport(method, params)

            if (asyncio.iscoroutinefunction(self.transport)
                    or asyncio.iscoroutinefunction(getattr(self.transport, "__call__", None))):
                pending = asyncio.create_task(self.transport(method, params))
            else:
                # Keep the executor Future directly. A cell runner may cancel
                # every Task; an intermediate Task would lose the worker when
                # cancelled even though that thread could still emit input.
                pending = asyncio.get_running_loop().run_in_executor(
                    None, send_sync)
            try:
                result = await asyncio.shield(pending)
                while asyncio.iscoroutine(result) or asyncio.isfuture(result):
                    pending = asyncio.ensure_future(result)
                    result = await asyncio.shield(pending)
                return result
            except asyncio.CancelledError as cancelled:
                cancel_requested.set()
                # Thread cancellation cannot stop input. Keep the gate until
                # transport completion, even if the caller cancels repeatedly.
                # Async transports follow the same rule: they must settle their
                # own side effects before returning or raising.
                record, transport_error, result = None, None, None
                while True:
                    while not pending.done():
                        try:
                            await asyncio.shield(pending)
                        except asyncio.CancelledError:
                            pass
                        except BaseException:
                            break
                    try:
                        result = pending.result()
                        if asyncio.isfuture(result):
                            pending = result
                            continue
                        if isinstance(result, dict):
                            record = result
                        elif asyncio.iscoroutine(result):
                            result.close()
                    except BaseException as error:
                        transport_error = error
                        if isinstance(getattr(error, "record", None), dict):
                            record = error.record
                    break
                emitted = None
                if (result is not_sent or method != "call"
                        or not _may_emit_input(params["tool"], params["args"])):
                    emitted = False
                elif record is not None and type(record.get("input_emitted")) is bool:
                    emitted = record["input_emitted"]
                raise CallCancelled(params.get("request_id"), emitted,
                                    record, transport_error) from cancelled
        finally:
            gate.release()

    async def call(self, tool, args, request_id=None, since=None):
        params = _call_params(tool, args, request_id, since)
        previous_attempt_unknown = False
        try:
            for attempt in range(self.retries + 1):
                try:
                    raw = await self._send("call", params)
                    return _raise_for(Record.wrap(raw))
                except TransportClosed as error:
                    previous_attempt_unknown = _may_emit_input(tool, args)
                    _check_retry(error, tool, args, params)
                    if attempt == self.retries:
                        raise
                    await asyncio.sleep(0.2 * (attempt + 1))
        except CallCancelled as error:
            if previous_attempt_unknown and error.record is None and error.input_emitted is False:
                raise CallCancelled(params.get("request_id"), None,
                                    transport_error=error.transport_error) from error
            raise
        except asyncio.CancelledError as error:
            # Cancellation during retry backoff does not begin another attempt.
            emitted = None if _may_emit_input(tool, args) else False
            raise CallCancelled(params.get("request_id"), emitted) from error

    async def schema(self):
        return Record.wrap(await self._send("schema", {}))

    async def find_all(self, target=None, page_size=50, **query):
        page = await self.find(target, limit=page_size, **query)
        matches = list(page.matches or [])
        while (page.page or {}).get("next_cursor"):
            page = await self.find(cursor=page.page["next_cursor"], limit=page_size)
            matches.extend(page.matches or [])
        return matches


def connect(socket=None, session=None):
    """A Desktop on this desktop's daemon, starting it if needed."""
    return Desktop(SocketTransport(path=socket, session=session))
