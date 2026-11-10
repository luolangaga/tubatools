from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import configure_updates
import install


class UpdateNotificationTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)

    def test_absent_store_does_not_change_notifications(self):
        with patch.object(configure_updates.subprocess, "run") as run:
            result = configure_updates.configure(self.root, self.root / "home")
        self.assertFalse(result["snap_store_present"])
        run.assert_not_called()

    def test_only_store_banners_and_automatic_downloads_change(self):
        entries = ["snap-store_ubuntu-software.desktop",
                   "snap-store_ubuntu-software-local-file.desktop"]
        for name in entries + ["firefox_firefox.desktop"]:
            (self.root / name).write_text("[Desktop Entry]\nName=Software\n")
        with patch.object(configure_updates.subprocess, "run",
                          return_value=SimpleNamespace(stdout="false\n")) as run:
            first = configure_updates.configure(self.root, self.root / "home")
            second = configure_updates.configure(self.root, self.root / "home")
        self.assertEqual(first, second)
        self.assertEqual(set(first["notification_applications"]), set(entries + ["io.snapcraft.Store.desktop"]))
        alias = self.root / "home/.local/share/applications/io.snapcraft.Store.desktop"
        self.assertIn("NoDisplay=true", alias.read_text())
        self.assertNotIn("NoDisplay", (self.root / entries[0]).read_text())
        commands = [call.args[0] for call in run.call_args_list]
        settings = [command for command in commands if command[:2] == ["gsettings", "set"]]
        self.assertEqual(len(settings), 6)
        self.assertTrue(all(command[-2:] == ["show-banners", "false"] for command in settings))
        self.assertTrue(all("snap-store-ubuntu-software" in command[2] or
                            "io-snapcraft-store" in command[2] for command in settings))
        snap_commands = [command[-1] for command in commands if command[0] == "snap"]
        self.assertEqual(set(snap_commands), {
            "gsettings set org.gnome.software download-updates false",
            "gsettings get org.gnome.software download-updates",
            "gsettings set org.gnome.software download-updates-notify false",
            "gsettings get org.gnome.software download-updates-notify",
        })

    def test_failed_readback_stops_configuration(self):
        (self.root / "snap-store_ubuntu-software.desktop").write_text("[Desktop Entry]\nName=Software\n")
        with patch.object(configure_updates.subprocess, "run",
                          return_value=SimpleNamespace(stdout="true\n")):
            with self.assertRaisesRegex(RuntimeError, "did not persist"):
                configure_updates.configure(self.root, self.root / "home")

    def test_preferences_installer_arguments_reach_cli_parser(self):
        desktop_home = self.root / "desktop user"
        user = SimpleNamespace(pw_name="desktop-user", pw_dir=str(desktop_home))
        with patch.object(install, "run") as run:
            install.install_phase("preferences", {}, self.root / "manifest.json",
                                  self.root, user)
        commands = [call.args[0] for call in run.call_args_list]
        updates_script = str(install.ROOT / "configure_updates.py")
        updates_commands = [command for command in commands if updates_script in command]
        self.assertEqual(updates_commands, [[
            "runuser", "-u", user.pw_name, "--", "python3", updates_script,
            "--home", str(desktop_home),
        ]])
        command = updates_commands[0]
        with patch.object(configure_updates.os, "getuid", return_value=1000), \
                patch.object(configure_updates, "configure", return_value={}) as configure:
            configure_updates.main(command[6:])
        configure.assert_called_once_with(home=desktop_home)


if __name__ == "__main__":
    unittest.main()
