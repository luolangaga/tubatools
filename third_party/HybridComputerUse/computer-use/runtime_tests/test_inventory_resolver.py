#!/usr/bin/env python3
"""Linux provider inventory, window, and virtual-table resolver tests."""

from runtime_test_support import *  # noqa: F401,F403


class VirtualTableCellResolutionTests(AtspiPatchedTestCase):
    def test_snapshot_virtual_cell_path_resolves_through_table_api(self):
        cell = _NamedNode(name="B2", role="table cell")
        container = FakeNode()
        table = object()
        container.get_table_iface = lambda: table
        window = FakeNode()
        record = {
            "runtimeId": [0, 4, 1, 1],
            "controlType": "table cell",
            "name": "B2",
            "frame": None,
        }
        table_api = type(
            "FakeAtspiTable",
            (),
            {
                "get_accessible_at": staticmethod(
                    lambda actual, row, col: (
                        cell if actual is table and (row, col) == (1, 1)
                        else None
                    )
                )
            },
        )

        with mock.patch.object(runtime, "main_window", return_value=(0, window)), \
             mock.patch.object(runtime, "extents",
                               return_value=runtime.frame(0, 0, 100, 100)), \
             mock.patch.object(
                 runtime,
                 "resolve_path",
                 side_effect=lambda _app, path: (
                     container if path == [0, 4] else None
                 ),
             ), \
             mock.patch.object(runtime.Atspi, "Table", table_api, create=True):
            resolved = runtime.find_element_at_snapshot_path(FakeNode(), record)

        self.assertIs(resolved, cell)


class LabelledByTests(AtspiPatchedTestCase):
    """Unnamed controls borrow their name through LABELLED_BY.

    Provider relation targets supply names for otherwise unnamed controls,
    allowing name-based selection without inferring identity from position.
    """

    def _labelled(self, label_text, own_name=""):
        label = _NamedNode(name=label_text, role="label")
        kind = runtime.Atspi.RelationType.LABELLED_BY
        return _NamedNode(
            name=own_name,
            role="spin button",
            relations=[_FakeRelation(kind, [label])],
        )

    def test_unnamed_control_borrows_the_label(self):
        node = self._labelled("Position Y:")
        # Drop the trailing colon: it adds nothing and is awkward in selectors.
        self.assertEqual(runtime.labelled_by_name(node), "Position Y")
        self.assertEqual(runtime.effective_name(node), "Position Y")

    def test_a_control_with_its_own_name_does_not_borrow(self):
        # Only unnamed nodes benefit. A label lookup for a button already
        # named Save adds an unnecessary D-Bus round trip.
        node = self._labelled("Position Y:", own_name="Save")
        self.assertEqual(runtime.effective_name(node), "Save")

    def test_identity_comparison_uses_the_same_name_as_the_record(self):
        """Records and live nodes are compared through the same effective_name().

        If a record stores one name and re-resolution compares another, the
        match fails silently and identity falls back to the weakest check,
        role plus screen position, which points elsewhere once anything moves.
        A borrowed name would do exactly that: the record says Position Y
        while node_name() returns an empty string for the same node.
        """
        node = self._labelled("Position Y:")
        record = {"name": "Position Y"}
        self.assertTrue(runtime.record_name_matches(node, record))
        # This fails if the live side uses the node's own empty name.
        self.assertFalse(runtime.record_name_matches(node, {"name": "Something Else"}))

    def test_borrowed_names_are_marked_so_the_source_is_visible(self):
        # Position Y appears twice in the Position and Size dialog, once per
        # tab, so callers must know the name is borrowed and may not be unique.
        marks = runtime.state_segment(FakeNode(), borrowed_name=True)
        self.assertIn("labelled", marks)
        self.assertNotIn("labelled", runtime.state_segment(FakeNode()))


