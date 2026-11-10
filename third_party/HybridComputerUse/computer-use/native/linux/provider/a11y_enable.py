"""Turn on desktop accessibility the way a screen reader does.

Toolkits decide whether to export an accessibility tree from session state,
and most decide only once, when the application starts:

- GTK 3 and GTK 4 always register on the AT-SPI bus.
- Qt 5/6 and Gecko register only if `org.a11y.Status.IsEnabled` was true
  when they started.
- Chromium and Electron read ACCESSIBILITY_ENABLED / GNOME_ACCESSIBILITY /
  QT_ACCESSIBILITY, then `IsEnabled`, then the GSettings key
  `toolkit-accessibility`, once at startup. Even then they expose only the
  application and its frames until an assistive technology calls
  GetAttributes or GetRelationSet, which they take as the sign that a screen
  reader is present (the heuristic Chromium keeps for Orca).
- GTK 2 needs the gail and atk-bridge GTK modules. A running GTK 2
  application loads them when the XSETTINGS value Gtk/Modules names them.

A plain desktop session has none of this switched on, so applications the
user starts expose no tree. On provider start this module sets `IsEnabled`
(at-spi-bus-launcher mirrors it to `toolkit-accessibility`) and, on Xfce,
adds gail and atk-bridge to Gtk/Modules. GNOME's settings daemon derives
Gtk/Modules from `toolkit-accessibility` itself. Both settings persist in
the user's configuration, as when Orca enables them.

`ScreenReaderEnabled` is never set: no toolkit above needs it, and the bus
launcher mirrors it to the GSettings key that starts Orca on GNOME.

Applications already running when accessibility is switched on stay
invisible if their toolkit reads the setting only at startup;
`invisible_window_notes` names them and says what to restart.
"""

import os
import re
import time

from .foundation import provider_bus_name, safe

try:
    import gi

    gi.require_version("Gio", "2.0")
    from gi.repository import Gio, GLib
except (ImportError, ValueError):
    Gio = None
    GLib = None


A11Y_BUS_NAME = "org.a11y.Bus"
A11Y_BUS_PATH = "/org/a11y/bus"
A11Y_STATUS_INTERFACE = "org.a11y.Status"
XFCONF_NAME = "org.xfce.Xfconf"
XFCONF_PATH = "/org/xfce/Xfconf"
XFCONF_INTERFACE = "org.xfce.Xfconf"
GTK2_ACCESSIBILITY_MODULES = ("gail", "atk-bridge")
DBUS_TIMEOUT_MS = 2000


class SessionBus:
    """The few synchronous session-bus calls this module needs."""

    def __init__(self, connection):
        self.connection = connection

    @classmethod
    def open(cls):
        if Gio is None:
            raise RuntimeError("Gio is unavailable")
        return cls(Gio.bus_get_sync(Gio.BusType.SESSION, None))

    def _call(self, name, path, interface, method, args, reply_type):
        return self.connection.call_sync(
            name, path, interface, method, args,
            GLib.VariantType.new(reply_type) if reply_type else None,
            Gio.DBusCallFlags.NONE, DBUS_TIMEOUT_MS, None,
        )

    def has_owner(self, name):
        reply = self._call(
            "org.freedesktop.DBus", "/org/freedesktop/DBus",
            "org.freedesktop.DBus", "NameHasOwner",
            GLib.Variant("(s)", (name,)), "(b)",
        )
        return bool(reply.unpack()[0])

    def get_property(self, name, path, interface, prop):
        reply = self._call(
            name, path, "org.freedesktop.DBus.Properties", "Get",
            GLib.Variant("(ss)", (interface, prop)), "(v)",
        )
        return reply.unpack()[0]

    def set_bool_property(self, name, path, interface, prop, value):
        self._call(
            name, path, "org.freedesktop.DBus.Properties", "Set",
            GLib.Variant("(ssv)", (interface, prop, GLib.Variant("b", value))),
            None,
        )

    def xfconf_get_string(self, channel, prop):
        reply = self._call(
            XFCONF_NAME, XFCONF_PATH, XFCONF_INTERFACE, "GetProperty",
            GLib.Variant("(ss)", (channel, prop)), "(v)",
        )
        return str(reply.unpack()[0])

    def xfconf_set_string(self, channel, prop, value):
        self._call(
            XFCONF_NAME, XFCONF_PATH, XFCONF_INTERFACE, "SetProperty",
            GLib.Variant("(ssv)", (channel, prop, GLib.Variant("s", value))),
            None,
        )


def _is_missing_property(error):
    return "PropertyNotFound" in str(error)


def merged_gtk_modules(current):
    """Return Gtk/Modules with gail and atk-bridge added, or None if present."""
    modules = [item for item in str(current or "").split(":") if item]
    missing = [item for item in GTK2_ACCESSIBILITY_MODULES if item not in modules]
    if not missing:
        return None
    return ":".join(modules + missing)


