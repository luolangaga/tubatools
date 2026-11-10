"""Linux provider module: the query prefilter of an AX find walk.

A find walk reads the window past the observe budget (FIND_WALK_LIMITS).
Building the full record of every kept node costs about 0.7 ms per node on
a Chromium page (action table, relations, text attributes, value), which
let an 8 s walk reach only about a third of a 3000-row table. The Go core
matches records against the query; this module decides, from the fields
the query reads, which kept nodes can match. render_tree builds the full
record (record_for) only for those candidates and for their ancestors, so
a match carries exactly the record observe builds for the same node.

The prefilter must never reject a node the Go matcher would accept. It
compares case- and accent-insensitively, which accepts every pair that Go's
strings.ToLower and strings.EqualFold accept and some more; Go then applies
the exact rule to the full record.
"""

import unicodedata

from .ax_node_properties import (
    element_value,
    node_description,
    placeholder_text,
)
from .ax_records import record_for
from .foundation import TOP_LEVEL_CONTAINER_ROLES, limit_text

# Text-only roles: not actionable; their only value is the text they carry.
TEXT_ONLY_ROLES = {"static", "label", "paragraph", "heading", "caption"}


def fold(text):
    """Return ``text`` case-folded with combining marks removed.

    Go lowers U+0130 to "i" and Python to "i" plus U+0307; dropping marks
    makes both forms equal. Accent-insensitive matching only adds candidates.
    """
    decomposed = unicodedata.normalize("NFD", str(text or "").casefold())
    return "".join(
        char for char in decomposed if not unicodedata.combining(char))


class FindFilter:
    """The name and text predicates of an AX find query.

    Role and state are not prefiltered: Go matches a role through its role
    ontology and the role the tree renders, and the state marks need the
    full record. A query with neither name nor text therefore makes every
    kept node a candidate.
    """

    __slots__ = ("name", "name_exact", "text", "text_exact")

    def __init__(self, name="", name_exact=False, text="", text_exact=False):
        self.name = str(name or "")
        self.name_exact = bool(name_exact)
        self.text = str(text or "")
        self.text_exact = bool(text_exact)

    @classmethod
    def from_wire(cls, value):
        """Build the filter from the observation's ``find_query`` object."""
        value = value or {}
        return cls(
            value.get("name", ""), value.get("name_exact", False),
            value.get("text", ""), value.get("text_exact", False))

    @property
    def active(self):
        return bool(self.name or self.text)

    @staticmethod
    def _hit(field, needle, exact):
        if exact:
            return fold(str(field or "").strip()) == fold(needle.strip())
        return fold(needle) in fold(field)

    def matches(self, name, description, value, placeholder):
        """Return whether a node with these fields can match the query.

        ``description``, ``value`` and ``placeholder`` are callables: they
        cost provider reads, and only a text query that the name did not
        decide reads them, in the order Go checks the fields.
        """
        if self.name and not self._hit(name, self.name, self.name_exact):
            return False
        if not self.text:
            return True
        if self._hit(name, self.text, self.text_exact):
            return True
        return any(
            self._hit(read(), self.text, self.text_exact)
            for read in (description, value, placeholder))


def light_record_eligible(raw_role, raw_name):
    """Return whether record_for() would keep this node's role and name.

    record_for() keeps the provider name of a named node and changes the
    role only for editable ``text`` nodes, top-level containers and the
    unnamed toggle of a LibreOffice combo. For any other named node the
    prefilter can read the record's name, description, value and placeholder
    without building the record. Every other node gets its full record
    before the prefilter runs.
    """
    return (
        bool(str(raw_name or "").strip())
        and raw_role != "text"
        and raw_role not in TOP_LEVEL_CONTAINER_ROLES
    )


