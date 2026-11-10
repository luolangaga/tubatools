import importlib.util
import io
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import zipfile

import pytest


ROOT = Path(__file__).resolve().parents[1]


def load_script(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / "scripts" / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def write_file(path, text, executable=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    if executable:
        path.chmod(0o755)


@pytest.fixture
def installer(tmp_path, monkeypatch):
    module = load_script("install-osworld-v2")
    source = tmp_path / "source"
    shutil.copytree(ROOT / "osworld-overlay", source / "osworld-overlay")
    checkout = tmp_path / "upstream without git"
    files = {
        "__init__.py": "",
        "controllers/python.py": "class PythonController:\n    def __init__(self, vm_ip, server_port=5000): pass\n",
        "providers/__init__.py": "def create_vm_manager_and_provider(provider_name, region, use_proxy=False, use_public_ip=False): pass\n",
        "providers/base.py": "class Provider: pass\n",
        "providers/docker/manager.py": "class DockerManager: pass\n",
        "providers/aws/proxy_pool.py": "class ProxyInfo: pass\ndef get_global_proxy_pool(): pass\ndef init_proxy_pool(): pass\n",
        "task_base.py": "class BaseTask:\n    def setup(self, env): pass\n    def evaluate(self, env): pass\n",
        "file_source.py": "def resolve_local_source(source): pass\n",
        "evaluators/metrics/__init__.py": "",
        "evaluators/metrics/utils.py": "def compare_urls(a, b): pass\n",
        "desktop_env.py": "# original environment\n",
    }
    for name, text in files.items():
        write_file(checkout / "desktop_env" / name, text)
    monkeypatch.setattr(module, "ROOT", source)
    monkeypatch.setattr(sys, "argv", ["install-osworld-v2.py", str(checkout)])
    return module, checkout, source / "osworld-overlay"


def test_installer_accepts_compatible_source_without_git(installer):
    module, checkout, overlay = installer
    assert not (checkout / ".git").exists()
    original = (checkout / "desktop_env/controllers/python.py").read_bytes()
    assert module.main() == 0
    for source in overlay.rglob("*.py"):
        assert (checkout / source.relative_to(overlay)).read_bytes() == source.read_bytes()
    assert (checkout / "desktop_env/controllers/python.py").read_bytes() == original


def test_installer_matches_positional_factory_and_keyword_controller_calls(installer):
    module, checkout, _ = installer
    (checkout / "desktop_env/providers/__init__.py").write_text(
        "def create_vm_manager_and_provider(name, location, /, *, use_proxy, use_public_ip): pass\n"
    )
    (checkout / "desktop_env/controllers/python.py").write_text(
        "class PythonController:\n    def __init__(self, *, vm_ip, server_port): pass\n"
    )
    assert module.main() == 0


@pytest.mark.parametrize("fault", ["missing", "syntax", "symbol", "signature", "extra_required", "overlay", "escape"])
def test_installer_rejects_incompatible_input_before_any_copy(installer, fault, tmp_path):
    module, checkout, overlay = installer
    controller = checkout / "desktop_env/controllers/python.py"
    if fault == "missing":
        controller.unlink()
    elif fault == "syntax":
        controller.write_text("class (", encoding="utf-8")
    elif fault == "symbol":
        controller.write_text("class Unrelated: pass\n", encoding="utf-8")
    elif fault == "signature":
        controller.write_text("class PythonController:\n    def __init__(self, host): pass\n")
    elif fault == "extra_required":
        factory = checkout / "desktop_env/providers/__init__.py"
        factory.write_text("def create_vm_manager_and_provider(provider_name, region, token, **kwargs): pass\n")
    elif fault == "overlay":
        write_file(overlay / "desktop_env/zzz.py", "def broken(")
    else:
        outside = tmp_path / "outside.py"
        outside.write_text("do not overwrite\n")
        (checkout / "desktop_env/zzz.py").symlink_to(outside)
        write_file(overlay / "desktop_env/zzz.py", "# replacement\n")
    original = (checkout / "desktop_env/desktop_env.py").read_bytes()
    with pytest.raises(SystemExit) as raised:
        module.main()
    assert raised.value.code == 2
    assert (checkout / "desktop_env/desktop_env.py").read_bytes() == original
    assert not (checkout / "desktop_env/cache.py").exists()
    if fault == "escape":
        assert outside.read_text() == "do not overwrite\n"


def test_installer_has_no_force_bypass(installer, monkeypatch):
    module, checkout, _ = installer
    monkeypatch.setattr(sys, "argv", ["install-osworld-v2.py", str(checkout), "--force"])
    with pytest.raises(SystemExit) as raised:
        module.main()
    assert raised.value.code == 2
    assert not (checkout / "desktop_env/cache.py").exists()










