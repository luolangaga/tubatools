import importlib.util
import json
from pathlib import Path
import sys
from types import ModuleType, SimpleNamespace

import pytest


ROOT = Path(__file__).resolve().parents[1]


@pytest.fixture
def canary_module(monkeypatch):
    # Import the real entrypoint without importing or starting the VM stack.
    dependencies = {
        "hcu.osworld.task_loader": {"load_task_config": None},
        "desktop_env.cache": {"run_scoped_cache_dir": lambda *args: "/unused-cache"},
        "desktop_env.file_source": {"configure": lambda *args, **kwargs: None},
        "desktop_env.desktop_env": {"DesktopEnv": None},
        "desktop_env.providers.volume": {"validate_guest_image_capacity": lambda *args: 1},
    }
    for name, attributes in dependencies.items():
        module = ModuleType(name)
        vars(module).update(attributes)
        monkeypatch.setitem(sys.modules, name, module)
    spec = importlib.util.spec_from_file_location(
        "canary_under_test", ROOT / "hcu/osworld/setup_canary.py",
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@pytest.mark.parametrize("version", ["v1", "v2"])
@pytest.mark.parametrize("outcome", ["disabled", "score", "error"])
def test_canary_uses_environment_evaluator_and_closes_guest(
    canary_module, monkeypatch, tmp_path, capsys, version, outcome,
):
    module = canary_module
    image = tmp_path / "guest.qcow2"
    image.write_bytes(b"mock image")
    fixture = tmp_path / "fixture"
    fixture.write_bytes(b"verifier fixture")
    result_dir = tmp_path / "result"
    events = []
    if version == "v1":
        selected = {"id": "selected", "config": []}
    else:
        selected = SimpleNamespace(id="selected", config=[], volume_size=None)

        def direct_evaluate(env):
            pytest.fail("canary bypassed the environment evaluator")

        selected.evaluate = direct_evaluate

    def load(*args, **kwargs):
        assert kwargs["eval_version"] == version
        assert kwargs["prefer_class"] == (version == "v2")
        return selected

    def preflight():
        events.append("preflight")
        return {}

    def reset(*, task_config):
        assert task_config is selected
        events.append("reset")

    def upload(files):
        assert files == [{"local_path": str(fixture), "path": "/tmp/fixture"}]
        events.append("fixture")

    def evaluate():
        events.append("evaluate")
        if outcome == "error":
            raise RuntimeError("evaluator failed")
        return {"score": 0.5} if version == "v2" else 0.5

    env = SimpleNamespace(
        provider=SimpleNamespace(container=SimpleNamespace(name="mock-container")),
        reset=reset,
        setup_controller=SimpleNamespace(
            assert_root_writable=lambda: None, _upload_file_setup=upload,
        ),
        controller=SimpleNamespace(get_screenshot=lambda: None),
        evaluate=evaluate,
        close=lambda: events.append("close"),
    )

    def create(**kwargs):
        assert events == ["preflight"]
        events.append("create")
        return env

    args = SimpleNamespace(
        guest_image=image, result_dir=result_dir, benchmark_version=version,
        task_id="selected", task_domain=None, run_evaluator=outcome != "disabled",
        evaluator_fixture=[] if outcome == "disabled" else [(fixture, "/tmp/fixture")],
        volume_size=40, ram_size="4G", health_url=[], client_password="mock",
        retain_container=False, asset_cache=tmp_path / "cache", offline_assets=True,
    )
    monkeypatch.chdir(tmp_path)
    monkeypatch.setenv("OSWORLD_DOCKER_RAM_SIZE", "original")
    monkeypatch.setattr(module, "OSWORLD_ROOT", tmp_path)
    monkeypatch.setattr(module, "parse_args", lambda: args)
    monkeypatch.setattr(module, "load_task_config", load)
    monkeypatch.setattr(module, "docker_runtime", preflight)
    monkeypatch.setattr(module, "DesktopEnv", create)
    if outcome == "error":
        with pytest.raises(RuntimeError, match="evaluator failed"):
            module.main()
    else:
        assert module.main() == 0
    report = json.loads((result_dir / "report.json").read_text())
    assert report["status"] == ("failed" if outcome == "error" else "passed")
    expected = ["preflight", "create", "reset"]
    if outcome != "disabled":
        expected.extend(["fixture", "evaluate"])
    assert events == [*expected, "close"]
    if outcome == "score":
        assert report["evaluator_result"] == ({"score": 0.5} if version == "v2" else 0.5)
    else:
        assert "evaluator_result" not in report
    if outcome != "error":
        assert json.loads(capsys.readouterr().out) == report
