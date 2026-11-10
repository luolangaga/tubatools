import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import tarfile
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer

import pytest

try:
    import tomllib
except ModuleNotFoundError:
    import tomli as tomllib

from hcu.osworld import agents


ROOT = Path(__file__).resolve().parents[1]


def render(tmp_path, agent, *, extra=(), env=None, python=sys.executable):
    base = tmp_path / "base instructions.md"
    instructions = tmp_path / "selected instructions.md"
    base.write_text("BASE_CONTEXT_MARKER\n", encoding="utf-8")
    instructions.write_text(
        'SELECTED_INSTRUCTIONS_MARKER\nUse "AX".\n\n---\nnot: [yaml\n',
        encoding="utf-8",
    )
    output = tmp_path / "profile" / (
        "config.toml" if agent in {"codex", "grok-build"} else
        ".gemini/settings.json" if agent == "gemini-cli" else "mcp.json"
    )
    environment = dict(os.environ, PYTHONPATH=str(ROOT))
    environment.update(env or {})
    result = subprocess.run(
        [python, "-m", "hcu.render_agent_config", "--agent", agent,
         "--gateway", "http://127.0.0.1:1", "--mcp", python,
         "--base-instructions", base.name, "--instructions-file", instructions.name,
         "--output", str(output), *extra],
        cwd=tmp_path, env=environment, capture_output=True, text=True,
        timeout=15,
    )
    assert result.returncode == 0, result.stderr
    return output, base, instructions


def read_toml(path):
    return tomllib.loads(path.read_text(encoding="utf-8"))


@pytest.mark.parametrize("home_source", ["default", "environment", "argument"])
def test_codex_render_preserves_context_and_uses_local_home(tmp_path, home_source):
    local_home = tmp_path / "local user"
    env_home = tmp_path / "env codex"
    cli_home = tmp_path / "cli codex"
    project_context = tmp_path / "AGENTS.md"
    project_context.write_text("PROJECT_CONTEXT_MARKER", encoding="utf-8")
    env = {"HOME": str(local_home), "USERPROFILE": str(local_home),
           "CODEX_HOME": ""}
    expected_home = local_home / ".codex"
    extra = []
    if home_source != "default":
        env["CODEX_HOME"] = str(env_home)
        expected_home = env_home
    if home_source == "argument":
        extra = ["--codex-home", str(cli_home)]
        expected_home = cli_home
    expected_home.mkdir(parents=True)
    user_context = expected_home / "AGENTS.md"
    user_context.write_text("USER_CONTEXT_MARKER", encoding="utf-8")
    output, base, instructions = render(tmp_path, "codex", extra=extra, env=env)
    config = read_toml(output)
    assert config["approval_policy"] == "on-request"
    assert config["sandbox_mode"] == "workspace-write"
    assert config["developer_instructions"] == instructions.read_text(encoding="utf-8")
    assert Path(config["model_instructions_file"]).read_text() == base.read_text()
    assert Path(config["model_instructions_file"]).is_absolute()
    assert Path(config["mcp_servers"]["computer_use"]["command"]).is_absolute()
    assert config["skills"]["config"][0]["path"] == (
        f"{expected_home}/skills/.system/{agents.CODEX_BUNDLED_SYSTEM_SKILLS[0]}/SKILL.md"
    )
    assert project_context.read_text() == "PROJECT_CONTEXT_MARKER"
    assert user_context.read_text() == "USER_CONTEXT_MARKER"
    assert base.read_text() == "BASE_CONTEXT_MARKER\n"


