"""Minimal Messages continuation loop; no API calls occur on import.

Install the Anthropic client and make hcu_sdk importable on the harness host.
Pass an authenticated client, a request with an explicitly selected supported
model, and one Forwarder for the whole conversation. The code-execution
container calls hcu_ax directly; it does not need the Python SDK installed.

    import hcu_sdk as hcu
    from hcu_sdk import ptc

    desktop = hcu.connect()
    forwarder = ptc.Forwarder(desktop, image_reader=ptc.local_image_reader(output_dir))
    request = {
        "model": selected_model, "max_tokens": 4096,
        "tools": [{"type": ptc.CODE_EXECUTION_CALLER, "name": "code_execution"}]
                 + ptc.tool_definitions(desktop.schema()),
        "messages": [{"role": "user", "content": user_instruction}],
    }
    response = run_messages(client, request, forwarder)

output_dir must be the daemon's configured output directory on this host.
For a remote desktop, replace the reader with a bounded guest-file fetcher.
This example does not install software into the cloud code container, resume
an expired container, or retry an API request after a network error.
"""

import copy
from datetime import datetime, timezone


class ContinuationLimit(RuntimeError):
    """A resumable request including the results of any completed tool calls."""

    def __init__(self, request, response):
        super().__init__("Messages continuation limit reached; resume with error.request and the same Forwarder")
        self.request = request
        self.response = response


def run_messages(client, request, forwarder, max_rounds=32):
    """Return the final response, retaining all assistant and tool blocks.

    The caller should disable automatic retries in its API client when the
    result of an API request is unknown. Reusing this Forwarder protects
    repeated known tool_use IDs; it does not deduplicate newly generated IDs.
    """
    if max_rounds < 1:
        raise ValueError("max_rounds must be positive")
    pending = copy.deepcopy(request)
    for _ in range(max_rounds):
        response = client.messages.create(**pending)
        value = response.model_dump(mode="json", exclude_none=True) if hasattr(response, "model_dump") else response
        content = value.get("content", [])
        events = [block for block in content if block.get("type") == "tool_use"]
        reason = value.get("stop_reason")
        if not events and reason != "pause_turn":
            return response
        container = value.get("container") or {}
        needs_container = any((event.get("caller") or {}).get("type", "direct") != "direct"
                              for event in events)
        if needs_container and not container.get("id"):
            raise RuntimeError("PTC continuation requires the response container id")
        if container.get("expires_at"):
            expires = datetime.fromisoformat(container["expires_at"].replace("Z", "+00:00"))
            if expires <= datetime.now(timezone.utc):
                raise RuntimeError("code execution container expired; no tools were forwarded")
        if any(event.get("name") not in ("hcu_ax", "hcu_gui") for event in events):
            raise ValueError("this example only forwards HCU tools")
        pending["messages"].append({"role": "assistant", "content": content})
        if events:
            pending["messages"].append({"role": "user", "content": [forwarder(event) for event in events]})
        if container.get("id"):
            pending["container"] = container["id"]
    raise ContinuationLimit(pending, response)
