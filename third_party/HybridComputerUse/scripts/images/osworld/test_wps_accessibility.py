"""Check WPS startup configuration with real shell launches, without a desktop."""

import os
from pathlib import Path
import subprocess
import tempfile
import unittest

import configure_wps_accessibility as accessibility


class WPSAccessibilityTests(unittest.TestCase):
    def prepare(self, root):
        # A child shell proves that the setting is exported, not just assigned.
        child = root / "child"
        child.write_text('printf "%s\\0" "$QT_LINUX_ACCESSIBILITY_ALWAYS_ON" "$@"; exit 23\n')
        original = f'#!/bin/bash\n\nexec /bin/bash "{child}" "$@"\n'.encode()
        for name in accessibility.APPLICATIONS:
            path = root / name
            path.write_bytes(original)
            path.chmod(0o751)
        return original

    def test_path_and_desktop_launches_keep_arguments_output_exit_and_permissions(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            original = self.prepare(root)
            environment = {**os.environ, "PATH": str(root),
                           "QT_LINUX_ACCESSIBILITY_ALWAYS_ON": "0"}
            arguments = ["a document.pptx", "", "--flag", "$(literal)", "文档"]
            baseline = subprocess.run([str(root / "wps"), *arguments], env=environment,
                                      capture_output=True)
            self.assertEqual(accessibility.configure(root)["updated"], 3)
            self.assertEqual(accessibility.configure(root)["updated"], 0)
            for name in accessibility.APPLICATIONS:
                path = root / name
                self.assertEqual(path.stat().st_mode & 0o777, 0o751)
                self.assertEqual(path.read_bytes().replace(accessibility.EXPORT, b"\n", 1), original)
                for command in (name, str(path)):
                    with self.subTest(command=command):
                        result = subprocess.run([command, *arguments], env=environment,
                                                capture_output=True)
                        self.assertEqual(result.returncode, baseline.returncode)
                        self.assertEqual(result.stderr, baseline.stderr)
                        self.assertEqual(result.stdout, b"1\0" + baseline.stdout[2:])

    def test_rejects_last_invalid_launcher_before_changing_any_entry(self):
        for invalid in (b"#!/bin/sh\nexit 0\n", b"#!/bin/bash\nexport QT_LINUX_ACCESSIBILITY_ALWAYS_ON=0\n"):
            with self.subTest(invalid=invalid), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                self.prepare(root)
                (root / "et").write_bytes(invalid)
                before = {name: (root / name).read_bytes() for name in accessibility.APPLICATIONS}
                with self.assertRaises(RuntimeError):
                    accessibility.configure(root)
                self.assertEqual(before, {name: (root / name).read_bytes() for name in before})

    def test_rejects_symlink_and_missing_or_nonexecutable_launcher(self):
        for kind in ("symlink", "missing", "nonexecutable"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                original = self.prepare(root)
                path = root / "et"
                if kind == "nonexecutable":
                    path.chmod(0o644)
                else:
                    path.unlink()
                    if kind == "symlink":
                        path.symlink_to(root / "wps")
                with self.assertRaises(RuntimeError):
                    accessibility.configure(root)
                self.assertEqual((root / "wps").read_bytes(), original)


if __name__ == "__main__":
    unittest.main()
