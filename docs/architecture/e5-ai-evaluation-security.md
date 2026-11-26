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
| E5.4 | Hybrid retrieval and citation metrics | CLOSED |
| E5.5 | Adversarial and prompt-injection testing | CLOSED |
| E5.6 | Inference authentication | CLOSED |
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

### E5.4 - Hybrid retrieval and citation acceptance

Status: CLOSED.

- Live PostgreSQL/pgvector, Python and Ollama component benchmark.
- Dataset version: 1.0.0; 12 cases and 8 synthetic documents.
- Recall@5: 1.00; Precision@5: 0.65; MRR: 1.00.
- Answer accuracy: 1.00; citation precision: 1.00.
- Abstention accuracy: 1.00; rejection accuracy: 1.00.
- Unauthorized exposure rate: 0.00.
- All eight quantitative acceptance gates passed.
- Release build passed.
- .NET regression: 322 passed, zero failed or skipped.
- Python regression: 96 passed, zero failed or skipped.
- Semantic retrieval cosine distance cutoff: 0.55.
- Suspicious evidence filtered before final top-five selection.
- Llama 3.2 extraction, Mistral answerability and constraint audits.
- Strict extractive grounding and citation validation retained.
- Measured report and predictions retained under evaluation/reports.
- Dataset and prediction SHA-256 hashes verified.
- Cloud resources provisioned: none.
- Cloud expenditure: EUR 0.

Limitations:
- The benchmark was used for iterative development and is not an independent holdout.
- Model judgments remain probabilistic.
- The benchmark exercises live retrieval and generation components rather than the complete governed API.
- Independent adversarial and holdout acceptance remains required in E5.5/E5.9.
- Diagnostic ML packages installed locally are not application dependencies.
- CR034 remains open until final E5 integration and merge.

## E5.5 - Adversarial security acceptance

- Unicode compatibility normalization and format-character removal.
- Multiline and obfuscated injection detection.
- Independent Python question and evidence screening.
- Unsafe grounded questions rejected at the inference API.
- Poisoned sources rejected by citation normalization.
- Truncated extraction and verification responses fail closed.
- Python regression: 127 passed.
- C# unit regression: 188 passed.
- Release build passed.
- Live previously unused security holdout: 8/8 passed.
- Holdout dataset and measured results retained in evaluation.
- No acceptance threshold was weakened.
- Cloud expenditure: EUR 0.

The holdout is small and component-level. Pattern-based screening
does not guarantee detection of arbitrary prompt injections.
Full authenticated API and hybrid regression follow in E5.9.

## E5.6 acceptance

- Shared-secret authentication protects all three inference endpoints.
- Constant-time credential comparison.
- Missing server secret fails closed.
- Missing, incorrect and duplicate credentials rejected.
- C# local inference clients attach the configured credential.
- Public liveness remains available.
- Python regression, Release build and C# unit regression passed.
- Secret generated for the current shell only; not committed.
- Live authenticated cross-service acceptance remains mandatory in E5.9.
- Cloud expenditure: EUR 0.
