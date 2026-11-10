"""连接回归默认不依赖 daemon；--daemon 仅供 Go 的隔离 fake backend 使用。

mock 只验证 SDK 控制流，不构成真实 Windows Winsock 或启动验证。
"""

import asyncio
import json
import os
import posixpath
import socket
import subprocess
import sys
import threading
import types
import unittest
from unittest import mock

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import hcu_sdk as hcu

DAEMON_TEST = "--daemon" in sys.argv
if DAEMON_TEST:
    sys.argv.remove("--daemon")


class ConnectionTests(unittest.TestCase):
    def test_unix_default_path_regressions(self):
        cases = [
            ({}, "/tmp/hcu-501/desktop-default.sock"),
            ({"TMPDIR": "/private/tmp", "DISPLAY": ":0"},
             "/private/tmp/hcu-501/desktop-0.sock"),
            ({"XDG_RUNTIME_DIR": "/run/user/501", "DISPLAY": ":0",
              "WAYLAND_DISPLAY": "wayland-1"}, "/run/user/501/hcu/desktop-wayland-1.sock"),
            ({"DISPLAY": ":1.0"}, "/tmp/hcu-501/desktop-1_0.sock"),
            ({"HCU_SOCKET": "/tmp/专用 socket"}, "/tmp/专用 socket"),
        ]
        for env, expected in cases:
            with self.subTest(env=env), mock.patch.object(hcu, "os", types.SimpleNamespace(
                    name="posix", environ=env, path=posixpath, getuid=lambda: 501)):
                self.assertEqual(hcu.default_socket(), expected)

    def test_windows_branch_delegates_without_posix_uid(self):
        path = r"C:\临时 目录\desktop.sock"
        native = types.SimpleNamespace(default_socket=mock.Mock(return_value=path),
                                       connect=mock.Mock(return_value=mock.Mock()))
        # 只模拟模块边界；真实地址转换由 Windows 上的 Go/Python 集成测试覆盖。
        with mock.patch.dict(sys.modules, {"hcu_sdk._windows_socket": native}), \
                mock.patch.object(hcu, "os", types.SimpleNamespace(name="nt", environ={})):
            transport = hcu.SocketTransport(timeout=3, start=False)
            self.assertEqual(transport.path, path)
            transport._open()
            native.connect.assert_called_once_with(path, 3)
            transport.close()

    def unavailable(self, env, *, start=True, run_error=None, run_result=None):
        transport = hcu.SocketTransport(path="专用 socket", start=start)
        with mock.patch.dict(os.environ, env, clear=True), \
                mock.patch.object(transport, "_open", side_effect=FileNotFoundError("无此端点")), \
                mock.patch.object(hcu.shutil, "which", return_value=None), \
                mock.patch.object(hcu.subprocess, "run", side_effect=run_error,
                                  return_value=run_result) as run:
            with self.assertRaises(hcu.DaemonUnavailable) as caught:
                transport("schema", {})
        self.assertIn("专用 socket", str(caught.exception))
        return caught.exception, run

    def test_missing_binary_explains_native_installation(self):
        error, run = self.unavailable({})
        self.assertIn("HCU_BIN", str(error))
        self.assertIn("native CLI and provider", str(error))
        self.assertIn("Python packages does not build", str(error))
        run.assert_not_called()

    def test_start_false_never_launches(self):
        error, run = self.unavailable({"HCU_BIN": "should-not-run"}, start=False)
        self.assertIn("auto-start is disabled", str(error))
        run.assert_not_called()

    def test_invalid_hcu_bin_preserves_native_path(self):
        binary = os.path.join("中文 空格", "computer-use.exe")
        error, run = self.unavailable({"HCU_BIN": binary}, run_error=FileNotFoundError("找不到程序"))
        self.assertIn("HCU_BIN/PATH executable", str(error))
        self.assertIn(repr(binary), str(error))
        self.assertEqual(run.call_args.args[0], [binary, "daemon", "start", "--socket", "专用 socket"])
        self.assertEqual(run.call_count, 1)

    def test_start_failure_retains_utf8_stderr(self):
        result = subprocess.CompletedProcess([], 9, stderr="原生提供程序不可用：路径含空格".encode())
        error, run = self.unavailable({"HCU_BIN": "computer-use.exe"}, run_result=result)
        self.assertIn("exit 9", str(error))
        self.assertIn("原生提供程序不可用：路径含空格", str(error))
        self.assertEqual(run.call_count, 1)

    def test_start_timeout_is_bounded_and_not_retried(self):
        error, run = self.unavailable({"HCU_BIN": "computer-use.exe"},
                                     run_error=subprocess.TimeoutExpired("computer-use", 20))
        self.assertIn("20s", str(error))
        self.assertEqual(run.call_args.kwargs["timeout"], 20)
        self.assertEqual(run.call_count, 1)

    def test_start_success_uses_argument_list_and_reconnects_once(self):
        path = os.path.join("中文 空格", "desktop.sock")
        binary = os.path.join("原生 程序", "computer-use.exe")
        transport = hcu.SocketTransport(path=path, session="会话")
        hello = {"protocol": hcu.PROTOCOL, "session": "会话", "instance_id": "test-instance"}
        with mock.patch.dict(os.environ, {"HCU_BIN": binary}), \
                mock.patch.object(transport, "_open", side_effect=[FileNotFoundError(), None]) as opened, \
                mock.patch.object(transport, "_exchange", return_value=hello) as exchange, \
                mock.patch.object(hcu.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)) as run:
            transport._connect()
        self.assertEqual(opened.call_count, 2)
        self.assertEqual(run.call_args.args[0], [binary, "daemon", "start", "--socket", path])
        self.assertEqual(run.call_count, 1)
        self.assertEqual(exchange.call_args.args[0], "hello")
        self.assertEqual(exchange.call_args.args[1]["session"], "会话")
        self.assertEqual(transport.hello, hello)