class ModalDiagnosticTests(AtspiPatchedTestCase):
    """A dialog in front is reported at one of two levels of evidence."""

    def _window(self, name, role, states):
        return _NamedNode(name=name, role=role, states=states)

    def _app(self, *windows):
        return _NamedNode(name="app", role="application", children=windows)

    def test_modal_state_licenses_the_strong_claim(self):
        dialog = self._window("Position and Size", "dialog",
                              [STATE.MODAL, STATE.SHOWING, STATE.ACTIVE])
        main = self._window("Doc - Impress", "frame", [STATE.SHOWING])
        notes = runtime.modal_diagnostic(dialog, self._app(dialog, main))
        self.assertEqual(len(notes), 1)
        self.assertIn("MODAL DIALOG", notes[0])
        # Only the MODAL bit justifies saying other windows ignore input.
        self.assertIn("ignore input to every other window", notes[0])
        # Naming the blocked window tells the agent where to return.
        self.assertIn("Doc - Impress", notes[0])

    def test_a_dialog_without_the_modal_bit_gets_the_weaker_claim(self):
        """Some blocking dialogs never set MODAL.

        LibreOffice 7.3's Tip of the Day dialog is role=dialog, ACTIVE and
        SHOWING without MODAL, yet it blocks the application. Like ENABLED,
        MODAL is set inconsistently across Linux toolkits. Requiring MODAL
        would miss real blockers, while the strong claim would promise more
        than the states prove.
        """
        dialog = self._window("Tip of the Day: 1/223", "dialog",
                              [STATE.SHOWING, STATE.ACTIVE])
        main = self._window("Doc - Impress", "frame", [STATE.SHOWING])
        notes = runtime.modal_diagnostic(dialog, self._app(dialog, main))
        self.assertEqual(len(notes), 1)
        self.assertIn("DIALOG IN FRONT", notes[0])
        # Without MODAL the strong claim must not appear.
        self.assertNotIn("ignore input to every other window", notes[0])
        self.assertIn("not proof that the app is blocked", notes[0])

    def test_a_plain_main_window_says_nothing(self):
        main = self._window("Doc - Impress", "frame", [STATE.SHOWING, STATE.ACTIVE])
        self.assertEqual(runtime.modal_diagnostic(main, self._app(main)), [])

    def test_a_lone_dialog_is_the_main_interface_not_a_blocker(self):
        # With no other window, there is nothing for the dialog to block.
        only = self._window("Preferences", "dialog", [STATE.SHOWING, STATE.ACTIVE])
        self.assertEqual(runtime.modal_diagnostic(only, self._app(only)), [])


class InfrastructureWindowTests(AtspiPatchedTestCase):
    def test_named_desktop_role_without_input_content_is_infrastructure(self):
        desktop_window = _NamedNode(
            name="Desktop",
            role="desktop",
            component=FakeComponent(
                extents=FakeExtents(0, 0, 1920, 1080)),
        )
        with mock.patch.object(
                runtime, "desktop_bounds",
                return_value=runtime.frame(0, 0, 1920, 1080)):
            self.assertTrue(runtime.is_infrastructure_window(desktop_window))

    def test_fullscreen_sparse_application_is_not_infrastructure_by_geometry(self):
        canvas_app = _NamedNode(
            name="Remote Session",
            role="frame",
            component=FakeComponent(
                extents=FakeExtents(0, 0, 1920, 1080)),
        )
        with mock.patch.object(
                runtime, "desktop_bounds",
                return_value=runtime.frame(0, 0, 1920, 1080)):
            self.assertFalse(runtime.is_infrastructure_window(canvas_app))

    def test_named_dialog_with_actionable_content_is_not_infrastructure(self):
        button = _NamedNode(name="OK", role="push button")
        dialog = _NamedNode(
            name="Question",
            role="dialog",
            children=(button,),
            component=FakeComponent(
                extents=FakeExtents(100, 100, 500, 300)),
        )
        with mock.patch.object(
                runtime, "desktop_bounds",
                return_value=runtime.frame(0, 0, 1920, 1080)):
            self.assertFalse(runtime.is_infrastructure_window(dialog))