@pytest.mark.parametrize("agent", agents.AGENT_PROFILES)
def test_standalone_expands_user_paths(tmp_path, agent):
    filename = (
        "config.toml" if agent in {"codex", "grok-build"} else
        ".gemini/settings.json" if agent == "gemini-cli" else "mcp.json"
    )
    render(
        tmp_path, agent,
        env={"HOME": str(tmp_path), "USERPROFILE": str(tmp_path)},
        extra=["--output", f"~/expanded/{filename}",
               "--base-instructions", "~/base instructions.md",
               "--instructions-file", "~/selected instructions.md", "--mcp", "~/computer-use"],
    )
    output = tmp_path / "expanded" / filename
    if agent == "codex":
        config = read_toml(output)
        assert config["model_instructions_file"] == str(tmp_path / "base instructions.md")
        server = config["mcp_servers"]["computer_use"]
        assert server["command"] == str(tmp_path / "computer-use")
    elif agent == "claude-code":
        server = json.loads(output.read_text())["mcpServers"]["computer_use"]
        assert server["args"][1] == str(tmp_path / "computer-use")
        assert "SELECTED_INSTRUCTIONS_MARKER" in output.with_suffix(".system-prompt.md").read_text()
    elif agent == "gemini-cli":
        server = json.loads(output.read_text())["mcpServers"]["computer-use"]
        assert server["command"] == str(tmp_path / "computer-use")
        assert "SELECTED_INSTRUCTIONS_MARKER" in output.with_name("GEMINI.md").read_text()
    else:
        assert str(tmp_path / "computer-use") in output.read_text()
        assert "SELECTED_INSTRUCTIONS_MARKER" in output.with_name("system-prompt.md").read_text()


def test_codex_config_quotes_native_paths_and_instruction_text():
    home = 'C:\\Users\\Local User\\.codex'
    instructions = 'Use "AX".\nLiteral backslash: \\ and non-BMP: \U0001f642'
    config = tomllib.loads(agents.codex_config(
        model="gpt-5.5", effort="xhigh", base_url="http://gateway/v1",
        codex_home=home, base_instructions="base.md", mcp_command="computer-use",
        developer_instructions=instructions,
    ))
    assert config["developer_instructions"] == instructions
    assert config["skills"]["config"][0]["path"].startswith(home + "/")


@pytest.mark.parametrize("agent", ["codex", "grok-build"])
def test_toml_config_preserves_non_bmp_windows_paths(agent):
    home = 'C:\\Users\\Desktop \U0001f4bb\\.codex'
    mcp = 'C:\\Users\\Desktop \U0001f4bb\\Computer Use\\computer-use.exe'
    kwargs = dict(model="model", effort="high", base_url="http://gateway/v1",
                  mcp_command=mcp)
    if agent == "codex":
        text = agents.codex_config(
            **kwargs, codex_home=home, base_instructions=home + '\\base.md',
            developer_instructions='Use "AX".\nLiteral backslash: \\; delete: \x7f; \U0001f4bb',
        )
    else:
        text = agents.grok_config(**kwargs)
    config = tomllib.loads(text)
    assert config["mcp_servers"]["computer_use"]["command"] == mcp
    if agent == "codex":
        assert config["model_instructions_file"] == home + '\\base.md'
        assert config["skills"]["config"][0]["path"].startswith(home + "/")
        assert config["developer_instructions"] == (
            'Use "AX".\nLiteral backslash: \\; delete: \x7f; \U0001f4bb'
        )


@pytest.mark.parametrize("agent", ["codex", "grok-build"])
def test_standalone_toml_config_from_unicode_directory(tmp_path, agent):
    directory = tmp_path / 'Computer Use \U0001f4bb'
    directory.mkdir()
    executable = directory / "computer-use.exe"
    output, _, _ = render(directory, agent, extra=["--mcp", str(executable)])
    config = read_toml(output)
    assert config["mcp_servers"]["computer_use"]["command"] == str(executable)


@pytest.mark.parametrize("missing_flag", ["--instructions-file", "--base-instructions"])
def test_missing_instructions_fail_before_overwriting_output(tmp_path, missing_flag):
    # render() reads both files before any per-agent branch.
    agent = "codex"
    output, base, instructions = render(tmp_path, agent)
    previous = {path: path.read_bytes() for path in output.parent.iterdir()}
    result = subprocess.run(
        [sys.executable, "-m", "hcu.render_agent_config", "--agent", agent,
         "--gateway", "http://127.0.0.1:1", "--mcp", sys.executable,
         "--base-instructions", str(base), "--instructions-file", str(instructions),
         missing_flag, str(tmp_path / "missing.md"), "--output", str(output)],
        cwd=ROOT, capture_output=True, text=True, timeout=15,
    )
    assert result.returncode != 0
    assert "missing.md" in result.stderr
    assert {path: path.read_bytes() for path in output.parent.iterdir()} == previous


