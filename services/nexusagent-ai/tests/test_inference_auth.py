import httpx
import pytest

from app.main import app, inference
from app.contracts import GenerationResponse


KEY = "test-only-authentication-" + "a" * 32

ENDPOINTS = [
    ("/v1/generate", {"prompt": "Test"}),
    ("/v1/embed", {"text": "Test"}),
    ("/v1/grounded", {
        "question": "Which database?",
        "evidence": [{"source_id": "S1", "excerpt": "PostgreSQL."}],
    }),
]


@pytest.mark.asyncio
@pytest.mark.parametrize("endpoint,payload", ENDPOINTS)
@pytest.mark.parametrize("headers", [
    {},
    {"X-Inference-Key": "incorrect"},
    [("X-Inference-Key", KEY), ("X-Inference-Key", KEY)],
])
async def test_auth_rejects_invalid_credentials(
    monkeypatch, endpoint, payload, headers
):
    monkeypatch.setenv("NEXUS_INFERENCE_API_KEY", KEY)

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app),
        base_url="http://test",
    ) as client:
        response = await client.post(
            endpoint, json=payload, headers=headers
        )

    assert response.status_code == 401


@pytest.mark.asyncio
@pytest.mark.parametrize("endpoint,payload", ENDPOINTS)
async def test_missing_server_secret_fails_closed(
    monkeypatch, endpoint, payload
):
    monkeypatch.delenv("NEXUS_INFERENCE_API_KEY", raising=False)

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app),
        base_url="http://test",
    ) as client:
        response = await client.post(endpoint, json=payload)

    assert response.status_code == 503


@pytest.mark.asyncio
async def test_authenticated_generation(monkeypatch):
    monkeypatch.setenv("NEXUS_INFERENCE_API_KEY", KEY)

    async def fake_generate(request):
        return GenerationResponse(model="local", answer="Accepted")

    monkeypatch.setattr(inference, "generate", fake_generate)

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app),
        base_url="http://test",
    ) as client:
        response = await client.post(
            "/v1/generate",
            json={"prompt": "Test"},
            headers={"X-Inference-Key": KEY},
        )

    assert response.status_code == 200
    assert response.json()["answer"] == "Accepted"


@pytest.mark.asyncio
async def test_public_liveness_without_secret(monkeypatch):
    monkeypatch.delenv("NEXUS_INFERENCE_API_KEY", raising=False)

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app),
        base_url="http://test",
    ) as client:
        response = await client.get("/health/live")

    assert response.status_code == 200