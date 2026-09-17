# NexusAgent AI — Non-Functional Requirements

**Project:** NexusAgent AI
**Phase:** P0 — Architecture and Governance Baseline
**Status:** Initial specification

## 1. Purpose

This document defines the measurable non-functional requirements (NFRs), service-level objectives (SLOs), quality gates, and operational constraints for NexusAgent AI.

These requirements establish the acceptance baseline used throughout P1–P19.

AI quality thresholds that require an empirical baseline are finalized through the evaluation harness in P14. Until then, this document defines the metric, measurement method, and release-gate requirement rather than inventing unsupported numerical quality targets.

---

## 2. Requirement Levels

Requirements use the following priorities:

- **MUST** — mandatory for the portfolio completion gate.
- **SHOULD** — expected unless an implementation constraint is documented.
- **MAY** — optional enhancement.

A phase cannot be considered complete when its applicable MUST requirements fail.

---

## 3. Availability and Resilience

### NFR-AVL-001 — API Availability

**Priority:** MUST

The demo API target is:

```text
Availability SLO >= 99.9%
```

Availability is measured for the defined demo service window using successful API responses excluding explicitly documented planned maintenance.

### NFR-AVL-002 — Safe Dependency Degradation

**Priority:** MUST

Failure of a model, retrieval service, Tool Gateway dependency, or asynchronous worker must not broaden authorization or execution authority.

The system must fail safely.

Expected behavior includes:

- model unavailable → deterministic error/fallback or bounded retry;
- Search unavailable → no fabricated evidence-backed answer;
- Tool Gateway unavailable → action remains unexecuted;
- approval service/state unavailable → L2 action remains unexecuted;
- policy evaluation failure → deny or escalate rather than execute;
- messaging failure → durable state remains recoverable.

### NFR-AVL-003 — Long-Running Workflow Durability

**Priority:** MUST

Long-running agent workflows must not depend on an HTTP request remaining connected.

Workflow state must be persisted and asynchronous processing used where appropriate.

### NFR-AVL-004 — Graceful Restart

**Priority:** MUST

A process/container restart must not cause:

- duplicate external side effects;
- loss of approved workflow state;
- unauthorized transition;
- bypass of approval; or
- corruption of durable workflow state.

---

## 4. Performance

### NFR-PERF-001 — Standard Grounded Q&A Latency

**Priority:** MUST

For the defined standard grounded Q&A benchmark:

```text
P95 end-to-end latency < 8 seconds
```

The measurement includes the synchronous request path required to produce the grounded answer.

Long-running agent workflows are excluded from this synchronous SLO and must use asynchronous processing.

### NFR-PERF-002 — Latency Percentiles

**Priority:** MUST

Operational telemetry must expose at least:

- P50;
- P95; and
- P99

for important API and dependency operations where sufficient samples exist.

### NFR-PERF-003 — Dependency Latency Attribution

**Priority:** MUST

Distributed traces must allow latency to be attributed to major operations including:

- API handling;
- PostgreSQL;
- retrieval;
- model inference;
- Service Bus processing;
- Tool Gateway execution; and
- verification.

### NFR-PERF-004 — Bounded AI Work

**Priority:** MUST

Agent workflows must have configurable limits for:

- maximum steps;
- token consumption;
- execution duration;
- retries; and
- concurrency where applicable.

An agent must not be permitted to loop indefinitely.

---

## 5. Retrieval Quality

### NFR-RAG-001 — Retrieval Metrics

**Priority:** MUST

The evaluation framework must measure, where applicable:

- Recall@K;
- Precision@K;
- MRR and/or NDCG;
- security-filter correctness; and
- stale-version retrieval rate.

### NFR-RAG-002 — Authorization Correctness

**Priority:** MUST

Unauthorized or cross-tenant documents must not be included in model context.

Target for the curated authorization/adversarial evaluation suite:

```text
Known unauthorized retrieval leakage = 0
Known cross-tenant retrieval leakage = 0
```

### NFR-RAG-003 — Retrieval Baseline

**Priority:** MUST

P8 retrieval improvements must be compared against the earlier retrieval baseline rather than evaluated only through subjective examples.

The comparison must identify the retrieval configuration under test.

### NFR-RAG-004 — Reproducibility

**Priority:** MUST

A retrieval evaluation result must be attributable to:

- dataset version;
- search index version;
- embedding model/version;
- retrieval configuration version; and
- application/release version.

