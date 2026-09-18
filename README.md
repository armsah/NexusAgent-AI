# NexusAgent AI

**Governed Enterprise Agentic RAG & Knowledge Automation Platform on Microsoft Azure**

NexusAgent AI is a portfolio-grade enterprise AI platform designed to demonstrate production-oriented engineering for Retrieval-Augmented Generation (RAG), multi-agent orchestration, secure tool execution, human approval, evaluation, LLMOps, observability, and prompt-injection defense.

The system is designed around a fundamental separation:

> AI components may reason and propose. Deterministic application controls own authentication, authorization, workflow state, risk, approval, privileged execution, and auditability.

## Project Status

**Current milestone:** P1 — Core Application Foundation

P0 established the architecture, governance model, threat model, autonomy policy, non-functional requirements, and evaluation strategy.

P1 implements the first executable platform foundation: a layered .NET solution, application-owned workflow state machine, ASP.NET Core API, PostgreSQL persistence, database migrations, health/readiness probes, and automated architecture, unit, and integration tests.

### Phase Status

| Phase | Deliverable                        | Status   |
| ----- | ---------------------------------- | -------- |
| P0    | Architecture & Governance Baseline | Complete |
| P1    | Core Application Foundation        | Complete |
| P2    | Terraform Azure Baseline           | Next     |

### P1 Evidence

P1 currently provides:

- .NET 10 solution with explicit Domain, Application, Infrastructure, API, and Workers boundaries;
- application-owned workflow lifecycle and state-transition invariants;
- PostgreSQL persistence through Entity Framework Core and Npgsql;
- EF Core migration for workflow and workflow-step state;
- server-derived development requester and tenant identity;
- ASP.NET Core workflow lifecycle endpoints;
- string-based workflow status serialization in the HTTP contract;
- liveness and PostgreSQL-backed readiness endpoints;
- Docker Compose PostgreSQL development environment;
- automated unit, PostgreSQL integration, and architecture dependency tests;
- manual end-to-end verification of `Created → Running → Completed` persistence.

**P1 exit condition:** the core request lifecycle works locally with durable PostgreSQL state and enforced application boundaries.

---

## P1 Local Development

### Prerequisites

- .NET SDK 10
- Docker Desktop with Linux containers
- Docker Compose

The repository pins the .NET SDK through `global.json`.

### Start PostgreSQL

P1 uses PostgreSQL 17 locally through Docker Compose:

```powershell
docker compose up -d
docker compose ps
```

The container exposes PostgreSQL on host port `15432`:

```text
127.0.0.1:15432 -> container:5432
```

Port `15432` is intentionally used to avoid collisions with locally installed PostgreSQL instances that commonly use port `5432`.

The credentials in `compose.yaml` are development-only credentials and must not be reused for deployed environments.

### Configure the API Connection

Set the connection string in the PowerShell session that will run the API:

```powershell
$env:ConnectionStrings__NexusAgent="Host=127.0.0.1;Port=15432;Database=nexusagent;Username=nexusagent;Password=nexusagent_dev_only"
```

The connection string is supplied through configuration rather than embedded in application source code.

### Apply Database Migrations

Restore the local EF Core tool if required:

```powershell
dotnet tool restore
```

Apply the current migration:

```powershell
dotnet ef database update `
  --project src\NexusAgent.Infrastructure\NexusAgent.Infrastructure.csproj `
  --startup-project src\NexusAgent.Api\NexusAgent.Api.csproj
```

P1 creates durable `workflows` and `workflow_steps` state in PostgreSQL.

### Run the API

```powershell
dotnet run `
  --project src\NexusAgent.Api\NexusAgent.Api.csproj `
  --urls http://localhost:5080
```

### Health and Readiness

Liveness:

```powershell
Invoke-RestMethod `
  -Uri "http://localhost:5080/health" `
  -Method Get
```

Database-backed readiness:

```powershell
Invoke-RestMethod `
  -Uri "http://localhost:5080/health/ready" `
  -Method Get
```

A healthy local environment returns `healthy` for liveness and `ready` when the API can connect to PostgreSQL.

### Workflow Lifecycle API

Create a workflow:

```powershell
$workflow = Invoke-RestMethod `
  -Uri "http://localhost:5080/api/workflows/" `
  -Method Post `
  -ContentType "application/json" `
  -Body '{"request":"Verify the NexusAgent workflow lifecycle."}'

$workflowId = $workflow.id
```

Retrieve it:

```powershell
Invoke-RestMethod `
  -Uri "http://localhost:5080/api/workflows/$workflowId" `
  -Method Get
```

Start it:

