"""Linux provider module: atspi inventory."""

import os
import time
from .foundation import (
    Atspi,
    OperationFailure,
    SETTLE_FOCUS_TIMEOUT_SECONDS,
    SETTLE_MIN_SECONDS,
    SETTLE_EVENT_FLOOR_SECONDS,
    SETTLE_POLL_SECONDS,
    SETTLE_QUIET_SECONDS,
    inventory_complete,
    limit_text,
    node_name,
    node_pid,
    node_role,
    process_generation,
    provider_bus_name,
    state_contains,
)

from .x11_windows import (
    app_windows,
    iter_apps,
)
from .primitives import quoted
from .window_diagnostics import accepts_user_input
from . import ui_events


def wait_for_app_quiet(monitor, bus, min_seconds, floor_seconds,
                       quiet_seconds, clock):
    """Wait until the application has reacted and gone quiet.

    Without the event monitor the settle wait slept a fixed ``min_seconds``.
    With it, the wait ends at the earliest of:

    - ``min_seconds`` after the start, as before (also the whole wait when
      the application emits no event at all);
    - ``quiet_seconds`` after the application's latest event, but not
      before ``floor_seconds``: the reaction to the input has finished.

    Only an application that reacts and then stays silent for
    ``quiet_seconds`` ends the wait early. A window that appears after such
    a silence and before ``min_seconds`` is missed here although the fixed
    wait would have caught it; dialogs measured in GTK, VCL and Gecko
    applications join the tree within about 0.07 s while the application is
    still emitting events, which the floor covers.
    """
    started = clock()
    deadline = started + min_seconds
    floor = started + min(floor_seconds, min_seconds)
    if not monitor.wait_for_event(bus, min_seconds):
        return
    while True:
        now = clock()
        if now >= deadline:
            return
        last = monitor.last_event_at(bus)
        quiet_until = max(
            floor, (last if last is not None else now) + quiet_seconds)
        if now >= quiet_until:
            return
        monitor.wait_for_event(bus, min(quiet_until, deadline) - now)


def wait_for_ui_to_settle(
    app,
    before,
    min_seconds=SETTLE_MIN_SECONDS,
    timeout_seconds=SETTLE_FOCUS_TIMEOUT_SECONDS,
    poll_seconds=SETTLE_POLL_SECONDS,
    clock=time.monotonic,
    sleep=time.sleep,
    monitor=None,
    quiet_seconds=SETTLE_QUIET_SECONDS,
    floor_seconds=SETTLE_EVENT_FLOOR_SECONDS,
):
    """Wait for the UI to settle after an action, before the caller snapshots.

    This closes a race that silently targets the wrong window: a new window
    joins the AT-SPI tree tens of milliseconds before ACTIVE moves to it, so
    a snapshot built in that interval is complete and self-consistent yet
    `main_window()` still selects the main window, and index-based actions
    silently operate on the wrong window. A longer fixed sleep only moves the
    boundary, which slower applications or busier machines hit again, so the
    wait is based on state:

    - First wait for the application to react: with the event monitor
      active, until it has been quiet for ``quiet_seconds`` after its
      reaction, at least ``floor_seconds`` and at most ``min_seconds``
      (wait_for_app_quiet); without the monitor, a fixed ``min_seconds``.
    - If the set of top-level windows is unchanged, nothing opened or
      closed; return at once, so the common path is not slowed down.
    - Otherwise wait until a newly appeared window reports ACTIVE or MODAL,
      or, if windows only closed, until a remaining window takes focus.
      Events from the application end each poll interval early.

    On timeout, return a note instead of None: a new window that did not
    take focus means the snapshot may show a different window than the
    caller expects.

    Limitation: a window that appears after the first phase is not
    detected. Dialogs in Gecko, VCL, GTK and Qt applications usually join
    the AT-SPI tree within about 0.07 s, while the application is still
    emitting events for the action. A slower application yields a snapshot
    of the pre-action state without the new window: a visible failure
    rather than a consistent tree of the wrong window.
    """
    if monitor is None:
        monitor = ui_events.MONITOR
    bus = provider_bus_name(app) if monitor.active else ""
    if bus:
        wait_for_app_quiet(
            monitor, bus, min_seconds, floor_seconds, quiet_seconds, clock)
    else:
        sleep(min_seconds)
    if before is None:
        return None
    deadline = clock() + timeout_seconds
    while True:
        try:
            windows = app_windows(app)
            current = {
                (index, node_role(window), node_name(window))
                for index, window in windows
            }
        except Exception:
            # The action may have closed the application. A failed settle
            # wait must not mask the real error, which the following
            # build_snapshot reports.
            return None
        if current == before:
            return None
        appeared = current - before
        for index, window in windows:
            focused = state_contains(window, Atspi.StateType.ACTIVE) or state_contains(
                window, Atspi.StateType.MODAL
            )
            if not focused:
                continue
            if not appeared or (index, node_role(window), node_name(window)) in appeared:
                return None
        if clock() >= deadline:
            return (
                "The set of top-level windows changed but no window took focus within "
                "{:.1f}s. The snapshot below may show a different window than the one "
                "this action opened — confirm the window title before using any "
                "target ref from it.".format(timeout_seconds)
            )
        if bus:
            monitor.wait_for_event(bus, poll_seconds)
        else:
            sleep(poll_seconds)


