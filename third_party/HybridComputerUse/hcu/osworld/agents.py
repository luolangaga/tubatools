"""Configuration for the guest-local task agents.

Codex reads a generated ``config.toml``; Claude Code receives CLI flags, an
MCP configuration and a schema adapter; Gemini CLI reads a ``settings.json``
from an isolated home; Grok Build reads a ``config.toml`` from an isolated
``GROK_HOME``. All four run through one guest launcher script
(``hcu.osworld.launcher``) that writes a turn-scoped completion record when
the agent exits.
"""

from __future__ import annotations

import json
from typing import Literal
import urllib.parse

from hcu.osworld.guest import (
    GUEST_CLAUDE_MCP_ADAPTER,
    GUEST_MCP,
)


# Per-agent facts the runner needs: the file under artifacts/, the gateway
# that serves the agent ("openai" is the Responses API), the process name the
# watchdog looks for, the defaults for --model and --effort (Gemini CLI takes
# a thinking level), and whether a turn can resume the agent's session.
# Grok Build 1.0.40 drops --system-prompt-override on --resume and falls back
# to its default prompt, so it cannot continue a task under the same
# instructions.
AGENT_PROFILES = {
    "codex": {
        "artifact": "codex",
        "gateway": "openai",
        "process": "codex",
        "default_model": "gpt-5.5",
        "default_effort": "xhigh",
        "can_resume": True,
    },
    "claude-code": {
        "artifact": "claude-code",
        "gateway": "anthropic",
        "process": "claude",
        "default_model": "claude-opus-5",
        "default_effort": "xhigh",
        "can_resume": True,
    },
    "gemini-cli": {
        "artifact": "gemini-cli.tgz",
        "gateway": "gemini",
        "process": "gemini-node",
        "default_model": "gemini-3.8-flash",
        "default_effort": "high",
        "can_resume": True,
    },
    "grok-build": {
        "artifact": "grok",
        "gateway": "openai",
        "process": "grok",
        "default_model": "grok-4.7",
        "default_effort": "xhigh",
        "can_resume": False,
    },
}
AGENTS = tuple(AGENT_PROFILES)


CLAUDE_BUILTIN_TOOLS = "Bash,Edit,Read"


CODEX_BUNDLED_SYSTEM_SKILLS = (
    "imagegen",
    "openai-docs",
    "plugin-creator",
    "review-agent",
    "skill-creator",
    "skill-installer",
)


CODEX_PROVIDER_ID = "osworld_gateway"
CODEX_PROVIDER_NAME = "OSWorld task-scoped model gateway"
# Longer than the gateway's rate-limit wait plus one slow generation.
CODEX_STREAM_IDLE_TIMEOUT_MS = 900_000


def task_gateway_base(
    gateway_base: str,
    affinity_id: str,
    *,
    affinity_prefix: str,
) -> str:
    """Gateway URL whose ``/t/<id>`` path carries the task affinity ID."""
    return (
        gateway_base.rstrip("/")
        + "/t/"
        + urllib.parse.quote(f"{affinity_prefix}-{affinity_id}", safe="")
    )


# Ablation configurations expose one desktop channel. The MCP server reads
# COMPUTER_USE_CHANNELS and lists only that channel's tool, so the agent never
# sees the other one. The values are the server's channel names.
CHANNEL_TOOLS = {
    "": ("AX", "GUI"),
    "a11y,keyboard": ("AX",),
    "gui,keyboard": ("GUI",),
}


def public_tools(channels: str) -> tuple[str, ...]:
    if channels not in CHANNEL_TOOLS:
        raise ValueError(
            "channels must be empty, a11y,keyboard or gui,keyboard, "
            f"got {channels!r}"
        )
    return CHANNEL_TOOLS[channels]


def _toml_string(value: str) -> str:
    # JSON's UTF-16 surrogate escapes are not valid TOML Unicode escapes.
    return json.dumps(value, ensure_ascii=False).replace("\x7f", "\\u007f")


