"""Opt-in wall-clock timing of provider operation phases.

Set ``COMPUTER_USE_PROVIDER_TIMING=1`` to write one JSON line per operation
to stderr, which the Go host forwards as a provider diagnostic. The timing
never reaches the model: responses are unchanged whether it is on or off.
"""

import json
import os
import sys
import time

_ENABLED_VALUES = {"1", "true", "yes", "on"}


def enabled():
    value = os.environ.get("COMPUTER_USE_PROVIDER_TIMING", "")
    return value.strip().lower() in _ENABLED_VALUES


class OperationTimer:
    """Accumulate named phase durations for one provider operation."""

    def __init__(self):
        self.started = time.monotonic()
        self.last = self.started
        self.phases = {}
        self.notes = {}

    def lap(self, name):
        now = time.monotonic()
        self.phases[name] = self.phases.get(name, 0.0) + (now - self.last)
        self.last = now


_current = None


def start():
    """Begin timing one operation when timing is on."""
    global _current
    _current = OperationTimer() if enabled() else None


def lap(name):
    """Charge the time since the previous lap to ``name``; no-op when off.

    Laps need one added line after each phase instead of re-indenting the
    phase, which keeps the instrumented code unchanged. Laps are sequential:
    a lap inside a helper (``snapshot.*``) takes its share of the time, and
    the caller's next lap gets only the remainder.
    """
    if _current is not None:
        _current.lap(name)


def note(name, value):
    """Attach a small diagnostic value to the current timing record."""
    if _current is not None:
        _current.notes[name] = value


def finish(operation, request_id=""):
    global _current
    timer, _current = _current, None
    if timer is None:
        return
    total = time.monotonic() - timer.started
    record = {
        "kind": "linux_provider_diagnostic",
        "request_id": str(request_id or "unknown"),
        "operation": str(operation or "unknown"),
        "stage": "timing",
        "diagnostic": json.dumps({
            "startedMonotonic": round(timer.started, 4),
            "totalMs": round(total * 1000, 1),
            "notes": timer.notes,
            "phasesMs": {
                name: round(value * 1000, 1)
                for name, value in timer.phases.items()
            },
        }, sort_keys=True),
    }
    print(json.dumps(record, separators=(",", ":")), file=sys.stderr,
          flush=True)
