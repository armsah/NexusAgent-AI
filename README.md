# NexusAgent AI

## What problem is solved?

Enterprise AI assistants must retrieve relevant information without crossing tenant boundaries, justify answers with verifiable evidence, and prevent autonomous actions from bypassing organizational policy. NexusAgent AI demonstrates governed agentic RAG with explicit authorization, grounded responses, bounded workflows, and human approval for sensitive operations.

## What is the architecture?

NexusAgent AI uses .NET 10, PostgreSQL 17, and pgvector. Its layered architecture separates API delivery, application orchestration, domain rules, infrastructure integrations, and background processing. The system combines document ingestion, hybrid retrieval, grounded-answer generation, policy evaluation, and durable workflow execution. The following diagram represents the target Microsoft Azure architecture, not an already deployed environment.

![Target Azure architecture](docs/images/target-azure-architecture.png)

## What senior engineering problems were solved?

[Mandatory completion register](docs/architecture/completion-register.md).


**E2 - Offline AI inference:** Python/FastAPI and Ollama provide local
generation, grounded answers and 1536-dimensional semantic embeddings
through C# REST adapters without requiring Azure inference services.

Validated 19 Python tests, the existing 250-test .NET regression suite,
and live C#-Python-Ollama communication.
See [E2 architecture and local setup](docs/architecture/e2-offline-llm.md).


**E1 - Governed agent harness:** Typed execution contracts, bounded retries,
cooperative cancellation, workflow ownership verification, deny-by-default
authorization, scoped dependency injection and execution telemetry.

Validated **250 automated tests** across unit, architecture and integration
suites. See [E1 architecture documentation](docs/architecture/e1-agent-harness.md).


Validated 231 regression tests and PostgreSQL outbox isolation, including initialization-failure cleanup.

The implementation addresses tenant isolation, evidence-backed answer verification, prompt-injection defenses, transactional outbox reliability, durable workflow state, bounded execution, and approval-controlled actions. Production and Staging do not expose development-identity business endpoints, while database-readiness failures return HTTP 503 without leaking connection details. The final Release build passed all 231 automated tests: 117 unit, 3 architecture, and 111 integration.

![End-to-end governed workflow](docs/images/end-to-end-workflow.png)

**E3 - Governed multi-agent orchestration:** Supervisor, Policy,
Knowledge and Verifier agents execute through a bounded coordinator
with workflow ownership checks, hybrid PostgreSQL retrieval,
local Ollama inference and independent extractive citation verification.

See [E3 architecture and acceptance](docs/architecture/e3-multi-agent-orchestration.md).

**E4 - Durable agent execution:** PostgreSQL-backed agent memory,
bounded planning, transactional checkpoints, resumable execution,
lease fencing, idempotency, cancellation, evidence replay verification
and durable human approvals. Cumulative test coverage: 342 passing tests.
Outstanding verification is tracked in the completion register.

See [E4 architecture and acceptance evidence](docs/architecture/e4-durable-agent-execution.md).

**E4 delivery:** PR #8 merged into main with 342 passing regression tests.
Durable execution, checkpoint memory, bounded planning, retries,
cancellation and persistent approvals are implemented. Residual
acceptance requirements remain mandatory in E5 and E15.