class ErrorRecordTests(unittest.TestCase):
    def test_reconnect_failure_keeps_unknown_effect_and_request_id(self):
        for asynchronous in (False, True):
            for failure in ("disabled", "missing_binary", "start_failed"):
                with self.subTest(asynchronous=asynchronous, failure=failure):
                    transport = hcu.SocketTransport(path="missing", start=failure != "disabled")
                    sock = mock.Mock()
                    sock.recv.side_effect = ConnectionResetError("reply lost")
                    transport._sock = sock
                    transport.hello = {"instance_id": "original"}
                    desktop = hcu.AsyncDesktop(transport) if asynchronous else hcu.Desktop(transport)
                    env = {"HCU_BIN": "missing.exe"} if failure == "start_failed" else {}
                    with mock.patch.dict(os.environ, env, clear=True), \
                            mock.patch.object(hcu.shutil, "which", return_value=None), \
                            mock.patch.object(hcu.subprocess, "run", side_effect=FileNotFoundError("binary")), \
                            mock.patch.object(transport, "_open", side_effect=FileNotFoundError("missing")):
                        with self.assertRaises(hcu.TransportEffectUnknown) as caught:
                            value = desktop.click("r1", request_id="未知输入")
                            if asynchronous:
                                asyncio.run(value)
                    self.assertEqual(caught.exception.request_id, "未知输入")
                    self.assertIsNone(caught.exception.input_emitted)
                    self.assertIsInstance(caught.exception.__cause__, hcu.DaemonUnavailable)
                    self.assertEqual(sock.sendall.call_count, 1)

    def test_partial_failure_keeps_full_utf8_record_and_never_replays(self):
        # socketpair 使用本机 socket，实现真实 JSON 编解码；不冒充 Windows AF_UNIX。
        for asynchronous in (False, True):
            for emitted in (False, True, None):
                with self.subTest(asynchronous=asynchronous, emitted=emitted):
                    client, server = socket.socketpair()
                    client.settimeout(5)
                    server.settimeout(5)
                    calls, errors = [], []
                    record = {
                        "ok": False, "code": "effect_unknown", "op": "act",
                        "message": "首步已提交，后续步骤失败，不能重复输入",
                        "hint": "先观察中文窗口，再决定下一步", "path": "actions[1]",
                        "input_emitted": emitted, "completed": 1, "requested": 2,
                        "effect_unknown": True, "request_id": "部分失败",
                        "evidence": {"步骤": [{"文本": "你好，世界"}], "未知字段": "保留"},
                    }

                    def respond():
                        try:
                            with server, server.makefile("rb") as stream:
                                for line in stream:
                                    request = json.loads(line)
                                    calls.append(request)
                                    result = ({"protocol": hcu.PROTOCOL, "session": "default",
                                               "instance_id": "fake-instance"}
                                              if request["method"] == "hello" else record)
                                    response = {"jsonrpc": "2.0", "id": request["id"], "result": result}
                                    server.sendall((json.dumps(response, ensure_ascii=False) + "\n").encode())
                        except BaseException as error:
                            errors.append(error)

                    thread = threading.Thread(target=respond, daemon=True)
                    thread.start()
                    transport = hcu.SocketTransport(path="test-only", start=False)
                    desktop = hcu.AsyncDesktop(transport) if asynchronous else hcu.Desktop(transport)
                    try:
                        with mock.patch.object(transport, "_open", side_effect=lambda: setattr(transport, "_sock", client)):
                            with self.assertRaises(hcu.EffectUnknown) as caught:
                                result = desktop.act([{"type": "type", "text": "中文输入"},
                                                      {"type": "key", "key": "enter"}], request_id="部分失败")
                                if asynchronous:
                                    asyncio.run(result)
                        error = caught.exception
                        self.assertEqual(error.record, record)
                        self.assertIs(error.input_emitted, emitted)
                        self.assertEqual((error.completed, error.requested), (1, 2))
                        self.assertEqual(error.path, "actions[1]")
                        self.assertIn(record["message"], str(error))
                        self.assertIn(record["hint"], str(error))
                    finally:
                        transport.close()
                        client.close()
                        thread.join(5)
                    self.assertFalse(thread.is_alive())
                    self.assertEqual(errors, [])
                    self.assertEqual([call["method"] for call in calls], ["hello", "call"])
                    self.assertEqual(calls[-1]["params"]["args"]["actions"][0]["text"], "中文输入")


