#!/usr/bin/env python3
from __future__ import annotations

import argparse
import ast
import inspect
import shutil
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]

# These interfaces are consumed by the overlay but supplied by the checkout.
REQUIRED_SYMBOLS = {
    "desktop_env/__init__.py": (),
    "desktop_env/controllers/python.py": ("PythonController.__init__",),
    "desktop_env/providers/__init__.py": ("create_vm_manager_and_provider",),
    "desktop_env/providers/base.py": ("Provider",),
    "desktop_env/providers/docker/manager.py": (),
    "desktop_env/providers/aws/proxy_pool.py": (
        "ProxyInfo", "get_global_proxy_pool", "init_proxy_pool",
    ),
    "desktop_env/task_base.py": ("BaseTask.setup", "BaseTask.evaluate"),
    "desktop_env/file_source.py": ("resolve_local_source",),
    "desktop_env/evaluators/metrics/__init__.py": (),
    "desktop_env/evaluators/metrics/utils.py": ("compare_urls",),
}


def check_call(function: ast.FunctionDef, positional_count: int, keywords: tuple[str, ...]) -> None:
    args = function.args
    parameters = []
    positional = args.posonlyargs + args.args
    required_count = len(positional) - len(args.defaults)
    for index, arg in enumerate(positional):
        kind = (
            inspect.Parameter.POSITIONAL_ONLY if index < len(args.posonlyargs)
            else inspect.Parameter.POSITIONAL_OR_KEYWORD
        )
        default = inspect.Parameter.empty if index < required_count else None
        parameters.append(inspect.Parameter(arg.arg, kind, default=default))
    if args.vararg:
        parameters.append(inspect.Parameter(args.vararg.arg, inspect.Parameter.VAR_POSITIONAL))
    for arg, default in zip(args.kwonlyargs, args.kw_defaults):
        parameters.append(inspect.Parameter(
            arg.arg, inspect.Parameter.KEYWORD_ONLY,
            default=inspect.Parameter.empty if default is None else None,
        ))
    if args.kwarg:
        parameters.append(inspect.Parameter(args.kwarg.arg, inspect.Parameter.VAR_KEYWORD))
    inspect.Signature(parameters).bind(*([None] * positional_count), **dict.fromkeys(keywords))


def installation_plan(checkout: Path, overlay: Path) -> list[tuple[Path, Path]]:
    symbols = {}
    for relative, names in REQUIRED_SYMBOLS.items():
        path = checkout / relative
        if not path.resolve().is_relative_to(checkout):
            raise ValueError(f"required module escapes checkout: {relative}")
        tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        for name in names:
            node = tree
            for part in name.split("."):
                node = next((
                    child for child in node.body
                    if isinstance(child, (ast.ClassDef, ast.FunctionDef)) and child.name == part
                ), None)
                if node is None:
                    raise ValueError(f"{relative} lacks required interface {name}")
            symbols[name] = node
    for name, positional_count, keywords in (
        ("PythonController.__init__", 1, ("vm_ip", "server_port")),
        ("create_vm_manager_and_provider", 2, ("use_proxy", "use_public_ip")),
    ):
        function = symbols[name]
        if not isinstance(function, ast.FunctionDef):
            raise ValueError(f"{name} must be a synchronous function")
        check_call(function, positional_count, keywords)

    plan = []
    for source in sorted(overlay.rglob("*.py")):
        if "__pycache__" in source.parts:
            continue
        ast.parse(source.read_text(encoding="utf-8"), filename=str(source))
        destination = checkout / source.relative_to(overlay)
        if not destination.resolve().is_relative_to(checkout):
            raise ValueError(f"overlay destination escapes checkout: {destination}")
        if destination.exists() and not destination.is_file():
            raise ValueError(f"overlay destination is not a file: {destination}")
        plan.append((source, destination))
    if not plan:
        raise ValueError("overlay contains no Python source files")
    return plan


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("osworld_v2_root", type=Path)
    args = parser.parse_args()
    checkout = args.osworld_v2_root.expanduser().resolve()
    overlay = ROOT / "osworld-overlay"
    try:
        plan = installation_plan(checkout, overlay)
    except (OSError, SyntaxError, TypeError, ValueError) as exc:
        parser.error(f"incompatible OSWorld V2 checkout or overlay: {exc}")
    for source, destination in plan:
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
    print(f"OSWorld V2 runtime overlay installed into {checkout}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
