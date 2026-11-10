"""Budget boundaries exercised through the real polling loop, with no guest."""

import json
from types import SimpleNamespace

import pytest

from hcu.osworld import agent_loop


@pytest.fixture
def polling(tmp_path, monkeypatch):
    state = SimpleNamespace(now=0.0, polls=0, done_at=None, kills=[], returncode=0)
    token_log = tmp_path / "tokens.jsonl"
    token_log.write_text("")
    monkeypatch.setattr(agent_loop, "time", SimpleNamespace(
        monotonic=lambda: state.now,
        sleep=lambda seconds: setattr(state, "now", state.now + seconds),
    ))
    monkeypatch.setattr(agent_loop.AgentWatchdog, "check", lambda *args: None)

    def fetch(*_args):
        state.polls += 1
        if state.done_at is not None and state.now >= state.done_at:
            return json.dumps({"turn_token": "turn", "returncode": state.returncode}).encode()
        return None

    monkeypatch.setattr(agent_loop, "fetch_guest_file", fetch)
    monkeypatch.setattr(agent_loop, "request_execute", lambda *args, **kwargs: state.kills.append(args))
    monkeypatch.setattr(agent_loop, "save_guest_artifact", lambda *args: None)
    monkeypatch.setattr(agent_loop, "thread_id_from_trace", lambda *args: "thread")

    def write_rows(*rows):
        token_log.write_text("".join(json.dumps(row) + "\n" for row in rows))

    def run(agent="codex", deadline=float("inf")):
        return agent_loop.wait_for_agent_turn(
            base_url="unused", result_dir=tmp_path, label="turn",
            settings=agent_loop.TurnSettings(
                agent=agent, max_model_calls=1, poll_interval=1,
                max_user_simulator_turns=0,
            ),
            token_log=token_log, affinity_task="task-test",
            expected_turn_token="turn",
            workload_unit="unused", deadline=deadline,
        )

    state.write_rows = write_rows
    state.run = run
    state.result_dir = tmp_path
    return state


def test_last_response_tools_can_finish_without_early_kill(polling):
    polling.write_rows({"task": "task-test", "model_step_completed": True})
    polling.done_at = 10
    result = polling.run()
    assert not result["truncated"]
    assert not result["budget_exhausted"]
    assert polling.now == 10
    assert not polling.kills


def test_attempts_and_non_generation_calls_do_not_stop_the_agent(polling):
    polling.write_rows(
        {"task": "task-test", "http_status": 503},
        {"task": "task-test", "path": "/v1/messages/count_tokens", "http_status": 200},
        {"task": "task-other", "budget_rejected": True},
    )
    polling.done_at = 3
    assert not polling.run("claude-code")["truncated"]
    assert not polling.kills


def test_next_generation_rejection_stops_retrying_agent(polling):
    polling.write_rows({"task": "task-test", "budget_rejected": True})
    result = polling.run()
    assert result["truncated"] and result["budget_exhausted"]
    assert len(polling.kills) == 1


def test_later_phase_keeps_the_task_scoped_budget(polling):
    delivered = {"task": "task-test", "model_step_completed": True}
    polling.write_rows(delivered)
    polling.done_at = 0
    assert not polling.run()["truncated"]
    assert not polling.kills

    polling.write_rows(delivered, {"task": "task-test", "budget_rejected": True})
    polling.done_at = None
    result = polling.run()
    assert result["truncated"] and result["budget_exhausted"]
    assert len(polling.kills) == 1


def test_inflight_reservation_rejection_is_not_budget_exhaustion(polling):
    polling.write_rows({"task": "task-test", "budget_inflight": True})
    polling.done_at = 2
    assert not polling.run()["truncated"]
    assert not polling.kills


def test_done_does_not_hide_a_real_budget_rejection(polling):
    polling.write_rows({"task": "task-test", "budget_rejected": True})
    polling.done_at = 0
    assert polling.run()["budget_exhausted"]


@pytest.mark.parametrize("health, limit, ok", [
    ('{"max_completed_responses_per_task": 1}', 1, True),
    ('{"max_completed_responses_per_task": 2}', 1, False),
    ('{"max_completed_responses_per_task": 0}', 1, False),
    ("not json", 1, False),
    ("not json", 0, True),
])
def test_gateway_cap_is_checked_once_before_the_agent_starts(health, limit, ok):
    from hcu.osworld.usage import check_gateway_budget

    if ok:
        check_gateway_budget(health, limit)
    else:
        with pytest.raises(RuntimeError, match="budget mismatch"):
            check_gateway_budget(health, limit)