---

## 6. Grounded Answer Quality

### NFR-AIQ-001 — Groundedness

**Priority:** MUST

Groundedness must be measured on a curated evaluation dataset.

The numeric release threshold will be established from the implemented baseline evaluation in P14 and versioned thereafter.

Candidate releases must not silently reduce the approved groundedness threshold.

### NFR-AIQ-002 — Citation Precision and Recall

**Priority:** MUST

The evaluation harness must measure:

- citation precision; and
- citation recall

against curated expected evidence.

The production/release threshold will be established from the measured baseline in P14.

### NFR-AIQ-003 — Unsupported Claims

**Priority:** MUST

Unsupported-claim rate must be measured.

The system should prefer an explicit insufficient-evidence response over inventing evidence.

### NFR-AIQ-004 — Citation Integrity

**Priority:** MUST

A generated citation must resolve to evidence actually present in the retrieved/context evidence set.

Unknown or fabricated citation identifiers must be rejected or treated as verification failure.

### NFR-AIQ-005 — Stable Citations

**Priority:** MUST

Citation identifiers must be sufficiently stable to associate an answer with:

- source document;
- document version;
- relevant section/page or equivalent provenance; and
- retrieved evidence record.

---

## 7. Agent Workflow Quality

### NFR-AGT-001 — Workflow Metrics

**Priority:** MUST

Agent evaluation must measure:

- task completion;
- plan validity;
- tool selection;
- tool argument accuracy;
- step count;
- latency; and
- token cost.

### NFR-AGT-002 — Explicit Workflow State

**Priority:** MUST

Business-critical workflow state must be represented through an explicit persisted state machine rather than only through conversational history.

### NFR-AGT-003 — Workflow Budgets

**Priority:** MUST

The Supervisor must enforce configurable:

- step budgets;
- token budgets;
- time/deadline budgets; and
- cancellation.

### NFR-AGT-004 — Agent Authority Boundaries

**Priority:** MUST

Agent responsibilities must not exceed their defined authority.

In particular:

- Supervisor cannot directly execute business tools;
- Knowledge Agent cannot bypass retrieval ACLs;
- Policy Agent cannot grant authorization;
- Action Agent can propose but cannot independently authorize execution;
- Verification Agent cannot override a rejected policy or approval decision.

---

## 8. Tool Safety

### NFR-TOOL-001 — No Generic Execution Path

**Priority:** MUST

The application must not expose generic:

- shell;
- SQL;
- Terraform;
- ARM payload;
- `kubectl`;
- arbitrary file/system; or
- arbitrary HTTP execution

to the model.

### NFR-TOOL-002 — Typed Tool Contracts

**Priority:** MUST

Every executable tool must have a typed server-side contract.

Tool input must undergo schema and semantic validation before execution.

### NFR-TOOL-003 — Unauthorized Destructive Actions

**Priority:** MUST

Target for the adversarial suite:

```text
Unauthorized destructive actions executed = 0
```

### NFR-TOOL-004 — Server-Side Identity

**Priority:** MUST

User, tenant, resource scope, and execution identity must not be trusted when supplied by model output.

Trusted identity context must originate from authenticated workflow/application state.

### NFR-TOOL-005 — Egress Control

**Priority:** MUST

Tool execution must use known destinations or allow-listed destination policy.

The model must not be able to construct an arbitrary executable URL.

---

## 9. Human Approval

### NFR-APR-001 — L2 Enforcement

**Priority:** MUST

Any action classified as L2 must not execute without a valid human approval record.

Target:

```text
Rejected-action execution rate = 0
Unapproved L2 execution rate = 0
```

### NFR-APR-002 — Exact Action Binding

**Priority:** MUST

Approval must be bound to:

- tool name;
- argument hash;
- requester;
- workflow/proposal identity; and
- expiration.

A modification to the approved tool or arguments invalidates the approval.

### NFR-APR-003 — Replay Protection

**Priority:** MUST

Expired, consumed, rejected, or otherwise invalid approval records must not authorize execution.

### NFR-APR-004 — Auditability

**Priority:** MUST

Approval events must record sufficient information to determine:

- requester;
- proposal;
- policy/risk result;
- approver;
- decision;
- timestamp;
- expiration; and
- subsequent execution outcome.

---

## 10. Security

### NFR-SEC-001 — Authentication

**Priority:** MUST

