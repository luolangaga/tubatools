"""Check source repairs and profile preservation without changing the host."""

import configparser
import io
import json
from pathlib import Path
import tempfile
import tarfile
import unittest
import xml.etree.ElementTree as ET
from unittest import mock

import configure_desktop
import configure_editor
import install
import install_qt
import install_libreoffice


class SetupTests(unittest.TestCase):
    def test_freecad_resources_are_readable_after_relocation_and_repeat(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            destination = root / "private"
            page = destination / "usr/share/freecad/Mod/Start/StartPage/StartPage.html"
            page.parent.mkdir(parents=True)
            page.write_text("Start page")
            resource_path = root / "freecad"
            for _ in range(2):
                install_qt.configure_freecad_resources(destination, resource_path)
                self.assertEqual((resource_path / "Mod/Start/StartPage/StartPage.html").read_text(), "Start page")

    def test_freecad_resources_preserve_conflicting_paths(self):
        for kind in ("directory", "file", "symlink", "dangling_symlink"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                destination = root / "private"
                page = destination / "usr/share/freecad/Mod/Start/StartPage/StartPage.html"
                page.parent.mkdir(parents=True)
                page.write_text("Start page")
                conflict = root / "freecad"
                if kind == "directory":
                    conflict.mkdir()
                elif kind == "file":
                    conflict.write_text("keep")
                else:
                    target = root / "other"
                    if kind == "symlink":
                        target.mkdir()
                    conflict.symlink_to(target)
                with self.assertRaisesRegex(RuntimeError, "already occupied"):
                    install_qt.configure_freecad_resources(destination, conflict)
                if kind.endswith("symlink"):
                    self.assertEqual(conflict.readlink(), target)
                elif kind == "file":
                    self.assertEqual(conflict.read_text(), "keep")
                else:
                    self.assertTrue(conflict.is_dir())

    def test_freecad_incomplete_resources_do_not_create_link(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with self.assertRaisesRegex(RuntimeError, "incomplete"):
                install_qt.configure_freecad_resources(root / "private", root / "freecad")
            self.assertFalse((root / "freecad").is_symlink())

    def test_spyder_repair_and_repeat_preserve_original(self):
        original = "image.scaled(image_width, image_height, Qt.KeepAspectRatio)\nsize = QSize(image_width, image_height)\n"
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "widget.py"
            path.write_text(original)
            result = install_qt.repair_spyder_file(path)
            self.assertEqual(result["calls_changed"], 2)
            self.assertIn("QSize(int(image_width), int(image_height))", path.read_text())
            self.assertEqual(path.with_suffix(".py.hcu-infra-original").read_text(), original)
            self.assertEqual(install_qt.repair_spyder_file(path)["spyder"], "already repaired")
            self.assertEqual(path.with_suffix(".py.hcu-infra-original").read_text(), original)

    def test_spyder_unknown_source_is_unchanged(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "widget.py"
            path.write_text("image.resize(width, height)\n")
            with self.assertRaisesRegex(RuntimeError, "Unrecognized"):
                install_qt.repair_spyder_file(path)
            self.assertEqual(path.read_text(), "image.resize(width, height)\n")

    def test_editor_preserves_settings_and_rejects_unparseable_profile(self):
        with tempfile.TemporaryDirectory() as directory:
            home = Path(directory)
            path = home / ".config/Code/User/settings.json"
            path.parent.mkdir(parents=True)
            path.write_text('{"editor.fontSize": 16}')
            configure_editor.configure(home)
            self.assertEqual(json.loads(path.read_text()), {
                "editor.fontSize": 16, "editor.accessibilitySupport": "on"})
            path.write_text("// JSONC profile\n{}")
            with self.assertRaises(json.JSONDecodeError):
                configure_editor.configure(home)
            self.assertEqual(path.read_text(), "// JSONC profile\n{}")

    def test_desktop_preferences_preserve_other_values(self):
        with tempfile.TemporaryDirectory() as directory:
            home = Path(directory)
            path = home / ".config/REAPER/reaper.ini"
            path.parent.mkdir(parents=True)
            path.write_text("[reaper]\nlinux_audio_mode=1\ncustomValue=keep\n")
            configure_desktop.configure(home, {"musescore_skipped_version": "selected"})
            parser = configparser.ConfigParser()
            parser.read(path)
            self.assertEqual(parser["reaper"]["customValue"], "keep")
            self.assertEqual(parser["reaper"]["linux_audio_mode"], "3")

    def test_preflight_rejects_missing_archive_before_installation(self):
        manifest = {"distribution": "ubuntu", "ubuntu_version": "22.04",
                    "architecture": "amd64", "joplin": {"file": "missing.AppImage"}}
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(install.platform, "freedesktop_os_release", return_value={
                    "ID": "ubuntu", "VERSION_ID": "22.04"}), \
                mock.patch.object(install.subprocess, "check_output", return_value="amd64\n"):
            with self.assertRaisesRegex(ValueError, "Missing asset"):
                install.preflight(manifest, Path(directory), ["joplin"])

    def test_freecad_rejects_other_package_selection_before_installation(self):
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(install_qt.subprocess, "run") as run:
            root = Path(directory)
            manifest = root / "manifest.json"
            manifest.write_text(json.dumps({"freecad_version": "different"}))
            archive = root / "freecad.tar"
            with tarfile.open(archive, "w") as output:
                output.add(manifest, arcname="manifest.json")
            with self.assertRaisesRegex(RuntimeError, "metadata differs"):
                install_qt.install_freecad(archive, {"freecad_version": "selected"})
            run.assert_not_called()

    def test_libreoffice_preferences_preserve_initialization_and_other_settings(self):
        oor = install_libreoffice.OOR
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(install_libreoffice.os, "chown"):
            path = Path(directory) / "profile/user/registrymodifications.xcu"
            path.parent.mkdir(parents=True)
            path.write_text(f'<oor:items xmlns:oor="{oor}">'
                '<item oor:path="/org.openoffice.Setup/Product">'
                '<prop oor:name="FirstRun"><value>true</value></prop>'
                '<prop oor:name="WhatsNew"><value>true</value></prop>'
                '</item></oor:items>')
            for _ in range(2):
                install_libreoffice.prepare_preferences(path, 1000, 1000)
            properties = [(prop.get(f"{{{oor}}}name"), prop.findtext("value"))
                          for item in ET.parse(path).getroot() for prop in item]
            self.assertEqual(properties.count(("FirstRun", "true")), 1)
            for name in ("WhatsNew", "ShowTipOfTheDay", "AutoPilot"):
                self.assertEqual(properties.count((name, "false")), 1)

    def test_libreoffice_rejects_missing_package_before_installation(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive = root / "release.tar"
            with tarfile.open(archive, "w") as output:
                item = tarfile.TarInfo("DEBS/one.deb")
                item.size = 4
                output.addfile(item, io.BytesIO(b"fake"))
            extracted = root / "extracted"
            extracted.mkdir()
            manifest = {"packages": ["one", "two"], "package_version": "selected",
                        "architecture": "amd64"}
            with mock.patch.object(install_libreoffice, "command", side_effect=lambda args:
                    {"Package": "one", "Version": "selected", "Architecture": "amd64"}[args[-1]]) as run:
                with self.assertRaisesRegex(ValueError, "Missing candidate packages: two"):
                    install_libreoffice.extract_packages(archive, extracted, manifest)
                self.assertTrue(all(call.args[0][0] == "dpkg-deb" for call in run.call_args_list))

    def test_libreoffice_new_navigator_has_complete_typed_docking_record(self):
        oor = install_libreoffice.OOR
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(install_libreoffice.os, "chown"):
            path = Path(directory) / "profile/user/registrymodifications.xcu"
            install_libreoffice.prepare_preferences(path, 1000, 1000)
            namespaces = dict(value for _, value in ET.iterparse(path, events=["start-ns"]))
            self.assertEqual(namespaces["xs"], install_libreoffice.XS)
            root = ET.parse(path).getroot()
            node = next(node for item in root for node in item
                        if node.get(f"{{{oor}}}name") == "simpress/10366")
            names = {element.get(f"{{{oor}}}name") for element in node}
            self.assertEqual(names, {"Visible", "UserData", "WindowState"})
            data = next(prop for child in node for prop in child
                        if prop.get(f"{{{oor}}}name") == "Data")
            self.assertEqual(data.get(f"{{{oor}}}type"), "xs:string")
            self.assertTrue(data.findtext("value"))

    def test_libreoffice_preserves_existing_navigator_preferences(self):
        oor = install_libreoffice.OOR
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(install_libreoffice.os, "chown"):
            path = Path(directory) / "profile/user/registrymodifications.xcu"
            path.parent.mkdir(parents=True)
            path.write_text(f'<oor:items xmlns:oor="{oor}">'
                '<item oor:path="/org.openoffice.Office.Views/Windows">'
                '<node oor:name="simpress/10366"><prop oor:name="WindowState">'
                '<value>user-selected geometry</value></prop></node></item></oor:items>')
            before = ET.tostring(ET.parse(path).getroot()[0])
            for _ in range(2):
                install_libreoffice.prepare_preferences(path, 1000, 1000)
            root = ET.parse(path).getroot()
            items = [item for item in root if item.get(f"{{{oor}}}path") == "/org.openoffice.Office.Views/Windows"]
            self.assertEqual(len(items), 1)
            self.assertEqual(ET.tostring(items[0]), before)

    def test_libreoffice_rejects_package_architecture_mismatch(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            archive = root / "release.tar"
            with tarfile.open(archive, "w") as output:
                item = tarfile.TarInfo("DEBS/one.deb")
                item.size = 4
                output.addfile(item, io.BytesIO(b"fake"))
            extracted = root / "extracted"
            extracted.mkdir()
            manifest = {"packages": ["one"], "package_version": "selected", "architecture": "amd64"}
            with mock.patch.object(install_libreoffice, "command", side_effect=lambda args:
                    {"Package": "one", "Version": "selected", "Architecture": "arm64"}[args[-1]]):
                with self.assertRaisesRegex(ValueError, "Unexpected package metadata"):
                    install_libreoffice.extract_packages(archive, extracted, manifest)


if __name__ == "__main__":
    unittest.main()
