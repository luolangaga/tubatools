import argparse
from pathlib import Path

from hcu.paths import anchor_path_args


def test_relative_path_arguments_are_anchored_to_the_caller(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    args = argparse.Namespace(
        result_dir="runs/one",
        guest_image=Path("images/guest.qcow2"),
        token_log="",
        instructions_file="/abs/instructions.md",
    )
    anchor_path_args(args, "result_dir", "guest_image", "token_log", "instructions_file")
    assert args.result_dir == str(tmp_path / "runs/one")
    assert args.guest_image == tmp_path / "images/guest.qcow2"
    assert isinstance(args.guest_image, Path)
    assert args.token_log == ""
    assert args.instructions_file == "/abs/instructions.md"


def test_home_relative_paths_expand_before_anchoring(monkeypatch, tmp_path):
    monkeypatch.setenv("HOME", str(tmp_path))
    args = argparse.Namespace(artifact_dir="~/artifacts")
    anchor_path_args(args, "artifact_dir")
    assert args.artifact_dir == str(tmp_path / "artifacts")
