"""Check package installation before desktop archives are unpacked."""

from pathlib import Path
import subprocess
import unittest
from unittest import mock

import install_qt


class JavaDependenciesTests(unittest.TestCase):
    def test_java_bridge_is_required_and_install_failure_stops_bootstrap(self):
        failure = subprocess.CalledProcessError(100, "apt-get")
        with mock.patch.object(install_qt.os, "geteuid", return_value=0), \
                mock.patch.object(install_qt.subprocess, "run", side_effect=failure) as run, \
                mock.patch.object(install_qt, "install_freecad") as freecad:
            with self.assertRaises(subprocess.CalledProcessError):
                install_qt.bootstrap(Path("/unused"), {"qt_packages": ["kate"]}, 1000, 1000)
        command = run.call_args.args[0]
        self.assertIn("kate", command)
        self.assertIn("libatk-wrapper-java", command)
        self.assertIn("libatk-wrapper-java-jni", command)
        self.assertTrue(run.call_args.kwargs["check"])
        self.assertEqual(run.call_args.kwargs["env"]["DEBIAN_FRONTEND"], "noninteractive")
        freecad.assert_not_called()


if __name__ == "__main__":
    unittest.main()