def loose(text):
    """Return lowercase alphanumerics only, for separator-insensitive matching.

    A reasonable guess such as `google-chrome` must match the application
    `Google Chrome`; otherwise callers list applications before every
    lookup instead of naming the one they want.
    """
    return "".join(ch for ch in str(text or "").lower() if ch.isalnum())


_GENERIC_EXECUTABLE_PATH_COMPONENTS = {
    "",
    "app",
    "apps",
    "application",
    "applications",
    "bin",
    "lib",
    "lib32",
    "lib64",
    "libexec",
    "local",
    "opt",
    "program",
    "programs",
    "sbin",
    "usr",
}


def executable_identity_names(path):
    """Return conservative product identity candidates from an executable path.

    The executable basename is always evidence. Some packaged applications put
    a launcher with a different name below a generic directory, for example:

        /usr/lib/<product>/program/<launcher>

    In that shape, the nearest non-generic ancestor is also useful product
    identity evidence. Do not keep walking through a meaningful immediate
    parent: `/opt/vendor/browser/browser` must not make `vendor` an alias.
    """
    normalized = os.path.normpath(str(path or "").strip())
    if not normalized or normalized in {".", os.sep}:
        return set()
    parts = [part.strip().lower() for part in normalized.split(os.sep) if part]
    if not parts:
        return set()
    base = parts[-1]
    stem = base.rsplit(".", 1)[0]
    names = {base}
    if stem:
        names.add(stem)
    skipped_generic_parent = False
    for parent in reversed(parts[:-1]):
        parent_stem = parent.rsplit(".", 1)[0]
        if parent in _GENERIC_EXECUTABLE_PATH_COMPONENTS:
            skipped_generic_parent = True
            continue
        if parent in {base, stem} or parent_stem in {base, stem}:
            break
        if skipped_generic_parent:
            names.add(parent)
            if parent_stem:
                names.add(parent_stem)
        break
    return {name for name in names if name}


