#!/usr/bin/env python3
"""Run every Linux provider test module as a single unittest suite."""

import unittest

from runtime_tests.test_core import *  # noqa: F401,F403
from runtime_tests.test_text_input import *  # noqa: F401,F403
from runtime_tests.test_input_method import *  # noqa: F401,F403
from runtime_tests.test_window_focus import *  # noqa: F401,F403
from runtime_tests.test_action_notes import *  # noqa: F401,F403
from runtime_tests.test_post_action_window import *  # noqa: F401,F403
from runtime_tests.test_gui import *  # noqa: F401,F403
from runtime_tests.test_gui_capture import *  # noqa: F401,F403
from runtime_tests.test_gui_input import *  # noqa: F401,F403
from runtime_tests.test_protocol import *  # noqa: F401,F403
from runtime_tests.test_inventory_resolver import *  # noqa: F401,F403
from runtime_tests.test_semantic_resolver import *  # noqa: F401,F403
from runtime_tests.test_snapshot import *  # noqa: F401,F403
from runtime_tests.test_node_properties import *  # noqa: F401,F403
from runtime_tests.test_find_walk import *  # noqa: F401,F403
from runtime_tests.test_app_window_listing import *  # noqa: F401,F403
from runtime_tests.test_snapshot_identity import *  # noqa: F401,F403
from runtime_tests.test_events import *  # noqa: F401,F403
from runtime_tests.test_capabilities import *  # noqa: F401,F403
from runtime_tests.test_combo_edit import *  # noqa: F401,F403
from runtime_tests.test_editable_click import *  # noqa: F401,F403
from runtime_tests.test_select_boundaries import *  # noqa: F401,F403
from runtime_tests.test_dropdown_navigation import *  # noqa: F401,F403
from runtime_tests.test_chromium_windows import *  # noqa: F401,F403
from runtime_tests.test_a11y_enable import *  # noqa: F401,F403
from runtime_tests.test_popup_rendering import *  # noqa: F401,F403
from runtime_tests.test_native_windows import *  # noqa: F401,F403
from runtime_tests.test_window_geometry import *  # noqa: F401,F403


if __name__ == "__main__":
    unittest.main(verbosity=2)
