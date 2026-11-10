"""Check repository scoping without invoking apt or changing host keys."""

from pathlib import Path
import unittest

import repair_chrome_repository as chrome


class ChromeRepositoryTests(unittest.TestCase):
    def test_scopes_chrome_and_preserves_other_entries(self):
        other = "deb https://example.org/debian stable main\n"
        original = ("# Chrome repository\n" + other +
                    "deb [arch=amd64] http://dl.google.com/linux/chrome/deb/ stable main\n")
        updated = chrome.bind_source(original, Path("/etc/apt/keyrings/chrome.gpg"))
        self.assertIn(other, updated)
        self.assertIn("[arch=amd64 signed-by=/etc/apt/keyrings/chrome.gpg] https://dl.google.com", updated)
        self.assertEqual(chrome.bind_source(updated, Path("/etc/apt/keyrings/chrome.gpg")), updated)

    def test_removes_legacy_key_binding_and_unsigned_trust_option(self):
        original = "deb [arch=amd64 trusted=yes signed-by=/old.gpg] https://dl.google.com/linux/chrome/deb stable main # managed\n"
        updated = chrome.bind_source(original, Path("/new.gpg"))
        self.assertNotIn("trusted=yes", updated)
        self.assertNotIn("/old.gpg", updated)
        self.assertIn("signed-by=/new.gpg", updated)
        self.assertTrue(updated.endswith("# managed\n"))

    def test_does_not_enable_disabled_source_or_rewrite_unknown_suite(self):
        for source in ("# deb https://dl.google.com/linux/chrome/deb/ stable main\n",
                       "deb https://dl.google.com/linux/chrome/deb/ testing main\n"):
            with self.assertRaises(ValueError):
                chrome.bind_source(source, Path("/new.gpg"))


if __name__ == "__main__":
    unittest.main()
