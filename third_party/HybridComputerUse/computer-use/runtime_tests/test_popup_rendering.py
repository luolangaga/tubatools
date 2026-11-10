#!/usr/bin/env python3
"""Linux provider rendering of combo box popups and their options."""

from runtime_test_support import *  # noqa: F401,F403


def _options(names, x=0, y=0):
    return tuple(
        _TreeNode(role="list item", name=name, x=x, y=y + 25 * index,
                  w=756, h=25)
        for index, name in enumerate(names)
    )


def _combo(name, popup, states=(STATE.SHOWING, STATE.VISIBLE), x=100, y=50,
           w=200, h=25, extra=()):
    combo = _TreeNode(role="combo box", name=name, x=x, y=y, w=w, h=h,
                      kids=tuple(extra) + (popup,))
    combo.states = set(states)
    return combo


def _list_popup(options, x=0, y=0, w=0, h=0, showing=True):
    popup = _TreeNode(role="list", x=x, y=y, w=w, h=h, kids=options)
    popup.states = {STATE.SHOWING, STATE.VISIBLE} if showing else set()
    return popup


def _render(*children):
    root = _TreeNode(role="dialog", name="Preferences", w=1000, h=700,
                     kids=children)
    return runtime.render_tree(
        root, None, [0], prune=True, indexer=runtime.StableIndexer({}))


class ClosedComboPopupTests(AtspiPatchedTestCase):
    """Options of a closed combo box are neither rendered nor addressable.

    Qt keeps SHOWING|VISIBLE on the popup list and its items while the popup
    is closed and reports the items at the screen origin; only the list's
    0x0 extents show that it is closed. Rendering them put options under
    every closed combo, and options of combos on hidden pages appeared at
    the top of the tree, where a click changed a setting nobody could see.
    """

    def test_closed_visible_combo_hides_its_options(self):
        combo = _combo("When minimized",
                       _list_popup(_options(["Never", "Always"])))
        records, _lines, _annotations = _render(combo)
        names = [record["name"] for record in records]
        self.assertIn("When minimized", names)
        self.assertNotIn("Never", names)
        self.assertNotIn("Always", names)

    def test_combo_on_hidden_page_does_not_leak_options(self):
        hidden = _combo("Menus language:",
                        _list_popup(_options(["English", "German"])),
                        states=(STATE.SENSITIVE,), w=0, h=0)
        page = _TreeNode(role="filler", w=0, h=0, kids=(hidden,))
        page.states = set()
        records, _lines, annotations = _render(
            page, _TreeNode(role="push button", name="Save", w=60, h=20))
        names = [record["name"] for record in records]
        self.assertEqual(["Preferences", "Save"], names)
        omitted = [a for a in annotations if a.get("kind") == "provider_omission"]
        self.assertIn("filtered", omitted[0]["reason"])

    def test_open_combo_renders_options(self):
        combo = _combo("When minimized", _list_popup(
            _options(["Never", "Always"], x=100, y=26), x=100, y=26, w=200,
            h=50))
        records, _lines, _annotations = _render(combo)
        names = [record["name"] for record in records]
        self.assertIn("Never", names)
        self.assertIn("Always", names)

    def test_expandable_combo_follows_its_expanded_state(self):
        def render(states):
            popup = _TreeNode(role="menu", x=100, y=76, w=200, h=50,
                              kids=_options(["Seattle"], x=100, y=76))
            records, _lines, _annotations = _render(
                _combo("City", popup, states=states))
            return [record["name"] for record in records]

        closed = render((STATE.SHOWING, STATE.VISIBLE, STATE.EXPANDABLE))
        opened = render((STATE.SHOWING, STATE.VISIBLE, STATE.EXPANDABLE,
                         STATE.EXPANDED))
        self.assertNotIn("Seattle", closed)
        self.assertIn("Seattle", opened)

    def test_popup_without_showing_is_closed(self):
        popup = _TreeNode(role="menu", x=100, y=76, w=200, h=50,
                          kids=_options(["Fusion"], x=100, y=76))
        popup.states = set()
        records, _lines, _annotations = _render(_combo("Style", popup))
        self.assertNotIn("Fusion", [record["name"] for record in records])

    def test_entry_of_closed_editable_combo_stays(self):
        entry = _TreeNode(role="entry", name="Font", x=100, y=50, w=180, h=25)
        combo = _combo("Font", _list_popup(_options(["Sans", "Serif"])),
                       extra=(entry,))
        records, _lines, _annotations = _render(combo)
        roles = [(record["controlType"], record["name"]) for record in records]
        self.assertIn(("entry", "Font"), roles)
        self.assertNotIn("Serif", [record["name"] for record in records])

    def test_list_box_outside_a_combo_is_rendered(self):
        listbox = _TreeNode(role="list", name="Playlist", x=10, y=10, w=300,
                            h=200, kids=_options(["song.mp3"], x=10, y=10))
        records, _lines, _annotations = _render(listbox)
        self.assertIn("song.mp3", [record["name"] for record in records])


if __name__ == "__main__":
    unittest.main()