def candidate_record(find, node, facts, path, window_bounds, text_limit,
                     probe_role, probe_name):
    """Return ``(record or None, candidate)`` for a node the walk keeps.

    A named node that light_record_eligible() admits is checked from the
    probe's name and the fields a text query reads, and gets its record
    only as a candidate. Any other node is checked on its full record. An
    ancestor of the resume node is never a candidate. ``probe_role`` is None
    when the walk did not probe the node.
    """
    may_match = find.relation(path) == "new"
    if probe_role is not None and light_record_eligible(
            probe_role, facts.value("name", "")):
        candidate = may_match and find.query.matches(
            probe_name,
            lambda: node_description(node, text_limit=text_limit, facts=facts),
            lambda: element_value(node, text_limit=text_limit, role=probe_role),
            lambda: placeholder_text(
                node, text_limit=text_limit, role=probe_role),
        )
        if not candidate:
            return None, False
        return record_for(node, None, path, window_bounds,
                          text_limit=text_limit, facts=facts), True
    record = record_for(node, None, path, window_bounds,
                        text_limit=text_limit, facts=facts)
    return record, may_match and find.query.matches(
        record["name"],
        lambda: record["description"],
        lambda: record["value"],
        lambda: record["placeholder"],
    )


def repeats_parent_text(facts, parent_text, text_limit):
    """Return whether a node is a childless text leaf repeating its parent.

    render_tree drops such a leaf whatever its frame and description hold,
    so a find walk decides it from role, name and child count. Any read
    error returns False, and the full probe decides.
    """
    if not parent_text:
        return False
    role, role_error = facts.strict("role")
    if role_error is not None or str(role or "") not in TEXT_ONLY_ROLES:
        return False
    name, name_error = facts.strict("name")
    if name_error is not None or limit_text(
            str(name or ""), text_limit=text_limit) != parent_text:
        return False
    count, count_error = facts.strict("child_count")
    if count_error is not None:
        return False
    try:
        return int(count or 0) == 0
    except Exception:
        return False


FIND_WALK_FIELDS = {"find_query", "find_resume_after"}
FIND_QUERY_FIELDS = {"name": str, "name_exact": bool, "text": str,
                     "text_exact": bool}


def find_fields_problem(observation):
    """Return what is wrong with the find fields of an observation, or ""."""
    query = observation.get("find_query")
    resume = observation.get("find_resume_after")
    if query is None and resume is None:
        return ""
    if observation.get("find_walk") is not True:
        return ".find_query and find_resume_after require find_walk"
    if resume is not None and (
        not isinstance(resume, list) or not resume
        or any(not isinstance(part, int) or isinstance(part, bool)
               or part < 0 for part in resume)
    ):
        return ".find_resume_after must be a non-empty runtime path"
    if query is not None and (not isinstance(query, dict) or any(
            field not in FIND_QUERY_FIELDS
            or not isinstance(value, FIND_QUERY_FIELDS[field])
            for field, value in query.items())):
        return (".find_query takes only the strings name and text and the "
                "booleans name_exact and text_exact")
    return ""


class FindWalk:
    """State of one find walk: its query, where it resumes, where it stopped.

    ``resume_after`` is the runtime path of the first node an earlier walk
    of the same query did not visit; this walk skips the nodes the earlier
    one visited. render_tree sets ``stop_path`` to the first node this walk
    did not visit because the node or character budget or the deadline ran
    out. Those limits only tighten as a walk goes on, so every later node in
    pre-order was left out too, and a walk resuming there misses nothing
    this one skipped for those reasons.
    """

    __slots__ = ("query", "resume_after", "stop_path", "records_omitted")

    def __init__(self, query=None, resume_after=None):
        self.query = query if query is not None else FindFilter()
        self.resume_after = (
            [int(part) for part in resume_after]
            if resume_after is not None else None)
        self.stop_path = None
        # Kept nodes the walk returned no record for; set by render_tree.
        self.records_omitted = 0

    @property
    def candidates_only(self):
        """Whether the records leave out kept nodes of the walked range."""
        return bool(self.records_omitted) or self.resume_after is not None

    def relation(self, path):
        """Place ``path`` relative to the node where the walk resumes.

        Return "searched" for a node an earlier walk already visited in
        pre-order, "ancestor" for an ancestor of the resume node (visited
        again only to reach it, never a candidate), and "new" otherwise.
        """
        resume = self.resume_after
        if resume is None:
            return "new"
        path = list(path)
        if len(path) < len(resume) and resume[:len(path)] == path:
            return "ancestor"
        if path < resume:
            return "searched"
        return "new"

    def note_stop(self, path):
        if self.stop_path is None:
            self.stop_path = [int(part) for part in path]