def codex_config(
    *,
    model: str,
    effort: str,
    base_url: str,
    codex_home: str,
    base_instructions: str,
    mcp_command: str,
    channels: str = "",
    developer_instructions: str | None = None,
    execution_context: Literal["standalone", "isolated-guest"] = "standalone",
    code_mode: bool = False,
) -> str:
    """Codex config with sandboxed shell execution and on-request approvals.

    Only callers that provide an externally isolated, disposable guest should
    select ``isolated-guest`` to retain unattended, unrestricted evaluation.
    This setting does not isolate the MCP server or its desktop session.

    ``code_mode`` gives the model only Codex's code tool. The computer-use
    MCP server stays registered, and code calls AX and GUI as
    ``tools.mcp__computer_use__AX`` and ``tools.mcp__computer_use__GUI``.
    Codex versions that defer MCP tools in code mode leave their declarations
    out of the code tool description; ``omit_tools_from`` keeps them in.
    """
    if execution_context == "standalone":
        approval_policy, sandbox_mode = "on-request", "workspace-write"
    elif execution_context == "isolated-guest":
        approval_policy, sandbox_mode = "never", "danger-full-access"
    else:
        raise ValueError(
            "execution_context must be standalone or isolated-guest, "
            f"got {execution_context!r}"
        )
    public_tools(channels)
    mcp_env = (
        f'\n[mcp_servers.computer_use.env]\nCOMPUTER_USE_CHANNELS = {_toml_string(channels)}\n'
        if channels
        else ""
    )
    disabled_system_skills = "\n\n".join(
        (
            "[[skills.config]]\n"
            "path = "
            + _toml_string(f"{codex_home}/skills/.system/{name}/SKILL.md")
            + "\n"
            "enabled = false"
        )
        for name in CODEX_BUNDLED_SYSTEM_SKILLS
    )
    extra_instructions = (
        "developer_instructions = "
        + _toml_string(developer_instructions)
        + "\n"
        if developer_instructions is not None
        else ""
    )
    code_mode_features = (
        "code_mode = true\ncode_mode_only = true\ncode_mode_host = true"
        if code_mode else "code_mode_host = false"
    )
    code_mode_mcp = 'omit_tools_from = ["deferred"]\n' if code_mode else ""
    return f"""\
model = {_toml_string(model)}
model_provider = "{CODEX_PROVIDER_ID}"
model_reasoning_effort = {_toml_string(effort)}
approval_policy = {_toml_string(approval_policy)}
sandbox_mode = {_toml_string(sandbox_mode)}
web_search = "disabled"
model_reasoning_summary = "detailed"
show_raw_agent_reasoning = true
model_instructions_file = {_toml_string(base_instructions)}
{extra_instructions}check_for_update_on_startup = false

[analytics]
enabled = false

[history]
persistence = "none"

[otel]
exporter = "none"
metrics_exporter = "none"
trace_exporter = "none"
log_user_prompt = false

[features]
shell_tool = true
unified_exec = true
view_image = true
plugins = false
plugin_sharing = false
remote_plugin = false
skill_search = false
skill_mcp_dependency_install = false
memories = false
goals = false
apps = false
hooks = false
multi_agent = false
browser_use = true
browser_use_external = true
browser_use_full_cdp_access = true
in_app_browser = true
workspace_dependencies = false
image_generation = false
computer_use = false
auth_elicitation = false
{code_mode_features}
fast_mode = false
in_app_chat = false
in_app_dictation = false
in_app_local_automation = false
in_app_updates = false
mcp_2026_07_28 = false
recommended_plugins = false
remote_compaction_v2 = false
tool_call_mcp_elicitation = false
tool_suggest = false
unbounded_connection_retries = false

[memories]
generate_memories = false
use_memories = false
disable_on_external_context = true

[tools.experimental_request_user_input]
enabled = false

[tools.update_plan]
enabled = false

{disabled_system_skills}

[model_providers.{CODEX_PROVIDER_ID}]
name = {_toml_string(CODEX_PROVIDER_NAME)}
base_url = {_toml_string(base_url)}
wire_api = "responses"
# The gateway holds a rate-limited request open while it waits for capacity.
stream_idle_timeout_ms = {CODEX_STREAM_IDLE_TIMEOUT_MS}

[mcp_servers.computer_use]
command = {_toml_string(mcp_command)}
args = ["mcp"]
required = true
startup_timeout_sec = 60
tool_timeout_sec = 180
{code_mode_mcp}{mcp_env}"""


def code_mode_profile(instructions: str, code_guide: str) -> str:
    """The profile for code mode: the selected profile, then the code guide."""
    return instructions + "\n\n" + code_guide if instructions else code_guide


