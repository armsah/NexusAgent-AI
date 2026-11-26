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

    async def accepted_verification(question, sentence):
        return True

    monkeypatch.setattr(
        client, "verify_answerability", accepted_verification
    )

    async def fake_post(endpoint, payload):
        return {"embeddings": [vector]}

    monkeypatch.setattr(client, "post", fake_post)

    with pytest.raises(InferenceError):
        await client.embed(EmbeddingRequest(text="test"))


@pytest.mark.asyncio
async def test_valid_embedding(monkeypatch):
    client = RetrievalClient()

    async def accepted_verification(question, sentence):
        return True

    monkeypatch.setattr(
        client, "verify_answerability", accepted_verification
    )

    async def fake_post(endpoint, payload):
        return {"embeddings": [[0.1] * 1536]}

    monkeypatch.setattr(client, "post", fake_post)

    result = await client.embed(EmbeddingRequest(text="test"))
    assert len(result.embedding) == 1536


@pytest.mark.asyncio
@pytest.mark.parametrize("answer,valid,accepted", [
    ("PostgreSQL", True, True),
    ("NONE", True, False),
    ("", True, False),
    ("Oracle", True, False),
    ("No information is available.", True, False),
    (True, False, False),
    (None, False, False),
    (123, False, False),
])
async def test_extraction_contract(monkeypatch, answer, valid, accepted):
    client = RetrievalClient()

    async def fake_post(endpoint, payload):
        assert endpoint == "api/generate"
        properties = payload["format"]["properties"]

        if "missing_constraints" in properties:
            return {
                "done": True,
                "response": json.dumps({"missing_constraints": []}),
            }



        result = (
            {"answer": answer}
            if "answer" in properties
            else {"supported": False}
        )

        return {
            "done": True,
            "response": json.dumps(result),
        }

    monkeypatch.setattr(client, "post", fake_post)

    if not valid:
        with pytest.raises(InferenceError):
            await client.assess_relevance(
                "Which database is used?",
                "The application uses PostgreSQL.",
            )
        return

    result = await client.assess_relevance(
        "Which database is used?",
        "The application uses PostgreSQL.",
    )

    assert result is accepted


@pytest.mark.asyncio
@pytest.mark.parametrize("primary,secondary,accepted", [
    ("PostgreSQL", True, True),
    ("NONE", True, False),
    ("PostgreSQL", False, False),
    ("Oracle", True, False),
])
async def test_dual_extraction(monkeypatch, primary, secondary, accepted):
    client = RetrievalClient()

    async def fake_post(endpoint, payload):
        properties = payload["format"]["properties"]

        if "missing_constraints" in properties:
            return {
                "done": True,
                "response": json.dumps({"missing_constraints": []}),
            }



        result = (
            {"answer": primary}
            if "answer" in properties
            else {"supported": secondary}
        )

        return {
            "done": True,
            "response": json.dumps(result),
        }

    monkeypatch.setattr(client, "post", fake_post)

    result = await client.grounded(GroundedRequest(
        question="Which database is used?",
        evidence=[
            Evidence(
                source_id="S1",
                excerpt="The application uses PostgreSQL.",
            )
        ],
    ))

    assert bool(result.cited_source_ids) is accepted

    if accepted:
        assert result.answer == "The application uses PostgreSQL [S1]."
    else:
        assert result.answer == "Insufficient evidence."


@pytest.mark.asyncio
@pytest.mark.parametrize("decision", ["true", 1, None, []])
async def test_invalid_verification(monkeypatch, decision):
    client = RetrievalClient()

    async def fake_post(endpoint, payload):
        return {
            "done": True,
            "response": json.dumps({"supported": decision}),
        }

    monkeypatch.setattr(client, "post", fake_post)

    with pytest.raises(InferenceError):
        await client.verify_answerability(
            "Which database is used?",
            "The application uses PostgreSQL.",
        )



