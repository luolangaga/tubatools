"""Messages the harness exchanges with the task agent.

The agent ends every turn with a completion line (``TASK_COMPLETE`` or
``TASK_INFEASIBLE``) or asks one question with ``ASK_USER``. The helpers here
build those prompts and parse the agent's final message.
"""

from __future__ import annotations


COMPLETE_SENTINEL = "TASK_COMPLETE"
INFEASIBLE_SENTINEL = "TASK_INFEASIBLE"
ASK_USER_SENTINEL = "ASK_USER"
DEFAULT_USER_RESPONSE = (
    "I have no further information to provide. "
    "I trust you can figure it out based on the current observation and the "
    "instruction. Please proceed with the next action. DO NOT ask me any more "
    "questions for this task."
)


def task_prompt(
    instruction: str,
    *,
    current_date: str | None = None,
    can_ask_user: bool = True,
) -> str:
    date_context = (
        f"Current date for this task: {current_date}\n\n"
        if current_date
        else ""
    )
    ask_contract = f"""
If information that only the user can supply is genuinely required, stop this
turn and reply in exactly this format:
{ASK_USER_SENTINEL}
<one concise question>
Do not use this for information that can be inspected from the environment.
"""
    if can_ask_user:
        ask_contract += f"""
A task-provided user-response channel is configured. Before replying
{INFEASIBLE_SENTINEL} because user-provided information, credentials, or input
files are missing, you must ask one concise question through {ASK_USER_SENTINEL}
and consider the returned answer. Do not ask about a blocker that the
environment itself can resolve.

If the user's instruction explicitly invites questions and a category, range,
eligibility, status, or assignment rule remains unresolved after inspection,
you must use {ASK_USER_SENTINEL} before any affected send, submit, publish, or
overwrite action. Do not choose a conventional interpretation silently.
Do not infer eligibility for one result from a source statement that discusses
only a different metric or consequence.
Bundle every unresolved rule that can change the submitted result into the
same concise question, then recompute the complete result after the answer.
"""
    return f"""\
{date_context}
{instruction}

Provide any textual answer requested by the user normally. When the task is
complete, put {COMPLETE_SENTINEL} alone on the final non-empty line of your
response. If no textual answer is requested, {COMPLETE_SENTINEL} may be the
entire response.
If the task cannot be completed because of missing credentials, unavailable
required capabilities, contradictory requirements, or another demonstrated
hard blocker, briefly explain the blocker and put {INFEASIBLE_SENTINEL} alone
on the final non-empty line.
{ask_contract}
"""


def split_completion_message(text: str) -> tuple[str, str | None]:
    """Separate a final-line harness status marker from user-facing text."""
    stripped = str(text or "").strip()
    if not stripped:
        return "", None
    lines = stripped.splitlines()
    sentinel = lines[-1].strip()
    if sentinel not in {COMPLETE_SENTINEL, INFEASIBLE_SENTINEL}:
        return stripped, None
    return "\n".join(lines[:-1]).strip(), sentinel


def user_question(final_message: str) -> str | None:
    body, sentinel = split_completion_message(final_message)
    if sentinel is not None:
        return None
    lines = body.splitlines()
    if lines and lines[0].strip() == ASK_USER_SENTINEL:
        question = "\n".join(lines[1:]).strip()
        if not question:
            raise ValueError("ASK_USER response did not include a question")
        return question
    return body


def user_response_prompt(answer: str) -> str:
    return f"""\
User response: {answer}

Treat this answer as resolving only the facts or choices it states. Before any
affected external or irreversible action, re-check the source evidence and the
original instruction for other unresolved eligibility, status, range,
assignment, recipient, or publication rules. If another material rule remains
unresolved and the user invited questions, use ASK_USER again. Do not treat one
answer as blanket approval for assumptions that were not asked or answered.
"""


def transient_resume_prompt(failure_message: str) -> str:
    return (
        "The previous model response stream ended before a completion "
        "declaration because of a transient upstream transport failure. "
        "Continue in this same task session from the current durable VM state. "
        "Do not repeat actions that are already complete. Inspect the current "
        "state before any mutation, then finish the original instruction.\n\n"
        f"Recorded transport failure: {failure_message}"
    )


def tool_argument_resume_prompt() -> str:
    return (
        "The previous shell tool call was rejected before process creation "
        "because its argv payload exceeded the operating-system limit. The "
        "rejected command had no side effects. Continue the original task in "
        "this same session without repeating or embedding that payload. "
        "Process an existing file, or start a small stdin-reading command with "
        "tty=true and stream data through write_stdin. Inspect current state "
        "before the next mutation."
    )
