from __future__ import annotations

import json
from types import SimpleNamespace

import httpx
import pytest

from app.ai.openai_compatible import generate_openai_compatible_json_completion
from app.ai.service import _build_knowledge_summary_system_prompt, _build_knowledge_summary_user_prompt
from app.core.exceptions import DependencyUnavailableError
from app.domains.knowledge.ai_summary import validate_knowledge_summary_content


class _FakeResponse:
    def __init__(self, payload: dict, status_code: int = 200, text: str | None = None) -> None:
        self._payload = payload
        self.status_code = status_code
        self.text = text if text is not None else json.dumps(payload)

    def raise_for_status(self) -> None:
        if self.status_code >= 400:
            raise httpx.HTTPStatusError(
                "provider failure",
                request=httpx.Request("POST", "http://127.0.0.1:11434/v1/chat/completions"),
                response=httpx.Response(self.status_code),
            )

    def json(self) -> dict:
        return self._payload


def test_openai_compatible_provider_parses_json_content(monkeypatch: pytest.MonkeyPatch) -> None:
    request_kwargs: dict[str, object] = {}

    def _fake_post(*args, **kwargs):
        request_kwargs.update(kwargs)
        return _FakeResponse(
            {
                "choices": [
                    {
                        "message": {
                            "content": json.dumps(
                                {
                                    "summary": "Provider summary.",
                                    "key_points": ["Point one.", "Point two."],
                                    "keywords": ["provider", "summary"],
                                }
                            )
                        }
                    }
                ]
            }
        )

    monkeypatch.setattr(
        "app.ai.openai_compatible.httpx.post",
        _fake_post,
    )

    result = generate_openai_compatible_json_completion(
        base_url="http://127.0.0.1:11434/v1",
        api_key="",
        model="qwen3.5:9b",
        timeout_seconds=60,
        system_prompt="Return JSON only.",
        user_prompt='{"title":"Provider note"}',
    )

    assert result == {
        "summary": "Provider summary.",
        "key_points": ["Point one.", "Point two."],
        "keywords": ["provider", "summary"],
    }
    assert request_kwargs["json"]["max_tokens"] == 1800
    assert request_kwargs["json"]["response_format"] == {"type": "json_object"}