```powershell
Invoke-RestMethod `
  -Uri "http://localhost:5080/api/workflows/$workflowId/start" `
  -Method Post
```

Complete it:

```powershell
Invoke-RestMethod `
  -Uri "http://localhost:5080/api/workflows/$workflowId/complete" `
  -Method Post
```

The verified P1 lifecycle is:

```text
Created -> Running -> Completed
```

Additional domain transitions support approval waiting, failure, and cancellation, while later phases add the governance mechanisms that drive those transitions.

### Development Identity

P1 derives a fixed development identity on the server:

```text
requesterId = local-developer
tenantId    = local-development
```

Requester and tenant identity are not accepted from the workflow request body as authoritative identity.

This is a local-development mechanism only. P3 replaces it with Microsoft Entra authentication and managed workload identity.

### Build and Test

Build the complete solution:

```powershell
dotnet build NexusAgent.slnx
```

Run all automated tests:

```powershell
dotnet test NexusAgent.slnx --no-build
```

The P1 test suite covers:

- domain workflow invariants and state transitions;
- application behavior;
- PostgreSQL workflow persistence;
- architectural dependency boundaries.

The PostgreSQL integration test requires the local Docker Compose database to be running on port `15432`.

---

## Business Scenario

European enterprises maintain operational knowledge across:

- policies;
- contracts;
- manuals;
- standard operating procedures;
- incident records;
- FAQs;
- internal APIs and business systems.

Employees need AI-assisted knowledge and workflow automation while preserving:

- document authorization;
- tenant isolation;
- evidence and citations;
- prompt-injection defenses;
- deterministic policy;
- human approval for consequential actions;
- auditability.

NexusAgent AI addresses this scenario with governed RAG and bounded agentic workflows.

---

## Architecture

NexusAgent uses an application-owned control plane implemented in ASP.NET Core/C#.

The application owns:

- workflow state;
- authorization-sensitive transitions;
- risk decisions;
- approval state;
- tool execution eligibility;
- idempotency;
- audit correlation.

Microsoft Foundry / Azure OpenAI provides AI capabilities behind application abstractions.

Azure AI Search provides authorization-aware enterprise retrieval.

Azure Service Bus supports durable asynchronous workflows.

PostgreSQL stores durable workflow and governance state.

Blob Storage / ADLS stores source documents and large artifacts.

### High-Level Architecture

```text
Enterprise User
      |
      v
ASP.NET Core Agent Gateway
      |
      v
Application-Owned Orchestration
      |
      +-------------------+--------------------+
      |                   |                    |
      v                   v                    v
Azure AI Search     Foundry / Azure       Azure Service Bus
                    OpenAI                      |
                                               v
                                            Workers
                                               |
                                               v
                                         Tool Gateway
                                               |
                                               v
                                      Business Systems

Durable state  -> PostgreSQL
Source content -> Blob Storage / ADLS
Telemetry      -> OpenTelemetry / Application Insights
```

Detailed architecture:

- [Architecture Specification](docs/architecture/architecture.md)
- [Problem Statement](docs/architecture/problem-statement.md)
- [Data Classification](docs/architecture/data-classification.md)
- [Non-Functional Requirements](docs/architecture/non-functional-requirements.md)

---

## Agent Model

NexusAgent defines five logical agent responsibilities.

| Agent        | Responsibility                              | Hard Boundary                              |
| ------------ | ------------------------------------------- | ------------------------------------------ |
| Supervisor   | Classify, plan, delegate, enforce budgets   | No direct business-tool execution          |
| Knowledge    | Retrieve and synthesize authorized evidence | No ACL bypass                              |
| Policy       | Interpret enterprise policy                 | Cannot grant authorization                 |
| Action       | Produce typed action proposals              | Proposal only; no unrestricted execution   |
| Verification | Verify evidence/results                     | Cannot override policy or missing approval |

These are logical responsibilities and do not require five independently deployed services.

---

## Governed Tool Execution

Privileged business actions are isolated behind a Tool Gateway.

Execution requires deterministic validation of:

```text
typed tool contract
        +
schema validation
        +
server-derived identity
        +
authorization
        +
risk classification
        +
human approval where required
        +
idempotency
        +
egress restrictions
        +
audit
```

The model is never given a generic privileged shell, SQL executor, Terraform/ARM executor, kubectl interface, filesystem/system tool, or arbitrary HTTP client.

---

## Autonomy Levels