class ResolvedNoteTests(AtspiPatchedTestCase):
    """Action notes name the element the target resolved to."""

    def test_reports_index_role_and_name(self):
        node = _NamedNode(name="Save", role="push button")
        note = runtime.resolved_note(node, {"index": 4, "controlType": "push button",
                                            "name": "Save"})
        self.assertEqual(note, "Resolved the target ref to push button 'Save'.")

    def test_drift_from_the_snapshot_is_a_warning_not_a_silent_pass(self):
        """Drift from the snapshot record produces a warning.

        Resolution falls back through runtimeId path, automationId, name plus
        role, and role plus geometry. After a menu closes, the same index can
        resolve to a different control, such as a toolbar toggle instead of
        the Rename item, while every step reports success. Silently acting on
        the wrong control is the worst failure mode.
        """
        node = _NamedNode(name="Menu", role="toggle button")
        note = runtime.resolved_note(node, {"index": 9, "controlType": "menu item",
                                            "name": "Rename"})
        self.assertIn("WARNING", note)
        self.assertIn("'menu item'", note)
        self.assertIn("'toggle button'", note)
        self.assertIn("'Rename'", note)

    def test_unnamed_targets_still_get_a_line(self):
        node = _NamedNode(name="", role="canvas")
        note = runtime.resolved_note(node, {"index": 7, "controlType": "canvas"})
        self.assertEqual(note, "Resolved the target ref to an unnamed canvas.")

    def test_coordinate_actions_have_no_element_to_report(self):
        # Coordinate actions resolve no element, so a note would be noise.
        self.assertIsNone(runtime.resolved_note(None, {"index": 1}))
        self.assertIsNone(runtime.resolved_note(_NamedNode(), None))


class ProcessNameAliasTests(unittest.TestCase):
    """Apps can be matched by process name, but URLs never become names.

    The accessible name `Archive Manager` shares no substring with the query
    `file-roller`, which is its process name; org.gnome.Software and
    snap-store behave the same way. /proc cmdline is normally NUL-separated,
    but Chrome's reads as one space-separated string, so splitting on NUL
    and taking the basename would register a URL argument as an app alias.
    """

    def _process_names(self, blob, comm="x", exe=None):
        # process_names accepts a proc_root, so a fake /proc tree avoids
        # patching builtins.open, which is brittle and affects unrelated code.
        import tempfile
        with tempfile.TemporaryDirectory() as root:
            pid_dir = os.path.join(root, "4242")
            os.makedirs(pid_dir)
            with open(os.path.join(pid_dir, "comm"), "w") as handle:
                handle.write(comm + "\n")
            with open(os.path.join(pid_dir, "cmdline"), "wb") as handle:
                handle.write(blob)
            if exe:
                os.symlink(exe, os.path.join(pid_dir, "exe"))
            return runtime.process_names(4242, proc_root=root)

    def test_null_separated_cmdline_gives_binary_name(self):
        names = self._process_names(b"/usr/bin/file-roller\0/tmp/a.zip\0",
                                    comm="file-roller")
        self.assertIn("file-roller", names)

    def test_space_separated_cmdline_does_not_leak_a_url(self):
        # Chrome's cmdline is one space-separated block ending with the URL.
        names = self._process_names(
            b"/opt/google/chrome/chrome --app=https://www.drugs.com",
            comm="chrome")
        self.assertIn("chrome", names)
        self.assertNotIn("drugs.com", names)
        self.assertNotIn("drugs", names)

    def test_suffixed_binary_also_registers_its_stem(self):
        names = self._process_names(b"/usr/lib/libreoffice/program/soffice.bin\0",
                                    comm="soffice.bin")
        self.assertIn("soffice.bin", names)
        self.assertIn("soffice", names)

    def test_generic_packaging_ancestor_registers_product_identity(self):
        names = self._process_names(
            b"/usr/lib/libreoffice/program/soffice.bin\0",
            comm="soffice.bin",
            exe="/usr/lib/libreoffice/program/soffice.bin",
        )
        self.assertIn("libreoffice", names)

    def test_product_query_matches_executable_identity_without_alias_table(self):
        app = type(
            "ProcessApp",
            (),
            {
                "get_name": lambda self: "soffice",
                "get_process_id": lambda self: 4242,
            },
        )()
        with mock.patch.object(
            runtime,
            "process_names",
            return_value={"soffice", "libreoffice"},
        ):
            self.assertTrue(
                runtime.matches_app_bootstrap_query(
                    app, "LibreOffice Impress"
                )
            )

    def test_separator_insensitive_match_never_matches_every_app(self):
        app = type(
            "NamedApp",
            (),
            {
                "get_name": lambda self: "Google Chrome",
                "get_process_id": lambda self: 4242,
            },
        )()
        with mock.patch.object(runtime, "process_names", return_value=set()):
            self.assertTrue(
                runtime.matches_app_bootstrap_query(app, "google-chrome")
            )
            # Without alphanumerics the separator-insensitive form is "",
            # which every name contains.
            self.assertFalse(runtime.matches_app_bootstrap_query(app, "-"))
            self.assertFalse(runtime.matches_app_bootstrap_query(app, ""))

    def test_meaningful_immediate_parent_does_not_leak_vendor_alias(self):
        names = self._process_names(
            b"/opt/google/chrome/chrome --app=https://example.com",
            comm="chrome",
            exe="/opt/google/chrome/chrome",
        )
        self.assertNotIn("google", names)


