#!/usr/bin/env python3
"""Refresh Chrome repository signing keys without upgrading the browser."""

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile


KEY_URL = "https://dl.google.com/linux/linux_signing_key.pub"
SOURCE = Path("/etc/apt/sources.list.d/google-chrome.list")
KEYRING = Path("/etc/apt/keyrings/google-chrome.gpg")
REPOSITORY = re.compile(
    r"^(?P<indent>\s*)(?P<kind>deb(?:-src)?)\s+"
    r"(?:\[(?P<options>[^\]]*)\]\s+)?"
    r"https?://dl\.google\.com/linux/chrome/deb/?\s+stable\s+main"
    r"(?P<suffix>\s*(?:#.*)?)$")


def bind_source(text, keyring):
    """Preserve other sources while scoping each active Chrome entry."""
    lines = []
    matches = 0
    for line in text.splitlines(keepends=True):
        match = REPOSITORY.fullmatch(line.rstrip("\n"))
        if match:
            options = [option for option in (match["options"] or "").split()
                       if option.split("=", 1)[0] not in ("signed-by", "trusted")]
            options.append("signed-by=" + str(keyring))
            line = (match["indent"] + match["kind"] + " [" + " ".join(options) + "] "
                    "https://dl.google.com/linux/chrome/deb/ stable main" + match["suffix"] + "\n")
            matches += 1
        elif not line.lstrip().startswith("#") and "dl.google.com/linux/chrome/deb" in line:
            raise ValueError("Unrecognized active Chrome repository entry")
        lines.append(line)
    if not matches:
        raise ValueError("No active Chrome repository entry in the selected source file")
    return "".join(lines)


def run(args, timeout=60):
    result = subprocess.run(args, capture_output=True, text=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError(f"{args[0]} failed ({result.returncode}):\n{result.stdout}\n{result.stderr}")
    return result


def browser_version():
    return run(["dpkg-query", "-W", "-f=${Version}", "google-chrome-stable"]).stdout.strip()


def replace_file(path, contents):
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent, prefix=path.name + ".", delete=False) as stream:
        temporary = Path(stream.name)
        try:
            stream.write(contents)
            stream.flush()
            os.fchmod(stream.fileno(), 0o644)
            temporary.replace(path)
        finally:
            temporary.unlink(missing_ok=True)


def repair(source=SOURCE, keyring=KEYRING):
    if os.geteuid() != 0:
        raise RuntimeError("Run as root inside the guest")
    updated = bind_source(source.read_text(), keyring)
    before = browser_version()
    with tempfile.TemporaryDirectory(prefix="chrome-repository-") as directory:
        root = Path(directory)
        public_key = root / "google.pub"
        binary_key = root / "google.gpg"
        run(["curl", "--fail", "--silent", "--show-error", "--location",
             "--proto", "=https", "--proto-redir", "=https", "--retry", "2",
             "--connect-timeout", "15", "--max-time", "90",
             "--output", str(public_key), KEY_URL], timeout=300)
        run(["gpg", "--batch", "--homedir", str(root), "--dearmor",
             "--output", str(binary_key), str(public_key)])
        metadata = run(["gpg", "--batch", "--homedir", str(root),
                        "--with-colons", "--show-keys", str(binary_key)]).stdout
        key_ids = [line.split(":")[4] for line in metadata.splitlines()
                   if line.startswith(("pub:", "sub:"))]
        if not key_ids:
            raise RuntimeError("The official signing-key response contains no OpenPGP public keys")
        replace_file(keyring, binary_key.read_bytes())
    replace_file(source, updated.encode())
    result = run(["apt-get", "update", "-o", "Dir::Etc::sourcelist=" + str(source),
                  "-o", "Dir::Etc::sourceparts=-", "-o", "APT::Get::List-Cleanup=0",
                  "-o", "APT::Update::Error-Mode=any"], timeout=300)
    after = browser_version()
    if before != after:
        raise RuntimeError("Chrome's installed version changed during repository maintenance")
    return {"source": str(source), "keyring": str(keyring), "key_ids": key_ids,
            "chrome_version_before": before, "chrome_version_after": after,
            "apt_update": {"stdout": result.stdout, "stderr": result.stderr}}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=SOURCE)
    args = parser.parse_args()
    print(json.dumps(repair(args.source), indent=2))