| Level                   | Meaning                               | Execution                        |
| ----------------------- | ------------------------------------- | -------------------------------- |
| L0 — Observe            | Read/retrieve/explain                 | No external mutation             |
| L1 — Recommend          | Produce structured proposal           | No external mutation             |
| L2 — Approved execution | Consequential mutation                | Explicit human approval required |
| L3 — Bounded autonomy   | Narrow pre-approved low-risk mutation | Deterministically constrained    |

Any action that changes an external, business, or production system is L2 by default.

L3 must be explicitly configured for a narrowly bounded low-risk operation.

See [Autonomy and Human-Approval Policy](docs/ai-safety/autonomy-policy.md).

---

## Retrieval Architecture

The target RAG pipeline is:

```text
Source documents
      |
      v
Normalize + version
      |
      v
ACL / metadata extraction
      |
      v
Structure-aware chunking
      |
      v
Embedding generation
      |
      v
Azure AI Search
      |
========================
       Query path
========================
      |
      v
Server-derived security filter
      |
      v
Hybrid lexical + vector retrieval
      |
      v
Semantic reranking where configured
      |
      v
Deduplication + context budgeting
      |
      v
Grounded generation
      |
      v
Citation verification
```

Authorization filtering occurs before enterprise evidence reaches the model.

---

## Security Model

NexusAgent treats both model output and retrieved content as untrusted.

Core security invariants include:

- model output cannot authenticate a user;
- model output cannot grant authorization;
- tenant scope is server-derived;
- document ACL filtering occurs before model context;
- retrieved content cannot override trusted application policy;
- secrets are not intentionally supplied to models;
- no unrestricted privileged tool exists;
- tool identity is server-derived;
- L2 execution requires exact valid approval;
- changed arguments invalidate approval;
- retries cannot duplicate business side effects;
- dependency failure cannot broaden authority.

Primary modeled threats include:

- direct prompt injection;
- indirect prompt injection;
- cross-tenant leakage;
- ACL bypass;
- tool privilege escalation;
- approval replay/substitution;
- data exfiltration;
- model-output spoofing;
- telemetry leakage;
- supply-chain compromise;
- cost denial of service.

See [Threat Model](docs/threat-model/threat-model.md).

---

## Evaluation Strategy

NexusAgent evaluates the complete governed system rather than only model output.

### Retrieval

- Recall@K
- Precision@K
- MRR/NDCG
- security-filter correctness
- stale-version retrieval rate

### Grounded Answers

- groundedness
- relevance
- completeness
- citation precision/recall
- unsupported-claim rate

### Agent Workflows

- completion
- plan validity
- tool selection
- argument accuracy
- steps
- latency
- token cost

### Safety

- unauthorized-action rate
- prompt-injection success rate
- data-exfiltration rate
- cross-tenant leakage

### Human Approval

- approval correctness
- rejected-action execution
- expiry behavior
- replay behavior

### Operations

- timeout rate
- DLQ rate
- fallback success
- P95 latency
- cost per workflow

Hard safety gates established at P0 include:

```text
unauthorized destructive actions = 0
cross-tenant leakage cases = 0
unapproved L2 executions = 0
rejected-action executions = 0
```

Empirical AI-quality thresholds will be established from measured baselines rather than invented during architecture design.

See [Evaluation Specification](docs/evaluations/evaluation-specification.md).

---

## Non-Functional Targets

Initial portfolio targets include:

| Area                                  | Target                                                           |
| ------------------------------------- | ---------------------------------------------------------------- |
| Availability                          | 99.9% demo API SLO                                               |
| Standard grounded Q&A latency         | P95 < 8 seconds                                                  |
| Unauthorized destructive actions      | 0                                                                |
| Cross-tenant leakage in curated suite | 0                                                                |
| Unapproved L2 executions              | 0                                                                |
| Rejected-action executions            | 0                                                                |
| Auditability                          | Consequential actions linked to identity/evidence/policy/release |
| Cost                                  | Token/search/tool cost attributable per workflow                 |

Long-running workflows use asynchronous execution and are not expected to meet the standard grounded-Q&A latency target.

---

## Architecture Decisions

### ADR-001 — Application-Owned Agent Orchestration

NexusAgent owns the authoritative workflow state machine in ASP.NET Core/workers.

Foundry agent capabilities may be used behind application abstractions but are not the sole authority for business-critical workflow state.

[Read ADR-001](docs/adr/ADR-001-custom-orchestration.md)

### ADR-002 — Azure AI Search for Enterprise Retrieval

Azure AI Search is the primary enterprise hybrid/vector retrieval platform.

PostgreSQL remains the transactional workflow/governance store.

[Read ADR-002](docs/adr/ADR-002-azure-ai-search.md)

---

## Target Technology Stack

