"""Check isolated application preferences and preservation of existing configuration."""

from pathlib import Path
import tempfile
import unittest

import configure_thunderbird


class ThunderbirdTests(unittest.TestCase):
    def test_repeat_preserves_profile_and_other_defaults(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            application = root / "application"
            defaults = application / "defaults/pref"
            defaults.mkdir(parents=True)
            (application / "omni.ja").touch()
            other = defaults / "channel-prefs.js"
            other.write_text('pref("app.update.channel", "release");\n')
            profile = root / "profile/prefs.js"
            profile.parent.mkdir()
            profile.write_text('user_pref("mail.server.server1.login_at_startup", true);\n')
            before = {p: p.read_bytes() for p in (other, profile)}
            first = configure_thunderbird.configure(application)
            content = Path(first["preferences"]).read_bytes()
            self.assertEqual(configure_thunderbird.configure(application), first)
            self.assertEqual(Path(first["preferences"]).read_bytes(), content)
            self.assertEqual({p: p.read_bytes() for p in before}, before)

    def test_unknown_application_and_conflicting_preferences_are_unchanged(self):
        with tempfile.TemporaryDirectory() as temporary:
            application = Path(temporary)
            with self.assertRaisesRegex(ValueError, "Expected an unpacked"):
                configure_thunderbird.configure(application)
            defaults = application / "defaults/pref"
            defaults.mkdir(parents=True)
            (application / "omni.ja").touch()
            path = defaults / "hcu-startup.js"
            path.write_text("// Administrator configuration\n")
            with self.assertRaisesRegex(ValueError, "Refusing to overwrite"):
                configure_thunderbird.configure(application)
            self.assertEqual(path.read_text(), "// Administrator configuration\n")

    def test_rejects_existing_and_dangling_symlinks(self):
        with tempfile.TemporaryDirectory() as temporary:
            application = Path(temporary)
            defaults = application / "defaults/pref"
            defaults.mkdir(parents=True)
            (application / "omni.ja").touch()
            target = application / "administrator.js"
            target.write_text(configure_thunderbird.PREFERENCES)
            path = defaults / "hcu-startup.js"
            path.symlink_to(target)
            for present in (True, False):
                if not present:
                    target.unlink()
                with self.assertRaisesRegex(ValueError, "Refusing to follow"):
                    configure_thunderbird.configure(application)
                self.assertTrue(path.is_symlink())
                self.assertEqual(target.exists(), present)
                if present:
                    self.assertEqual(target.read_text(), configure_thunderbird.PREFERENCES)


if __name__ == "__main__":
    unittest.main()
