import json

import pytest

from app.ollama import InferenceError
from app.retrieval import (
    EmbeddingRequest,
    Evidence,
    GroundedRequest,
    RetrievalClient,
)


@pytest.mark.asyncio
@pytest.mark.parametrize("vector", [
    [],
    [0.0] * 1536,
    [1.0] * 1535,
    [float("nan")] * 1536,
    [float("inf")] * 1536,
    ["invalid"] * 1536,
])
async def test_invalid_embedding(monkeypatch, vector):
    client = RetrievalClient()

    async def fake_post(endpoint, payload):
        return {"embeddings": [vector]}

    monkeypatch.setattr(client, "post", fake_post)

    with pytest.raises(InferenceError):
        await client.embed(EmbeddingRequest(text="test"))


@pytest.mark.asyncio
async def test_valid_embedding(monkeypatch):
    client = RetrievalClient()

    async def fake_post(endpoint, payload):
        return {"embeddings": [[0.1] * 1536]}

    monkeypatch.setattr(client, "post", fake_post)

    result = await client.embed(EmbeddingRequest(text="test"))
    assert len(result.embedding) == 1536


@pytest.mark.asyncio
@pytest.mark.parametrize("citations,valid", [
    (["S1"], True),
    (["unknown"], False),
    ([], True),
])
async def test_grounded_citations(monkeypatch, citations, valid):
    client = RetrievalClient()

    async def fake_post(endpoint, payload):
        return {
            "done": True,
            "response": json.dumps({
                "answer": "Test answer",
                "cited_source_ids": citations,
            }),
        }

    monkeypatch.setattr(client, "post", fake_post)

    request = GroundedRequest(
        question="Test question",
        evidence=[Evidence(source_id="S1", excerpt="Test evidence")],
    )

    if valid:
        result = await client.grounded(request)
        assert result.cited_source_ids == citations

        if not citations:
            assert result.answer == "Insufficient evidence."
    else:
        with pytest.raises(InferenceError):
            await client.grounded(request)