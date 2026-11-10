import importlib.util
from pathlib import Path

import pytest


MODULE_PATH = (
    Path(__file__).resolve().parents[1]
    / "osworld-overlay/desktop_env/cdp.py"
)
SPEC = importlib.util.spec_from_file_location("hcu_cdp", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
cdp = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(cdp)


class FakeChromium:
    def __init__(self, outcomes):
        self.outcomes = list(outcomes)
        self.calls = 0

    def connect_over_cdp(self, url):
        self.calls += 1
        outcome = self.outcomes.pop(0)
        if isinstance(outcome, Exception):
            raise outcome
        return outcome


def test_cdp_retry_recovers_without_launching_another_browser(monkeypatch):
    chromium = FakeChromium(
        [RuntimeError("socket hang up"), RuntimeError("reset"), "browser"]
    )
    diagnostics = []
    sleeps = []
    monkeypatch.setattr(
        cdp,
        "_endpoint_diagnostic",
        lambda url: diagnostics.append(url) or "HTTP 200",
    )
    monkeypatch.setattr(cdp.time, "sleep", sleeps.append)

    browser = cdp.connect_over_cdp_with_retry(
        chromium,
        "http://127.0.0.1:9222",
    )

    assert browser == "browser"
    assert chromium.calls == 3
    assert diagnostics == [
        "http://127.0.0.1:9222",
        "http://127.0.0.1:9222",
    ]
    assert sleeps == [1.0, 2.0]


def test_cdp_retry_preserves_all_attempt_diagnostics(monkeypatch):
    chromium = FakeChromium(
        [RuntimeError("one"), RuntimeError("two"), RuntimeError("three")]
    )
    monkeypatch.setattr(cdp, "_endpoint_diagnostic", lambda url: "HTTP 503")
    monkeypatch.setattr(cdp.time, "sleep", lambda seconds: None)

    with pytest.raises(cdp.CDPConnectionError) as error:
        cdp.connect_over_cdp_with_retry(
            chromium,
            "http://127.0.0.1:9222",
        )

    message = str(error.value)
    assert "one" in message
    assert "two" in message
    assert "three" in message
    assert message.count("HTTP 503") == 3


def test_cdp_retry_rejects_zero_attempts():
    with pytest.raises(ValueError, match="attempts must be positive"):
        cdp.connect_over_cdp_with_retry(
            FakeChromium([]),
            "http://127.0.0.1:9222",
            attempts=0,
        )