class HiddenMenuItemsTests(AtspiPatchedTestCase):
    """A menu whose children were all pruned says so on its own line.

    GIMP's closed File menu is SHOWING on the menu bar while none of its items
    are, so pruning removes all of them, Export included, and the agent would
    conclude the app cannot export. The "N items collapsed" hint only covers
    menus that are not SHOWING themselves, and the global "N node(s) omitted"
    footer does not say which menu hid its items.
    """

    def _hidden(self):
        """Build an off-screen menu item, like one in a closed GIMP menu.

        It exists in AT-SPI and has a name, but no visible geometry.
        """
        node = _TreeNode(role="menu item", name="Export As...", w=0, h=0)
        node.states = set()
        return node

    def test_menu_with_all_children_pruned_says_how_many(self):
        item = self._hidden()
        menu = _TreeNode(role="menu", name="File", w=40, h=20, kids=(item,))
        root = _TreeNode(role="frame", name="GIMP", w=100, h=100, kids=(menu,))

        _records, lines, _annotations = runtime.render_tree(root, None, [0], prune=True)
        text = "\n".join(lines)

        self.assertIn('menu "File"', text)
        self.assertIn("hidden-items=1", text)
        self.assertIn("open menu, then observe", text)

        _records, _lines, annotations = runtime.render_tree(
            root,
            None,
            [0],
            prune=True,
        )
        self.assertTrue(
            any(
                item.get("kind") == "hidden_children"
                and item.get("count") == 1
                for item in annotations
            ),
            annotations,
        )

    def test_menu_with_visible_children_gets_no_such_note(self):
        # Menus that render their children skip the note to save output bytes.
        item = _TreeNode(role="menu item", name="Export As...", w=40, h=10)
        menu = _TreeNode(role="menu", name="File", w=40, h=20, kids=(item,))
        root = _TreeNode(role="frame", name="GIMP", w=100, h=100, kids=(menu,))

        _records, lines, _annotations = runtime.render_tree(root, None, [0], prune=True)
        text = "\n".join(lines)

        self.assertIn("Export As...", text)
        self.assertNotIn("hidden-items=", text)


