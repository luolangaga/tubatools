"""Programmatic tool calling (PTC) adapter for the Anthropic API.

Two pieces, both thin:

1. tool_definitions(schema) turns the daemon's published schemas into the
   client tools hcu_ax and hcu_gui. Nothing is generated ahead of time: the
   schema is read from the running daemon (Desktop.schema()), so the PTC
   definitions cannot drift from MCP. Local $ref definitions are inlined,
   because client tool input schemas must be self-contained.

2. Forwarder is one API conversation's harness side. It retains event
   outcomes, uses Desktop.call for safe retries, and returns records JSON.
   Direct calls can also return images through an explicit image reader.

allowed_callers is not a security boundary (Anthropic documents this):
code that runs in the container can call any tool its caller list allows,
and all safety checks (argument validation, ref resolution, window binding,
preflight before input) stay in the computer-use core. hcu_ax can be
called from code; hcu_gui is direct by default. Configure image_reader to
deliver its screenshots; guest paths are never read implicitly.
"""

import copy
import base64
import json
import os
import stat
import threading
import uuid

from . import HcuError, TransportClosed, TransportEffectUnknown, _may_emit_input

CODE_EXECUTION_CALLER = "code_execution_20260120"

# Call options accepted next to the tool arguments in a tool_use input.
_OPTIONS = {
    "request_id": {"type": "string", "maxLength": 128,
                   "description": "Optional input idempotency key within the daemon replay cache; the harness supplies one otherwise."},
    "since": {"type": "string", "description": "observe: snapshot_id of an earlier observe; returns a diff."},
}

_READ_MODE = "read"


def _inline_refs(schema):
    """Replace local {"$ref": "#/$defs/x"} with the definition."""
    definitions = schema.get("$defs", {})

    def walk(node):
        if isinstance(node, dict):
            ref = node.get("$ref")
            if isinstance(ref, str) and ref.startswith("#/$defs/"):
                return walk(copy.deepcopy(definitions[ref[len("#/$defs/"):]]))
            return {key: walk(value) for key, value in node.items() if key != "$defs"}
        if isinstance(node, list):
            return [walk(item) for item in node]
        return node

    return walk(schema)


def _input_schema(tool, records):
    schema = _inline_refs(tool["input_schema"])
    properties = schema.setdefault("properties", {})
    if tool["name"] == "AX":
        # read is a records-only mode; it has no MCP form.
        mode = dict(properties["mode"])
        mode["enum"] = list(mode["enum"]) + [_READ_MODE]
        mode["description"] = mode.get("description", "") + " read returns the live value and states of refs."
        properties["mode"] = mode
        properties["refs"] = {
            "type": "array", "minItems": 1, "items": {"type": "string", "pattern": "^r[1-9][0-9]*$"},
            "description": "read: element refs rN.",
        }
    for name, option in _OPTIONS.items():
        properties[name] = option
    if "oneOf" in schema:
        # GUI: one object schema per branch; the options are valid on both.
        for branch in schema["oneOf"]:
            branch.setdefault("properties", {}).update(_OPTIONS)
    return schema


_DESCRIPTIONS = {
    "AX": "Desktop accessibility (HCU AX). Same arguments as the AX MCP tool, plus mode read, "
          "request_id and since. Returns a JSON string: {ok, code, message, hint, op, window, "
          "elements|matches|read|diff, page{returned,total,next_cursor}, input_emitted, "
          "completed, requested, timing_ms}. Never truncated; page with next_cursor. observe "
          "full/screenshot adds screen{width,height,image}: the current GUI frame.",
    "GUI": "Full-desktop screenshot and pixel input (HCU GUI). Same arguments as the GUI MCP tool, "
           "plus request_id. Returns records JSON with screen{width,height,image}, the GUI frame "
           "whose pixels x/y use; image is a guest file path. zoom returns images[0] and "
           "evidence.zoom{region,scale_x,scale_y}: frame x = region[0] + u/scale_x, "
           "y = region[1] + v/scale_y. Direct calls also return image blocks when the harness has "
           "an image reader. Code callers receive JSON only and need a direct observation to show "
           "an image to the model.",
}


def tool_definitions(schema, gui_from_code=False):
    """Client tool definitions for the Messages API, from Desktop.schema()."""
    tools = []
    for tool in schema["tools"]:
        name = "hcu_" + tool["name"].lower()
        definition = {
            "name": name,
            "description": _DESCRIPTIONS[tool["name"]],
            "input_schema": _input_schema(tool, schema.get("records", {})),
        }
        if tool["name"] == "AX" or gui_from_code:
            definition["allowed_callers"] = [CODE_EXECUTION_CALLER]
        tools.append(definition)
    return tools


def local_image_reader(root, max_bytes=5 * 1024 * 1024):
    """Read PNG/JPEG files beneath an operator-selected local POSIX directory.

    The directory must be the daemon's output directory on this host. Symlinks
    and nonregular files are refused. For a remote guest, supply a reader that
    fetches only that guest's output files through your existing connection.
    """
    configured_root = os.path.abspath(os.fspath(root))
    root = os.path.realpath(configured_root)
    if max_bytes < 1:
        raise ValueError("max_bytes must be positive")

    def read(path):
        relative = os.path.relpath(os.path.abspath(path), configured_root)
        if os.pardir in relative.split(os.sep):
            relative = os.path.relpath(os.path.abspath(path), root)
        parts = relative.split(os.sep)
        if relative == os.curdir or os.pardir in parts:
            raise ValueError("image path is outside the configured output directory")
        directory = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try:
            for part in parts[:-1]:
                child = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=directory)
                os.close(directory)
                directory = child
            descriptor = os.open(parts[-1], os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=directory)
            with os.fdopen(descriptor, "rb") as stream:
                info = os.fstat(stream.fileno())
                if not stat.S_ISREG(info.st_mode) or info.st_size > max_bytes:
                    raise ValueError("image must be a regular file within the byte limit")
                data = stream.read(max_bytes + 1)
        finally:
            os.close(directory)
        if len(data) > max_bytes:
            raise ValueError("image exceeds the byte limit")
        if data.startswith(b"\x89PNG\r\n\x1a\n"):
            return "image/png", data
        if data.startswith(b"\xff\xd8\xff"):
            return "image/jpeg", data
        raise ValueError("expected a PNG or JPEG screenshot")

    return read


