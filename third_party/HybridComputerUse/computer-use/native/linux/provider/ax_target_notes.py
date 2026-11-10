"""Model-facing action notes: channel tags and target resolution."""

from .ax_records import effective_name, effective_role

A11Y_CHANNEL = "[a11y]"
GUI_CHANNEL = "[gui]"


def addressing_channel(element_record):
    """Return the note channel: [a11y] for tree targets, [gui] for points."""
    return A11Y_CHANNEL if element_record else GUI_CHANNEL


UNVERIFIED_SYNTHESIS = (
    "Delivery to the intended target was not verified: AT-SPI input synthesis is "
    "global and reports success as soon as the event is queued."
)

# do_action's return value proves only toolkit acceptance, not an effect.
# The acted-on node's state can change without the intended UI transition;
# verification must check the requested outcome, not just that node's state.
UNVERIFIED_SEMANTIC = (
    "The toolkit accepted the call; that is not evidence the action took effect. "
    "AT-SPI actions on Linux routinely report success while doing nothing. "
    "Confirm from the returned state before treating this as done."
)

# Correction appended after a coordinate fallback. It only helps when element
# targeting was possible, so callers decide whether to append it.
PREFER_ELEMENT_INDEX = (
    " This did not use element-targeted invocation. If the target appears in the "
    "accessibility tree, prefer an AX click with its rN ref; it is verified, cheaper, "
    "and does not steal focus."
)


def resolved_note(element, element_record):
    """Describe which element the action was applied to.

    Action notes otherwise say what was done but not to whom. This matters
    most for selectors such as ``push button "Save"``: the caller cannot see
    which live node the selector resolved to.

    The note reports the live node's role and name, not the snapshot
    record's. When they differ it adds a warning: the reference now resolves
    to a different control, a silent retargeting that would otherwise be
    reported as a successful action.
    """
    if element is None or not element_record:
        return None
    live_role = str(effective_role(element) or "")
    live_name = str(effective_name(element) or "")
    if live_name:
        shown = "{} {!r}".format(live_role, live_name)
    else:
        shown = "an unnamed {}".format(live_role or "element")
    note = "Resolved the target ref to {}.".format(shown)

    was_role = str(element_record.get("controlType") or "")
    was_name = str(element_record.get("name") or "")
    drifted = []
    if was_role and was_role != live_role:
        drifted.append("role was {!r}, now {!r}".format(was_role, live_role))
    if was_name and was_name != live_name:
        drifted.append("name was {!r}, now {!r}".format(was_name, live_name))
    if drifted:
        note += (
            " WARNING: this no longer matches the snapshot you read ({}). The "
            "reference resolved to a DIFFERENT control than the one you picked; "
            "re-read the tree before trusting this action.".format("; ".join(drifted))
        )
    return note
