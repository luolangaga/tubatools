from __future__ import annotations

import logging
import time
from typing import Any

import requests


class CDPConnectionError(RuntimeError):
    """Raised after bounded attempts to connect to an existing CDP endpoint."""


def _endpoint_diagnostic(url: str) -> str:
    endpoint = url.rstrip("/") + "/json/version"
    try:
        response = requests.get(endpoint, timeout=3)
        body = response.text[:300].replace("\n", " ")
        return f"GET {endpoint} -> {response.status_code}: {body!r}"
    except Exception as exc:
        return f"GET {endpoint} -> {type(exc).__name__}: {exc}"


def connect_over_cdp_with_retry(
    chromium: Any,
    url: str,
    *,
    attempts: int = 3,
    initial_delay: float = 1.0,
    logger: logging.Logger | None = None,
) -> Any:
    if attempts < 1:
        raise ValueError("attempts must be positive")
    log = logger or logging.getLogger("desktopenv.cdp")
    failures: list[str] = []

    for attempt in range(1, attempts + 1):
        try:
            return chromium.connect_over_cdp(url)
        except Exception as exc:
            diagnostic = _endpoint_diagnostic(url)
            failure = (
                f"attempt {attempt}/{attempts}: "
                f"{type(exc).__name__}: {exc}; {diagnostic}"
            )
            failures.append(failure)
            log.warning("CDP connection failed: %s", failure)
            if attempt < attempts:
                time.sleep(initial_delay * (2 ** (attempt - 1)))

    raise CDPConnectionError(
        f"CDP connection failed after {attempts} attempts at {url}: "
        + " | ".join(failures)
    )
