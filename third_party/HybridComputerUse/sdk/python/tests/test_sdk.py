"""SDK tests against a running daemon.

computer-use's Go test TestPythonSDKAgainstDaemon starts a daemon with a
fake desktop and runs this file with HCU_SOCKET set. Run it by hand against
any daemon the same way.
"""

import asyncio
import json
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

import hcu_sdk as hcu  # noqa: E402
from hcu_sdk import ptc  # noqa: E402
from test_ptc import PTCAdapterTests, PTCMessagesTests  # noqa: E402,F401
from test_async_cancellation import AsyncCancellationTests, EventLoopTests  # noqa: E402,F401
from test_code import CodeTests  # noqa: E402,F401


def recording_transport(calls):
    def transport(method, params):
        calls.append((method, params))
        return {"ok": True, "op": "test", "timing_ms": {}}
    return transport


# Wait step kinds: the decoder accepts exactly one per step. The schema has
# no enum for them; published_items checks each is a published step field.
WAIT_SUBJECTS = ("window", "query", "ref", "text", "settle")

# Published items that the SDK reaches only through its generic calls
# (act, gui, call), not through a dedicated helper.
SDK_GENERIC_ONLY = {"gui:double_click", "gui:move", "gui:drag",
                    "gui:scroll", "gui:type", "gui:keypress", "gui:wait"}


def published_items(schema):
    """AX modes, observe values, act step types, wait kinds and GUI action
    types, read from the daemon's published schemas."""
    items = set()
    tools = {tool["name"]: tool["input_schema"] for tool in schema["tools"]}
    ax = tools["AX"]["properties"]
    step = ax["actions"]["items"]["properties"]
    items.update("ax.mode:" + value for value in ax["mode"]["enum"])
    items.update("ax.observe:" + value for value in ax["observe"]["enum"])
    items.update("ax.act:" + value for value in step["type"]["enum"])
    for subject in WAIT_SUBJECTS:
        assert subject in step, "wait subject %s is not a published step field" % subject
        items.add("ax.wait:" + subject)

    def walk(node):
        if isinstance(node, dict):
            kind = node.get("type")
            if isinstance(kind, dict) and isinstance(kind.get("const"), str):
                items.add("gui:" + kind["const"])
            for child in node.values():
                walk(child)
        elif isinstance(node, list):
            for child in node:
                walk(child)
    walk(tools["GUI"])
    return items


def call_items(tool, args):
    """The published items one call uses."""
    if tool == "GUI":
        return {"gui:" + action["type"] for action in args["actions"]}
    items = {"ax.mode:" + args["mode"]}
    if "observe" in args:
        items.add("ax.observe:" + args["observe"])
    for action in args.get("actions", []):
        items.add("ax.act:" + action["type"])
        if action["type"] == "wait":
            items.update("ax.wait:" + key for key in WAIT_SUBJECTS if key in action)
    return items


# A zoom result as the core returns it: region [x0, y0, x1, y1] of the frame,
# shown at width x height, so frame x = x0 + u/scale_x, y = y0 + v/scale_y.
ZOOM_RECORD = {"ok": True, "op": "gui", "images": ["/tmp/zoom.png"],
               "evidence": {"zoom": {"frame": "a04f6a8e", "region": [600, 60, 680, 100],
                                     "width": 640, "height": 320, "scale_x": 8, "scale_y": 8}}}


class ClickZoomTests(unittest.TestCase):
    def test_clicks_the_frame_pixel_of_the_zoom_point(self):
        calls = []
        hcu.Desktop(recording_transport(calls)).click_zoom(ZOOM_RECORD, 212, 96, button="right")
        (method, params), = calls
        self.assertEqual(method, "call")
        self.assertEqual(params["tool"], "GUI")
        self.assertEqual(params["args"], {"actions": [{"type": "click", "x": 626.5, "y": 72.0, "button": "right"}]})
        self.assertIn("request_id", params)

    def test_rejects_a_record_without_a_zoom_mapping(self):
        calls = []
        with self.assertRaises(ValueError):
            hcu.Desktop(recording_transport(calls)).click_zoom({"ok": True, "screen": {}}, 1, 1)
        self.assertEqual(calls, [])

    def test_async_desktop_has_the_same_helper(self):
        calls = []

        async def transport(method, params):
            calls.append((method, params))
            return {"ok": True, "op": "gui", "timing_ms": {}}
        asyncio.run(hcu.AsyncDesktop(transport).click_zoom(ZOOM_RECORD, 0, 0))
        self.assertEqual(calls[0][1]["args"]["actions"], [{"type": "click", "x": 600.0, "y": 60.0}])


