import json
import os
from pathlib import Path
import subprocess

import pytest



ROOT = Path(__file__).resolve().parents[1]
OSWORLD_ROOT = Path(
    os.environ.get("OSWORLD_V2_ROOT", ROOT / "external" / "OSWorld-V2")
)


def require_osworld_checkout() -> Path:
    python = OSWORLD_ROOT / ".venv/bin/python"
    if not (OSWORLD_ROOT / "desktop_env").is_dir() or not python.is_file():
        pytest.skip(f"OSWorld-V2 checkout with .venv not found at {OSWORLD_ROOT}")
    return python


@pytest.mark.parametrize(
    ("module", "expected"),
    [
        ("hcu.osworld.run_task", ["--benchmark-version {v1,v2}", "--instructions-file"]),
        (
            "hcu.osworld.setup_canary",
            ["--benchmark-version {v1,v2}", "--task-domain TASK_DOMAIN"],
        ),
    ],
)
def test_entrypoint_help_imports_with_osworld_checkout(module, expected):
    python = require_osworld_checkout()
    result = subprocess.run(
        [str(python), "-m", module, "--help"],
        cwd=ROOT,
        env={**os.environ, "OSWORLD_V2_ROOT": str(OSWORLD_ROOT)},
        capture_output=True,
        text=True,
        timeout=60,
    )
    assert result.returncode == 0, result.stderr
    for text in expected:
        assert text in result.stdout


STAGING_PROBE = """
import argparse, json, sys
from pathlib import Path
from hcu.osworld import staging
from hcu.osworld.agents import AGENT_PROFILES

root, out, agent = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
instructions_file = Path(sys.argv[4]) if sys.argv[4] else None
profile = AGENT_PROFILES[agent]
args = argparse.Namespace(
    agent=agent, model=profile["default_model"],
    effort=profile["default_effort"],
    gateway={"openai": "http://gateway:8600", "anthropic": "http://gateway:8537",
             "gemini": "http://gateway:8571"}[profile["gateway"]],
    asset_cache="/unused/cache", offline_assets=False,
    benchmark_version="v1", channels="", task_id="001", task_domain="",
)
run = staging.TaskRun(
    args=args, run_tag="RUN", result_dir=out,
    token_log=out / "tokens.jsonl", affinity_task="task-RUN",
    agent_binary=out / profile["artifact"],
    node_binary=out / "node" if agent == "gemini-cli" else None,
    mcp_binary=out / "computer-use",
    instructions_file=instructions_file,
    base_instructions_file=root / "instructions/base.md",
    task={"instruction": "Do the task."}, phases=[],
    instruction="Do the task.", current_date="2000-01-01",
    run_timezone="UTC", run_started_at_utc="2000-01-01T00:00:00+00:00",
    explicit_task_date=None, guest_image="",
)
out.mkdir(parents=True, exist_ok=True)
staging.stage_local_files(run)
uploads, executables = staging.runtime_uploads(run)
print(json.dumps({
    "endpoint": run.agent_endpoint,
    "uploads": {row["path"]: Path(row["local_path"]).name for row in uploads},
    "executables": executables,
    "system_prompt": run.base_instructions_copy.read_text(),
    "instructions": run.instructions_copy.read_text() if run.instructions_copy.exists() else None,
    "metadata": staging.initial_meta(run, {}),
    "launcher": run.launcher_path.read_text(),
}))
"""


def stage(tmp_path, agent, profile="hybrid"):
    python = require_osworld_checkout()
    result = subprocess.run(
        [str(python), "-c", STAGING_PROBE, str(ROOT), str(tmp_path), agent,
         str(ROOT / "instructions" / f"{profile}.md") if profile else ""],
        cwd=ROOT,
        env={**os.environ, "OSWORLD_V2_ROOT": str(OSWORLD_ROOT)},
        capture_output=True,
        text=True,
        timeout=60,
    )
    assert result.returncode == 0, result.stderr
    return json.loads(result.stdout)


def test_each_agent_is_bound_to_its_gateway_with_the_task_prefix(tmp_path):
    expected = {
        "codex": "http://gateway:8600/t/task-RUN",
        "claude-code": "http://gateway:8537/t/task-RUN",
        "gemini-cli": "http://gateway:8571/t/task-RUN",
        "grok-build": "http://gateway:8600/t/task-RUN",
    }
    staged = {agent: stage(tmp_path / agent, agent) for agent in expected}
    for agent, endpoint in expected.items():
        assert staged[agent]["endpoint"] == endpoint
    for agent in ("codex", "grok-build"):
        config = (tmp_path / agent / "guest-config.toml").read_text(encoding="utf-8")
        assert 'base_url = "http://gateway:8600/t/task-RUN/v1"' in config
    assert (
        "ANTHROPIC_BASE_URL=http://gateway:8537/t/task-RUN"
        in staged["claude-code"]["launcher"]
    )
    assert (
        "GOOGLE_GEMINI_BASE_URL=http://gateway:8571/t/task-RUN"
        in staged["gemini-cli"]["launcher"]
    )


