"""Provider-independent text identity and rendering primitives."""

import hashlib


def quoted(text):
    escaped = str(text).replace("\\", "\\\\").replace('"', '\\"')
    escaped = escaped.replace("\r", "\\r").replace("\n", "\\n")
    return '"' + escaped + '"'


def semantic_text_key(value):
    """Match model-facing semantic text after whitespace normalization."""
    return " ".join(str(value or "").split())


def name_fingerprint(text):
    """Stable fingerprint for names that are truncated in model output."""
    return hashlib.blake2b(
        str(text or "").encode("utf-8"),
        digest_size=8,
    ).hexdigest()