class SDKAgainstDaemon(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.desktop = hcu.Desktop(hcu.SocketTransport(path=os.environ["HCU_SOCKET"], session="sdk", start=False))
        cls.schema = cls.desktop.schema()

    @classmethod
    def tearDownClass(cls):
        cls.desktop.close()

    def helper_calls(self):
        calls = []
        builder = hcu.Desktop(recording_transport(calls))
        builder.apps()
        builder.observe("w1", root="focused", scope="content", observe="diff")
        builder.observe("w1", observe="tree")
        builder.observe("w1", observe="full")
        builder.observe("w1", observe="screenshot")
        builder.wait({"window": "Saved", "timeout_ms": 1})
        builder.wait({"ref": "r1", "state": "checked"}, window_ref="w1")
        builder.wait({"query": {"role": "dialog"}, "gone": True, "timeout_ms": 1})
        builder.wait({"text": "Saved", "timeout_ms": 1}, window_ref="w1")
        builder.find("w1", role="button", name_has="Sa", text="x", state="checked", limit=5)
        builder.find(cursor="f1", limit=5)
        builder.read(["r1", "r2"])
        builder.click("r1", wait={"ref": "r1", "state": "checked", "timeout_ms": 1})
        builder.type("hello", ref="r2")
        builder.type("hello")
        builder.key("ctrl+o", wait={"settle": True, "timeout_ms": 1})
        builder.key("ctrl+s")
        builder.set_value("r2", "42")
        builder.select("r2", text="ell")
        builder.select("r3", to_ref="r4")
        builder.scroll("r2", "down", pages=2)
        builder.activate("w1")
        builder.activate("Sheet")
        builder.close_window("w9")
        builder.screenshot()
        builder.zoom([0, 0, 80, 40])
        builder.click_zoom(ZOOM_RECORD, 40, 20)
        return calls

    def test_every_helper_builds_a_call_the_core_accepts(self):
        # Drift guard: each helper's arguments go through the core's own
        # decoder and published schema; none may come back invalid_argument.
        calls = self.helper_calls()
        builder = hcu.Desktop(recording_transport(calls))
        builder.act([{"type": "click", "ref": "r1"}, {"type": "wait", "ref": "r1", "timeout_ms": 1}], window_ref="w1")
        builder.gui([{"type": "click", "x": 1, "y": 2}], settle_ms=0)
        for _method, params in calls:
            raw = self.desktop.transport("call", dict(params, request_id=None))
            self.assertNotEqual(raw.get("code"), "invalid_argument",
                                "%s rejected: %s" % (json.dumps(params["args"]), raw.get("message")))

    def test_helpers_reach_every_published_item(self):
        # Drift guard: a mode, observe value, act step, wait kind or GUI action
        # added to the contract fails here until a helper builds it or
        # SDK_GENERIC_ONLY names it.
        published = published_items(self.schema)
        helped = set()
        for _method, params in self.helper_calls():
            if params["tool"] == "GUI" or params["args"]["mode"] != "read":
                helped |= call_items(params["tool"], params["args"])
        self.assertEqual(published - helped - SDK_GENERIC_ONLY, set(), "published items without a helper")
        self.assertEqual(helped & SDK_GENERIC_ONLY, set(), "items with a helper listed as generic only")
        self.assertEqual(SDK_GENERIC_ONLY - published, set(), "SDK_GENERIC_ONLY names unpublished items")

    def test_observation_calls_carry_no_request_id(self):
        # screenshot and zoom only look: no request_id, retry-safe.
        for args in ({"actions": [{"type": "screenshot"}]},
                     {"actions": [{"type": "zoom", "region": [0, 0, 80, 40]}]}):
            self.assertFalse(hcu._may_emit_input("GUI", args))
            self.assertNotIn("request_id", hcu._call_params("GUI", args, None, None))
        click = {"actions": [{"type": "click", "x": 1, "y": 2}]}
        self.assertTrue(hcu._may_emit_input("GUI", click))
        self.assertIn("request_id", hcu._call_params("GUI", click, None, None))

    def test_errors_are_classes_named_by_code(self):
        for code in self.schema.errors:
            cls = getattr(hcu, hcu._class_name(code))
            self.assertIs(cls, hcu.error_class(code))
            self.assertEqual(cls.code, code)
            self.assertTrue(issubclass(cls, hcu.HcuError))
        with self.assertRaises(hcu.StaleRef) as caught:
            self.desktop.click("r9999")
        self.assertFalse(caught.exception.input_emitted)
        self.assertIn("hint", str(caught.exception))
        with self.assertRaises(hcu.InvalidArgument) as caught:
            self.desktop.call("AX", {"mode": "act", "actions": [{"action": "click", "ref": "r1"}]})
        self.assertEqual(caught.exception.path, "actions[0].action")

    def test_records_flow_refs_and_paging(self):
        apps = self.desktop.apps()
        self.assertEqual(apps.windows[0].ref, "w1")
        tree = self.desktop.observe("w1")
        self.assertTrue(tree.snapshot_id)
        button = next(e for e in tree.elements if e.role == "button" and e.ref)
        matches = list(self.desktop.find_all("w1", page_size=1, role="button"))
        self.assertIn(button.ref, [m.ref for m in matches])
        read = self.desktop.read([button.ref])
        self.assertTrue(read.read[0].ok)
        again = self.desktop.observe("w1", since=tree.snapshot_id)
        self.assertTrue(again.diff.unchanged)
        acted = self.desktop.click(button.ref)
        self.assertTrue(acted.ok and acted.request_id and acted.input_emitted)
        self.assertIn("provider", acted.timing_ms)

    def test_discovery_accepts_published_names(self):
        window = self.desktop.apps().windows[0].ref
        tree = self.desktop.observe(window_ref=window)
        button = next(e for e in tree.elements if e.role == "button" and e.ref)
        found = self.desktop.find(window_ref=window, role="button", name_equals=button.name)
        self.assertIn(button.ref, [m.ref for m in found.matches])
        shorthand = self.desktop.find(window, role="button", name=button.name)
        self.assertEqual([m.ref for m in found.matches], [m.ref for m in shorthand.matches])
        found = self.desktop.find(window_ref=window, name_contains=button.name)
        self.assertIn(button.ref, [m.ref for m in found.matches])
        all_matches = self.desktop.find_all(window_ref=window, role="button", page_size=1)
        self.assertIn(button.ref, [m.ref for m in all_matches])

    def test_unknown_find_fields_reach_core_validation(self):
        with self.assertRaises(hcu.InvalidArgument) as caught:
            self.desktop.find("w1", unsupported_filter="value")
        self.assertIn("unsupported_filter", str(caught.exception))
        self.assertFalse(caught.exception.input_emitted)
        with self.assertRaises(hcu.InvalidArgument):
            self.desktop.find("w1", role="button", limit=0)

    def test_discovery_conflicts_fail_without_transport(self):
        calls = []
        d = hcu.Desktop(recording_transport(calls))
        conflicts = [
            lambda: d.observe("w1", window_ref="w2"),
            lambda: d.observe(app="example", window_ref="w1"),
            lambda: d.find("w1", app="example"),
            lambda: d.find("w1", name="Save", name_equals="Other"),
            lambda: d.find("w1", name_equals="Save", name="Other"),
            lambda: d.find(cursor="f1", role="button"),
            lambda: d.find(cursor="f1", window_ref="w1"),
            lambda: d.find(cursor="f1", scope="content"),
        ]
        for conflict in conflicts:
            with self.subTest(call=conflict), self.assertRaises(TypeError):
                conflict()
        self.assertEqual(calls, [])

    def test_async_discovery_uses_same_names(self):
        async def exercise():
            d = hcu.AsyncDesktop(self.desktop.transport)
            tree = await d.observe(window_ref="w1")
            button = next(e for e in tree.elements if e.role == "button" and e.ref)
            found = await d.find(window_ref="w1", name_equals=button.name)
            self.assertIn(button.ref, [m.ref for m in found.matches])
            all_matches = await d.find_all(window_ref="w1", role="button", page_size=1)
            self.assertIn(button.ref, [m.ref for m in all_matches])
        asyncio.run(exercise())

    def test_request_id_is_reused_on_retry(self):
        tree = self.desktop.observe("w1")
        button = next(e for e in tree.elements if e.role == "button" and e.ref)
        first = self.desktop.click(button.ref, request_id="sdk-retry-1")
        second = self.desktop.click(button.ref, request_id="sdk-retry-1")
        self.assertFalse(first.replayed)
        self.assertTrue(second.replayed)

    def test_reconnect_after_a_dropped_connection(self):
        desktop = hcu.Desktop(hcu.SocketTransport(path=os.environ["HCU_SOCKET"], session="sdk-drop", start=False))
        desktop.apps()
        desktop.transport._sock.close()  # the next send fails
        self.assertTrue(desktop.apps().ok)
        desktop.close()

    def test_async_desktop_over_the_socket(self):
        async def main():
            desktop = hcu.AsyncDesktop(hcu.SocketTransport(path=os.environ["HCU_SOCKET"], session="sdk-async", start=False))
            apps = await desktop.apps()
            matches = await desktop.find_all("w1", page_size=1, role="button")
            return apps, matches
        apps, matches = asyncio.run(main())
        self.assertTrue(apps.ok and matches)

    def test_ptc_definitions_and_forwarder(self):
        tools = ptc.tool_definitions(self.schema)
        by_name = {tool["name"]: tool for tool in tools}
        self.assertEqual(set(by_name), {"hcu_ax", "hcu_gui"})
        self.assertEqual(by_name["hcu_ax"]["allowed_callers"], [ptc.CODE_EXECUTION_CALLER])
        self.assertNotIn("allowed_callers", by_name["hcu_gui"])
        self.assertNotIn("$ref", json.dumps(tools))
        self.assertNotIn("$defs", json.dumps(tools))
        for tool in tools:
            self.assertLess(len(json.dumps(tool["input_schema"])), 16000)

        # A synthetic code-execution tool_use block, forwarded to the daemon.
        block = {"type": "tool_use", "id": "toolu_01", "name": "hcu_ax",
                 "input": {"mode": "find", "window_ref": "w1", "query": {"role": "button"}, "limit": 1},
                 "caller": {"type": ptc.CODE_EXECUTION_CALLER, "tool_id": "srvtoolu_01"}}
        self.desktop.apps()
        result = ptc.forward(block, self.desktop)
        self.assertEqual(result["tool_use_id"], "toolu_01")
        record = json.loads(result["content"])
        self.assertTrue(record["ok"], record)
        self.assertEqual(record["page"]["returned"], 1)

    def test_ptc_container_script_end_to_end(self):
        # The container side: async tool functions that the harness answers
        # by forwarding each tool_use to the daemon.
        counter = {"n": 0}

        def make(name):
            async def tool(arguments):
                counter["n"] += 1
                block = {"type": "tool_use", "id": "toolu_%d" % counter["n"], "name": name, "input": arguments,
                         "caller": {"type": ptc.CODE_EXECUTION_CALLER}}
                return ptc.forward(block, self.desktop)["content"]
            return tool

        hcu_ax, hcu_gui = make("hcu_ax"), make("hcu_gui")

        async def container():
            d = hcu.AsyncDesktop(hcu.ptc_transport(hcu_ax, hcu_gui))
            await d.apps()
            buttons = await d.find_all("w1", page_size=1, role="button")
            acted = await d.click(buttons[0].ref)
            return buttons, acted

        buttons, acted = asyncio.run(container())
        self.assertTrue(buttons and acted.ok and acted.request_id)
        self.assertGreaterEqual(counter["n"], 3)

    def test_ptc_event_replay_against_daemon(self):
        forwarder = ptc.Forwarder(self.desktop)
        self.desktop.apps()
        tree = self.desktop.observe("w1")
        button = next(e for e in tree.elements if e.role == "button" and e.ref)
        block = {"type": "tool_use", "id": "toolu_live_repeat", "name": "hcu_ax",
                 "input": {"mode": "act", "actions": [{"type": "click", "ref": button.ref}]}}
        first = forwarder(block)
        repeated = forwarder(block)
        record = json.loads(first["content"])
        self.assertEqual(first, repeated)
        self.assertTrue(record["ok"] and record["request_id"].startswith("ptc-"))
        # The shared daemon can recover the same input by its generated id too.
        raw = self.desktop.call("AX", block["input"], request_id=record["request_id"])
        self.assertTrue(raw.replayed)


if __name__ == "__main__":
    unittest.main(verbosity=1)
