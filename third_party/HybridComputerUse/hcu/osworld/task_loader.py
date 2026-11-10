# SPDX-License-Identifier: Apache-2.0
# Modified by the Hybrid Computer Use authors from task_loader.py
# in xlang-ai/OSWorld-V2 (revision d578d2d4). See THIRD_PARTY_NOTICES.md.

from __future__ import annotations

import importlib.util
import json
import os
import sys
from pathlib import Path
from typing import Any, Optional

from desktop_env.task_base import BaseTask


def _normalize_examples_root(base_dir: Optional[str]) -> Optional[str]:
    if not base_dir:
        return None

    root = os.path.abspath(base_dir)
    basename = os.path.basename(root)
    parent_basename = os.path.basename(os.path.dirname(root))

    if basename in {"examples", "examples_v2", "examples_windows", "task_class"}:
        return os.path.dirname(root)
    if basename == "tasks" and parent_basename == "examples_v2":
        return os.path.dirname(os.path.dirname(root))
    if basename == "examples_v2_backup" and parent_basename == "examples":
        return os.path.dirname(os.path.dirname(root))
    return root


def _load_task_module(task_path: str) -> Any:
    module_name = f"osworld_task_{os.path.splitext(os.path.basename(task_path))[0]}"
    spec = importlib.util.spec_from_file_location(module_name, task_path)
    if spec is None or spec.loader is None:
        raise ImportError(f"Unable to load task module from {task_path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = module
    spec.loader.exec_module(module)
    return module


def _instantiate_task_from_module(module: Any) -> Any:
    if hasattr(module, "get_task") and callable(module.get_task):
        return module.get_task()
    if hasattr(module, "TASK_CLASS"):
        return module.TASK_CLASS()
    if hasattr(module, "Task") and callable(module.Task):
        return module.Task()
    for value in module.__dict__.values():
        if isinstance(value, type) and issubclass(value, BaseTask) and value is not BaseTask:
            return value()
    raise ValueError("No task class found in module. Expected get_task(), TASK_CLASS, or BaseTask subclass.")


def load_task_from_file(task_path: str) -> Any:
    if task_path.endswith(".py"):
        module = _load_task_module(task_path)
        return _instantiate_task_from_module(module)
    with open(task_path, "r", encoding="utf-8") as f:
        return json.load(f)


def find_task_class_path(
    task_id: Optional[str],
    base_dir: Optional[str],
    domain: Optional[str] = None,
    eval_version: Optional[str] = None,
) -> Optional[str]:
    if not task_id or not base_dir:
        return None

    candidates = []
    root = _normalize_examples_root(base_dir)
    if not root:
        return None

    def add_candidate(path: str) -> None:
        if path not in candidates:
            candidates.append(path)

    if domain:
        add_candidate(os.path.join(root, "task_class", domain, f"task_{task_id}.py"))
        add_candidate(os.path.join(root, "task_class", domain, f"{task_id}.py"))

    add_candidate(os.path.join(root, "task_class", f"task_{task_id}.py"))
    add_candidate(os.path.join(root, "task_class", f"{task_id}.py"))

    for candidate in candidates:
        if os.path.exists(candidate):
            return candidate

    return None


def resolve_task_json_path(
    task_id: Optional[str],
    base_dir: Optional[str],
    domain: Optional[str] = None,
    eval_version: Optional[str] = None,
) -> Optional[str]:
    if not task_id or not base_dir:
        return None

    root = _normalize_examples_root(base_dir)
    if not root:
        return None

    candidates: list[str] = []

    def add_candidate(path: str) -> None:
        if path not in candidates:
            candidates.append(path)

    if eval_version == "v2":
        add_candidate(os.path.join(root, "examples", "examples_v2_backup", f"{task_id}.json"))
        if domain:
            add_candidate(os.path.join(root, "examples_v2", domain, f"{task_id}.json"))
        add_candidate(os.path.join(root, "examples_v2", "tasks", f"{task_id}.json"))
    elif eval_version == "windows":
        if domain:
            add_candidate(os.path.join(root, "examples_windows", domain, f"{task_id}.json"))
    elif eval_version == "v1":
        if domain:
            add_candidate(os.path.join(root, "examples", domain, f"{task_id}.json"))
        else:
            matches = sorted(
                Path(root).glob(f"examples/*/{task_id}.json")
            )
            if len(matches) == 1:
                add_candidate(str(matches[0]))
            elif len(matches) > 1:
                raise ValueError(
                    f"Task id {task_id!r} is ambiguous across V1 domains: "
                    + ", ".join(str(path) for path in matches)
                )
    else:
        if domain:
            add_candidate(os.path.join(root, "examples", domain, f"{task_id}.json"))
            add_candidate(os.path.join(root, "examples_windows", domain, f"{task_id}.json"))
            add_candidate(os.path.join(root, "examples_v2", domain, f"{task_id}.json"))
        add_candidate(os.path.join(root, "examples", "examples_v2_backup", f"{task_id}.json"))
        add_candidate(os.path.join(root, "examples_v2", "tasks", f"{task_id}.json"))

    for candidate in candidates:
        if os.path.exists(candidate):
            return candidate

    if eval_version == "v2":
        return os.path.join(root, "examples", "examples_v2_backup", f"{task_id}.json")
    if eval_version == "windows" and domain:
        return os.path.join(root, "examples_windows", domain, f"{task_id}.json")
    if domain:
        return os.path.join(root, "examples", domain, f"{task_id}.json")
    return None


def load_task_config(
    config_file: Optional[str],
    task_id: Optional[str] = None,
    base_dir: Optional[str] = None,
    domain: Optional[str] = None,
    eval_version: Optional[str] = None,
    prefer_class: bool = True,
) -> Any:
    class_path = None
    if prefer_class:
        class_path = find_task_class_path(task_id, base_dir, domain, eval_version)
        if class_path:
            return load_task_from_file(class_path)

    resolved_config_file = config_file
    if (not resolved_config_file or not os.path.exists(resolved_config_file)) and task_id and base_dir:
        resolved_config_file = resolve_task_json_path(task_id, base_dir, domain, eval_version)

    if not resolved_config_file or not os.path.exists(resolved_config_file):
        raise FileNotFoundError(resolved_config_file or config_file or "<no config file>")

    return load_task_from_file(resolved_config_file)