@pytest.mark.asyncio
@pytest.mark.parametrize("supported", [True, False])
async def test_semantic_paraphrase_verification(monkeypatch, supported):
    client = RetrievalClient()

    async def fake_post(endpoint, payload):
        properties = payload["format"]["properties"]

        if "missing_constraints" in properties:
            return {
                "done": True,
                "response": json.dumps({"missing_constraints": []}),
            }



        result = (
            {"answer": "bounded retries"}
            if "answer" in properties
            else {"supported": supported}
        )

        return {
            "done": True,
            "response": json.dumps(result),
        }

    monkeypatch.setattr(client, "post", fake_post)

    result = await client.grounded(GroundedRequest(
        question="How can interrupted processing continue?",
        evidence=[
            Evidence(
                source_id="S1",
                excerpt="Failed jobs permit bounded retries.",
            )
        ],
    ))

    if supported:
        assert result.cited_source_ids == ["S1"]
        assert result.answer == "Failed jobs permit bounded retries [S1]."
    else:
        assert result.cited_source_ids == []
        assert result.answer == "Insufficient evidence."


@pytest.mark.asyncio
async def test_extraction_and_verification_use_distinct_models(monkeypatch):
    client = RetrievalClient()
    calls = []

    async def fake_post(endpoint, payload):
        calls.append(payload["model"])
        properties = payload["format"]["properties"]

        if "missing_constraints" in properties:
            return {
                "done": True,
                "response": json.dumps({"missing_constraints": []}),
            }

        if "answer" in properties:
            response = {"answer": "PostgreSQL"}
        else:
            response = {"supported": True}

        return {
            "done": True,
            "response": json.dumps(response),
        }

    monkeypatch.setattr(client, "post", fake_post)

    result = await client.grounded(GroundedRequest(
        question="Which database is used?",
        evidence=[
            Evidence(
                source_id="S1",
                excerpt="The application uses PostgreSQL.",
            )
        ],
    ))

    assert calls == ["llama3.2:3b", "mistral:7b", "mistral:7b"]
    assert result.cited_source_ids == ["S1"]
    assert result.answer == "The application uses PostgreSQL [S1]."


@pytest.mark.asyncio
async def test_paraphrased_extraction_is_not_returned(monkeypatch):
    client = RetrievalClient()

    async def fake_post(endpoint, payload):
        return {
            "done": True,
            "response": json.dumps({
                "answer": "The system utilizes PostgreSQL."
            }),
        }

    monkeypatch.setattr(client, "post", fake_post)

    result = await client.grounded(GroundedRequest(
        question="Which database is used?",
        evidence=[
            Evidence(
                source_id="S1",
                excerpt="The application uses PostgreSQL.",
            )
        ],
    ))

    assert result.cited_source_ids == []
    assert result.answer == "Insufficient evidence."



@pytest.mark.asyncio
@pytest.mark.parametrize("missing,accepted", [
    ([], True),
    (["Required entity category"], False),
    (["Required access classification"], False),
    (["Required relationship"], False),
])
async def test_constraint_audit(monkeypatch, missing, accepted):
    client = RetrievalClient()

    async def fake_post(endpoint, payload):
        return {
            "done": True,
            "done_reason": "stop",
            "response": json.dumps({
                "missing_constraints": missing
            }),
        }

    monkeypatch.setattr(client, "post", fake_post)

    assert await client.verify_constraints(
        "Which encrypted protocol transfers records?",
        "Records are transferred using HTTP.",
    ) is accepted


@pytest.mark.asyncio
@pytest.mark.parametrize("invalid", [
    None, True, 1, "none", [None], [""], [123],
])
async def test_invalid_constraint_audit(monkeypatch, invalid):
    client = RetrievalClient()

    async def fake_post(endpoint, payload):
        return {
            "done": True,
            "response": json.dumps({
                "missing_constraints": invalid
            }),
        }

    monkeypatch.setattr(client, "post", fake_post)

    with pytest.raises(InferenceError):
        await client.verify_constraints(
            "Identify the transport protocol.",
            "Records use HTTP.",
        )


@pytest.mark.asyncio
async def test_missing_constraint_vetoes_grounded_answer(monkeypatch):
    client = RetrievalClient()

    async def fake_post(endpoint, payload):
        properties = payload["format"]["properties"]

        if "answer" in properties:
            response = {"answer": "HTTP"}
        elif "supported" in properties:
            response = {"supported": True}
        else:
            response = {
                "missing_constraints": ["Encryption"]
            }

        return {
            "done": True,
            "response": json.dumps(response),
        }

    monkeypatch.setattr(client, "post", fake_post)

    result = await client.grounded(GroundedRequest(
        question="Which encrypted protocol transfers records?",
        evidence=[
            Evidence(
                source_id="S1",
                excerpt="Records are transferred using HTTP.",
            )
        ],
    ))

    assert result.cited_source_ids == []
    assert result.answer == "Insufficient evidence."