def enable_session_accessibility(bus=None):
    """Switch session accessibility on; return what was done, for diagnostics.

    Idempotent: a session that already has accessibility on (for example
    one that exported a11y-env.sh) sees reads and no writes.
    """
    facts = {"changed": [], "errors": []}
    try:
        bus = bus or SessionBus.open()
    except Exception as error:
        facts["errors"].append("session bus: {}".format(error))
        return facts
    try:
        # The call also starts at-spi-bus-launcher through D-Bus activation
        # when no desktop component has started it yet.
        enabled = bool(bus.get_property(
            A11Y_BUS_NAME, A11Y_BUS_PATH, A11Y_STATUS_INTERFACE, "IsEnabled"))
        if not enabled:
            bus.set_bool_property(
                A11Y_BUS_NAME, A11Y_BUS_PATH, A11Y_STATUS_INTERFACE,
                "IsEnabled", True)
            facts["changed"].append("org.a11y.Status.IsEnabled=true")
    except Exception as error:
        facts["errors"].append("org.a11y.Status: {}".format(error))
    try:
        if bus.has_owner(XFCONF_NAME):
            try:
                current = bus.xfconf_get_string("xsettings", "/Gtk/Modules")
            except Exception as error:
                if not _is_missing_property(error):
                    raise
                current = ""
            merged = merged_gtk_modules(current)
            if merged is not None:
                bus.xfconf_set_string("xsettings", "/Gtk/Modules", merged)
                facts["changed"].append("xsettings Gtk/Modules={}".format(merged))
    except Exception as error:
        facts["errors"].append("xsettings Gtk/Modules: {}".format(error))
    return facts


# Applications already asked for their attributes, by bus name. A bus name
# is unique for the lifetime of one connection, so a restarted application
# is asked again.
_attribute_requested = set()


# Time Chromium needs after the first GetAttributes to insert the web
# document below its frame (about 20 ms); a read right after the call
# misses the page.
CHROMIUM_WAKE_SETTLE_SECONDS = 0.15


def request_full_tree(app):
    """Ask an application for its attributes once, as a screen reader does.

    Chromium and Electron treat GetAttributes on any of their objects as the
    sign that an assistive technology is present and only then build the
    tree below their frames; before the call a browser window exposes only
    its frame. Other toolkits answer it as an ordinary
    read. One call per application connection keeps the cost to one round
    trip.
    """
    key = provider_bus_name(app)
    if not key or key in _attribute_requested:
        return False
    _attribute_requested.add(key)
    safe(app.get_attributes)
    return str(safe(app.get_toolkit_name, "") or "") == "Chromium"


def request_full_trees(apps):
    """Wake every new application, then wait once if a Chromium one woke."""
    woke_chromium = False
    for app in apps:
        woke_chromium = request_full_tree(app) or woke_chromium
    if woke_chromium:
        time.sleep(CHROMIUM_WAKE_SETTLE_SECONDS)


# Process image markers, checked against the paths mapped by a process. A
# path is a marker when it ends with the given name or contains it as a
# directory component, so versioned libraries match.
_TOOLKIT_MARKERS = (
    # Prefixes, not file names: vendors rename bundled Qt (WPS Office maps
    # libQt5CoreKso.so.5.12.10).
    ("Qt", ("libQt5Core", "libQt6Core")),
    ("Gecko", ("libxul.so",)),
    ("Chromium", ("chrome_100_percent.pak",)),
    # Before GTK: a Java runtime may load GTK for its look and feel.
    ("Java", ("libjvm.so",)),
    ("GTK 2", ("libgtk-x11-2.0.so",)),
    ("GTK", ("libgtk-3.so", "libgtk-4.so")),
)


def _mapped_library_names(pid, proc_root):
    try:
        with open(os.path.join(proc_root, str(int(pid)), "maps"),
                  encoding="utf-8", errors="replace") as maps:
            return {
                os.path.basename(line.split()[-1])
                for line in maps
                if "/" in line
            }
    except (OSError, ValueError):
        return set()


_QT_CORE_VERSION = re.compile(r"^libQt[56]Core\w*\.so\.(\d+)\.(\d+)")


def process_qt_version(pid, proc_root="/proc"):
    """Return (major, minor) of the Qt core library a process maps, or None."""
    for name in _mapped_library_names(pid, proc_root):
        match = _QT_CORE_VERSION.match(name)
        if match:
            return int(match.group(1)), int(match.group(2))
    return None


def process_toolkit(pid, proc_root="/proc"):
    """Return the UI toolkit a process has mapped, or "" when unknown."""
    names = _mapped_library_names(pid, proc_root)
    for toolkit, markers in _TOOLKIT_MARKERS:
        for marker in markers:
            if any(name.startswith(marker) or name == marker for name in names):
                return toolkit
    return ""