def assert_rendered_instructions(output, agent, base, instructions):
    if agent == "codex":
        config = read_toml(output)
        assert config.get("developer_instructions", "") == instructions
        assert Path(config["model_instructions_file"]).read_text() == base
        assert all(not row["enabled"] for row in config["skills"]["config"])
    elif agent == "gemini-cli":
        assert output.with_name("GEMINI.md").read_text() == instructions
        assert output.with_name("base-instructions.md").read_text() == base
        settings = json.loads(output.read_text())
        assert settings["skills"]["enabled"] is False
        assert settings["admin"]["skills"]["enabled"] is False
    else:
        prompt = (
            output.with_suffix(".system-prompt.md") if agent == "claude-code"
            else output.with_name("system-prompt.md")
        )
        assert prompt.read_text() == base + ("\n\n" + instructions if instructions else "")


@pytest.mark.parametrize("agent", agents.AGENT_PROFILES)
def test_standalone_injects_plain_text_and_clears_disabled_profile(tmp_path, agent):
    output, base, instructions = render(tmp_path, agent)
    assert_rendered_instructions(output, agent, base.read_text(), instructions.read_text())
    output, base, _ = render(tmp_path, agent, extra=["--instructions-file", ""])
    assert_rendered_instructions(output, agent, base.read_text(), "")
    for path in output.parent.iterdir():
        assert "SELECTED_INSTRUCTIONS_MARKER" not in path.read_text()


@pytest.mark.parametrize("profile", ["hybrid", "ax-only", "gui-only"])
def test_standalone_loads_real_instruction_files(tmp_path, profile):
    # Codex embeds the text in TOML; the other agents copy it verbatim
    # (test_standalone_injects_plain_text_and_clears_disabled_profile).
    agent = "codex"
    base = ROOT / "instructions/base.md"
    instructions = ROOT / "instructions" / f"{profile}.md"
    output, _, _ = render(tmp_path, agent, extra=[
        "--base-instructions", str(base), "--instructions-file", str(instructions),
    ])
    assert_rendered_instructions(output, agent, base.read_text(), instructions.read_text())


def test_claude_rendered_adapter_runs_with_rendering_interpreter(tmp_path):
    # A venv distinguishes the selected interpreter from /usr/bin/python3.
    import venv

    venv.EnvBuilder(with_pip=False, system_site_packages=True).create(tmp_path / "venv")
    python = str(tmp_path / "venv" / (
        "Scripts/python.exe" if os.name == "nt" else "bin/python"
    ))
    output, base, instructions = render(
        tmp_path, "claude-code", python=python,
        extra=["--channels", "a11y,keyboard"],
    )
    server = json.loads(output.read_text())["mcpServers"]["computer_use"]
    assert server["command"] == python
    prompt = output.with_suffix(".system-prompt.md").read_text()
    assert base.read_text() in prompt
    assert instructions.read_text() in prompt
    (tmp_path / "mcp").write_text(
        'import json, os, sys\n'
        'request = json.loads(sys.stdin.readline())\n'
        'assert request["method"] == "tools/list"\n'
        'assert os.environ["COMPUTER_USE_CHANNELS"] == "a11y,keyboard"\n'
        'schema = {"oneOf": [{"type": "object", "properties": '
        '{"mode": {"const": "observe"}}, "required": ["mode"]}]}\n'
        'print(json.dumps({"jsonrpc": "2.0", "id": request["id"], '
        '"result": {"tools": [{"name": "AX", "inputSchema": schema}]}}))\n',
        encoding="utf-8",
    )
    result = subprocess.run(
        [server["command"], *server["args"]], cwd=tmp_path,
        env=dict(os.environ, **server["env"]),
        input='{"jsonrpc":"2.0","id":1,"method":"tools/list"}\n',
        capture_output=True, text=True, timeout=15,
    )
    assert result.returncode == 0, result.stderr
    schema = json.loads(result.stdout)["result"]["tools"][0]["inputSchema"]
    assert "oneOf" not in schema
    assert schema["properties"]["mode"] == {"const": "observe"}