@pytest.mark.parametrize("agent", ["codex", "claude-code", "gemini-cli", "grok-build"])
@pytest.mark.parametrize("profile", ["hybrid", "ax-only", "gui-only", None])
def test_each_agent_receives_instructions_through_its_own_channel(tmp_path, agent, profile):
    from hcu.osworld.guest import GUEST_GEMINI_CONTEXT, GUEST_INSTRUCTIONS

    instructions_file = ROOT / "instructions" / f"{profile}.md" if profile else None
    instructions = instructions_file.read_text() if instructions_file else None
    base = (ROOT / "instructions/base.md").read_text()
    staged = stage(tmp_path, agent, profile)
    assert staged["instructions"] == instructions
    assert staged["metadata"]["instructions_file"] == (
        str(instructions_file) if instructions_file else None
    )
    assert staged["metadata"]["base_instructions_file"] == str(ROOT / "instructions/base.md")
    assert "skill_file" not in staged["metadata"]
    if agent in ("codex", "gemini-cli"):
        assert staged["system_prompt"] == base
        context_path = GUEST_INSTRUCTIONS if agent == "codex" else GUEST_GEMINI_CONTEXT
        if instructions_file:
            assert staged["uploads"][context_path] == "operating-instructions.md"
            assert (tmp_path / staged["uploads"][context_path]).read_bytes() == instructions_file.read_bytes()
        else:
            assert context_path not in staged["uploads"]
    else:
        assert staged["system_prompt"] == base + ("\n\n" + instructions if instructions else "")
        assert GUEST_INSTRUCTIONS not in staged["uploads"]
        assert GUEST_GEMINI_CONTEXT not in staged["uploads"]


def test_gemini_ships_its_own_node_and_unpacks_the_cli_in_the_guest(tmp_path):
    staged = stage(tmp_path, "gemini-cli")
    uploads = staged["uploads"]
    assert uploads["/tmp/agent-task/gemini-cli.tgz"] == "gemini-cli.tgz"
    assert uploads["/home/user/.local/bin/gemini-node"] == "node"
    assert "/tmp/agent-task/gemini-cli.tgz" not in staged["executables"]
    assert "/home/user/.local/bin/gemini-node" in staged["executables"]
    experiments = json.loads(
        (tmp_path / "guest-experiments.json").read_text(encoding="utf-8")
    )
    assert experiments["flags"][0]["intValue"] == "600"
    settings = json.loads((tmp_path / "guest-settings.json").read_text())
    assert settings["modelConfigs"]["customOverrides"][0]["modelConfig"] == {
        "generateContentConfig": {"thinkingConfig": {"thinkingLevel": "HIGH"}}
    }


def test_channel_ablation_exposes_one_tool_to_each_agent():
    from hcu.osworld.agents import claude_mcp_config, codex_config, public_tools

    assert public_tools("") == ("AX", "GUI")
    assert public_tools("a11y,keyboard") == ("AX",)
    assert public_tools("gui,keyboard") == ("GUI",)
    with pytest.raises(ValueError):
        public_tools("a11y")

    codex = codex_config(
        model="m", effort="xhigh", base_url="http://g/v1", codex_home="/h",
        base_instructions="/b.md", mcp_command="/computer-use",
        channels="a11y,keyboard",
    )
    assert "[mcp_servers.computer_use.env]" in codex
    assert 'COMPUTER_USE_CHANNELS = "a11y,keyboard"' in codex
    assert "computer_use.env" not in codex_config(
        model="m", effort="xhigh", base_url="http://g/v1", codex_home="/h",
        base_instructions="/b.md", mcp_command="/computer-use",
    )

    claude = json.loads(claude_mcp_config(channels="gui,keyboard"))
    assert claude["mcpServers"]["computer_use"]["env"] == {
        "COMPUTER_USE_CHANNELS": "gui,keyboard"
    }
    assert "env" not in json.loads(claude_mcp_config())["mcpServers"]["computer_use"]


def test_gemini_and_grok_expose_only_the_requested_channel():
    from hcu.osworld.agents import gemini_settings, grok_config

    settings = json.loads(gemini_settings(
        model="gemini-3.8-flash", effort="native", channels="a11y,keyboard"
    ))
    assert settings["mcpServers"]["computer-use"]["env"] == {
        "COMPUTER_USE_CHANNELS": "a11y,keyboard"
    }
    assert settings["mcp"]["allowed"] == list(settings["mcpServers"])
    assert "customOverrides" not in settings["modelConfigs"]
    assert "google_web_search" in settings["tools"]["exclude"]
    with pytest.raises(ValueError):
        gemini_settings(model="gemini-3.8-flash", effort="xhigh")

    grok = grok_config(
        model="grok-4.7", base_url="http://g/t/task-x/v1", effort="xhigh",
        channels="gui,keyboard",
    )
    assert 'COMPUTER_USE_CHANNELS = "gui,keyboard"' in grok
    assert "computer_use.env" not in grok_config(
        model="grok-4.7", base_url="http://g/t/task-x/v1", effort="xhigh"
    )
