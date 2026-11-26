# E5 - AI Evaluation, Grounding and Inference Security

## Baseline

- E4 closure commit: 4f4ee16.
- Previous full regression: 348 passing tests.
- E5 baseline Python regression: 27 passed, zero failed or skipped.
- Known warning: Starlette TestClient/httpx deprecation.
- Cloud expenditure: EUR 0.

## Implementation and acceptance register

| Step | Requirement | Status |
|---|---|---|
| E5.1 | Baseline and source audit | CLOSED |
| E5.2 | Versioned evaluation datasets | CLOSED |
| E5.3 | Automated evaluation framework | CLOSED |
| E5.4 | Hybrid retrieval and citation metrics | OPEN |
| E5.5 | Adversarial and prompt-injection testing | OPEN |
| E5.6 | Inference authentication | OPEN |
| E5.7 | Concurrency, request limits and timeouts | OPEN |
| E5.8 | Dependency warning remediation | OPEN |
| E5.9 | Full regression and live acceptance | OPEN |
| E5.10 | Documentation, PR merge and formal closure | OPEN |

## E5.1 audit findings

- Existing Python extractive normalization rejects unsupported
  sentences, unknown citations and inconsistent citation declarations.
- C# verifier independently retrieves authorized evidence.
- PostgreSQL retrieval combines lexical and semantic rankings
  using reciprocal rank fusion.
- Semantic retrieval requires matching embedding model and
  document content version.
- Retrieval quality requires quantitative relevance evaluation.
- Extractive verification alone does not establish relevance
  to the original question.
- Inference authentication and resource controls require
  dedicated implementation and negative acceptance tests.
- The Starlette dependency warning remains unresolved.

## Progression policy

Every step requires documented acceptance evidence, successful
applicable tests, a committed implementation and remote branch
verification before the next step begins.

Stage-level closure additionally requires a merged pull request,
synchronized main and completion-register updates.

No required implementation may be silently deferred.


## E5.2 acceptance evidence

- Dataset version: 1.0.0.
- Schema version: 1.
- Eight synthetic documents and twelve evaluation cases.
- Eight evaluation categories.
- Dataset SHA256: 98456EE772A28C9A0B6C2843885C96E2B0EB5B37236CBDD92FD4257386D50B84.
- Twenty-three dataset contract tests.
- Existing Python regression preserved.
- Expected outcomes and authorization labels validated.
- Dataset integrity verified; actual model performance is not
  measured by these contract tests.
- Cloud expenditure: EUR 0.


## E5.3 acceptance evidence

- Reusable Python evaluator and command-line runner.
- Eight quantitative metrics and configurable thresholds.
- Seventeen permanent evaluator tests.
- Complete prediction coverage required.
- Dataset and prediction SHA256 recorded in JSON reports.
- Reference fixture passes all metric gates.
- Negative CLI fixture returns exit code 1.
- Unauthorized and unknown document exposure fails acceptance.
- Duplicate retrieval and citation identifiers rejected.
- Reference predictions are synthetic oracle fixtures, not
  measured model performance.
- Precision@5 uses returned results up to five as denominator.
- Actual PostgreSQL and inference evaluation follows in E5.4
  and E5.5.
- Cloud expenditure: EUR 0.