@pytest.mark.parametrize("enabled", [True, False])
@pytest.mark.parametrize("model,effort,gateway,task", [
    ("claude-sonnet-5", "high", "http://localhost:9999/", "task-a"),
    ("claude-opus-5", "max", "http://localhost:9998/prefix", "task-b"),
])
def test_claude_launch_artifact_consumes_configuration(
    tmp_path, monkeypatch, enabled, model, effort, gateway, task
):
    output, base, instructions = render(tmp_path, "claude-code", extra=[
        "--model", model, "--effort", effort, "--gateway", gateway,
        "--task-id", task, "--output", "relative profile/mcp.json",
        *([] if enabled else ["--instructions-file", ""]),
    ])
    output = tmp_path / "relative profile/mcp.json"
    launcher = output.with_suffix(".launch.py")
    captured = []

    def client(command, *, env):
        captured.append((command, env))
        return 7

    monkeypatch.setattr(subprocess, "call", client)
    monkeypatch.setattr(sys, "argv", [str(launcher), "-p", "PROMPT WITH SPACES"])
    monkeypatch.setenv("CLAUDE_CODE_USE_BEDROCK", "1")
    # Absolute file bindings must survive launching from another directory.
    monkeypatch.chdir(ROOT)
    with pytest.raises(SystemExit) as exit_result:
        runpy.run_path(str(launcher), run_name="__main__")
    assert exit_result.value.code == 7
    command, environment = captured[0]
    assert command[0] == "claude"
    assert command[-2:] == ["-p", "PROMPT WITH SPACES"]
    assert command[command.index("--model") + 1] == model
    assert command[command.index("--effort") + 1] == effort
    assert command[command.index("--permission-mode") + 1] == "manual"
    assert "--allowedTools" not in command
    assert "--dangerously-skip-permissions" not in command
    assert environment["ANTHROPIC_BASE_URL"] == gateway.rstrip("/") + "/t/" + task
    assert environment["ANTHROPIC_API_KEY"] == "hcu-task-gateway"
    assert environment["CLAUDE_CODE_USE_BEDROCK"] == "0"
    config = Path(command[command.index("--mcp-config") + 1])
    assert config.is_absolute()
    assert json.loads(config.read_text())["mcpServers"]["computer_use"]
    prompt = Path(command[command.index("--system-prompt-file") + 1])
    assert prompt.is_absolute()
    assert prompt.read_text() == base.read_text() + (
        "\n\n" + instructions.read_text() if enabled else ""
    )


@pytest.mark.parametrize("agent,relative,expected", [
    ("gemini-cli", "profile/mcp.json", "<home>/.gemini/settings.json"),
    ("gemini-cli", "profile/settings.json", "<home>/.gemini/settings.json"),
    ("gemini-cli", "profile/.gemini/mcp.json", "<home>/.gemini/settings.json"),
    ("grok-build", "profile/mcp.json", "<home>/config.toml"),
    ("grok-build", "profile/custom.toml", "<home>/config.toml"),
    ("grok-build", "profile/config.TOML", "<home>/config.toml"),
])
def test_rejects_undiscoverable_output_before_writes(tmp_path, agent, relative, expected):
    output = tmp_path / relative
    result = subprocess.run(
        [sys.executable, "-m", "hcu.render_agent_config",
         "--agent", agent, "--gateway", "http://localhost:1",
         "--mcp", sys.executable, "--output", str(output)],
        cwd=ROOT, capture_output=True, text=True, timeout=15,
    )
    assert result.returncode != 0
    assert expected in result.stderr
    assert not output.parent.exists()
    output.parent.mkdir(parents=True)
    output.write_text("EXISTING_CONFIG")
    result = subprocess.run(
        [sys.executable, "-m", "hcu.render_agent_config",
         "--agent", agent, "--gateway", "http://localhost:1",
         "--mcp", sys.executable, "--output", str(output)],
        cwd=ROOT, capture_output=True, text=True, timeout=15,
    )
    assert result.returncode != 0
    assert output.read_text() == "EXISTING_CONFIG"
    assert list(output.parent.iterdir()) == [output]


