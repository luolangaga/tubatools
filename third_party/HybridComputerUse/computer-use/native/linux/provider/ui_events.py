"""AT-SPI event monitor: answers "did this application emit any event?".

The monitor is a dirty bit, not a cache of provider values. Event payloads
are never used to update a field: an event only proves that something in
the application may have changed. Several LibreOffice properties change
without an event on the changed object, so events cannot say which field
changed.

The provider process runs no GLib main loop, so events queue on the D-Bus
connection between requests. pump() dispatches the backlog. D-Bus delivers
the messages of one sender in order, so after a method call to an
application returns, every event that application emitted before replying
is already queued; pumping after such a read therefore sees them all. If
the backlog cannot be drained within the pump budget, the monitor marks
every application dirty instead of trusting a partial count.
"""

import time

from .foundation import Atspi

try:
    from gi.repository import GLib
except (ImportError, ValueError):  # pragma: no cover - GLib ships with gi
    GLib = None


# Event classes whose arrival marks an application dirty. "object:" covers
# children, state, property, text, selection, active-descendant, bounds,
# value and attribute changes; "window:" covers create, destroy, activate
# and deactivate; "document:" covers load and reload. A narrower list would
# spare the applications some signal emission, but every narrower list
# considered omitted a change class that the snapshot renders.
EVENT_CLASSES = ("object:", "window:", "document:")

# Maximum time one pump may spend dispatching queued events. A dialog opening
# in LibreOffice queues about 2,500 events, which take about 40 ms.
PUMP_BUDGET_SECONDS = 0.25

# Applications learn about a new listener asynchronously through the
# registry, so events from the first moments after registration can be
# lost. Snapshots whose walk started in this window are never reused.
REGISTRATION_GRACE_SECONDS = 1.0

# Sleep between pumps while waiting for events.
WAIT_STEP_SECONDS = 0.004


class EventMonitor:
    """Per-application event generations, fed by one AT-SPI listener."""

    def __init__(self, clock=time.monotonic, sleep=time.sleep):
        self.clock = clock
        self.sleep = sleep
        self.active = False
        self.registered_at = None
        self._listener = None
        self._generations = {}
        self._last_event_at = {}
        # Bumped by events whose application cannot be identified and by
        # undrained backlogs: it invalidates every application at once.
        self._global_generation = 0

    def start(self):
        """Register the listener; on any failure the monitor stays inactive."""
        if self.active:
            return True
        if GLib is None:
            return False
        try:
            listener = Atspi.EventListener.new(self._on_event)
            for event_class in EVENT_CLASSES:
                if not listener.register(event_class):
                    raise RuntimeError("registration refused: " + event_class)
        except Exception:
            self._listener = None
            return False
        self._listener = listener
        self.registered_at = self.clock()
        self.active = True
        return True

    def _on_event(self, event):
        # Keep this callback cheap: a dialog can emit thousands of events.
        # app.bus_name is a field of an object libatspi already holds;
        # reading it makes no D-Bus call.
        try:
            bus = event.source.app.bus_name
        except Exception:
            bus = None
        self.record(bus)
        # When an application leaves the desktop, the registry announces it
        # with a children-changed event from the registry's own bus name; the
        # departed application is the event's child. Count it as an event of
        # that application too, so that state cached for it is dropped.
        try:
            if str(event.type).startswith("object:children-changed"):
                child_bus = event.any_data.app.bus_name
                if child_bus and child_bus != bus:
                    self.record(child_bus)
        except Exception:
            pass

    def record(self, bus):
        """Count one event from ``bus``; None means the source is unknown."""
        now = self.clock()
        if not bus:
            self._global_generation += 1
            return
        self._generations[bus] = self._generations.get(bus, 0) + 1
        self._last_event_at[bus] = now

    def pump(self, budget_seconds=PUMP_BUDGET_SECONDS):
        """Dispatch queued events; return False if the backlog was not drained."""
        if not self.active:
            return False
        context = GLib.MainContext.default()
        deadline = self.clock() + budget_seconds
        while context.pending():
            if self.clock() >= deadline:
                self._global_generation += 1
                return False
            context.iteration(False)
        return True

    def mark(self, bus):
        """Return an opaque token for "the event state of ``bus`` now"."""
        return (self._global_generation, self._generations.get(bus, 0))

    def unchanged_since(self, bus, token):
        return self.active and token == self.mark(bus)

    def last_event_at(self, bus):
        return self._last_event_at.get(bus)

    def trusted_after(self, started_at):
        """Return whether a walk started at ``started_at`` saw every event."""
        return (
            self.active
            and self.registered_at is not None
            and started_at >= self.registered_at + REGISTRATION_GRACE_SECONDS
        )

    def wait_for_event(self, bus, seconds):
        """Wait up to ``seconds`` for an event from ``bus``; True if one came."""
        token = self.mark(bus)
        deadline = self.clock() + seconds
        while True:
            self.pump()
            if self.mark(bus) != token:
                return True
            remaining = deadline - self.clock()
            if remaining <= 0:
                return False
            self.sleep(min(WAIT_STEP_SECONDS, remaining))


MONITOR = EventMonitor()


def start():
    return MONITOR.start()


def pump():
    return MONITOR.pump()