User-facing access must use Microsoft Entra ID authentication in the Azure implementation.

### NFR-SEC-002 — Workload Identity

**Priority:** MUST

Azure workload authentication must use managed identity where supported.

Long-lived Azure application credentials must not be stored in application configuration.

### NFR-SEC-003 — CI/CD Authentication

**Priority:** MUST

GitHub Actions must authenticate to Azure using OIDC/workload federation rather than a stored long-lived Azure client secret.

### NFR-SEC-004 — Least Privilege

**Priority:** MUST

Application identities must receive only the permissions required for their responsibilities.

Privileged tool execution rights must be separated from ordinary API/orchestration rights.

### NFR-SEC-005 — Security Trimming

**Priority:** MUST

Document tenant/group authorization must be applied before retrieved content reaches model context.

### NFR-SEC-006 — Secret Exclusion

**Priority:** MUST

Secrets, access tokens, connection strings containing credentials, private keys, and privileged headers must not intentionally be placed in model context.

### NFR-SEC-007 — Prompt Injection Defense

**Priority:** MUST

The implementation must include adversarial testing for direct and indirect prompt injection.

Deterministic security boundaries must remain effective even when prompt-injection detection fails.

### NFR-SEC-008 — Cross-Tenant Isolation

**Priority:** MUST

Cross-tenant data disclosure must be tested explicitly.

Target on the curated adversarial suite:

```text
Cross-tenant leakage cases = 0
```

---

## 11. Reliability and Messaging

### NFR-REL-001 — Timeouts and Cancellation

**Priority:** MUST

Network operations to model, search, tools, persistence, messaging, and Azure dependencies must use bounded timeouts and cancellation where supported.

### NFR-REL-002 — Retry Policy

**Priority:** MUST

Retries are permitted only for operations classified as transient and must have:

- bounded attempt count;
- bounded total duration; and
- backoff/jitter where appropriate.

### NFR-REL-003 — Idempotency

**Priority:** MUST

Business-affecting tool execution and asynchronous consumers must be idempotent.

Retries must not duplicate business side effects.

### NFR-REL-004 — Durable Messaging

**Priority:** MUST

Long-running background operations must use durable messaging where required by the workflow design.

### NFR-REL-005 — Dead-Letter Queue

**Priority:** MUST

Poison messages must be moved to a DLQ after the configured retry policy is exhausted.

A documented re-drive procedure must exist before portfolio completion.

### NFR-REL-006 — Transactional Consistency

**Priority:** MUST where applicable

State changes that must emit a durable event should use a transactional outbox or equivalent consistency mechanism.

Consumers should use durable inbox/idempotency records where duplicate delivery can affect business state.

---

## 12. Auditability

### NFR-AUD-001 — Consequential Action Traceability

**Priority:** MUST

Every consequential action must be attributable to:

- requester;
- tenant;
- workflow;
- release ID;
- evidence;
- model deployment;
- prompt version;
- policy result;
- tool proposal;
- approval;
- executor;
- result; and
- correlation/trace identifiers.

### NFR-AUD-002 — Append-Oriented Security Records

**Priority:** MUST

Security-meaningful decisions and execution records must use append-oriented audit semantics.

### NFR-AUD-003 — Release Attribution

**Priority:** MUST

Consequential AI behavior must be attributable to an AI release manifest.

---

## 13. Observability

### NFR-OBS-001 — Distributed Tracing

**Priority:** MUST

OpenTelemetry must provide end-to-end trace correlation across applicable:

```text
API
→ orchestration
→ retrieval
→ model
→ messaging/worker
→ approval
→ Tool Gateway
→ verification
```

### NFR-OBS-002 — Trace Propagation

**Priority:** MUST

Trace/correlation context must propagate through HTTP and asynchronous messaging boundaries.

### NFR-OBS-003 — SRE Metrics

**Priority:** MUST

The operational dashboard must expose relevant:

- request rate;
- latency;
- error rate;
- resource saturation;
- Container App replicas;
- PostgreSQL connections;
- queue depth;
- dead-letter count;
- oldest message age.

### NFR-OBS-004 — AI Metrics

**Priority:** MUST

Telemetry must expose relevant:

- model latency;
- input tokens;
- output tokens;
- model errors;
- throttling;
- retrieval latency;
- zero-result rate;
- agent step count;
- tool-call count;
- verification failures;
- safety/policy denials.