@pytest.mark.parametrize("enabled", [True, False])
def test_grok_environment_resolves_generated_config(tmp_path, capsys, monkeypatch, enabled):
    from hcu import render_agent_config

    base = tmp_path / "base.md"
    profile = tmp_path / "profile.md"
    base.write_text("BASE_CONTEXT")
    profile.write_text("PROFILE_CONTEXT")
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(sys, "argv", [
        "render_agent_config", "--agent", "grok-build",
        "--gateway", "http://localhost:1/", "--task-id", "grok-task",
        "--model", "grok-4.7", "--effort", "high",
        "--mcp", sys.executable, "--output", "relative home/config.toml",
        "--base-instructions", str(base),
        "--instructions-file", str(profile) if enabled else "",
    ])
    assert render_agent_config.main() == 0
    lines = capsys.readouterr().out.splitlines()
    environment = json.loads(next(
        line.removeprefix("Grok environment: ") for line in lines
        if line.startswith("Grok environment: ")
    ))
    home = Path(environment["GROK_HOME"])
    assert home.is_absolute()
    output = home / "config.toml"
    assert str(output) == lines[-1]
    config = read_toml(output)
    assert config["models"]["default"] == "gateway"
    assert config["model"]["gateway"]["model"] == "grok-4.7"
    assert config["model"]["gateway"]["base_url"] == "http://localhost:1/t/grok-task/v1"
    assert config["model"]["gateway"]["reasoning_efforts"][0]["value"] == "high"
    assert config["mcp_servers"]["computer_use"]["command"] == sys.executable
    assert_rendered_instructions(
        output, "grok-build", base.read_text(), profile.read_text() if enabled else "",
    )


@pytest.mark.parametrize("enabled", [True, False])
def test_gemini_environment_resolves_generated_files(tmp_path, capsys, monkeypatch, enabled):
    from hcu import render_agent_config

    base = tmp_path / "base.md"
    profile = tmp_path / "profile.md"
    base.write_text("BASE_CONTEXT")
    profile.write_text("PROFILE_CONTEXT")
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(sys, "argv", [
        "render_agent_config", "--agent", "gemini-cli",
        "--gateway", "http://localhost:1/", "--task-id", "gemini-task",
        "--mcp", sys.executable, "--output", "relative home/.gemini/settings.json",
        "--base-instructions", str(base),
        "--instructions-file", str(profile) if enabled else "",
    ])
    assert render_agent_config.main() == 0
    lines = capsys.readouterr().out.splitlines()
    environment = json.loads(next(
        line.removeprefix("Gemini environment: ") for line in lines
        if line.startswith("Gemini environment: ")
    ))
    home = Path(environment["GEMINI_CLI_HOME"])
    assert home.is_absolute()
    config = home / ".gemini/settings.json"
    assert str(config) == lines[-1]
    assert json.loads(config.read_text())["mcpServers"]["computer-use"]
    assert (home / ".gemini/GEMINI.md").read_text() == (
        "PROFILE_CONTEXT" if enabled else ""
    )
    assert Path(environment["GEMINI_SYSTEM_MD"]).read_text() == "BASE_CONTEXT"
    assert json.loads(Path(environment["GEMINI_EXP"]).read_text())
    assert environment["GOOGLE_GEMINI_BASE_URL"] == "http://localhost:1/t/gemini-task"


@pytest.fixture(scope="module")
def gemini_storage_module(tmp_path_factory):
    archive = ROOT / "artifacts/gemini-cli.tgz"
    node = ROOT / "artifacts/node"
    if not archive.is_file() or not node.is_file():
        pytest.skip("Pinned Gemini archive and Node artifact required")
    extracted = tmp_path_factory.mktemp("gemini-consumer")
    storage_module = None
    with tarfile.open(archive) as bundle:
        for member in bundle.getmembers():
            path = Path(member.name)
            if (
                not member.isfile() or path.is_absolute() or ".." in path.parts
                or not member.name.startswith("package/")
                or path.suffix not in (".js", ".json")
            ):
                continue
            target = extracted / path
            target.parent.mkdir(parents=True, exist_ok=True)
            content = bundle.extractfile(member).read()
            target.write_bytes(content)
            if (
                storage_module is None and path.suffix == ".js"
                and b"static getGlobalSettingsPath()" in content
            ):
                storage_module = target
    assert storage_module is not None, "Pinned client storage implementation not found"
    return node, storage_module


