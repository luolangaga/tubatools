"""Accessors for OSWorld task definitions and the run-bound task date.

V1 tasks are JSON dictionaries and V2 tasks are ``BaseTask`` objects; the
helpers here read both shapes.
"""

from __future__ import annotations

import datetime
from pathlib import Path
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError


def task_value(task, key: str, default=None):
    if hasattr(task, "get"):
        return task.get(key, default)
    return getattr(task, key, default)


def task_phases(task) -> list[dict]:
    getter = getattr(task, "get_phases", None)
    if not callable(getter):
        return []
    phases = getter() or []
    if not isinstance(phases, list):
        raise TypeError("get_phases() must return a list")
    return phases


def task_current_date(
    task,
    *,
    run_started_at_utc: str | None = None,
    timezone_name: str = "UTC",
) -> str:
    value = None
    if hasattr(task, "get") and callable(getattr(task, "get")):
        value = task.get("task_current_date")
    if value is None:
        value = getattr(task, "task_current_date", None)
    if value is not None:
        return str(value)

    raw_started = (
        str(run_started_at_utc or "").strip()
        or datetime.datetime.now(datetime.timezone.utc).isoformat()
    )
    parsed = datetime.datetime.fromisoformat(
        raw_started.replace("Z", "+00:00")
    )
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=datetime.timezone.utc)
    try:
        zone = ZoneInfo(timezone_name)
    except ZoneInfoNotFoundError as exc:
        raise ValueError(
            f"Unknown run timezone: {timezone_name}"
        ) from exc
    return parsed.astimezone(zone).date().isoformat()


def bind_task_runtime_timezone(task, timezone_name: str) -> None:
    normalized = str(timezone_name or "").strip()
    if not normalized:
        raise ValueError("Run timezone must not be empty")
    if hasattr(task, "__setitem__"):
        task["runtime_timezone"] = normalized
        return
    setattr(task, "runtime_timezone", normalized)


def resolve_guest_image(task, args) -> str:
    task_image = str(task_value(task, "image", "") or "")
    if not args.guest_image:
        return task_image

    image = Path(args.guest_image).expanduser().resolve()
    if not image.is_file():
        raise FileNotFoundError(f"Guest image not found: {image}")
    return str(image)


def phase_delivery_status(
    phases: list[dict],
    phase_details: list[dict],
) -> dict:
    expected = len(phases) if phases else 1
    delivered = len(phase_details)
    delivered_phase_numbers = [
        int(detail.get("phase_index", index))
        for index, detail in enumerate(phase_details, start=1)
    ]
    missing_phase_numbers = [
        index
        for index in range(1, expected + 1)
        if index not in delivered_phase_numbers
    ]
    return {
        "expected_phase_count": expected,
        "delivered_phase_count": delivered,
        "delivered_phase_numbers": delivered_phase_numbers,
        "missing_phase_numbers": missing_phase_numbers,
        "all_phases_delivered": (
            delivered == expected
            and not missing_phase_numbers
        ),
    }