**E4 completed (PR #10):** 348 passing tests, actual API-host crash recovery, delayed checkpoint rollback and replay cancellation verified. Cloud expenditure EUR 0.

**E5 progress:** Baseline audit, versioned evaluation dataset and automated evaluation framework and live hybrid retrieval benchmark and adversarial security acceptance completed (12 cases, 8 documents, 8 categories). AI evaluation, grounding and inference security implementation tracked in [E5 acceptance register](docs/architecture/e5-ai-evaluation-security.md).

**E5.6 completed:** Inference authentication verified. See the E5 acceptance register.

**E5.7 completed:** Concurrency, request limits and timeouts verified. See the E5 acceptance register.

**E5.8 completed:** Dependency warning remediation verified. See the E5 acceptance register.

**E5 acceptance:** 490 cumulative passing tests, including dedicated semantic relevance cutoff and safe candidate backfill regression. All benchmark and security holdout gates passed.

**E5 completed (PR #11):** AI evaluation, adversarial security, authenticated inference, resource controls and retrieval relevance validated locally. 490 cumulative passing tests. Cloud expenditure EUR 0.

### E6 - Java functional event processing

- E6.1 CLOSED: existing event contracts and Java environment audited.
- E6.2 CLOSED: pinned Maven wrapper and Java 21 build verified.
- E6.6 CLOSED: event integrity, deduplication and lifecycle validation; 66 Java tests passed.
- E6.7 security acceptance PASSED: 53 Java dependencies audited, zero OSV findings, 66 Java tests passed.
- E6.7 document wire interoperability PASSED: actual C# serialization consumed by Java.
- E6.7 CLOSED: document and workflow wire interoperability verified.
- E6 CLOSED (8/8): Java functional event processing, cross-language contracts and 576 regression tests passed; merged through PR #12.
- Architecture: [E6 implementation record](docs/architecture/e6-java-event-processing.md).

- E6.3 CLOSED: Spring Boot service foundation. 3 Java tests passed.

- E6.4 CLOSED: Immutable versioned event contracts. 26 Java tests passed.

- E6.5 CLOSED: Functional transformations and analytics. 40 Java tests passed.

- E7.1 CLOSED: digest-pinned Kafka KRaft infrastructure, explicit topics, publication, consumption and persistent replay verified.
- E7.2-E7.4 OPEN: durable event production, Kafka Streams and final acceptance.

- E7.2 progress: Kafka producer transport and partition-key validation implemented; transactional integration remains open.

- E7.2 progress: document publisher integrated with Kafka; live transport delivery verified.

- E7.2 progress: durable PostgreSQL workflow journal and transactional append foundation implemented.

- E7.2 progress: automatic transactional workflow lifecycle capture and rollback verified.

- E7.2 progress: EF workflow lifecycle migration applied and registered successfully.

- E7.2 progress: durable ordered workflow dispatch and broker failure recovery verified.

- E7.2: Event tenant capacity aligned across C#, Java and PostgreSQL.

- E7.2: Database publication safeguards and failure quarantine implemented.

- E7.2: Document predecessor ordering CLOSED; 18 targeted tests passed.

- E7.2: Rejection lifecycle, atomic approval association and automatic expiration verified; continuation, domain rejection tests and recovery acceptance remain open.

- E7.2 workflow-bound gateway CLOSED: PostgreSQL-backed single-use approval consumption, workflow and caller binding, concurrent invocation protection and replay rejection verified locally.

- E7.2: Durable approved-action continuation, transactional execution receipts and fail-closed recovery implemented; four database acceptance scenarios passed.

- E7.2: Unused approval expiration and durable claim interruption recovery verified; external tool process interruption acceptance subsequently passed.

- E7.2 workflow-bound execution and continuation CLOSED: actual .NET process interruption, durable recovery and replay rejection verified.

- E7.2 domain rejection lifecycle CLOSED: 18 dedicated domain acceptance cases, PostgreSQL lifecycle acceptance and related regressions passed.

- E7.2 CLOSED (11/11): durable C# event publication, workflow lifecycle governance, approval continuation and live Kafka publication recovery verified.

- E7.3.1 CLOSED: Kafka Streams 4.1.1 foundation, aligned dependencies, persistent state stores and 77 passing Java tests.

- E7.3.2 CLOSED: persistent production Kafka Streams topology and 89 passing Java tests.

- E7.3.3 checkpoint: persistent global identity registry implemented; 97 Java tests passed; production repartition integration pending.

- E7.3.3 CLOSED: production global identity registry, dual repartition boundaries and 104 passing Java tests.

- E7.3.4 CLOSED: persistent sequence buffering, ordered lifecycle processing and 112 passing Java tests.

- E7.3.5 CLOSED: persistent tenant-wide document, workflow, approval and duration analytics; compacted tenant snapshot topic; 131 passing Java tests.

- E7.3.6 CLOSED: live Kafka dead-letter acceptance, eleven rejection scenarios, source-coordinate verification, sensitive-data redaction, failure isolation and verified test resource cleanup; 132 passing Java tests.

- E7.3.7 CLOSED: live Kafka broker interruption recovery, aborted transaction isolation, fresh-directory changelog restoration, buffered sequence recovery and duplicate suppression; 133 passing Java tests.

- E7.3.8 checkpoint: 133 passing Java tests; consolidated streaming regression passed; six mandatory operational acceptance gates remain open.

- E738-01 CLOSED: forced JVM termination during an uncommitted Kafka Streams transaction, committed replay, duplicate suppression and restored tenant counters; 134 passing Java tests.

### Durable streaming gap monitoring

Pending-event observation timestamps survive Kafka Streams state restoration.
Targeted topology, observation and live recovery tests passed.
Live scheduled monitoring, overdue detection and resolved-gap acceptance passed. E738-02 is closed for instance-local monitoring.

See [E738 monitoring evidence](docs/architecture/e738-gap-monitoring-evidence.md).

## How can I run or inspect the demo?

Multi-agent execution is disabled by default and requires
explicit development opt-in: `Agents__EnableDevelopmentExecution=true`.

E3 corrective acceptance: 314 cumulative passing tests across
.NET and Python, including live local inference.


The local demo requires .NET 10, PostgreSQL 17 with pgvector, and configured Development database and document-storage settings. From the repository root, run these PowerShell commands:

    dotnet restore NexusAgent.slnx
    dotnet build NexusAgent.slnx
    dotnet run --project src/NexusAgent.Api

To run the complete automated test suite:

    dotnet test NexusAgent.slnx

The API exposes /health and /health/ready. Business endpoints are available only in Development. Application code is in src, automated tests in tests, Azure infrastructure definitions in infra/terraform, and operational procedures in docs/runbooks/production-operations.md. Azure deployment is not required for the local demo.


### Authenticated local inference

Generate a strong session credential before starting either service.
Both Python and .NET must inherit the same NEXUS_INFERENCE_API_KEY
environment variable.

    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $env:NEXUS_INFERENCE_API_KEY = [BitConverter]::ToString($bytes).Replace("-", "").ToLowerInvariant()

Start Python in a child process inheriting that environment:

    Start-Process powershell.exe -ArgumentList '-NoExit','-File','services/nexusagent-ai/Start-LocalInference.ps1'

The inference service binds to 127.0.0.1:8001 with one worker.
Do not commit credentials or expose the local service publicly.

E5 consolidated regression: 486 passing tests, eight benchmark
gates and eight security holdout cases.