### NFR-OBS-005 — Sensitive Data Minimization

**Priority:** MUST

Telemetry must not intentionally record credentials or unrestricted confidential model/document payloads.

Logging must follow the P0 data-classification policy.

---

## 14. Cost and FinOps

### NFR-COST-001 — Cost Attribution

**Priority:** MUST

The demo must make cost per request/workflow measurable.

At minimum, attribution should include available:

- model token usage;
- model invocation counts;
- Search usage indicators;
- tool invocation counts; and
- relevant runtime allocation.

### NFR-COST-002 — Token Budgets

**Priority:** MUST

Agent workflows must enforce configurable token budgets.

### NFR-COST-003 — Step Budgets

**Priority:** MUST

Agent workflows must enforce configurable maximum step counts.

### NFR-COST-004 — Cost Denial-of-Service Controls

**Priority:** MUST

The system must implement controls appropriate to the request boundary, including:

- rate limiting;
- workflow budgets;
- bounded retries; and
- concurrency limits.

---

## 15. Infrastructure and Deployment

### NFR-INF-001 — Infrastructure as Code

**Priority:** MUST

The Azure environment must be reproducible through Terraform.

The portfolio must demonstrate that the environment can be created from a clean resource-group/subscription strategy.

### NFR-INF-002 — No Manual-Only Critical Infrastructure

**Priority:** MUST

Infrastructure required for the repeatable demo must not depend exclusively on undocumented portal configuration.

Any unavoidable manual/bootstrap operation must be documented.

### NFR-INF-003 — Immutable Application Releases

**Priority:** SHOULD

Application deployments should use immutable container image tags/digests and an identifiable release manifest.

### NFR-INF-004 — Production-Reference Networking

**Priority:** MUST before portfolio completion

A production-reference topology must document private networking/DNS for sensitive Azure PaaS paths where supported.

The low-cost development environment may initially use public endpoints with strict identity and firewall controls.

---

## 16. Maintainability and Architecture

### NFR-MNT-001 — Dependency Direction

**Priority:** MUST

Domain and Application layers must not depend directly on Azure SDKs or model-specific frameworks.

Target dependency direction:

```text
Domain/Application → interfaces/ports
Infrastructure → Azure/model/search/persistence implementations
API/Workers → composition root and transport
```

### NFR-MNT-002 — Model Abstraction

**Priority:** MUST

Model inference must be accessed through an application abstraction such as `IModelClient` or equivalent.

Changing an approved model deployment must not require rewriting domain/business logic.

### NFR-MNT-003 — Retrieval Abstraction

**Priority:** MUST

Retrieval must be accessed through an application abstraction such as `IRetriever` or equivalent.

### NFR-MNT-004 — Explicit Contracts

**Priority:** MUST

APIs, messages, tool calls, approvals, and adapter boundaries must use explicit contracts.

### NFR-MNT-005 — Architecture Tests

**Priority:** MUST

Automated architecture tests must enforce critical dependency/layering rules.

---

## 17. Testability

### NFR-TST-001 — Unit Tests

**Priority:** MUST

Unit tests must cover deterministic domain/application behavior including:

- workflow transitions;
- risk classification;
- approval rules;
- citation parsing/validation;
- tool argument validation.

### NFR-TST-002 — Integration Tests

**Priority:** MUST

Integration tests must cover applicable:

- PostgreSQL;
- Service Bus;
- Azure AI Search;
- application infrastructure adapters.

### NFR-TST-003 — Contract Tests

**Priority:** MUST

Contract tests must cover:

- HTTP DTOs;
- tool schemas;
- message envelopes;
- model adapter;
- retrieval adapter.

### NFR-TST-004 — AI Quality Tests

**Priority:** MUST

A fixed, versioned evaluation set must be executable repeatedly and used to compare candidate releases against an approved baseline.

### NFR-TST-005 — Adversarial Tests

**Priority:** MUST

The suite must include at least:

- direct prompt injection;
- indirect prompt injection;
- poisoned retrieved content;
- unauthorized tool escalation;
- data-exfiltration attempt;
- cross-tenant retrieval attempt;
- forged identity/tenant argument;
- approval replay.

### NFR-TST-006 — Failure Tests

**Priority:** MUST

The project must demonstrate at least:

- model timeout/unavailability;
- Search failure/degradation;
- poison Service Bus message;
- approval expiration;
- tool dependency outage; and
- bad AI release rollback/fallback.

