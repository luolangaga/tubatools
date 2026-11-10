from __future__ import annotations

from collections.abc import Callable
import math
import traceback
from typing import Any


def run_official_evaluator(evaluate: Callable[[], Any]) -> dict:
    """Run the official OSWorld evaluator once and record the outcome.

    Scores must be explicit and float-convertible (including bool and numeric
    scalars), and the converted value must be finite. Zero is a valid score.
    An evaluator exception, including a result without a valid score, is
    recorded instead of raised, so the run still seals an explicit RAW
    error and the result is excluded from scoring rather than lost. Rejected
    results are preserved as text so NaN and non-JSON objects remain evidence,
    not machine-consumable scores.
    """
    result_repr = None
    try:
        result = evaluate()
        result_repr = repr(result)
        payload = dict(result) if isinstance(result, dict) else {"score": result}
        payload["score"] = float(payload["score"])
        if not math.isfinite(payload["score"]):
            raise ValueError("Evaluator score must be finite")
    except Exception as exc:
        return {
            "status": "error",
            "result": None,
            "error": f"{type(exc).__name__}: {exc}",
            "traceback": traceback.format_exc(),
            "result_repr": result_repr,
        }
    return {
        "status": "completed",
        "result": payload,
        "error": None,
        "traceback": None,
    }
