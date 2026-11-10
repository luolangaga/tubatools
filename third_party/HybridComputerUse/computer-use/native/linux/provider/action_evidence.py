"""Operation-scoped action evidence shared by Linux provider modules."""

_CURRENT_ACTION_EVIDENCE = None


def reset():
    global _CURRENT_ACTION_EVIDENCE
    _CURRENT_ACTION_EVIDENCE = None


def replace(evidence):
    global _CURRENT_ACTION_EVIDENCE
    _CURRENT_ACTION_EVIDENCE = evidence


def current():
    return _CURRENT_ACTION_EVIDENCE


def update(**fields):
    if _CURRENT_ACTION_EVIDENCE is not None:
        _CURRENT_ACTION_EVIDENCE.update(fields)


def snapshot():
    return dict(_CURRENT_ACTION_EVIDENCE or {})