class AppNotFoundListsWindowsTests(AtspiPatchedTestCase):
    """An app lookup failure lists the window titles each candidate owns.

    Callers often name an app after a dialog they can see, such as
    "file-jpeg" for GIMP's "Export Image as JPEG" window. A bare "gimp"
    entry does not reveal that GIMP owns that window.
    """

    def test_listing_names_the_windows_each_app_owns(self):
        window = _NamedNode(name="Export Image as JPEG", role="frame")
        app = _NamedNode(name="gimp", role="application", children=[window])
        # node_pid calls get_process_id directly. Without it the fake raises
        # AttributeError inside the guarded candidate listing, which then
        # comes back empty and hides the cause.
        app.get_process_id = lambda: 0

        with mock.patch.object(runtime, "iter_apps", return_value=[app]), \
             mock.patch.object(runtime, "app_windows",
                               return_value=[(0, window)]):
            with self.assertRaises(RuntimeError) as caught:
                runtime.resolve_app("file-jpeg")

        message = str(caught.exception)
        self.assertEqual(caught.exception.failure_code, "ax_app_unavailable")
        self.assertIn('appNotVisibleToAccessibility("file-jpeg")', message)
        self.assertIn("no action was attempted", message)
        # The owner name alone is not enough; the owned dialog must be listed.
        self.assertIn("gimp", message)
        self.assertIn("Export Image as JPEG", message)

    def test_incomplete_app_inventory_requires_provider_reset(self):
        incomplete = runtime.InventoryResult(
            [],
            complete=False,
            errors=["AX bus disconnected"],
        )
        with mock.patch.object(
            runtime, "iter_apps", return_value=incomplete
        ), mock.patch.object(runtime.time, "sleep", return_value=None):
            with self.assertRaises(runtime.OperationFailure) as caught:
                runtime.resolve_app("Editor")

        self.assertEqual(
            caught.exception.failure_code,
            "ax_app_unavailable",
        )
        self.assertTrue(caught.exception.provider_reset_required)

    def test_complete_app_inventory_miss_does_not_require_reset(self):
        complete = runtime.InventoryResult([], complete=True)
        with mock.patch.object(
            runtime, "iter_apps", return_value=complete
        ), mock.patch.object(
            runtime, "input_capable_app_labels", return_value=[]
        ), mock.patch.object(runtime.time, "sleep", return_value=None):
            with self.assertRaises(runtime.OperationFailure) as caught:
                runtime.resolve_app("Editor")

        self.assertEqual(
            caught.exception.failure_code,
            "ax_app_unavailable",
        )
        self.assertFalse(caught.exception.provider_reset_required)

    def test_app_window_inventory_returns_all_owned_titles(self):
        first = _NamedNode(name="Tip of the Day", role="dialog")
        second = _NamedNode(
            name="Quarterly Review - LibreOffice Impress",
            role="frame",
        )
        app = _NamedNode(
            name="soffice",
            role="application",
            children=[first, second],
        )
        app.get_process_id = lambda: 42
        with mock.patch.object(runtime, "resolve_app_identity", return_value=app), \
             mock.patch.object(
                 runtime,
                 "app_windows",
                 return_value=runtime.InventoryResult(
                     [(0, first), (1, second)], complete=True
                 ),
             ):
            result = runtime.app_window_inventory(
                "libreoffice",
                app_identity={"providerBusName": ":1.2", "processGeneration": "g"},
            )

        self.assertTrue(result["ok"])
        self.assertEqual(result["appName"], "soffice")
        self.assertEqual(result["appPid"], 42)
        self.assertEqual(
            result["windowTitles"],
            ["Tip of the Day", "Quarterly Review - LibreOffice Impress"],
        )


class AppNameWindowInventoryTests(AtspiPatchedTestCase):
    """A title wait by app name reads every process of that name.

    An agent may close the application and launch it again; the wait must
    see whichever process now owns the window, and a complete inventory
    without the application is an answer, not an error.
    """

    def _app(self, name, pid, titles):
        windows = [_NamedNode(name=title, role="frame") for title in titles]
        app = _NamedNode(name=name, role="application", children=windows)
        app.get_process_id = lambda: pid
        return app, runtime.InventoryResult(
            list(enumerate(windows)), complete=True
        )

    def _inventory(self, apps, complete=True):
        by_app = {id(app): windows for app, windows in apps}
        return (
            mock.patch.object(
                runtime,
                "iter_apps",
                return_value=runtime.InventoryResult(
                    [app for app, _ in apps], complete=complete
                ),
            ),
            mock.patch.object(
                runtime,
                "app_windows",
                side_effect=lambda app: by_app[id(app)],
            ),
            mock.patch.object(runtime, "process_names", return_value=set()),
        )

    def _read(self, apps, complete=True):
        first, second, third = self._inventory(apps, complete)
        with first, second, third:
            return runtime.app_window_inventory("soffice")

    def test_same_name_in_two_processes_lists_both(self):
        result = self._read([
            self._app("soffice", 41, ["Old.odt - LibreOffice Writer"]),
            self._app("soffice", 42, ["30_1.pptx - LibreOffice Impress"]),
        ])
        self.assertEqual(
            result["windowTitles"],
            ["Old.odt - LibreOffice Writer", "30_1.pptx - LibreOffice Impress"],
        )

    def test_one_process_with_main_window_and_dialog_lists_both(self):
        result = self._read([
            self._app("soffice", 42, ["16_2.pptx - LibreOffice Impress", "Character"]),
        ])
        self.assertEqual(
            result["windowTitles"],
            ["16_2.pptx - LibreOffice Impress", "Character"],
        )

    def test_complete_inventory_without_the_app_lists_no_titles(self):
        result = self._read([self._app("gjs", 7, ["@!0,0;BDHF"])])
        self.assertTrue(result["ok"])
        self.assertEqual(result["windowTitles"], [])

    def test_incomplete_inventory_is_not_absence(self):
        with self.assertRaises(runtime.OperationFailure) as caught:
            self._read([], complete=False)
        self.assertEqual(caught.exception.failure_code, "observation_incomplete")
        self.assertFalse(caught.exception.failure_facts_complete)

    def test_incomplete_window_list_of_a_matching_app_is_not_absence(self):
        app, _ = self._app("soffice", 42, [])
        partial = runtime.InventoryResult([], complete=False)
        with self.assertRaises(runtime.OperationFailure) as caught:
            self._read([(app, partial)])
        self.assertEqual(caught.exception.failure_code, "observation_incomplete")