_RESTART_ADVICE = {
    "Qt": (
        "Qt reads the accessibility setting only when it starts; restart the "
        "application"
    ),
    # Qt 5.15 and later also read IsEnabled, which the provider switches on.
    # Earlier Qt reads only ScreenReaderEnabled, which is bound to the
    # desktop's screen-reader setting, so the provider leaves it alone.
    "Qt before 5.15": (
        "This Qt version exports a tree only when started with "
        "QT_LINUX_ACCESSIBILITY_ALWAYS_ON=1 in its environment; restart it "
        "that way"
    ),
    "Java": (
        "Java exports a tree only through the Java ATK wrapper; restart it "
        "with -Djavax.accessibility.assistive_technologies="
        "org.GNOME.Accessibility.AtkWrapper (for example in "
        "JDK_JAVA_OPTIONS) and the wrapper jar and its native library on the "
        "runtime's boot class path and boot library path"
    ),
    "Gecko": (
        "Gecko reads the accessibility setting only when it starts; restart "
        "the application"
    ),
    "Chromium": (
        "Chromium and Electron read the accessibility setting only when they "
        "start; quit every window of the application and start it again"
    ),
    "GTK 2": (
        "GTK 2 needs the gail and atk-bridge modules; restart the application "
        "with GTK_MODULES=gail:atk-bridge if it does not appear within a few "
        "seconds"
    ),
    "GTK": (
        "GTK registers on its own unless it was started with NO_AT_BRIDGE=1 "
        "or without the session's accessibility bus; restart it from the "
        "desktop session"
    ),
}


def invisible_window_notes(managed_windows, accessible_pids, proc_root="/proc",
                           windowless_toolkits=None):
    """Name titled windows whose process has no accessibility application.

    Without this the window is simply absent from the listing, and the
    agent cannot tell an application that exports nothing from one that
    needs a restart.

    ``windowless_toolkits`` maps a process that is on the bus without any
    accessible window to the toolkit names of its registrations. Such a
    process gets the advice when its UI toolkit is not among them: WPS
    Office loads GTK for its Qt platform theme, GTK registers an empty
    application, and the Qt windows stay off the bus.
    """
    windowless_toolkits = windowless_toolkits or {}
    seen = set()
    notes = []
    for window in managed_windows:
        pid = int(window.get("pid") or 0)
        title = str(window.get("title") or "").strip()
        if pid <= 0 or not title or pid in seen:
            continue
        if pid in accessible_pids and pid not in windowless_toolkits:
            continue
        seen.add(pid)
        toolkit = process_toolkit(pid, proc_root)
        if toolkit.lower() in windowless_toolkits.get(pid, ()):
            continue
        version = process_qt_version(pid, proc_root) if toolkit == "Qt" else None
        if version is not None and version < (5, 15):
            advice = _RESTART_ADVICE["Qt before 5.15"]
            toolkit = "Qt {}.{}".format(*version)
        else:
            advice = _RESTART_ADVICE.get(toolkit)
        if advice is None:
            # No known toolkit: nothing would make the application export a
            # tree, and its windows are already listed as having none.
            continue
        notes.append(
            'Not on the accessibility bus: "{}" (pid {}, {}). {}. Until then '
            "only GUI can read and operate it.".format(
                title, pid, toolkit, advice)
        )
    return notes


def untracked_window_notes(managed_windows, accessible_titles):
    """Name on-screen windows of accessible processes that the tree lacks.

    ``accessible_titles`` maps each process on the bus to the titles of its
    AT-SPI top-level windows. Electron shows native message boxes ("A
    setting has changed that requires a restart") as managed windows of the
    same process that never enter its tree, so the listing gave no sign
    that a modal box was waiting. Windows parked off screen are skipped.
    """
    notes = []
    for window in managed_windows:
        pid = int(window.get("pid") or 0)
        title = str(window.get("title") or "").strip()
        titled = {name for name in accessible_titles.get(pid, ()) if name}
        # Desktop components (panels) expose only untitled frames while
        # X11 titles them; only applications with titled trees qualify. The
        # window manager title may add the application name to the
        # accessible one (Qt: "Untitled" vs "Untitled  — Kate"), so a
        # contained title counts as the same window.
        if not titled or not title or any(
                name == title or name in title for name in titled):
            continue
        if int(window.get("x", 0)) + int(window.get("width", 0)) <= 0 or (
                int(window.get("y", 0)) + int(window.get("height", 0)) <= 0):
            continue
        notes.append(
            'Window without an accessibility tree: "{}" (pid {}), a window of '
            "an application listed above. It may be a native message box "
            "waiting for an answer; only GUI can read and operate it.".format(
                title, pid)
        )
    return notes