---

## 18. AI Release Management

### NFR-LLMO-001 — Release Manifest

**Priority:** MUST

Every promoted AI release must identify:

```text
applicationCommit
modelDeployment
systemPromptVersion
retrievalConfigVersion
embeddingModelVersion
searchIndexVersion
toolCatalogVersion
policyBundleVersion
evaluationDatasetVersion
evaluationRunId
releasedAtUtc
```

### NFR-LLMO-002 — Quality Gate

**Priority:** MUST

A candidate release must pass defined software, AI-quality, and safety gates before promotion.

### NFR-LLMO-003 — Regression Comparison

**Priority:** MUST

Candidate AI behavior must be compared against an approved baseline.

Quality regressions must be visible rather than hidden by aggregate success metrics.

### NFR-LLMO-004 — Rollback

**Priority:** MUST

The system must support rollback or safe fallback for a bad:

- model deployment;
- prompt;
- retrieval configuration;
- tool catalog/policy release; or
- application release.

---

## 19. Initial Acceptance Matrix

| Area                             | P0 Target                                   | Final Evidence                |
| -------------------------------- | ------------------------------------------- | ----------------------------- |
| API availability                 | ≥ 99.9% demo SLO                            | SLO dashboard / benchmark     |
| Standard Q&A latency             | P95 < 8 s                                   | Load-test report + traces     |
| Retrieval quality                | Recall@K, Precision@K, MRR/NDCG measured    | Evaluation report             |
| Unauthorized retrieval           | 0 known leakage cases in curated suite      | Security/evaluation tests     |
| Cross-tenant leakage             | 0 known leakage cases in curated suite      | Adversarial tests             |
| Groundedness                     | Threshold established from P14 baseline     | Evaluation report             |
| Citation precision/recall        | Threshold established from P14 baseline     | Citation evaluation           |
| Unauthorized destructive actions | 0                                           | Adversarial report            |
| Unapproved L2 execution          | 0                                           | Approval tests/audit          |
| Rejected-action execution        | 0                                           | Approval tests/audit          |
| Workflow durability              | Survives expected restart/transient failure | Failure test                  |
| Duplicate side effects           | 0 in idempotency test scenarios             | Integration/E2E tests         |
| Distributed trace                | Complete cross-layer workflow trace         | Application Insights evidence |
| Cost attribution                 | Cost/workflow measurable                    | FinOps evidence               |
| Infrastructure recreation        | Terraform from clean environment            | Terraform evidence            |
| Long-lived Azure CI credential   | 0                                           | OIDC configuration            |

---

## 20. Baseline Quality-Gate Policy

P0 deliberately does not invent groundedness or citation-score thresholds without measured data.

The release-gate process is therefore:

```text
P0
Define metrics and mandatory safety invariants
        ↓
P5–P8
Establish retrieval/RAG behavior
        ↓
P14
Run versioned baseline evaluation
        ↓
Approve numerical quality thresholds
        ↓
Candidate releases
Compare candidate against approved baseline
        ↓
Pass
Promote
        ↓
Fail
Block promotion / investigate / rollback
```

Safety invariants such as authorization, tenant isolation, approval enforcement, and prohibited execution are not probabilistic quality targets. They require zero known violations in the curated test suites.

---

## 21. Phase Mapping

These requirements are implemented and proven progressively:

- **P1:** application boundaries, persistence, local lifecycle and tests;
- **P2:** reproducible Azure infrastructure;
- **P3:** identity, managed identity and OIDC;
- **P4–P8:** ingestion, retrieval and grounded-answer quality;
- **P9:** durable agent workflow behavior;
- **P10–P11:** Tool Gateway, risk and approval safety;
- **P12:** asynchronous reliability and DLQ;
- **P13:** verification and negative testing;
- **P14:** numerical AI-quality baseline and CI gate;
- **P15:** adversarial security evidence;
- **P16:** end-to-end observability;
- **P17:** cost and token attribution;
- **P18:** production-reference network controls;
- **P19:** load, failure and rollback benchmark.

---

## 22. P0 Exit Relevance

P0 requires measurable expectations before implementation begins.

This specification establishes:

- SLOs;
- hard safety invariants;
- required quality metrics;
- operational requirements;
- release-management requirements; and
- evidence required to demonstrate compliance.

Later phases may refine numerical thresholds using measured results, but they must not silently weaken the security and governance invariants established here.
