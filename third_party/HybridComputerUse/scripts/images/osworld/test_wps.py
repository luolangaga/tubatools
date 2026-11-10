"""Check selected package validation and installation boundaries without apt."""

from pathlib import Path
import os
import stat
import tempfile
import unittest
from unittest import mock

import install_wps


class WPSInstallerTests(unittest.TestCase):
    def setUp(self):
        self.metadata = {"Package": "wps-office", "Version": "selected", "Architecture": "amd64"}
        self.manifest = {"package": self.metadata}

    def test_rejects_mutable_download_with_another_version(self):
        with mock.patch.object(install_wps, "run", side_effect=["wps-office", "newer", "amd64"]):
            with self.assertRaisesRegex(ValueError, "metadata differs"):
                install_wps.inspect_package(Path("wps.deb"), self.manifest)

    def test_check_runs_only_dependency_simulation(self):
        with tempfile.NamedTemporaryFile(suffix=".deb") as package, \
                mock.patch.object(install_wps, "inspect_package", return_value=self.metadata), \
                mock.patch.object(install_wps, "run", side_effect=["", "dependency plan"]) as run:
            result = install_wps.install(Path(package.name), self.manifest, check=True)
            self.assertFalse(result["installation_performed"])
            self.assertIn("--simulate", run.call_args_list[-1].args[0])
            self.assertIn("--no-remove", run.call_args_list[-1].args[0])
            self.assertEqual(run.call_count, 2)

    def test_installed_version_must_match_selection(self):
        with tempfile.NamedTemporaryFile(suffix=".deb") as package, \
                mock.patch.object(install_wps, "inspect_package", return_value=self.metadata), \
                mock.patch.object(install_wps.os, "geteuid", return_value=0), \
                mock.patch.object(install_wps, "snapshot_mime_files", return_value={}), \
                mock.patch.object(install_wps, "run", side_effect=[
                    "", "dependency plan", "installed", "", "different\tinstall ok installed"]):
            with self.assertRaisesRegex(RuntimeError, "selected installed state"):
                install_wps.install(Path(package.name), self.manifest)

    def test_successful_install_configures_accessibility(self):
        with tempfile.NamedTemporaryFile(suffix=".deb") as package, \
                mock.patch.object(install_wps, "inspect_package", return_value=self.metadata), \
                mock.patch.object(install_wps.os, "geteuid", return_value=0), \
                mock.patch.object(install_wps, "snapshot_mime_files", return_value={}), \
                mock.patch.object(install_wps, "run", side_effect=[
                    "", "dependency plan", "installed", "", "selected\tinstall ok installed"]), \
                mock.patch.object(install_wps.configure_wps_accessibility, "configure") as configure:
            result = install_wps.install(Path(package.name), self.manifest)
            configure.assert_called_once_with()
            self.assertEqual(result["accessibility"], configure.return_value)

    def test_restores_bytes_permissions_and_absent_files(self):
        with tempfile.TemporaryDirectory() as directory:
            existing = Path(directory) / "mimeapps.list"
            new = Path(directory) / "new-mimeapps.list"
            original = b"# preserve formatting\n[Default Applications]\ntext/plain = editor.desktop;\n"
            existing.write_bytes(original)
            existing.chmod(0o640)
            with mock.patch.object(install_wps, "mime_paths", return_value=[existing, new]):
                snapshot = install_wps.snapshot_mime_files()
            existing.write_text("[Default Applications]\ntext/plain=wps.desktop\n")
            existing.chmod(0o666)
            new.write_text("created by package scripts")
            install_wps.restore_mime_files(snapshot)
            self.assertEqual(existing.read_bytes(), original)
            self.assertEqual(stat.S_IMODE(existing.stat().st_mode), 0o640)
            self.assertEqual((existing.stat().st_uid, existing.stat().st_gid),
                             (snapshot[existing]["uid"], snapshot[existing]["gid"]))
            self.assertFalse(new.exists())

    def test_restores_mime_preferences_when_package_installation_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "package.deb"
            package.touch()
            preferences = Path(directory) / "mimeapps.list"
            preferences.write_bytes(b"original preferences\n")
            def command(args, **kwargs):
                if "-y" in args:
                    preferences.write_text("modified before failure")
                    preferences.chmod(0o666)
                    raise RuntimeError("package installation failed")
                return ""
            preferences.chmod(0o600)
            with mock.patch.object(install_wps, "inspect_package", return_value=self.metadata), \
                    mock.patch.object(install_wps, "run", side_effect=command), \
                    mock.patch.object(install_wps.os, "geteuid", return_value=0), \
                    mock.patch.object(install_wps, "mime_paths", return_value=[preferences]):
                with self.assertRaisesRegex(RuntimeError, "package installation failed"):
                    install_wps.install(package, self.manifest)
            self.assertEqual(preferences.read_bytes(), b"original preferences\n")
            self.assertEqual(stat.S_IMODE(preferences.stat().st_mode), 0o600)

    def test_restores_symlink_and_original_target_contents(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "preferences.list"
            target.write_text("original target\n")
            link = Path(directory) / "mimeapps.list"
            link.symlink_to(target.name)
            with mock.patch.object(install_wps, "mime_paths", return_value=[link]):
                snapshot = install_wps.snapshot_mime_files()
            link.write_text("package update")
            link.unlink()
            link.write_text("replaced link")
            install_wps.restore_mime_files(snapshot)
            self.assertTrue(link.is_symlink())
            self.assertEqual(os.readlink(link), target.name)
            self.assertEqual(target.read_text(), "original target\n")


if __name__ == "__main__":
    unittest.main()