def test_openai_compatible_provider_raises_dependency_unavailable_on_transport_failure(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def _raise_http_error(*args, **kwargs):
        raise httpx.ConnectError("connection refused")

    monkeypatch.setattr("app.ai.openai_compatible.httpx.post", _raise_http_error)

    with pytest.raises(DependencyUnavailableError, match="AI provider request failed"):
        generate_openai_compatible_json_completion(
            base_url="http://127.0.0.1:11434/v1",
            api_key="",
            model="qwen3.5:9b",
            timeout_seconds=60,
            system_prompt="Return JSON only.",
            user_prompt='{"title":"Provider note"}',
        )


def test_openai_compatible_provider_raises_non_2xx_error(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(
        "app.ai.openai_compatible.httpx.post",
        lambda *args, **kwargs: _FakeResponse({"error": "provider failure"}, status_code=502),
    )

    with pytest.raises(DependencyUnavailableError, match="AI provider returned a non-2xx response"):
        generate_openai_compatible_json_completion(
            base_url="http://127.0.0.1:11434/v1",
            api_key="",
            model="qwen3.5:9b",
            timeout_seconds=60,
            system_prompt="Return JSON only.",
            user_prompt='{"title":"Provider note"}',
        )


def test_openai_compatible_provider_raises_timeout_error(monkeypatch: pytest.MonkeyPatch) -> None:
    def _raise_timeout(*args, **kwargs):
        raise httpx.ReadTimeout("timed out")

    monkeypatch.setattr("app.ai.openai_compatible.httpx.post", _raise_timeout)

    with pytest.raises(DependencyUnavailableError, match="AI provider request timed out"):
        generate_openai_compatible_json_completion(
            base_url="http://127.0.0.1:11434/v1",
            api_key="",
            model="qwen3.5:9b",
            timeout_seconds=60,
            system_prompt="Return JSON only.",
            user_prompt='{"title":"Provider note"}',
        )


def test_openai_compatible_provider_reports_empty_content_with_debug_log(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    captured_event: dict[str, object] = {}

    def _capture_log_event(logger, *, level, event, **fields):
        captured_event["level"] = level
        captured_event["event"] = event
        captured_event["fields"] = fields

    monkeypatch.setattr("app.ai.openai_compatible.log_event", _capture_log_event)
    payload = {
        "choices": [
            {
                "message": {
                    "content": "",
                    "reasoning": "Thinking Process: ...",
                },
                "finish_reason": "length",
            }
        ]
    }
    monkeypatch.setattr(
        "app.ai.openai_compatible.httpx.post",
        lambda *args, **kwargs: _FakeResponse(payload, text=json.dumps(payload)),
    )

    with pytest.raises(DependencyUnavailableError, match="AI provider returned empty completion content"):
        generate_openai_compatible_json_completion(
            base_url="http://127.0.0.1:11434/v1",
            api_key="",
            model="qwen3.5:9b",
            timeout_seconds=60,
            system_prompt="Return JSON only.",
            user_prompt='{"title":"Provider note"}',
        )

    assert captured_event["event"] == "ai_provider_empty_completion_content"
    assert captured_event["fields"]["finish_reason"] == "length"
    assert captured_event["fields"]["message_payload"] == {"content": "", "reasoning": "Thinking Process: ..."}


def test_openai_compatible_provider_rejects_non_json_content(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(
        "app.ai.openai_compatible.httpx.post",
        lambda *args, **kwargs: _FakeResponse(
            {
                "choices": [
                    {
                        "message": {
                            "content": "summary: not valid json",
                        }
                    }
                ]
            }
        ),
    )

    with pytest.raises(DependencyUnavailableError, match="AI provider returned non-JSON completion content"):
        generate_openai_compatible_json_completion(
            base_url="http://127.0.0.1:11434/v1",
            api_key="",
            model="qwen3.5:9b",
            timeout_seconds=60,
            system_prompt="Return JSON only.",
            user_prompt='{"title":"Provider note"}',
        )


def test_knowledge_summary_provider_integration_rejects_empty_content(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    payload = {
        "choices": [
            {
                "message": {"content": "", "reasoning": "Thinking Process: ..."},
                "finish_reason": "length",
            }
        ]
    }
    monkeypatch.setattr(
        "app.ai.openai_compatible.httpx.post",
        lambda *args, **kwargs: _FakeResponse(payload),
    )

    with pytest.raises(DependencyUnavailableError, match="AI provider returned empty completion content"):
        _generate_and_validate_knowledge_summary()


def test_knowledge_summary_provider_integration_rejects_non_json_content(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        "app.ai.openai_compatible.httpx.post",
        lambda *args, **kwargs: _FakeResponse(
            {"choices": [{"message": {"content": "not json"}, "finish_reason": "stop"}]}
        ),
    )

    with pytest.raises(DependencyUnavailableError, match="AI provider returned non-JSON completion content"):
        _generate_and_validate_knowledge_summary()


def test_knowledge_summary_provider_integration_rejects_invalid_payload_shape(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        "app.ai.openai_compatible.httpx.post",
        lambda *args, **kwargs: _FakeResponse(
            {
                "choices": [
                    {
                        "message": {
                            "content": json.dumps(
                                {
                                    "key_points": ["Point one."],
                                    "keywords": ["knowledge"],
                                }
                            )
                        },
                        "finish_reason": "stop",
                    }
                ]
            }
        ),
    )

    with pytest.raises(ValueError, match="knowledge_summary content_json must contain exactly summary, key_points, and keywords"):
        _generate_and_validate_knowledge_summary()


def test_knowledge_summary_provider_integration_accepts_valid_json(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        "app.ai.openai_compatible.httpx.post",
        lambda *args, **kwargs: _FakeResponse(
            {
                "choices": [
                    {
                        "message": {
                            "content": json.dumps(
                                {
                                    "summary": "A concise note summary.",
                                    "key_points": ["Point one.", "Point two."],
                                    "keywords": ["knowledge", "summary"],
                                }
                            )
                        },
                        "finish_reason": "stop",
                    }
                ]
            }
        ),
    )

    result = _generate_and_validate_knowledge_summary()

    assert result["summary"] == "A concise note summary."
    assert result["key_points"] == ["Point one.", "Point two."]
    assert result["keywords"] == ["knowledge", "summary"]


def test_knowledge_summary_user_prompt_omits_null_fields() -> None:
    knowledge_entry = SimpleNamespace(
        title=None,
        content="A compact content body.",
        source_text=None,
    )

    user_prompt = _build_knowledge_summary_user_prompt(knowledge_entry)

    assert user_prompt == '{"content":"A compact content body."}'


def _generate_and_validate_knowledge_summary() -> dict[str, object]:
    knowledge_entry = SimpleNamespace(
        title="Weekly review notes",
        content="This week I refactored the ingestion pipeline, fixed retry edge cases, and kept the local-first scope narrow.",
        source_text="Need a short explanation-oriented summary for later lookup.",
    )
    result = generate_openai_compatible_json_completion(
        base_url="http://127.0.0.1:11434/v1",
        api_key="",
        model="qwen3.5:9b",
        timeout_seconds=60,
        system_prompt=_build_knowledge_summary_system_prompt(),
        user_prompt=_build_knowledge_summary_user_prompt(knowledge_entry),
    )
    validate_knowledge_summary_content(result)
    return result