@pytest.mark.parametrize("enabled", [True, False])
def test_pinned_gemini_discovers_settings_and_context(
    tmp_path, gemini_storage_module, enabled
):
    node, module = gemini_storage_module
    output, _, instructions = render(
        tmp_path, "gemini-cli", extra=[] if enabled else ["--instructions-file", ""]
    )
    script = """
import fs from 'node:fs';
const {Storage, getGlobalMemoryPaths} = await import(process.argv[1]);
const settings = Storage.getGlobalSettingsPath();
const memory = await getGlobalMemoryPaths();
console.log(JSON.stringify({
  settings, config: JSON.parse(fs.readFileSync(settings, 'utf8')),
  memory: memory.map(path => ({path, text: fs.readFileSync(path, 'utf8')})),
}));
"""
    result = subprocess.run(
        [str(node), "--input-type=module", "-e", script, module.as_uri()],
        cwd=tmp_path,
        env=dict(os.environ, GEMINI_CLI_HOME=str(output.parent.parent)),
        capture_output=True, text=True, timeout=30,
    )
    assert result.returncode == 0, result.stderr
    consumed = json.loads(result.stdout.splitlines()[-1])
    assert Path(consumed["settings"]) == output
    assert consumed["config"]["mcpServers"]["computer-use"]
    assert consumed["memory"] == [{
        "path": str(output.with_name("GEMINI.md")),
        "text": instructions.read_text() if enabled else "",
    }]


@pytest.mark.parametrize("enabled,model,effort", [
    (True, "claude-sonnet-5", "high"),
    (False, "claude-opus-5", "max"),
])
def test_pinned_claude_consumes_launch_configuration(tmp_path, enabled, model, effort):
    binary = Path(os.environ.get("CLAUDE_TEST_BINARY", ROOT / "artifacts/claude-code"))
    if not binary.is_file():
        pytest.skip("Claude artifact absent; set CLAUDE_TEST_BINARY for consumer test")
    requests = []

    class Handler(BaseHTTPRequestHandler):
        def do_POST(self):
            body = self.rfile.read(int(self.headers["Content-Length"]))
            requests.append((self.path, json.loads(body)))
            # Inspect native-client requests locally; never contact a model.
            response = b'{"type":"error","error":{"type":"invalid_request_error","message":"offline consumer test"}}'
            self.send_response(400)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(response)))
            self.end_headers()
            self.wfile.write(response)

        def log_message(self, *args):
            pass

    with HTTPServer(("127.0.0.1", 0), Handler) as endpoint:
        thread = threading.Thread(
            target=endpoint.serve_forever, kwargs={"poll_interval": 0.01}, daemon=True
        )
        thread.start()
        try:
            output, base, instructions = render(tmp_path, "claude-code", extra=[
                "--gateway", f"http://127.0.0.1:{endpoint.server_port}",
                "--task-id", "offline-claude", "--model", model, "--effort", effort,
                *([] if enabled else ["--instructions-file", ""]),
            ])
            launch = runpy.run_path(str(output.with_suffix(".launch.py")))
            result = subprocess.run(
                [str(binary), *launch["command"][1:], "-p",
                 "--no-session-persistence", "OFFLINE_USER_REQUEST"],
                cwd=tmp_path,
                env=dict(os.environ, **launch["environment"],
                         CLAUDE_CONFIG_DIR=str(tmp_path / "claude-home")),
                capture_output=True, text=True, timeout=45,
            )
        finally:
            endpoint.shutdown()
            thread.join(timeout=5)
    assert requests, result.stderr + result.stdout
    path, request = requests[0]
    assert path.startswith("/t/offline-claude/v1/messages")
    assert request["model"] == model
    assert request["output_config"]["effort"] == effort
    prompt = "\n".join(block["text"] for block in request["system"])
    assert base.read_text().strip() in prompt
    assert (instructions.read_text().strip() in prompt) is enabled


