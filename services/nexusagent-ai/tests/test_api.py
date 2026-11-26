import pytest
from fastapi.testclient import TestClient

from app.main import app, inference
from app.contracts import GenerationResponse
from app.ollama import InferenceError


client = TestClient(app)
TEST_KEY = "test-only-inference-key-" + "a" * 32


@pytest.fixture(autouse=True)
def configure_test_auth(monkeypatch):
    monkeypatch.setenv("NEXUS_INFERENCE_API_KEY", TEST_KEY)
    client.headers["X-Inference-Key"] = TEST_KEY


def test_liveness():
    response = client.get("/health/live")
    assert response.status_code == 200
    assert response.json()["status"] == "healthy"


@pytest.mark.parametrize("payload", [
    {"prompt": ""},
    {"prompt": "   "},
    {"prompt": "x" * 16001},
    {"prompt": "test", "max_tokens": 0},
    {"prompt": "test", "max_tokens": 2049},
    {"prompt": "test", "model": "unapproved-model"},
])
def test_invalid_request(payload):
    assert client.post("/v1/generate", json=payload).status_code == 422


def test_generation(monkeypatch):
    async def fake_generate(request):
        return GenerationResponse(
            model="qwen2.5:3b",
            answer="Local answer",
        )

    monkeypatch.setattr(inference, "generate", fake_generate)

    response = client.post(
        "/v1/generate",
        json={"prompt": "Test"},
    )

    assert response.status_code == 200
    assert response.json()["answer"] == "Local answer"


def test_inference_failure(monkeypatch):
    async def fake_generate(request):
        raise InferenceError("Internal details")

    monkeypatch.setattr(inference, "generate", fake_generate)

    response = client.post(
        "/v1/generate",
        json={"prompt": "Test"},
    )

    assert response.status_code == 503
    assert response.json()["detail"] == "Local inference unavailable"