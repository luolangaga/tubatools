"""Keep Gemini usage counts stable across the MCP server alias change."""

import json

import pytest

from hcu.osworld.traces import summarize_gemini_trace


@pytest.mark.parametrize("server", ["computer-use", "computer_use"])
def test_gemini_trace_counts_current_and_historical_server_names(server):
    events = [
        {"type": "tool_use", "tool_name": f"mcp_{server}_AX"},
        {"type": "tool_use", "tool_name": f"mcp_{server}_GUI"},
        {"type": "tool_use", "tool_name": "run_shell_command"},
        {"type": "tool_use", "tool_name": "mcp_other_AX"},
        {"type": "result", "status": "success", "stats": {
            "input_tokens": 100, "cached": 60, "output_tokens": 10,
        }},
    ]
    summary = summarize_gemini_trace("\n".join(map(json.dumps, events)).encode())
    assert summary["mcp_calls"] == 2
    assert summary["tool_breakdown"] == {"AX": 1, "GUI": 1}
    assert summary["shell_calls"] == 1
    assert summary["input_tokens"] == 100
    assert summary["cached_input_tokens"] == 60