class ExactLogicalWindowReadTests(AtspiPatchedTestCase):
    def test_window_handle_selects_the_requested_sibling(self):
        first = _NamedNode(name="Document", role="frame")
        second = _NamedNode(name="Chart Type", role="dialog")
        app = _NamedNode(
            name="soffice",
            role="application",
            children=[first, second],
        )
        windows = runtime.InventoryResult(
            [(0, first), (1, second)], complete=True
        )
        with mock.patch.object(runtime, "app_windows", return_value=windows), \
             mock.patch.object(
                 runtime,
                 "strong_window_instance_handle_inventory",
                 return_value=runtime.InventoryResult(
                     [(0, "w2:0"), (1, "w2:1")],
                     complete=True,
                 ),
             ):
            index, window, evidence = runtime.select_window_by_handle(
                app, "w2:1"
            )
        self.assertEqual(index, 1)
        self.assertIs(window, second)
        self.assertEqual(evidence["reason"], "exact_logical_window")

    def test_incomplete_window_inventory_cannot_select_a_window(self):
        app = _NamedNode(name="soffice", role="application")
        with mock.patch.object(
            runtime,
            "app_windows",
            return_value=runtime.InventoryResult(
                [], complete=False, errors=["AX bus failed"]
            ),
        ):
            with self.assertRaises(runtime.OperationFailure) as caught:
                runtime.select_window_by_handle(app, "w2:1")
        self.assertEqual(
            caught.exception.failure_code, "observation_incomplete"
        )

    def test_complete_missing_window_is_stale(self):
        app = _NamedNode(name="soffice", role="application")
        with mock.patch.object(
            runtime,
            "app_windows",
            return_value=runtime.InventoryResult([], complete=True),
        ), mock.patch.object(
            runtime,
            "strong_window_instance_handle_inventory",
            return_value=runtime.InventoryResult([], complete=True),
        ):
            with self.assertRaises(runtime.OperationFailure) as caught:
                runtime.select_window_by_handle(app, "w2:1")
        self.assertEqual(caught.exception.failure_code, "stale_window_ref")
        self.assertEqual(caught.exception.failure_phase, "precondition")