def process_names(pid, proc_root="/proc"):
    """Return verified process and executable product identities, lowercase.

    AX application names, launcher names, and user-facing product names often
    differ. `/proc` provides process-backed evidence without maintaining a
    product alias table. Ambiguity is still resolved by the caller across all
    current applications rather than selecting the first match.
    """
    names = set()
    if not pid:
        return names
    try:
        comm_path = os.path.join(proc_root, str(int(pid)), "comm")
        with open(comm_path, encoding="utf-8") as handle:
            comm = handle.read().strip().lower()
        if comm:
            names.add(comm)
    except Exception:
        pass
    try:
        cmd_path = os.path.join(proc_root, str(int(pid)), "cmdline")
        with open(cmd_path, "rb") as handle:
            raw = handle.read()
        # Split on NUL, then on whitespace.
        #
        # cmdline is NUL-separated by convention, but not every process
        # keeps it: Chrome can expose its whole command line as one
        # space-separated block. Splitting only on NUL would take that block
        # as argv[0], and its basename, taken from a URL argument, would
        # register a website name as a Chrome alias for resolve_app().
        first = raw.split(b"\0")[0].decode("utf-8", "replace").split()
        if first:
            names.update(executable_identity_names(first[0]))
    except Exception:
        pass
    try:
        exe_path = os.readlink(
            os.path.join(proc_root, str(int(pid)), "exe")
        )
        names.update(executable_identity_names(exe_path))
    except Exception:
        pass
    return names


def matches_app_bootstrap_query(app, query):
    """Match only application/process identity, never a document or dialog title."""
    normalized = str(query or "").strip().lower()
    if not normalized:
        return False
    if normalized.isdigit() and node_pid(app) == int(normalized):
        return True
    tight_query = loose(normalized)
    for candidate in process_names(node_pid(app)):
        tight_process = loose(candidate)
        if tight_query and tight_process and (
            tight_query == tight_process
            or tight_query in tight_process
            or tight_process in tight_query
        ):
            return True
    app_name = str(node_name(app) or "").strip().lower()
    tight_name = loose(app_name)
    return bool(
        app_name == normalized
        or normalized in app_name
        or (
            tight_query
            and tight_name
            and (
                tight_query == tight_name
                or tight_query in tight_name
                or tight_name in tight_query
            )
        )
    )


def input_capable_app_labels():
    """Return one label per application that can take user input.

    Each label carries the process name and window titles. Only
    input-capable applications are listed: including window managers,
    compositors, panels or the desktop itself invites the caller to operate
    on them, and input sent there goes nowhere. The list means "applications
    you can operate", not "processes that are alive".
    """

    running = []
    try:
        for app in iter_apps():
            name = str(node_name(app) or "").strip()
            if not name:
                continue
            if not accepts_user_input(app):
                continue
            # Include the process name. Accessibility names and process names
            # often differ completely (Archive Manager vs file-roller), and
            # callers usually guess the latter; showing both lets them map
            # one to the other instead of guessing again.
            others = sorted(n for n in process_names(node_pid(app))
                            if loose(n) and loose(n) != loose(name))
            label = "{} ({})".format(name, others[0]) if others else name
            # Include window titles as well, so callers can tell which
            # application owns a dialog. Dialog titles such as GIMP's
            # "Script-Fu Console" or "Export Image as JPEG" are natural guesses
            # for an application name, and a bare "gimp" label does not show
            # that these windows belong to it.
            titles = []
            for _, window in app_windows(app):
                title = str(node_name(window) or "").strip()
                if title and loose(title) != loose(name):
                    titles.append(limit_text(title, text_limit=44))
                if len(titles) >= 2:
                    break
            if titles:
                label += " — windows: " + ", ".join(quoted(t) for t in titles)
            running.append(label)
    except Exception:
        pass
    return running


def infrastructure_app_error(app, query):
    """Return the error rejecting a query that matched desktop infrastructure.

    The query must be rejected explicitly rather than served. If any process
    with a top-level window were accepted, window managers, the desktop and
    panels would pass as applications: input sent to them goes nowhere while
    the caller sees one "success" after another.
    """
    name = str(node_name(app) or "").strip() or query
    candidates = input_capable_app_labels()
    listing = (" Applications that can take input right now: {}.".format(
        ", ".join(sorted(set(candidates))[:20])) if candidates else
        " No application with an input-capable window is currently on the "
        "accessibility bus.")
    return RuntimeError(
        'appNotUsable("{}"): {!r} is on the accessibility bus, but every '
        'one of its top-level windows is untitled and exposes no interactive '
        'content. That is the shape of desktop infrastructure — a window manager, '
        'compositor, panel or the desktop backdrop — not of an application window '
        'that can receive user input. Keystrokes and clicks addressed here are '
        'delivered nowhere, and the window would return an empty tree.{} Pass one '
        'of those names, or list windows with AX {{"mode":"apps"}}.'.format(
            query, name, listing))