@pytest.mark.parametrize("enabled", [True, False])
def test_codex_cli_consumes_instructions_and_existing_context(tmp_path, enabled):
    binary = Path(os.environ.get("CODEX_TEST_BINARY", ROOT / "artifacts/codex"))
    if not binary.is_file():
        pytest.skip("Codex artifact absent; set CODEX_TEST_BINARY for consumer test")
    home = tmp_path / "profile"
    home.mkdir()
    user_context = home / "AGENTS.md"
    user_context.write_text("USER_CONTEXT_MARKER", encoding="utf-8")
    project_context = tmp_path / "AGENTS.md"
    project_context.write_text("PROJECT_CONTEXT_MARKER", encoding="utf-8")
    requests = []

    class Handler(BaseHTTPRequestHandler):
        def do_POST(self):
            body = self.rfile.read(int(self.headers["Content-Length"]))
            requests.append(json.loads(body))
            # Stop after inspecting the request, without model calls or tools.
            response = b'{"error":{"message":"offline consumer test","type":"invalid_request_error"}}'
            self.send_response(400)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(response)))
            self.end_headers()
            self.wfile.write(response)

        def log_message(self, *args):
            pass

    with HTTPServer(("127.0.0.1", 0), Handler) as endpoint:
        thread = threading.Thread(
            target=endpoint.serve_forever, kwargs={"poll_interval": 0.01}, daemon=True
        )
        thread.start()
        try:
            output, base, instructions = render(
                tmp_path, "codex", env={"CODEX_HOME": str(home)},
                extra=["--gateway", f"http://127.0.0.1:{endpoint.server_port}"]
                + ([] if enabled else ["--instructions-file", ""]),
            )
            result = subprocess.run(
                [str(binary), "exec", "--strict-config", "--skip-git-repo-check", "--ephemeral",
                 "--ignore-rules", "-c", "mcp_servers.computer_use.enabled=false",
                 "--json", "OFFLINE_USER_REQUEST"],
                cwd=tmp_path, env=dict(os.environ, CODEX_HOME=str(output.parent)),
                capture_output=True, text=True, timeout=45,
            )
        finally:
            endpoint.shutdown()
            thread.join(timeout=5)
    assert requests, result.stderr
    request = requests[0]
    assert base.read_text().strip() in request["instructions"]
    messages = request["input"]
    developer_messages = json.dumps([
        message for message in messages if message.get("role") == "developer"
    ])
    permissions = "\n".join(
        block["text"]
        for message in messages if message.get("role") == "developer"
        for block in message.get("content", [])
        if "<permissions instructions>" in block.get("text", "")
    )
    assert "`sandbox_mode` is `workspace-write`" in permissions, permissions
    assert "Network access is restricted" in permissions, permissions
    # Native exec disables interactive approvals but must retain the sandbox.
    assert "Approval policy is currently never" in permissions, permissions
    assert "danger-full-access" not in permissions
    assert ("SELECTED_INSTRUCTIONS_MARKER" in developer_messages) is enabled
    assert "USER_CONTEXT_MARKER" in json.dumps(messages)
    assert "PROJECT_CONTEXT_MARKER" in json.dumps(messages)
    assert user_context.read_text() == "USER_CONTEXT_MARKER"
    assert project_context.read_text() == "PROJECT_CONTEXT_MARKER"


def test_codex_code_mode_render_appends_the_code_guide(tmp_path):
    output, _base, instructions = render(tmp_path, "codex", extra=("--code-mode",))
    rendered = read_toml(output)
    assert rendered["features"]["code_mode_only"] is True
    assert rendered["mcp_servers"]["computer_use"]["omit_tools_from"] == ["deferred"]
    guide = (ROOT / "instructions/hybrid-code.md").read_text(encoding="utf-8")
    assert rendered["developer_instructions"] == instructions.read_text(encoding="utf-8") + "\n\n" + guide


def test_code_mode_is_rejected_for_other_agents(tmp_path):
    result = subprocess.run(
        [sys.executable, "-m", "hcu.render_agent_config", "--agent", "claude-code",
         "--gateway", "http://127.0.0.1:1", "--mcp", sys.executable,
         "--output", str(tmp_path / "mcp.json"), "--code-mode"],
        env=dict(os.environ, PYTHONPATH=str(ROOT)), capture_output=True, text=True, timeout=15,
    )
    assert result.returncode == 2 and "--code-mode applies only" in result.stderr