def claude_mcp_config(
    *,
    adapter: str = GUEST_CLAUDE_MCP_ADAPTER,
    mcp_command: str = GUEST_MCP,
    channels: str = "",
    python_executable: str = "/usr/bin/python3",
) -> str:
    """Claude Code MCP configuration that runs the MCP behind the adapter."""
    public_tools(channels)
    server: dict = {
        "type": "stdio",
        "command": python_executable,
        "args": [adapter, mcp_command, "mcp"],
    }
    if channels:
        server["env"] = {"COMPUTER_USE_CHANNELS": channels}
    return json.dumps({"mcpServers": {"computer_use": server}}, indent=2) + "\n"


def claude_system_prompt(base_instructions: str, instructions: str) -> str:
    """Claude Code gets the profile in its replacement system prompt.

    A ``CLAUDE.md`` file would also load ambient configuration, so the profile
    is appended to the base instructions instead.
    """
    return base_instructions + ("\n\n" + instructions if instructions else "")


def grok_system_prompt(base_instructions: str, instructions: str) -> str:
    """Grok Build ignores its rules files under a system-prompt override, so
    the profile follows the base instructions in the override itself."""
    return base_instructions + ("\n\n" + instructions if instructions else "")


# Gemini CLI tools that talk to the user or the web, or add planning and
# memory layers outside the benchmark contract.
GEMINI_EXCLUDED_TOOLS = (
    "google_web_search", "web_fetch", "save_memory", "write_todos", "ask_user",
    "enter_plan_mode", "exit_plan_mode", "invoke_agent", "activate_skill",
    "update_topic",
)
GEMINI_THINKING_LEVELS = ("minimal", "low", "medium", "high")
# Gemini parses MCP identity at the first underscore after its "mcp_" prefix.
GEMINI_MCP_SERVER = "computer-use"
# Gemini CLI waits 60 s for response headers by default. The gateway relays a
# streamed response only after it completes, so long generations need more.
GEMINI_REQUEST_TIMEOUT_SEC = 600
GEMINI_EXPERIMENT_REQUEST_TIMEOUT = 45773134


def gemini_experiments() -> str:
    """Local experiment flags for ``GEMINI_EXP``; only the request timeout."""
    return json.dumps({
        "flags": [{
            "flagId": GEMINI_EXPERIMENT_REQUEST_TIMEOUT,
            "intValue": str(GEMINI_REQUEST_TIMEOUT_SEC),
        }],
        "experimentIds": [],
    }, indent=2) + "\n"


def gemini_settings(
    *,
    model: str,
    effort: str,
    mcp_command: str = GUEST_MCP,
    channels: str = "",
) -> str:
    """Gemini CLI ``settings.json`` with ambient features off and the MCP allowed.

    ``dynamicModelConfiguration`` is required: without it CLI 0.60.0 rewrites
    the selected model. Only the global ``GEMINI.md`` next to this file is
    loaded as context, and it carries the profile; directory discovery is off.
    ``effort`` is a Gemini thinking level, or ``native`` for the model default.
    """
    public_tools(channels)
    if effort != "native" and effort not in GEMINI_THINKING_LEVELS:
        raise ValueError(
            "Gemini CLI effort must be native or a thinking level ("
            + ", ".join(GEMINI_THINKING_LEVELS) + f"), got {effort!r}"
        )
    server: dict = {
        "command": mcp_command,
        "args": ["mcp"],
        "timeout": 180000,
        "trust": False,
    }
    if channels:
        server["env"] = {"COMPUTER_USE_CHANNELS": channels}
    settings = {
        # The gateway enforces the model-call budget; the CLI has no turn cap.
        "model": {"name": model, "maxSessionTurns": -1},
        "experimental": {
            "dynamicModelConfiguration": True,
            "enableAgents": False,
            "autoMemory": False,
            "taskTracker": False,
        },
        "general": {
            "enableAutoUpdate": False,
            "enableAutoUpdateNotification": False,
            "topicUpdateNarration": False,
            "plan": {"enabled": False, "modelRouting": False},
        },
        "skills": {"enabled": False},
        "hooksConfig": {"enabled": False},
        "admin": {"extensions": {"enabled": False}, "skills": {"enabled": False}},
        "tools": {"exclude": list(GEMINI_EXCLUDED_TOOLS)},
        "mcpServers": {GEMINI_MCP_SERVER: server},
        "mcp": {"allowed": [GEMINI_MCP_SERVER]},
        "security": {
            "auth": {"selectedType": "gemini-api-key", "enforcedType": "gemini-api-key"},
            "environmentVariableRedaction": {"allowed": ["COMPUTER_USE_CHANNELS"]},
        },
        "context": {
            "fileName": "GEMINI.md",
            "discoveryMaxDirs": 0,
            "memoryBoundaryMarkers": [],
            "loadMemoryFromIncludeDirectories": False,
            "includeDirectoryTree": False,
        },
        "advanced": {"ignoreLocalEnv": True, "autoConfigureMemory": False},
        "privacy": {"usageStatisticsEnabled": False},
        "telemetry": {"enabled": False},
        # Compression and loop detection otherwise fall back to older models.
        "modelConfigs": {"customAliases": {
            alias: {"modelConfig": {"model": model}}
            for alias in ("chat-compression-default", "loop-detection",
                          "loop-detection-double-check")
        }},
    }
    if effort != "native":
        settings["modelConfigs"]["customOverrides"] = [{
            "match": {"model": model},
            "modelConfig": {"generateContentConfig": {
                "thinkingConfig": {"thinkingLevel": effort.upper()},
            }},
        }]
    return json.dumps(settings, indent=2) + "\n"