def resolve_app(query):
    """Resolve an application with an input-capable window by name or PID.

    The query is matched against application display names and process
    names, and a transient failure is retried a few times before resolution
    fails.

    The contract is deliberately narrower than "a process with a top-level
    window". Window managers, compositors, panels and the desktop each own a
    top-level window and would otherwise resolve as applications, so such
    matches are rejected with a list of candidates; see
    infrastructure_app_error.

    Enumeration can fail transiently while an application rebuilds its
    windows. With LibreOffice 7.3, a click that opened or closed a dialog was
    followed by `appNotFound("soffice")` although the application stayed on
    the AT-SPI desktop throughout: any link in the enumeration chain
    (`Atspi.get_desktop`, `get_child_count`, `get_name`) can fail
    momentarily, and `safe()` turns that failure into absence. Unreadable is
    not absent. Reporting it as absent misinforms the agent, which then tries
    other names, restarts the application or gives up, when reading again
    would have succeeded.

    The retry limit stays small so that a truly missing application does not
    keep the caller waiting.
    """
    from .ax_actions import RESOLVE_APP_ATTEMPTS, RESOLVE_APP_RETRY_SECONDS

    last_error = None
    # An application that matched but has only infrastructure windows is
    # neither a match nor a miss: keep looking at other candidates, and if
    # none is found, use it to explain the rejection, which is more useful
    # than a not-found error.
    infrastructure = None
    incomplete_inventory = False
    for attempt in range(RESOLVE_APP_ATTEMPTS):
        try:
            matches = []
            inventory = iter_apps()
            incomplete_inventory = not inventory_complete(inventory)
            for app in inventory:
                if matches_app_bootstrap_query(app, query):
                    if accepts_user_input(app):
                        matches.append(app)
                        continue
                    infrastructure = app
            if incomplete_inventory:
                last_error = RuntimeError(
                    "accessibility application inventory was incomplete"
                )
                if attempt + 1 < RESOLVE_APP_ATTEMPTS:
                    time.sleep(RESOLVE_APP_RETRY_SECONDS)
                    continue
                break
            if len(matches) == 1:
                return matches[0]
            if len(matches) > 1:
                labels = sorted({
                    node_name(app) or "(unnamed application)"
                    for app in matches
                })
                # The server turns these process identities into the wN
                # of each matching window; the provider cannot issue refs.
                raise OperationFailure(
                    'appTargetAmbiguous("{}"): {} input-capable accessibility '
                    'applications match this bootstrap query: {}. No action was '
                    'attempted. Pass the wN of one window as window_ref; AX '
                    '{{"mode":"apps"}} lists the windows.'.format(
                        query, len(matches), ", ".join(labels[:10])),
                    failure_code="ax_app_ambiguous",
                    failure_phase="precondition",
                    input_events_emitted=False,
                    candidates=[
                        {
                            "name": node_name(app) or "",
                            "providerBusName": provider_bus_name(app),
                            "processGeneration": process_generation(node_pid(app)),
                        }
                        for app in matches
                    ],
                    candidate_count=len(matches),
                )
            last_error = None
        except Exception as error:  # A failed enumeration counts as a miss.
            if isinstance(error, OperationFailure):
                raise
            last_error = error
        if attempt + 1 < RESOLVE_APP_ATTEMPTS:
            time.sleep(RESOLVE_APP_RETRY_SECONDS)
    if infrastructure is not None:
        raise infrastructure_app_error(infrastructure, query)
    if last_error is not None:
        raise OperationFailure(
            'accessibilityChannelUnavailable("{}") after {} attempts: the accessibility '
            'bus could not enumerate the requested application (last error: {}). This '
            'does not prove the application is closed or absent from the visual desktop. '
            'No action was attempted; inspect gui with actions=[] and retry accessibility '
            'after the window settles.'.format(
                query, RESOLVE_APP_ATTEMPTS, last_error),
            failure_code="ax_app_unavailable",
            failure_phase="precondition",
            input_events_emitted=False,
            provider_reset_required=True,
        )
    # On failure, list the applications that can take input, so a wrong
    # guess costs no extra round trip: the next call can use the right name
    # instead of listing applications first. A dead-end error and one with
    # candidates are very different outcomes for the agent.
    running = input_capable_app_labels()
    if running:
        raise OperationFailure(
            'appNotVisibleToAccessibility("{}"): no current accessibility application '
            'matched this query. This does not prove the application is not running or '
            'visible in GUI, and no action was attempted. Applications currently visible to the '
            'accessibility bus, with their process names in parentheses: {}. '
            'Names are matched case- and separator-insensitively against the '
            'application display name and process name, so both '
            '"google-chrome" and "file-roller" resolve.'.format(
                query, ", ".join(sorted(set(running))[:20])),
            failure_code="ax_app_unavailable",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    raise OperationFailure(
        'appNotVisibleToAccessibility("{}"): no input-capable application is currently '
        'visible on the accessibility bus. This does not prove the application is not '
        'running or visible in GUI, and no action was attempted. Inspect gui with '
        'actions=[] before deciding the application is absent.'.format(query),
        failure_code="ax_app_unavailable",
        failure_phase="precondition",
        input_events_emitted=False,
    )


def resolve_app_identity(identity):
    from .ax_actions import RESOLVE_APP_ATTEMPTS, RESOLVE_APP_RETRY_SECONDS

    provider_bus = str((identity or {}).get("providerBusName") or "").strip()
    generation = str((identity or {}).get("processGeneration") or "").strip()
    if not provider_bus or not generation:
        raise OperationFailure(
            "invalid_app_ref: the server-owned application identity is incomplete; "
            "no action was attempted.",
            failure_code="invalid_target",
            failure_phase="precondition",
            input_events_emitted=False,
        )
    final_complete = False
    for attempt in range(RESOLVE_APP_ATTEMPTS):
        try:
            inventory = iter_apps()
        except Exception:
            final_complete = False
            if attempt + 1 < RESOLVE_APP_ATTEMPTS:
                time.sleep(RESOLVE_APP_RETRY_SECONDS)
                continue
            break
        final_complete = inventory_complete(inventory)
        matches = []
        for app in inventory:
            if (
                provider_bus_name(app) == provider_bus
                and process_generation(node_pid(app)) == generation
            ):
                matches.append(app)
        if len(matches) == 1:
            if not accepts_user_input(matches[0]):
                raise infrastructure_app_error(matches[0], "app_ref")
            return matches[0]
        if len(matches) > 1:
            raise OperationFailure(
                "ambiguous_app_ref: the exact provider identity resolved to multiple "
                "applications; no action was attempted.",
                failure_code="ambiguous_target",
                failure_phase="precondition",
                input_events_emitted=False,
            )
        if attempt + 1 < RESOLVE_APP_ATTEMPTS:
            time.sleep(RESOLVE_APP_RETRY_SECONDS)
    if not final_complete:
        raise OperationFailure(
            "observation_incomplete: the accessibility application inventory "
            "could not be read completely after bounded readiness retries; no "
            "action was attempted.",
            failure_code="observation_incomplete",
            failure_phase="precondition",
            input_events_emitted=False,
            failure_subject="app",
            provider_reset_required=True,
        )
    raise OperationFailure(
        "stale_app_ref: the application process generation no longer exists; "
        "no action was attempted. Observe applications again.",
        failure_code="stale_app_ref",
        failure_phase="precondition",
        input_events_emitted=False,
        failure_subject="app",
    )


def resolve_operation_app(operation):
    identity = operation.get("app_identity")
    if identity is not None:
        return resolve_app_identity(identity)
    return resolve_app(operation.get("app", ""))
