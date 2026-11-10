#!/usr/bin/env python3
"""真实 CLI 的 SDK 只读冒烟测试，不调用 AX/GUI 或连接现有桌面 daemon。"""

import asyncio
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "sdk" / "python"))
import hcu_sdk as hcu


def check(desktop, endpoint, asynchronous=False):
    try:
        schema = asyncio.run(desktop.schema()) if asynchronous else desktop.schema()
        names = [tool.name for tool in schema.tools]
        if names != ["AX", "GUI"]:
            raise RuntimeError("unexpected schema tools: %r" % names)
        hello = desktop.transport.hello
        if hello["protocol"] != hcu.PROTOCOL or not hello.get("instance_id"):
            raise RuntimeError("invalid daemon hello: %r" % hello)
        status = desktop.transport("status", {})
        if status["pid"] != hello["pid"] or os.path.normcase(status["socket"]) != os.path.normcase(endpoint):
            raise RuntimeError("status does not identify the dedicated smoke daemon")
        return {"pid": hello["pid"], "instance_id": hello["instance_id"], "tools": names}
    finally:
        desktop.transport.close()


def main():
    if len(sys.argv) != 2:
        print("usage: smoke-sdk.py /path/to/computer-use", file=sys.stderr)
        return 2
    binary = Path(sys.argv[1]).resolve(strict=True)
    original = os.environ.copy()
    # Mac 的系统临时目录较长；Windows 使用当前账户的系统临时目录。
    base = None if os.name == "nt" else "/tmp"
    with tempfile.TemporaryDirectory(prefix="hcu", dir=base) as root:
        native = Path(root) / "原生 程序"
        native.mkdir()
        copied = native / binary.name
        shutil.copy2(binary, copied)
        # 原生包以同目录的独立 provider 发布，保留它的相对位置。
        for provider in binary.parent.glob("computer-use-*-provider*"):
            if provider.is_file():
                shutil.copy2(provider, native / provider.name)
        temporary = Path(root) / "中文 空格"
        temporary.mkdir()
        os.environ.update({"HCU_BIN": str(copied), "HCU_SESSION": "sdk smoke 中文",
                           "HCU_SOCKET": "", "XDG_RUNTIME_DIR": "", "DISPLAY": "", "WAYLAND_DISPLAY": "",
                           "TMPDIR": str(temporary), "TMP": str(temporary), "TEMP": str(temporary),
                           "COMPUTER_USE_OUTPUT_DIR": str(Path(root) / "output"), "PYTHONUTF8": "1"})
        endpoint = hcu.default_socket()
        if Path(endpoint).exists():
            raise RuntimeError("refusing to use an existing endpoint: " + endpoint)
        results = []
        try:
            # 首次 sync connect 必须走真实 daemon start；随后 async 重用同一 daemon。
            results.append(check(hcu.connect(), endpoint))
            results.append(check(hcu.AsyncDesktop(), endpoint, asynchronous=True))
            results.append(check(hcu.connect(socket=endpoint, session="explicit 中文"), endpoint))
            results.append(check(hcu.AsyncDesktop(hcu.SocketTransport(path=endpoint, start=False)),
                                 endpoint, asynchronous=True))
            os.environ["HCU_SOCKET"] = endpoint
            results.append(check(hcu.connect(), endpoint))
            results.append(check(hcu.AsyncDesktop(), endpoint, asynchronous=True))
            if len({item["instance_id"] for item in results}) != 1:
                raise RuntimeError("clients did not reuse the same daemon instance")
            status = subprocess.run([str(copied), "daemon", "status", "--socket", endpoint],
                                    check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=25)
            if json.loads(status.stdout)["pid"] != results[0]["pid"]:
                raise RuntimeError("CLI and SDK disagree on daemon pid")
            print(json.dumps({"platform": sys.platform, "python": sys.version.split()[0],
                              "endpoint": endpoint, "binary": str(copied),
                              "connections": len(results), "desktop_inputs": 0,
                              "daemon": results[0]}, ensure_ascii=False))
        finally:
            try:
                # 即使启动失败，也只清理本次创建的专用 endpoint。
                subprocess.run([str(copied), "daemon", "stop", "--socket", endpoint],
                               check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=25)
                deadline = time.monotonic() + 5
                while Path(endpoint).exists() and time.monotonic() < deadline:
                    time.sleep(0.05)
                if Path(endpoint).exists():
                    raise RuntimeError("smoke daemon did not remove its socket after stop")
            finally:
                os.environ.clear()
                os.environ.update(original)
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main())
