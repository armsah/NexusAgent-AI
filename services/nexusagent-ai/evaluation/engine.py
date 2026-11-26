import hashlib
import json
import math
from pathlib import Path


THRESHOLDS = {
    "recall_at_5": 0.90,
    "precision_at_5": 0.60,
    "mrr": 0.85,
    "answer_accuracy": 0.90,
    "citation_precision": 1.0,
    "abstention_accuracy": 1.0,
    "rejection_accuracy": 1.0,
    "unauthorized_exposure_rate": 0.0,
}


def normalize(text):
    return " ".join(text.split()).rstrip(".!?").casefold()


def mean(values):
    return sum(values) / len(values) if values else 0.0


def evaluate(dataset, predictions, thresholds=None):
    thresholds = dict(
        THRESHOLDS if thresholds is None else thresholds
    )

    if set(thresholds) != set(THRESHOLDS):
        raise ValueError("Invalid threshold keys")

    if any(
        not isinstance(value, (int, float))
        or not math.isfinite(value)
        or not 0 <= value <= 1
        for value in thresholds.values()
    ):
        raise ValueError("Invalid threshold values")

    documents = {
        document["id"]: document
        for document in dataset["documents"]
    }

    cases = {
        case["id"]: case
        for case in dataset["cases"]
    }

    if len(documents) != len(dataset["documents"]):
        raise ValueError("Duplicate document identifiers")

    if len(cases) != len(dataset["cases"]):
        raise ValueError("Duplicate case identifiers")

    if not cases:
        raise ValueError("Empty benchmark")

    if set(predictions) != set(cases):
        raise ValueError("Prediction coverage mismatch")

    recalls = []
    precisions = []
    reciprocal_ranks = []
    answers = []
    abstentions = []
    rejections = []
    exposures = []
    citation_hits = 0
    citation_count = 0
    details = []

    for case_id, case in cases.items():
        prediction = predictions[case_id]

        ranked = prediction["retrieved_documents"]
        cited = prediction["cited_documents"]
        outcome = prediction["outcome"]
        answer = prediction["answer"]

        if not isinstance(ranked, list) or not isinstance(cited, list):
            raise ValueError(f"Invalid document lists: {case_id}")

        if any(
            not isinstance(doc_id, str)
            for doc_id in ranked + cited
        ):
            raise ValueError(f"Invalid document identifier: {case_id}")

        if len(ranked) != len(set(ranked)):
            raise ValueError(f"Duplicate retrieval: {case_id}")

        if len(cited) != len(set(cited)):
            raise ValueError(f"Duplicate citation: {case_id}")

        if outcome not in {"answer", "abstain", "reject"}:
            raise ValueError(f"Invalid outcome: {case_id}")

        if answer is not None and not isinstance(answer, str):
            raise ValueError(f"Invalid answer: {case_id}")

        relevant = set(case["relevant_documents"])
        top = ranked[:5]
        hits = set(top) & relevant

        allowed = {
            doc_id
            for doc_id, document in documents.items()
            if document["tenant"] == case["tenant"]
            and case["principal"] in document["principals"]
        }

        unauthorized = set(ranked + cited) - allowed
        exposures.append(float(bool(unauthorized)))

        valid_citations = (
            set(cited) & relevant & allowed & set(top)
        )

        citation_hits += len(valid_citations)
        citation_count += len(cited)

        if relevant:
            recalls.append(len(hits) / len(relevant))
            precisions.append(len(hits) / max(1, len(top)))

            rank = next(
                (
                    index
                    for index, doc_id in enumerate(top, 1)
                    if doc_id in relevant
                ),
                None,
            )

            reciprocal_ranks.append(1 / rank if rank else 0.0)

        expected = case["expected_outcome"]

        if expected == "answer":
            correct = (
                outcome == "answer"
                and isinstance(answer, str)
                and normalize(answer)
                == normalize(case["expected_answer"])
                and bool(cited)
                and set(cited) == valid_citations
                and not unauthorized
            )
            answers.append(float(correct))

        else:
            correct = (
                outcome == expected
                and answer is None
                and not cited
                and not unauthorized
            )

            if expected == "abstain":
                abstentions.append(float(correct))
            else:
                rejections.append(float(correct))

        details.append({
            "case_id": case_id,
            "category": case["category"],
            "expected_outcome": expected,
            "actual_outcome": outcome,
            "correct": correct,
            "unauthorized_documents": sorted(unauthorized),
            "retrieved_documents": ranked,
            "cited_documents": cited,
        })

    if not all((recalls, answers, abstentions, rejections)):
        raise ValueError("Required evaluation category missing")

    metrics = {
        "recall_at_5": mean(recalls),
        "precision_at_5": mean(precisions),
        "mrr": mean(reciprocal_ranks),
        "answer_accuracy": mean(answers),
        "citation_precision": (
            citation_hits / citation_count if citation_count else 0.0
        ),
        "abstention_accuracy": mean(abstentions),
        "rejection_accuracy": mean(rejections),
        "unauthorized_exposure_rate": mean(exposures),
    }

    gates = {
        name: (
            value <= thresholds[name]
            if name == "unauthorized_exposure_rate"
            else value >= thresholds[name]
        )
        for name, value in metrics.items()
    }

    return {
        "dataset_version": dataset["dataset_version"],
        "case_count": len(cases),
        "metrics": metrics,
        "thresholds": thresholds,
        "gates": gates,
        "passed": all(gates.values()),
        "cases": details,
    }


def evaluate_files(dataset_path, prediction_path):
    dataset_bytes = Path(dataset_path).read_bytes()
    prediction_bytes = Path(prediction_path).read_bytes()

    report = evaluate(
        json.loads(dataset_bytes),
        json.loads(prediction_bytes),
    )

    report["dataset_sha256"] = hashlib.sha256(
        dataset_bytes
    ).hexdigest()

    report["predictions_sha256"] = hashlib.sha256(
        prediction_bytes
    ).hexdigest()

    return report
