import httpx
import pytest

from app.main import app, inference
from app.contracts import GenerationResponse
from app.ollama import InferenceError


KEY = "test-only-inference-key-" + "a" * 32


@pytest.fixture(autouse=True)
def configure_test_auth(monkeypatch):
    monkeypatch.setenv("NEXUS_INFERENCE_API_KEY", KEY)


def client_for_app():
    return httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app),
        base_url="http://test",
        headers={"X-Inference-Key": KEY},
    )


@pytest.mark.asyncio
async def test_liveness():
    async with client_for_app() as client:
        response = await client.get("/health/live")

    assert response.status_code == 200
    assert response.json()["status"] == "healthy"


@pytest.mark.asyncio
@pytest.mark.parametrize("payload", [
    {"prompt": ""},
    {"prompt": "   "},
    {"prompt": "x" * 16001},
    {"prompt": "test", "max_tokens": 0},
    {"prompt": "test", "max_tokens": 2049},
    {"prompt": "test", "model": "unapproved-model"},
])
async def test_invalid_request(payload):
    async with client_for_app() as client:
        response = await client.post(
            "/v1/generate", json=payload
        )

    assert response.status_code == 422


@pytest.mark.asyncio
async def test_generation(monkeypatch):
    async def fake_generate(request):
        return GenerationResponse(
            model="qwen2.5:3b",
            answer="Local answer",
        )

    monkeypatch.setattr(inference, "generate", fake_generate)

    async with client_for_app() as client:
        response = await client.post(
            "/v1/generate", json={"prompt": "Test"}
        )

    assert response.status_code == 200
    assert response.json()["answer"] == "Local answer"


@pytest.mark.asyncio
async def test_inference_failure(monkeypatch):
    async def fake_generate(request):
        raise InferenceError("Internal details")

    monkeypatch.setattr(inference, "generate", fake_generate)

    async with client_for_app() as client:
        response = await client.post(
            "/v1/generate", json={"prompt": "Test"}
        )

    assert response.status_code == 503
    assert response.json()["detail"] == "Local inference unavailable"