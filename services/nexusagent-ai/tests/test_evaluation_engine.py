import copy
import json
from pathlib import Path

import pytest

from evaluation.engine import evaluate, evaluate_files


DATASET_PATH = (
    Path(__file__).resolve().parents[1]
    / "evaluation" / "datasets" / "v1" / "benchmark.json"
)

DATASET = json.loads(DATASET_PATH.read_text(encoding="utf-8"))


@pytest.fixture
def predictions():
    return {
        case["id"]: {
            "retrieved_documents": case["relevant_documents"].copy(),
            "cited_documents": case["relevant_documents"].copy(),
            "outcome": case["expected_outcome"],
            "answer": case["expected_answer"],
        }
        for case in DATASET["cases"]
    }


def test_perfect_reference_predictions(predictions):
    report = evaluate(DATASET, predictions)

    assert report["passed"]
    assert report["case_count"] == 12

    for name, value in report["metrics"].items():
        assert value == (
            0.0 if name == "unauthorized_exposure_rate" else 1.0
        )


def test_wrong_answer_fails_gate(predictions):
    predictions["EVAL-001"]["answer"] = "NexusAgent uses Oracle."

    report = evaluate(DATASET, predictions)

    assert not report["passed"]
    assert report["metrics"]["answer_accuracy"] == pytest.approx(0.8)


def test_unauthorized_retrieval_fails_gate(predictions):
    predictions["EVAL-005"]["retrieved_documents"] = ["D04"]

    report = evaluate(DATASET, predictions)

    assert not report["passed"]
    assert report["metrics"]["unauthorized_exposure_rate"] > 0


def test_unknown_document_fails_gate(predictions):
    predictions["EVAL-001"]["retrieved_documents"].append("UNKNOWN")

    assert not evaluate(DATASET, predictions)["passed"]


def test_unauthorized_citation_fails_gate(predictions):
    predictions["EVAL-001"]["cited_documents"] = ["D04"]

    report = evaluate(DATASET, predictions)

    assert not report["passed"]
    assert report["metrics"]["citation_precision"] < 1


def test_irrelevant_authorized_citation_fails_gate(predictions):
    predictions["EVAL-001"]["retrieved_documents"].append("D02")
    predictions["EVAL-001"]["cited_documents"] = ["D02"]

    assert not evaluate(DATASET, predictions)["passed"]


def test_missing_citation_fails_answer_gate(predictions):
    predictions["EVAL-001"]["cited_documents"] = []

    assert not evaluate(DATASET, predictions)["passed"]


def test_abstention_failure(predictions):
    predictions["EVAL-004"]["outcome"] = "answer"
    predictions["EVAL-004"]["answer"] = "EUR 100."

    assert not evaluate(DATASET, predictions)["passed"]


def test_injection_rejection_failure(predictions):
    predictions["EVAL-008"]["outcome"] = "abstain"

    assert not evaluate(DATASET, predictions)["passed"]


def test_missing_prediction(predictions):
    del predictions["EVAL-001"]

    with pytest.raises(ValueError, match="coverage"):
        evaluate(DATASET, predictions)


def test_duplicate_retrieval(predictions):
    predictions["EVAL-001"]["retrieved_documents"] = ["D01", "D01"]

    with pytest.raises(ValueError, match="Duplicate retrieval"):
        evaluate(DATASET, predictions)


def test_duplicate_citation(predictions):
    predictions["EVAL-001"]["cited_documents"] = ["D01", "D01"]

    with pytest.raises(ValueError, match="Duplicate citation"):
        evaluate(DATASET, predictions)


def test_recall_and_rank_calculation(predictions):
    predictions["EVAL-001"]["retrieved_documents"] = ["D02", "D01"]

    report = evaluate(DATASET, predictions)

    assert report["metrics"]["recall_at_5"] == 1.0
    assert report["metrics"]["precision_at_5"] == pytest.approx(0.9)
    assert report["metrics"]["mrr"] == pytest.approx(0.9)


def test_recall_failure(predictions):
    predictions["EVAL-001"]["retrieved_documents"] = []

    report = evaluate(DATASET, predictions)

    assert report["metrics"]["recall_at_5"] == pytest.approx(0.8)
    assert not report["passed"]


def test_reproducible_report(predictions):
    assert evaluate(DATASET, predictions) == evaluate(
        copy.deepcopy(DATASET),
        copy.deepcopy(predictions),
    )


def test_file_hashes(predictions, tmp_path):
    path = tmp_path / "predictions.json"
    path.write_text(json.dumps(predictions), encoding="utf-8")

    report = evaluate_files(DATASET_PATH, path)

    assert report["passed"]
    assert len(report["dataset_sha256"]) == 64
    assert len(report["predictions_sha256"]) == 64


def test_invalid_thresholds(predictions):
    with pytest.raises(ValueError, match="threshold"):
        evaluate(DATASET, predictions, {"recall_at_5": 2.0})