def grok_config(
    *,
    model: str,
    base_url: str,
    effort: str,
    mcp_command: str = GUEST_MCP,
    channels: str = "",
) -> str:
    """Grok Build ``config.toml`` bound to the task gateway with ambient features off.

    The model is registered under the key ``gateway`` so the CLI never
    contacts x.ai directly; the gateway URL already carries the task path.
    """
    public_tools(channels)
    q = _toml_string
    mcp_env = (
        f"\n[mcp_servers.computer_use.env]\nCOMPUTER_USE_CHANNELS = {q(channels)}\n"
        if channels
        else ""
    )
    off = lambda *keys: "\n".join(f"{key} = false" for key in keys)  # noqa: E731
    return f"""\
[cli]
auto_update = false
use_leader = false

[models]
default = "gateway"
allowed_models = ["gateway", {q(model)}]
session_summary = "gateway"
image_description = "gateway"
web_search = "gateway"
prompt_suggestion = "gateway"
max_retries = 0
subagent_rate_limit_max_attempts = 0

[model.gateway]
model = {q(model)}
name = {q(model)}
base_url = {q(base_url)}
api_backend = "responses"
env_key = "GROK_GATEWAY_TOKEN"
reasoning_summary = "none"
context_window = 500000
max_retries = 0
inference_idle_timeout_secs = 600
subagent_rate_limit_max_attempts = 0
supports_backend_search = false

[[model.gateway.reasoning_efforts]]
id = {q(effort)}
name = {q(effort.title())}
value = {q(effort)}

[endpoints]
models_base_url = {q(base_url)}
models_list_url = {q(base_url + "/models")}
xai_api_base_url = {q(base_url)}
cli_chat_proxy_base_url = {q(base_url)}

[mcp_servers.computer_use]
command = {q(mcp_command)}
args = ["mcp"]
enabled = true
startup_timeout_sec = 60
tool_timeout_sec = 180
{mcp_env}
[features]
{off("remote_fetch", "managed_config", "telemetry", "title_refresh", "turn_summary",
     "session_recap", "session_search", "feedback", "backend_tools", "image_gen",
     "video_gen", "voice_mode", "non_git_warning", "ask_user_question")}

[telemetry]
otel_enabled = false
trace_upload = false

[relay]
enabled = false

[memory]
enabled = false

[memory_v2]
enabled = false

[session]
load_envrc = false

[subagents]
enabled = false

[workflows]
enabled = false

[ui]
prompt_suggestions = false

[compat.claude]
{off("agents", "hooks", "mcps", "rules", "skills", "sessions")}

[compat.cursor]
{off("agents", "hooks", "mcps", "rules", "skills", "sessions")}

[compat.codex]
{off("hooks", "skills", "sessions")}
"""


def thread_id_from_trace(raw: bytes | None, agent: str = "codex") -> str:
    if not raw:
        return ""
    for line in raw.decode("utf-8", errors="replace").splitlines():
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        if agent == "codex" and event.get("type") == "thread.started":
            return str(event.get("thread_id") or "")
        if agent == "claude-code" and event.get("session_id"):
            return str(event["session_id"])
        if agent == "gemini-cli" and event.get("type") == "init":
            return str(event.get("session_id") or "")
        if agent == "grok-build" and event.get("type") == "end":
            return str(event.get("sessionId") or "")
    return ""
