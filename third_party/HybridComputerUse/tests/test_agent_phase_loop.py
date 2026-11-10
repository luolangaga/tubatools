"""run_agent_phase drives turns, resumes and ASK_USER without a VM or model."""

import json
from types import SimpleNamespace

import pytest

from hcu.osworld import agent_loop


def _turn(returncode=0, final="done\nTASK_COMPLETE", thread="th-1", error=""):
    return {
        "done_payload": {"returncode": returncode},
        "truncated": False,
        "budget_exhausted": False,
        "trace_raw": json.dumps({"error": error}).encode(),
        "stderr_raw": b"boom",
        "final_message": final,
        "thread_id": thread,
        "workload_unit": "unit",
        "rate_limit_wait_sec": 0.0,
    }


@pytest.fixture
def phase(monkeypatch, tmp_path):
    calls = SimpleNamespace(launch=[], prompts=[], sleeps=[], turns=[])
    monkeypatch.setattr(agent_loop, "request_execute", lambda *a, **k: {})
    monkeypatch.setattr(
        agent_loop, "request_launch",
        lambda _url, command: calls.launch.append(command) or "unit",
    )
    monkeypatch.setattr(
        agent_loop, "upload_prompt",
        lambda _env, _path, content: calls.prompts.append(content),
    )
    monkeypatch.setattr(agent_loop.time, "sleep", calls.sleeps.append)
    monkeypatch.setattr(
        agent_loop, "wait_for_agent_turn",
        lambda **_kw: calls.turns.pop(0),
    )
    monkeypatch.setattr(
        agent_loop, "summarize_agent_trace",
        lambda raw, _agent: {
            "agent_error": json.loads(raw)["error"],
            "failure_classification": json.loads(raw)["error"] or None,
        },
    )

    def run(agent="codex", user_simulator=None, multi_phase=False):
        env = SimpleNamespace(user_simulator=user_simulator)
        return agent_loop.run_agent_phase(
            env=env, base_url="http://guest", result_dir=tmp_path,
            prompt_path=tmp_path / "prompt.txt", phase_index=1,
            phase_name="main", instruction="do it", current_date=None,
            settings=agent_loop.TurnSettings(
                agent=agent, max_model_calls=0, poll_interval=0,
                max_user_simulator_turns=0,
            ),
            token_log=tmp_path / "tokens.jsonl", affinity_task="t",
            deadline=1e18, multi_phase=multi_phase,
        )

    return calls, run, tmp_path


def test_rate_limited_turn_resumes_the_same_session(phase):
    calls, run, _ = phase
    calls.turns = [
        _turn(returncode=1, error="upstream_rate_limit", thread="th-9"),
        _turn(),
    ]
    result = run()
    assert [command[1:3] for command in calls.launch] == [
        ["start", ""], ["resume", "th-9"],
    ]
    assert calls.sleeps == [agent_loop.RATE_LIMIT_RESUME_BACKOFF_SEC]
    assert [turn["retry_assisted"] for turn in result["turns"]] == [False, True]


def test_rate_limit_resumes_stop_at_the_limit(phase):
    calls, run, _ = phase
    calls.turns = [
        _turn(returncode=1, error="upstream_rate_limit")
        for _ in range(agent_loop.RATE_LIMIT_RESUME_LIMIT + 1)
    ]
    with pytest.raises(RuntimeError, match="turn 3 exited with return code 1"):
        run()
    assert len(calls.sleeps) == agent_loop.RATE_LIMIT_RESUME_LIMIT


def test_unresumable_failure_reports_the_turn_and_error(phase):
    calls, run, _ = phase
    calls.turns = [_turn(returncode=2, error="")]
    with pytest.raises(RuntimeError, match="codex turn 1 exited with return code 2: boom"):
        run()
    assert calls.sleeps == []


def test_ask_user_answer_is_logged_and_sent_back(phase):
    calls, run, result_dir = phase
    simulator = SimpleNamespace(respond=lambda question: "use A4")
    calls.turns = [_turn(final="ASK_USER\nWhich paper size?"), _turn()]
    result = run(user_simulator=simulator, multi_phase=True)
    assert result["user_exchanges"][0]["question"] == "Which paper size?"
    assert calls.launch[1][1:3] == ["resume", "th-1"]
    assert "use A4" in calls.prompts[-1]
    row = json.loads((result_dir / "traj.jsonl").read_text())
    assert row["user_answer"] == "use A4" and row["phase_name"] == "main"
    logged = json.loads((result_dir / "user-simulator.jsonl").read_text())
    assert logged == result["user_exchanges"][0]