class Forwarder:
    """Keep one instance for an entire API conversation, including resends.

    Outcomes are retained for this object's lifetime. A repeated event never
    sends another desktop call, even after daemon replacement or cache expiry.
    Capacity exhaustion refuses new events rather than evicting old ones.
    This ledger is not durable: do not resume pending input with a new object
    after the harness process restarts.

    image_reader(path) returns (media_type, bytes) from a trusted output source.
    No reader means no implicit filesystem or network access.
    """

    def __init__(self, desktop, image_reader=None, max_events=1000):
        if max_events < 1:
            raise ValueError("max_events must be positive")
        self.desktop = desktop
        self.image_reader = image_reader
        self.max_events = max_events
        self._events = {}
        self._namespace = uuid.uuid4().hex
        self._lock = threading.Lock()

    def __call__(self, tool_use):
        event = copy.deepcopy(tool_use)
        event_id = event.get("id")
        if not isinstance(event_id, str) or not event_id or len(event_id) > 80:
            raise ValueError("tool_use.id must be a nonempty string of at most 80 characters")
        name = event.get("name")
        if name not in ("hcu_ax", "hcu_gui"):
            raise ValueError("not an hcu tool: %r" % name)
        if not isinstance(event.get("input"), dict):
            raise ValueError("tool_use.input must be an object")
        caller = event.get("caller")
        if caller is None:
            caller = {"type": "direct"}
            event["caller"] = caller
        if not isinstance(caller, dict) or caller.get("type") not in (
                "direct", CODE_EXECUTION_CALLER, "code_execution_20260521", "code_execution_20250825"):
            raise ValueError("tool_use.caller must identify direct or code_execution")
        with self._lock:
            prior = self._events.get(event_id)
            if prior is not None:
                if prior[0] != event:
                    raise ValueError("tool_use.id reused with different arguments or caller")
                record = prior[1]
                if record is None:
                    raise RuntimeError("previous forwarding was interrupted; input may have run")
            else:
                if len(self._events) >= self.max_events:
                    raise RuntimeError("PTC event capacity reached; stop this conversation without replaying input")
                # Retain even an interrupted attempt. It must not become a new input.
                self._events[event_id] = (event, None)
                record = self._call(event)
                self._events[event_id] = (event, record)
            return self._result(event, record)

    def _call(self, event):
        arguments = dict(event["input"])
        request_id = arguments.pop("request_id", None)
        since = arguments.pop("since", None)
        tool = "AX" if event["name"] == "hcu_ax" else "GUI"
        mutation = _may_emit_input(tool, arguments)
        if request_id is None and mutation:
            request_id = "ptc-%s-%s" % (self._namespace, event["id"])
        try:
            return dict(self.desktop.call(tool, arguments, request_id=request_id, since=since))
        except HcuError as error:
            return dict(error.record)
        except (TransportEffectUnknown, TransportClosed, OSError, RuntimeError) as error:
            # A transport failure cannot establish that attempted input did not run.
            return {"ok": False, "code": "effect_unknown" if mutation else "runtime_error",
                    "message": str(error), "request_id": request_id,
                    "input_emitted": None if mutation else False,
                    "hint": "Observe current state before issuing any new input." if mutation else "Retry with a new tool_use event."}

    def _result(self, event, record):
        record = copy.deepcopy(record)
        result = {"type": "tool_result", "tool_use_id": event["id"], "is_error": False}
        caller = event.get("caller") or {}
        direct = caller.get("type", "direct") == "direct"
        paths = list(record.get("images") or [])
        screen_path = (record.get("screen") or {}).get("image")
        if screen_path:
            paths.insert(0, screen_path)
        paths = list(dict.fromkeys(paths))
        images = []
        if direct and paths:
            try:
                if self.image_reader is None:
                    raise ValueError("configure image_reader for this desktop's output files")
                for path in paths:
                    media_type, data = self.image_reader(path)
                    if (media_type not in ("image/png", "image/jpeg") or not isinstance(data, bytes)
                            or not data or len(data) > 5 * 1024 * 1024):
                        raise ValueError("image_reader must return PNG/JPEG bytes within 5 MiB")
                    images.append({"type": "image", "source": {"type": "base64", "media_type": media_type,
                                   "data": base64.b64encode(data).decode("ascii")}})
            except Exception as error:
                # The action record remains intact: image delivery can fail after input.
                record["delivery_error"] = {"code": "image_unavailable", "message": str(error)}
                result["is_error"] = True
                images = []
        text = json.dumps(record, ensure_ascii=False)
        result["content"] = [{"type": "text", "text": text}] + images if images else text
        return result


_forwarder_lock = threading.Lock()


def forward(tool_use, desktop):
    """Forward with a conversation ledger retained on this Desktop instance.

    Use Forwarder(desktop, image_reader=...) when returning direct images.
    Keep the same Desktop for resends; pending input cannot be resumed safely
    by constructing a new Desktop after a harness restart.
    """
    with _forwarder_lock:
        adapter = getattr(desktop, "_ptc_forwarder", None)
        if adapter is None:
            adapter = Forwarder(desktop)
            desktop._ptc_forwarder = adapter
    return adapter(tool_use)
