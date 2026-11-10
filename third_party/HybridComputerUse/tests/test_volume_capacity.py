import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace

import pytest


ROOT = Path(__file__).resolve().parents[1]


@pytest.fixture
def volume():
    spec = importlib.util.spec_from_file_location(
        "volume_under_test",
        ROOT / "osworld-overlay/desktop_env/providers/volume.py",
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@pytest.mark.parametrize("required", [None, 40, 64, "64"])
def test_capacity_accepts_requirements_within_existing_image(
    volume, monkeypatch, tmp_path, required
):
    image = tmp_path / "guest.qcow2"
    image.touch()
    calls = []

    def inspect(command, **kwargs):
        calls.append(command)
        return SimpleNamespace(stdout=json.dumps({"virtual-size": 64 * 1024**3}))

    monkeypatch.setattr(volume.subprocess, "run", inspect)
    assert volume.validate_guest_image_capacity(image, required) == 64
    assert calls == [["qemu-img", "info", "--output=json", str(image)]]


def test_capacity_rejects_requirement_larger_than_image(volume, monkeypatch, tmp_path):
    image = tmp_path / "guest.qcow2"
    image.touch()
    monkeypatch.setattr(
        volume.subprocess, "run",
        lambda *args, **kwargs: SimpleNamespace(
            stdout=json.dumps({"virtual-size": 64 * 1024**3})
        ),
    )
    with pytest.raises(ValueError, match="requested=100GiB image=64GiB"):
        volume.validate_guest_image_capacity(image, 100)


@pytest.mark.parametrize("required", [0, -1])
def test_invalid_volume_size_fails_before_inspection(volume, monkeypatch, required):
    def unexpected_inspection(*args, **kwargs):
        pytest.fail("Invalid size must fail before inspecting the image")

    monkeypatch.setattr(volume.subprocess, "run", unexpected_inspection)
    with pytest.raises(ValueError, match="must be positive"):
        volume.validate_guest_image_capacity("unused.qcow2", required)
