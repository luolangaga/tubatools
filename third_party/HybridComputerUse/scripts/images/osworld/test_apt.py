"""Check cache isolation configuration without changing the host."""

from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

import configure_apt


class AptConfigurationTests(unittest.TestCase):
    @unittest.skipUnless(shutil.which("apt-config"), "APT configuration parser is unavailable")
    def test_apt_reads_empty_binary_cache_paths_and_keeps_archive_cache(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "apt.conf"
            configure_apt.configure(path)
            configure_apt.configure(path)
            output = subprocess.check_output([
                "apt-config", "-c", str(path), "shell",
                "PKG", "Dir::Cache::pkgcache", "SRC", "Dir::Cache::srcpkgcache",
                "ARCHIVES", "Dir::Cache::archives"], text=True)
            self.assertIn("PKG=''", output)
            self.assertIn("SRC=''", output)
            self.assertIn("ARCHIVES='archives/'", output)
            self.assertEqual(path.stat().st_mode & 0o777, 0o644)

    def test_conflicting_configuration_is_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "apt.conf"
            path.write_text('Dir::Cache::pkgcache "custom.bin";\n')
            before = path.read_text()
            with self.assertRaisesRegex(RuntimeError, "already occupied"):
                configure_apt.configure(path)
            self.assertEqual(path.read_text(), before)

    def test_symlink_is_rejected_without_modifying_target(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / "other.conf"
            target.write_text(configure_apt.CONFIG)
            path = root / "apt.conf"
            path.symlink_to(target)
            with self.assertRaisesRegex(RuntimeError, "already occupied"):
                configure_apt.configure(path)
            self.assertEqual(target.read_text(), configure_apt.CONFIG)
            self.assertTrue(path.is_symlink())


if __name__ == "__main__":
    unittest.main()