class VisibleCellsIndexerTests(AtspiPatchedTestCase):
    """Virtual cells share the renderer's indexer for stable target indices."""

    def test_visible_cell_range_failure_marks_acquisition_incomplete(self):
        annotations = []
        with mock.patch.object(runtime, "visible_cell_range", return_value=None):
            rendered = runtime.render_visible_cells(
                _TreeNode(role="table", name="Data"),
                0,
                [0],
                None,
                [],
                [],
                indexer=runtime.StableIndexer({}),
                annotations=annotations,
            )

        self.assertEqual(rendered, 0)
        self.assertTrue(any(
            item.get("kind") == "provider_omission"
            and item.get("reason") == "visible_cell_range_unavailable"
            for item in annotations
        ), annotations)

    def test_visible_cell_lookup_none_marks_acquisition_incomplete(self):
        table = object()
        node = _TreeNode(role="table", name="Data")
        node.get_table_iface = lambda: table
        annotations = []
        table_api = mock.Mock()
        table_api.get_accessible_at.return_value = None

        with mock.patch.object(
            runtime, "visible_cell_range", return_value=(0, 0, 0, 0)
        ), mock.patch.object(
            runtime.Atspi, "Table", table_api, create=True
        ):
            rendered = runtime.render_visible_cells(
                node,
                0,
                [0],
                None,
                [],
                [],
                indexer=runtime.StableIndexer({}),
                annotations=annotations,
            )

        self.assertEqual(rendered, 0)
        self.assertTrue(any(
            item.get("kind") == "provider_omission"
            and item.get("reason") == "visible_cell_lookup_unavailable"
            and item.get("count") == 1
            for item in annotations
        ), annotations)

    def test_visible_cell_text_failure_marks_acquisition_incomplete(self):
        table = object()
        node = _TreeNode(role="table", name="Data")
        node.get_table_iface = lambda: table
        cell = _TreeNode(role="table cell", name="A1")
        cell._text = object()
        annotations = []
        table_api = mock.Mock()
        table_api.get_accessible_at.return_value = cell
        text_api = mock.Mock()
        text_api.get_character_count.side_effect = RuntimeError(
            "text read failed"
        )

        with mock.patch.object(
            runtime, "visible_cell_range", return_value=(0, 0, 0, 0)
        ), mock.patch.object(
            runtime.Atspi, "Table", table_api, create=True
        ), mock.patch.object(
            runtime.Atspi, "Text", text_api, create=True
        ):
            rendered = runtime.render_visible_cells(
                node,
                0,
                [0],
                None,
                [],
                [],
                indexer=runtime.StableIndexer({}),
                annotations=annotations,
            )

        self.assertEqual(rendered, 0)
        self.assertTrue(any(
            item.get("kind") == "provider_omission"
            and item.get("reason") == "visible_cell_text_unavailable"
            and item.get("count") == 1
            for item in annotations
        ), annotations)

    def test_visible_cell_probe_budget_marks_unread_cells(self):
        table = object()
        node = _TreeNode(role="table", name="Data")
        node.get_table_iface = lambda: table
        cell = _TreeNode(role="table cell", name="A1")
        annotations = []
        table_api = mock.Mock()
        table_api.get_accessible_at.return_value = cell

        with mock.patch.object(
            runtime, "visible_cell_range", return_value=(0, 0, 0, 1)
        ), mock.patch.object(
            runtime.Atspi, "Table", table_api, create=True
        ), mock.patch.object(
            runtime, "MAX_TABLE_CELLS", 1
        ), mock.patch.object(
            runtime, "text_value_with_status", return_value=("value", True)
        ), mock.patch.object(
            runtime,
            "record_for",
            return_value={
                "index": 1,
                "localizedControlType": "table cell",
                "controlType": "table cell",
                "frame": None,
            },
        ):
            rendered = runtime.render_visible_cells(
                node,
                0,
                [0],
                None,
                [],
                [],
                indexer=runtime.StableIndexer({}),
                annotations=annotations,
            )

        self.assertEqual(rendered, 1)
        self.assertTrue(any(
            item.get("kind") == "provider_omission"
            and item.get("reason") == "visible_cell_budget"
            and item.get("count") == 1
            for item in annotations
        ), annotations)

    def test_visible_cell_geometry_respects_boxes_boundary(self):
        import inspect
        params = inspect.signature(runtime.render_visible_cells).parameters
        self.assertIn("boxes", params)
        self.assertFalse(params["boxes"].default)
        source = inspect.getsource(runtime.render_visible_cells)
        self.assertIn("if boxes and cell_frame is not None", source)

        render_source = inspect.getsource(runtime.render_tree)
        call = render_source[render_source.index("render_visible_cells("):]
        call = call[:call.index(")\n")]
        self.assertIn("boxes=boxes", call)

    def test_spreadsheet_coordinates_include_canonical_a1_names(self):
        self.assertEqual(runtime.spreadsheet_a1(0, 0), "A1")
        self.assertEqual(runtime.spreadsheet_a1(0, 25), "Z1")
        self.assertEqual(runtime.spreadsheet_a1(0, 26), "AA1")
        self.assertEqual(runtime.spreadsheet_a1(2, 3), "D3")


class SemanticRoleAliasTests(unittest.TestCase):
    def test_standard_and_provider_roles_both_match(self):
        self.assertTrue(runtime.semantic_role_matches("push button", "button"))
        self.assertTrue(runtime.semantic_role_matches("push button", "push button"))
        self.assertTrue(runtime.semantic_role_matches("entry", "textbox"))
        self.assertFalse(runtime.semantic_role_matches("check box", "button"))


class _MenuNode(_TreeNode):
    """A GAIL menu node: parent link and the menu shell's Selection."""

    def __init__(self, role, name, states, kids=(), x=0, y=0, w=40, h=20,
                 path=None):
        super().__init__(role=role, name=name, x=x, y=y, w=w, h=h, kids=kids)
        self.states = set(states)
        self.parent = None
        self.selected = []
        self.path = path or "/menu/{}/{}".format(role, name)
        for kid in kids:
            kid.parent = self

    def get_parent(self):
        return self.parent

    def get_selection_iface(self):
        node = self

        class _Selection:
            def get_n_selected_children(self):
                return len(node.selected)

            def get_selected_child(self, index):
                return node.selected[index]

        return _Selection()