@unittest.skipUnless(DAEMON_TEST, "仅由 Go 测试启动隔离 fake daemon")
class AgainstDaemonTests(unittest.TestCase):
    def test_partial_failure_replay_does_not_repeat_first_action(self):
        transport = hcu.SocketTransport(path=os.environ["HCU_TEST_ENDPOINT"],
                                        session="部分失败", start=False)
        desktop = hcu.Desktop(transport)
        try:
            window = desktop.apps().windows[0].ref
            button = next(element for element in desktop.observe(window).elements
                          if element.role == "button" and element.ref)
            records = []
            for _ in range(2):
                with self.assertRaises(hcu.HcuError) as caught:
                    desktop.act([{"type": "click", "ref": button.ref},
                                 {"type": "wait", "window": "不存在的中文窗口", "timeout_ms": 1}],
                                request_id="中文 部分失败")
                records.append(caught.exception.record)
                self.assertEqual((caught.exception.completed, caught.exception.requested), (1, 2))
                self.assertIs(caught.exception.input_emitted, True)
            self.assertEqual(records[0]["request_id"], "中文 部分失败")
            self.assertTrue(records[1]["replayed"])
            # Go 测试同时检查 provider.inputCount() == 1。
        finally:
            desktop.close()

    def test_sync_and_async_default_explicit_and_environment_paths(self):
        endpoint = os.environ["HCU_TEST_ENDPOINT"]
        for asynchronous in (False, True):
            for mode in ("default", "explicit", "environment"):
                with self.subTest(asynchronous=asynchronous, mode=mode), \
                        mock.patch.dict(os.environ, {"HCU_SOCKET": endpoint if mode == "environment" else "",
                                                     "HCU_SESSION": "连接 会话"}):
                    if mode == "default":
                        self.assertEqual(os.path.normcase(hcu.default_socket()), os.path.normcase(endpoint))
                    if asynchronous:
                        transport = hcu.SocketTransport(path=endpoint, session="连接 会话", start=False) if mode == "explicit" else None
                        desktop = hcu.AsyncDesktop(transport)
                    else:
                        desktop = hcu.connect(socket=endpoint, session="连接 会话") if mode == "explicit" else hcu.connect()
                    # Go fixture 已监听，禁止错误端点触发真实 CLI 自启动。
                    desktop.transport.start = False
                    try:
                        schema = asyncio.run(desktop.schema()) if asynchronous else desktop.schema()
                        self.assertEqual([tool.name for tool in schema.tools], ["AX", "GUI"])
                        self.assertTrue(schema.errors)
                        self.assertEqual(desktop.transport.hello["protocol"], hcu.PROTOCOL)
                        self.assertEqual(desktop.transport.hello["session"], "连接 会话")
                        self.assertTrue(desktop.transport.hello["instance_id"])
                        self.assertEqual(desktop.transport.hello["pid"], int(os.environ["HCU_TEST_DAEMON_PID"]))
                    finally:
                        desktop.transport.close()


if __name__ == "__main__":
    unittest.main()
