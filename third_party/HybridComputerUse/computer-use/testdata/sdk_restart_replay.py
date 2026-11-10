"""Real daemon/socket replay regression; Go owns the reply-dropping proxy."""

import asyncio
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "sdk" / "python"))
import hcu_sdk as hcu

transport = hcu.SocketTransport(path=os.environ["HCU_SOCKET"], start=False)
scenario, style = sys.argv[1:]
disabled_ids = {"no-id": "", "blank-id": " \t", "invalid-id": 7}


async def run():
    desktop = hcu.AsyncDesktop(transport) if style == "async" else hcu.Desktop(transport)

    async def call(name, *args, **kwargs):
        result = getattr(desktop, name)(*args, **kwargs)
        return await result if style == "async" else result

    # Schema opens hello without triggering the read-only fault injection.
    await call("schema")
    original = transport.hello.get("instance_id")
    assert original or scenario == "legacy"
    if scenario == "read":
        result = await call("apps")
        assert result.ok and transport.hello["instance_id"] != original
        print("read retried after daemon restart")
        return

    window = (await call("apps")).windows[0].ref
    tree = await call("observe", window)
    button = next(element for element in tree.elements if element.role == "button" and element.ref)
    if scenario in ("restart", "legacy", "expired", "evicted", "unsent") or scenario in disabled_ids:
        try:
            options = {"request_id": disabled_ids[scenario]} if scenario in disabled_ids else {}
            await call("click", button.ref, **options)
        except hcu.TransportEffectUnknown as error:
            assert error.input_emitted is None
            if scenario in disabled_ids:
                assert error.request_id == (disabled_ids[scenario] or None)
            else:
                assert error.request_id
            if scenario == "restart":
                assert transport.hello["instance_id"] != original
            print(scenario, "blocked before replay; unknown input", error.request_id)
        else:
            raise AssertionError("restart replay must fail as unknown effect")
        assert (await call("apps")).ok
    else:
        result = await call("click", button.ref)
        assert result.ok and result.replayed and result.input_emitted
        assert transport.hello["instance_id"] == original
        print("same daemon returned original result without second input")


try:
    asyncio.run(run())
finally:
    transport.close()
