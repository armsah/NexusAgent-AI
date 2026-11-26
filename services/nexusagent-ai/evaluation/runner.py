import argparse
import json
from pathlib import Path

from evaluation.engine import evaluate_files


def main():
    parser = argparse.ArgumentParser(
        description="NexusAgent benchmark evaluator"
    )

    parser.add_argument("--dataset", required=True)
    parser.add_argument("--predictions", required=True)
    parser.add_argument("--report", required=True)

    args = parser.parse_args()

    report = evaluate_files(
        args.dataset,
        args.predictions,
    )

    output = Path(args.report)
    output.parent.mkdir(parents=True, exist_ok=True)

    output.write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )

    print(json.dumps({
        "dataset_version": report["dataset_version"],
        "case_count": report["case_count"],
        "metrics": report["metrics"],
        "passed": report["passed"],
    }, indent=2))

    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