def test_rate_limit_waits_extend_the_deadline_and_count_as_progress(polling, monkeypatch):
    monkeypatch.setattr(agent_loop, "MODEL_PROGRESS_TIMEOUT_SEC", 5)
    waits = []

    def fetch(*_args):
        polling.polls += 1
        # The gateway waits out a rate limit every 4 s for 12 s of a 5 s deadline.
        if polling.now in (2, 6, 10):
            waits.append({"task": "task-test", "rate_limit_wait": True, "wait_sec": 4})
            polling.write_rows(*waits)
        if polling.now >= 14:
            return b'{"turn_token":"turn","returncode":0}'
        return None

    monkeypatch.setattr(agent_loop, "fetch_guest_file", fetch)
    result = polling.run(deadline=5)
    assert not result["truncated"]
    assert result["rate_limit_wait_sec"] == 12
    assert not polling.kills


@pytest.mark.parametrize("last_response", [False, True])
def test_no_progress_and_last_response_hangs_are_bounded(polling, monkeypatch, last_response):
    monkeypatch.setattr(agent_loop, "MODEL_PROGRESS_TIMEOUT_SEC", 5)
    if last_response:
        polling.write_rows({"task": "task-test", "model_step_completed": True})
    result = polling.run()
    assert result["truncated"] and not result["budget_exhausted"]
    assert len(polling.kills) == 1
    assert polling.now == 8


def test_explicit_shorter_deadline_is_preserved(polling):
    result = polling.run(deadline=2)
    assert result["truncated"] and not result["budget_exhausted"]
    assert polling.now == 5


@pytest.mark.parametrize("failure, message", [
    ({"generation_retry_exhausted": True}, "consecutive generation failure"),
])
@pytest.mark.parametrize("after_stop", [False, True])
def test_completion_fetch_revalidates_new_failure_rows(polling, monkeypatch, failure, message, after_stop):
    def fetch(*_args):
        polling.write_rows({"task": "task-test", **failure})
        return b'{"turn_token":"turn","returncode":0}'

    monkeypatch.setattr(agent_loop, "fetch_guest_file", fetch)
    with pytest.raises(RuntimeError, match=message):
        polling.run(deadline=0 if after_stop else float("inf"))
    assert len(polling.kills) == 1


@pytest.mark.parametrize("failure, message", [
    ({"generation_retry_exhausted": True}, "consecutive generation failure"),
])
def test_budget_failures_export_turn_and_aggregate_artifacts(polling, monkeypatch, failure, message):
    polling.write_rows({"task": "task-test", **failure})
    saved = []

    def save(_base, guest_path, local_path):
        saved.append(guest_path)
        data = guest_path.encode() + b"\n"
        local_path.write_bytes(data)
        return data

    monkeypatch.setattr(agent_loop, "save_guest_artifact", save)
    with pytest.raises(RuntimeError, match=message):
        polling.run()
    assert len(polling.kills) == 1
    assert saved == [agent_loop.GUEST_TRACE, agent_loop.GUEST_STDERR, agent_loop.GUEST_FINAL]
    for name in ("agent.jsonl", "agent.stderr"):
        assert (polling.result_dir / name).read_bytes() == (
            polling.result_dir / f"turn-{name}"
        ).read_bytes()
    assert (polling.result_dir / "turn-final.txt").is_file()


@pytest.mark.parametrize("stop_fails", [False, True])
def test_secondary_cleanup_failures_preserve_original_exception(polling, monkeypatch, stop_fails):
    original = RuntimeError("original budget failure")
    saved = []

    def read_log(*_args):
        raise original

    def save(_base, guest_path, _local_path):
        saved.append(guest_path)
        if guest_path == agent_loop.GUEST_TRACE:
            raise OSError("trace export failed")
        return b"preserved"

    def stop(*_args, **_kwargs):
        raise OSError("stop failed")

    monkeypatch.setattr(agent_loop, "read_task_log", read_log)
    monkeypatch.setattr(agent_loop, "save_guest_artifact", save)
    if stop_fails:
        monkeypatch.setattr(agent_loop, "request_execute", stop)
    with pytest.raises(RuntimeError) as caught:
        polling.run()
    assert caught.value is original
    assert saved == [agent_loop.GUEST_TRACE, agent_loop.GUEST_STDERR, agent_loop.GUEST_FINAL]
    assert (polling.result_dir / "agent.stderr").read_bytes() == b"preserved\n"


@pytest.mark.parametrize("caller_handling_error", [False, True])
def test_successful_turn_does_not_hide_export_failure(polling, monkeypatch, caller_handling_error):
    polling.done_at = 0
    error = OSError("trace export failed")
    saved = []

    def save(_base, guest_path, _local_path):
        saved.append(guest_path)
        if guest_path == agent_loop.GUEST_TRACE:
            raise error
        return None

    monkeypatch.setattr(agent_loop, "save_guest_artifact", save)
    with pytest.raises(OSError) as caught:
        if caller_handling_error:
            try:
                raise RuntimeError("unrelated caller error")
            except RuntimeError:
                polling.run()
        else:
            polling.run()
    assert caught.value is error
    assert len(saved) == 3
    assert not polling.kills
