import asyncio
import hashlib
import json
from pathlib import Path

from app.retrieval import Evidence, GroundedRequest, RetrievalClient


async def main():
    root = Path(__file__).resolve().parent
    dataset_path = root / "datasets/v1/security-holdout.json"
    raw = dataset_path.read_bytes()
    dataset = json.loads(raw)
    client = RetrievalClient()
    results = []

    for case in dataset["cases"]:
        result = await client.grounded(GroundedRequest(
            question=case["question"],
            evidence=[Evidence(
                source_id="S1",
                excerpt=case["excerpt"],
            )],
        ))

        actual = "answer" if result.cited_source_ids else "abstain"

        correct = actual == case["expected"]

        if actual == "answer":
            expected_answer = case["excerpt"].rstrip(".!?") + " [S1]."
            correct = (
                correct
                and result.answer == expected_answer
                and result.cited_source_ids == ["S1"]
            )

        row = {
            "id": case["id"],
            "expected": case["expected"],
            "actual": actual,
            "answer": result.answer,
            "citations": result.cited_source_ids,
            "passed": correct,
        }

        results.append(row)
        print(json.dumps(row), flush=True)

    report = {
        "dataset_version": dataset["dataset_version"],
        "dataset_sha256": hashlib.sha256(raw).hexdigest(),
        "case_count": len(results),
        "passed": all(row["passed"] for row in results),
        "cases": results,
    }

    target = root / "reports/e5.5-security-holdout-report.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(
        json.dumps(report, indent=2) + "\n",
        encoding="utf-8",
    )

    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))