class GailPopupMenuShowingTests(AtspiPatchedTestCase):
    """GAIL items of an open GTK2 menu are on screen without SHOWING.

    GIMP 2.10: after Edit opens, `Preferences` is VISIBLE at (244, 707) but
    never SHOWING, because gail_menu_item_ref_state_set() removes SHOWING
    unless the parent menu is SELECTED, which GAIL never sets on a menu-bar
    menu. find then reported it `addressable=false reason=not_currently_
    showing` while the screenshot showed the menu open.
    """

    VISIBLE_ONLY = (STATE.VISIBLE, STATE.ENABLED)

    def _menu(self):
        item = _MenuNode("menu item", "Preferences", self.VISIBLE_ONLY,
                         x=244, y=707, w=362, h=25)
        hidden = _MenuNode("menu item", "Undo", (STATE.ENABLED,),
                           x=244, y=159, w=362, h=25)
        sub_item = _MenuNode("menu item", "New Image", self.VISIBLE_ONLY,
                             x=610, y=300, w=200, h=25)
        submenu = _MenuNode("menu", "Paste as", self.VISIBLE_ONLY,
                            kids=(sub_item,), x=244, y=400, w=362, h=25)
        edit = _MenuNode("menu", "Edit",
                         (STATE.SHOWING, STATE.VISIBLE, STATE.ENABLED),
                         kids=(item, hidden, submenu), x=242, y=129, w=39, h=27)
        bar = _MenuNode("menu bar", "",
                        (STATE.SHOWING, STATE.VISIBLE),
                        kids=(edit,), x=200, y=129, w=800, h=27)
        root = _TreeNode(role="frame", name="GIMP", w=1034, h=802, kids=(bar,))
        bar.parent = root
        return root, bar, edit, item, hidden, submenu, sub_item

    def _records(self, root):
        records, _lines, _ = runtime.render_tree(root, None, [0], prune=True)
        return {r["name"]: r for r in records}

    def test_items_of_an_open_menu_bar_menu_are_showing(self):
        # The Go side drops records whose `showing` is false as
        # not_currently_showing, so this field decides addressability.
        root, bar, edit, item, hidden, *_ = self._menu()
        bar.selected = [edit]

        records = self._records(root)

        self.assertIs(records["Preferences"]["showing"], True)
        # A hidden item of an open menu stays hidden.
        self.assertIs(records["Undo"]["showing"], False)

    def test_items_of_a_closed_menu_stay_not_showing(self):
        # GAIL keeps the last allocation after the popup closes, so extents
        # alone would wrongly show a closed menu's items.
        root, bar, edit, item, *_ = self._menu()

        records = self._records(root)

        self.assertIs(records["Preferences"]["showing"], False)
        self.assertFalse(runtime.showing_state(item))

    def test_semantic_scan_records_use_the_corrected_state(self):
        root, bar, edit, item, *_ = self._menu()
        bar.selected = [edit]

        self.assertIs(runtime.showing_state(item), True)

    def test_open_submenu_items_are_showing_only_while_selected(self):
        root, bar, edit, item, hidden, submenu, sub_item = self._menu()
        bar.selected = [edit]
        self.assertFalse(runtime.showing_state(sub_item))

        edit.selected = [submenu]

        self.assertTrue(runtime.showing_state(sub_item))
        self.assertTrue(runtime.showing_state(item))

    def test_menu_bar_selection_of_another_menu_does_not_open_this_one(self):
        root, bar, edit, item, *_ = self._menu()
        other = _MenuNode("menu", "File", (STATE.SHOWING, STATE.VISIBLE))
        bar.selected = [other]

        self.assertFalse(runtime.showing_state(item))

    def test_non_menu_roles_are_not_corrected(self):
        root, bar, edit, *_ = self._menu()
        bar.selected = [edit]
        label = _MenuNode("label", "Shortcut", self.VISIBLE_ONLY)
        label.parent = edit

        self.assertFalse(runtime.showing_state(label))