| Area                      | Technology                                         |
| ------------------------- | -------------------------------------------------- |
| API/control plane         | ASP.NET Core / C#                                  |
| Evaluation/data utilities | Python                                             |
| AI                        | Microsoft Foundry / Azure OpenAI                   |
| Retrieval                 | Azure AI Search                                    |
| Messaging                 | Azure Service Bus                                  |
| Compute                   | Azure Container Apps                               |
| Transactional state       | Azure Database for PostgreSQL Flexible Server      |
| Documents/artifacts       | Azure Blob Storage / ADLS                          |
| Identity                  | Microsoft Entra ID                                 |
| Workload identity         | Azure managed identities                           |
| Secrets                   | Azure Key Vault                                    |
| Infrastructure            | Terraform                                          |
| Observability             | OpenTelemetry, Application Insights, Azure Monitor |
| CI/CD identity            | GitHub Actions OIDC                                |

---

## Repository Structure

```text
.
├── README.md
├── NexusAgent.slnx
├── global.json
├── dotnet-tools.json
├── compose.yaml
├── docs/
│   ├── architecture/
│   ├── adr/
│   ├── threat-model/
│   ├── ai-safety/
│   ├── evaluations/
│   ├── runbooks/
│   └── cost/
├── src/
│   ├── NexusAgent.Domain/
│   ├── NexusAgent.Application/
│   ├── NexusAgent.Infrastructure/
│   ├── NexusAgent.Api/
│   └── NexusAgent.Workers/
└── tests/
    ├── NexusAgent.UnitTests/
    ├── NexusAgent.IntegrationTests/
    └── NexusAgent.ArchitectureTests/
```

P1 establishes the executable solution boundaries. Additional components such as retrieval, AI adapters, the Tool Gateway, evaluation infrastructure, and Azure deployment assets are introduced incrementally in later phases.

---

## Implementation Roadmap

| Phase  | Deliverable                                                                        |
| ------ | ---------------------------------------------------------------------------------- |
| **P0** | **Complete — Architecture, governance, threat model, autonomy, evaluation design** |
| **P1** | **Complete — .NET solution, API, boundaries, PostgreSQL workflow state**           |
| **P2** | **Next — Terraform Azure baseline**                                                |
| P3     | Entra authentication, managed identity, GitHub OIDC                                |
| P4     | Document ingestion and versioning                                                  |
| P5     | Azure AI Search hybrid/vector retrieval + ACL metadata                             |
| P6     | Foundry/Azure OpenAI model adapter                                                 |
| P7     | Grounded Q&A with stable citations                                                 |
| P8     | Query rewrite, reranking, context budgeting                                        |
| P9     | Supervisor + Knowledge/Policy agent orchestration                                  |
| P10    | Typed Tool Gateway                                                                 |
| P11    | Risk engine + human approval                                                       |
| P12    | Service Bus async execution, retries, DLQ                                          |
| P13    | Verification Agent + deterministic postconditions                                  |
| P14    | Offline evaluation harness and release gates                                       |
| P15    | Prompt-injection/content-safety test suite                                         |
| P16    | End-to-end OpenTelemetry/Application Insights                                      |
| P17    | Token and cost budgets                                                             |
| P18    | Production-reference private networking                                            |
| P19    | Load, failure, rollback benchmark and runbooks                                     |

---

## P0 Evidence

The P0 architecture and governance evidence is contained in:

```text
docs/
├── architecture/
│   ├── architecture.md
│   ├── data-classification.md
│   ├── non-functional-requirements.md
│   └── problem-statement.md
├── adr/
│   ├── ADR-001-custom-orchestration.md
│   └── ADR-002-azure-ai-search.md
├── ai-safety/
│   └── autonomy-policy.md
├── evaluations/
│   └── evaluation-specification.md
└── threat-model/
    └── threat-model.md
```

## P1 Phase Exit

P1 establishes:

- a buildable .NET 10 solution with explicit architectural boundaries;
- application-owned workflow state and transition invariants;
- ASP.NET Core lifecycle endpoints;
- durable PostgreSQL workflow persistence through EF Core;
- reproducible database migrations;
- server-derived local development identity;
- liveness and database-backed readiness probes;
- string-based workflow status contracts;
- automated unit, PostgreSQL integration, and architecture tests;
- verified local `Created → Running → Completed` request lifecycle.

P1 deliberately does not implement production authentication, RAG, model invocation, multi-agent orchestration, privileged tool execution, human approval records, Azure infrastructure, or production observability. Those capabilities remain assigned to subsequent phases.

**Next phase:** P2 — Terraform Azure Baseline.
