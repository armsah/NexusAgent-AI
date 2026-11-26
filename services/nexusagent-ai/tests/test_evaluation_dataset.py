import json
from pathlib import Path

import pytest


DATASET = (
    Path(__file__).resolve().parents[1]
    / "evaluation" / "datasets" / "v1" / "benchmark.json"
)

CORPUS = json.loads(DATASET.read_text(encoding="utf-8"))
DOCUMENTS = {doc["id"]: doc for doc in CORPUS["documents"]}
CASES = CORPUS["cases"]


def test_dataset_version():
    assert CORPUS["schema_version"] == 1
    assert CORPUS["dataset_version"] == "1.0.0"


def test_unique_identifiers():
    assert len(DOCUMENTS) == len(CORPUS["documents"])
    assert len({case["id"] for case in CASES}) == len(CASES)


def test_minimum_coverage():
    assert len(CASES) >= 12
    assert len(DOCUMENTS) >= 8
    assert {
        "grounding",
        "insufficient_evidence",
        "tenant_isolation",
        "authorization",
        "prompt_injection",
        "indirect_injection",
        "semantic_retrieval",
        "hallucination",
    } <= {case["category"] for case in CASES}


@pytest.mark.parametrize("document", CORPUS["documents"])
def test_document_contract(document):
    assert document["id"]
    assert document["tenant"]
    assert document["principals"]
    assert len(set(document["principals"])) == len(document["principals"])
    assert document["text"].strip()


@pytest.mark.parametrize("case", CASES, ids=lambda case: case["id"])
def test_case_contract(case):
    assert case["question"].strip()
    assert case["tenant"]
    assert case["principal"]
    assert case["expected_outcome"] in {"answer", "abstain", "reject"}

    relevant = case["relevant_documents"]
    assert len(relevant) == len(set(relevant))

    for document_id in relevant:
        document = DOCUMENTS[document_id]
        assert document["tenant"] == case["tenant"]
        assert case["principal"] in document["principals"]

    if case["expected_outcome"] == "answer":
        assert relevant
        assert case["expected_answer"]
        assert any(
            case["expected_answer"] in DOCUMENTS[doc_id]["text"]
            for doc_id in relevant
        )
    else:
        assert not relevant
        assert case["expected_answer"] is None
