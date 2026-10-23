# NexusAgent AI

**Governed Enterprise Agentic RAG & Knowledge Automation Platform on Microsoft Azure**

NexusAgent AI is a portfolio-grade enterprise AI platform designed to demonstrate production-oriented engineering for Retrieval-Augmented Generation (RAG), multi-agent orchestration, secure tool execution, human approval, evaluation, LLMOps, observability, and prompt-injection defense.

The system is designed around a fundamental separation:

> AI components may reason and propose. Deterministic application controls own authentication, authorization, workflow state, risk, approval, privileged execution, and auditability.

## Project Status

**Current milestone:** P7 Answer Verification and Prompt-Injection Defense complete; 211 tests passed (102 unit, 3 architecture, 106 integration). Merged into `main` and pushed to GitHub. P8 Observability and Evaluation is next.

| Phase | Deliverable | Status |
|---|---|---|
| P0 | Governance, document authorization model, threat model, evaluation baseline | Complete |
| P1 | Versioned document ingestion, transactional outbox, durable event processing | Complete |
| P2 | Tenant-aware ACL-filtered lexical and hybrid vector retrieval | Complete locally |
| P3 | Grounded Q&A, source citations, verification, abstention | Complete locally |
| P4 | Application-owned bounded orchestration, durable workflow state, retries, cancellation, authorization, and concurrency protection | Complete — merged into main |
| P5 | Allow-listed typed Tool Gateway | Complete — merged into main |
| P6 | Risk decisions, human approvals, immutable action payloads | Complete - merged into main |
| P7 | Verifier, prompt-injection defense, adversarial retrieval | Complete - merged into main |
| P8 | Token spend, retrieval quality, OpenTelemetry traces | Planned |
| P9 | Failure drills, load testing, recovery, release rollback | Planned |

### Implemented Capabilities

**Application foundation**
- .NET 10 solution with Domain, Application, Infrastructure, API, and Workers projects.
- PostgreSQL persistence using Entity Framework Core and Npgsql.
- Durable workflow lifecycle and database-backed readiness checks.
- Unit, integration, and architecture tests.

**P1 — Document ingestion**
- Document CRUD with tenant and document ACL metadata.
- Versioned document content and document history.
- Transactional PostgreSQL outbox.
- Durable event publication and processing with retries, leases, and failure handling.
- Background document indexing with persistent processing checkpoints.

**P2 — Secure retrieval**
- PostgreSQL full-text document indexing and lexical ranking.
- pgvector-backed 1,536-dimensional embeddings.
- Hybrid lexical/vector retrieval with reciprocal-rank fusion.
- Server-derived development tenant and requester identity.
- Tenant isolation, document ACL filtering, and exclusion of deleted documents.
- Content-version checks that prevent stale search indexes from being returned.
- Optional Azure OpenAI embedding provider; no Azure deployment required for local development.
- HTTP integration tests covering unauthorized, cross-tenant, deleted, stale, and hybrid retrieval scenarios.

**Verification:** The complete local solution test run passed, including 106 integration tests, with zero integration failures.

**Important limitations:** Development identity is not production authentication. Azure resources have not been provisioned. Grounded question answering and application-owned workflow orchestration are implemented locally. Production authentication, model-driven Supervisor/Knowledge/Policy agents, privileged tools, human approval workflows, and production observability remain future work.

**Azure deployment:** Deferred until explicitly authorized.

---

## Existing .NET Foundation — Local Development

### Prerequisites
- .NET SDK 10
- Docker Desktop with Linux containers
- Docker Compose

The repository pins the .NET SDK through `global.json`.

### Start PostgreSQL
Local development uses PostgreSQL 17 with the pgvector extension through Docker Compose:

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

Database migrations create durable workflow, document, outbox, search-index, and vector-index state in PostgreSQL.

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
tenantId    = local-development
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

The implemented local retrieval backend uses PostgreSQL full-text search and pgvector. Azure AI Search remains a documented target architecture option, not a deployed dependency.

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
      |                   |                    |
      v                   v                    v
Azure AI Search     Foundry / Azure       Azure Service Bus
                    OpenAI                      |
                                               v
                                            Workers
                                               |
                                               v
                                         Tool Gateway
                                               |
                                               v
                                      Business Systems
Durable state  -> PostgreSQL
Source content -> Blob Storage / ADLS
Telemetry      -> OpenTelemetry / Application Insights
```

Detailed architecture:

- [Architecture Specification](docs/architecture/architecture.md)
- [Problem Statement](docs/architecture/problem-statement.md)
- [Data Classification](docs/architecture/data-classification.md)
- [Non-Functional Requirements](docs/architecture/non-functional-requirements.md)

---

## Agent Model
NexusAgent defines five logical agent responsibilities.

| Agent        | Responsibility                              | Hard Boundary                              |
| ------------ | ------------------------------------------- | ------------------------------------------ |
| Supervisor   | Classify, plan, delegate, enforce budgets   | No direct business-tool execution          |
| Knowledge    | Retrieve and synthesize authorized evidence | No ACL bypass                              |
| Policy       | Interpret enterprise policy                 | Cannot grant authorization                 |
| Action       | Produce typed action proposals              | Proposal only; no unrestricted execution   |
| Verification | Verify evidence/results                     | Cannot override policy or missing approval |

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
| Level                   | Meaning                               | Execution                        |
| ----------------------- | ------------------------------------- | -------------------------------- |
| L0 — Observe            | Read/retrieve/explain                 | No external mutation             |
| L1 — Recommend          | Produce structured proposal           | No external mutation             |
| L2 — Approved execution | Consequential mutation                | Explicit human approval required |
| L3 — Bounded autonomy   | Narrow pre-approved low-risk mutation | Deterministically constrained    |

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

| Area                                  | Target                                                           |
| ------------------------------------- | ---------------------------------------------------------------- |
| Availability                          | 99.9% demo API SLO                                               |
| Standard grounded Q&A latency         | P95 < 8 seconds                                                  |
| Unauthorized destructive actions      | 0                                                                |
| Cross-tenant leakage in curated suite | 0                                                                |
| Unapproved L2 executions              | 0                                                                |
| Rejected-action executions            | 0                                                                |
| Auditability                          | Consequential actions linked to identity/evidence/policy/release |
| Cost                                  | Token/search/tool cost attributable per workflow                 |

Long-running workflows use asynchronous execution and are not expected to meet the standard grounded-Q&A latency target.

---

## Architecture Decisions

### ADR-001 — Application-Owned Agent Orchestration
NexusAgent owns the authoritative workflow state machine in ASP.NET Core/workers.

Foundry agent capabilities may be used behind application abstractions but are not the sole authority for business-critical workflow state.

[Read ADR-001](docs/adr/ADR-001-custom-orchestration.md)

### ADR-002 — Azure AI Search for Enterprise Retrieval
ADR-002 documents Azure AI Search as the target enterprise retrieval platform. The implemented P2 retrieval backend uses PostgreSQL full-text search and pgvector; Azure AI Search integration has not been implemented.

PostgreSQL remains the transactional workflow/governance store.

[Read ADR-002](docs/adr/ADR-002-azure-ai-search.md)

---

## Target Technology Stack
| Area                      | Technology                                         |
| ------------------------- | -------------------------------------------------- |
| API/control plane         | ASP.NET Core / C#                                  |
| Evaluation/data utilities | Python                                             |
| AI                        | Microsoft Foundry / Azure OpenAI                   |
| Retrieval                 | Azure AI Search                                    |
| Messaging                 | Azure Service Bus                                  |
| Compute                   | Azure Container Apps                               |
| Transactional state       | Azure Database for PostgreSQL Flexible Server      |
| Documents/artifacts       | Azure Blob Storage / ADLS                          |
| Identity                  | Microsoft Entra ID                                 |
| Workload identity         | Azure managed identities                           |
| Secrets                   | Azure Key Vault                                    |
| Infrastructure            | Terraform                                          |
| Observability             | OpenTelemetry, Application Insights, Azure Monitor |
| CI/CD identity            | GitHub Actions OIDC                                |

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
├── tests/
│   ├── NexusAgent.UnitTests/
│   ├── NexusAgent.IntegrationTests/
│   └── NexusAgent.ArchitectureTests/
└── infra/
    └── terraform/
        └── environments/
            └── dev/
                ├── .terraform.lock.hcl
                ├── ai.tf
                ├── apps.tf
                ├── backend.tf
                ├── data.tf
                ├── main.tf
                ├── outputs.tf
                ├── platform.tf
                ├── security.tf
                ├── variables.tf
                ├── versions.tf
                └── terraform.tfvars.example
```

Under the previous roadmap, P1 established the executable .NET solution and P2 added the Terraform Azure infrastructure baseline. These completed foundations are retained. The revised P0–P9 roadmap governs all subsequent implementation. Azure deployment remains deferred until explicitly authorized.

---

## Implementation Roadmap

The following P0–P9 roadmap supersedes the original P0–P19 roadmap.

| Phase | Implementation | Azure focus | Required evidence | Exit criterion |
|---|---|---|---|---|
| P0 | Governed knowledge use cases, document ACLs, citation targets, action-risk tiers | Architecture / Entra | Threat model and evaluation set | Retrieval and autonomy boundaries approved |
| P1 | Ingestion API, Blob document versioning, ingestion events | ASP.NET Core / Blob / Service Bus | Ingestion replay tests | Updates and deletions propagate safely |
| P2 | ACL-filtered hybrid/vector indexes and tenant metadata | Azure AI Search | Cross-tenant denial tests | Only authorized passages retrieved |
| P3 | Grounded Q&A, citation verification, abstention | Azure OpenAI / Foundry | Grounding benchmark | Unsupported claims rejected |
| P4 | Supervisor, Knowledge and Policy agents with durable task state | Orchestration / PostgreSQL | Workflow traces | Complete — merged into main |
| P5 | Allow-listed knowledge actions through typed Tool Gateway | Container Apps / API | Tool contracts | Complete — merged into main |
| P6 | Risk decisions, human approvals, immutable action payloads | Entra / Policy / PostgreSQL | Approval replay tests | Privileged changes require valid approval |
| P7 | Verifier agent, prompt-injection defenses, adversarial retrieval | Foundry / AI Security | Red-team report | Malicious content cannot redirect actions |
| P8 | Token spend, retrieval quality, end-to-end traces | OpenTelemetry / Monitor | Quality-cost dashboard | Every answer traceable to evidence |
| P9 | Load, model outages, index drift, recovery, release rollback | Terraform / GitHub Actions | Failure drills and runbooks | Reproducible portfolio demonstration |

### Existing Foundation

The previous implementation produced:

- A buildable .NET 10 solution with Domain, Application, Infrastructure, API and Worker projects.
- PostgreSQL-backed workflow state and EF Core migrations.
- Unit, integration and architecture tests.
- Terraform configuration for the Azure development environment.
- Locally validated Terraform configuration and plan.

These assets are reused in the revised phases.

### Deployment Policy

Complete implementation and local verification before requesting approval
to provision billable Azure resources. Azure deployment is a separate,
explicitly authorized milestone. Live Azure verification remains pending.

---## P0 Evidence

Revised P0 establishes the governed knowledge architecture and security
boundaries for the P0–P9 implementation roadmap.

### Governance Deliverables

| Deliverable | Evidence |
|---|---|
| Enterprise knowledge use cases | `docs/architecture/governed-knowledge-use-cases.md` |
| Document authorization and tenant isolation | `docs/architecture/document-authorization-model.md` |
| System architecture and trust boundaries | `docs/architecture/architecture.md` |
| Data classification | `docs/architecture/data-classification.md` |
| Non-functional requirements | `docs/architecture/non-functional-requirements.md` |
| Action-risk tiers and approval boundaries | `docs/ai-safety/autonomy-policy.md` |
| Threat model | `docs/threat-model/threat-model.md` |
| Evaluation requirements | `docs/evaluations/evaluation-specification.md` |
| Versioned evaluation cases | `docs/evaluations/p0-evaluation-cases.json` |
| Architecture decisions | `docs/adr/ADR-001-custom-orchestration.md`, `docs/adr/ADR-002-azure-ai-search.md` |

### P0 Acceptance Criteria

- Governed knowledge use cases are documented.
- Tenant and document ACL boundaries are defined.
- Unauthorized retrieval is explicitly prohibited.
- Citation verification and abstention are required.
- Action-risk tiers and human approval boundaries are defined.
- Threat scenarios are mapped to evaluation cases.
- The initial evaluation dataset contains 12 uniquely identified cases.
- The revised P0–P9 implementation roadmap is documented.

**Implementation status:** P0 design and governance baseline complete.
Grounded generation,
approval enforcement, production authentication, and Azure verification remain pending.

---
## Previous Roadmap — P1 Foundation Evidence

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

P1 deliberately did not implement production authentication, RAG, model invocation, multi-agent orchestration, privileged tool execution, human approval records, or production observability. P2 subsequently completed the Terraform Azure infrastructure baseline without deploying resources.

**Original governance milestone:** Revised P0 — Governed Knowledge Architecture and Security Boundaries.

---

## Existing Azure Terraform Infrastructure Foundation

### Overview

The `infra/terraform/environments/dev/` directory defines the Azure development environment for NexusAgent AI using Terraform.

The infrastructure supports .NET API and background-worker workloads, messaging, document storage, observability, and optional AI services.

**Status:** Infrastructure-as-Code validated. Azure deployment deferred.

### Technology Stack
- Terraform >= 1.9
- AzureRM provider 4.x
- Microsoft Azure (`swedencentral`)
- Azure Container Apps
- Azure Container Registry
- Azure PostgreSQL Flexible Server
- Azure Service Bus
- Azure Blob Storage
- Azure Key Vault
- Azure Monitor / Application Insights
- Azure AI Search (optional)
- Azure OpenAI (optional)

### Infrastructure Components
| Component                      | Purpose                                       |
| ------------------------------ | --------------------------------------------- |
| Resource Group                 | Logical resource organization                 |
| Container Registry             | Container image storage                       |
| Container Apps Environment     | Hosting environment for application workloads |
| API Container App              | HTTP API hosting                              |
| Worker Container App           | Background processing                         |
| PostgreSQL Flexible Server     | Relational data persistence                   |
| Service Bus                    | Asynchronous workflow messaging               |
| Blob Storage                   | Document storage                              |
| Key Vault                      | Secrets management                            |
| User-Assigned Managed Identity | Workload authentication                       |
| Log Analytics                  | Centralized logging                           |
| Application Insights           | Application observability                     |
| Azure AI Search                | Optional retrieval infrastructure             |
| Azure OpenAI                   | Optional AI model service                     |

### Deployment Controls
The following variables control optional infrastructure:

```hcl
deploy_api           = false
deploy_data_services = false
enable_ai_services   = false
```

When disabled:

- API and Worker Container Apps are excluded from the plan.
- PostgreSQL resources are excluded from the plan.
- Azure AI Search and Azure OpenAI resources are excluded from the plan.

Core infrastructure remains part of the Terraform plan.

**These flags do not make a deployment free.**

### Security
The configuration uses:

- User-assigned managed identity for application workloads.
- Azure RBAC for resource authorization.
- Container Registry authentication without an admin account.
- Service Bus with local authentication disabled.
- Storage with shared-key authentication disabled.
- Microsoft Entra ID authentication for supported Storage operations.
- Private Blob container access.
- HTTPS-only Storage traffic.
- TLS 1.2 minimum for Storage and Service Bus.
- Key Vault RBAC authorization and purge protection.

Some Azure services currently have public network endpoints enabled.

Network isolation and private endpoints require additional design and

validation before a production deployment.

### Terraform State
The intended production approach is an Azure Storage remote backend.

The remote backend is currently documented in `backend.tf` but is

not enabled.

Terraform uses the default local backend for development-time validation

and planning.

State files, plans, and local variable files must not be committed.

### Local Validation

Prerequisites:

1. Terraform CLI installed.
2. Azure CLI installed and authenticated.
3. Access to the target Azure subscription.
4. A local `terraform.tfvars` file based on `terraform.tfvars.example`.

Initialize Terraform without a remote backend:

```powershell
terraform init -backend=false -reconfigure
```

Format and validate:

```powershell
terraform fmt
terraform validate
```

Generate a plan:

```powershell
terraform plan -input=false
```

The current development configuration produces:

```text
Plan: 16 to add, 0 to change, 0 to destroy.
```

This is a planning result, not proof that Azure resource provisioning

or application integration will succeed.

### Deployment Prerequisites

Before any deployment:

1. Obtain explicit approval to provision billable Azure resources.
2. Configure and initialize the Azure Storage remote-state backend.
3. Confirm resource names and Azure service availability.
4. Verify required Azure RBAC permissions.
5. Verify Microsoft Entra ID access for Storage container management.
6. Replace placeholder container images with published images.
7. Configure PostgreSQL credentials securely.
8. Review networking, public access, and production security requirements.
9. Confirm Azure AI service quotas and regional availability.
10. Review estimated Azure costs and establish budget controls.
11. Test application connectivity, identity permissions, and observability.

### Current Validation Status

- Terraform initialization: passed.
- Terraform formatting: passed.
- Terraform validation: passed.
- Terraform plan: passed.
- Planned core resources: 16.
- Optional application resources: disabled.
- Optional PostgreSQL resources: disabled.
- Optional AI resources: disabled.
- Azure deployment: not performed.

### Project Policy

Azure infrastructure deployment is intentionally deferred until the

the revised implementation milestones are completed and deployment is explicitly approved.

Azure infrastructure was not deployed during P2. Deployment and runtime integration verification remain deferred until the revised implementation milestones are completed and deployment is explicitly approved.

**Previous roadmap P2 status:** Infrastructure-as-Code complete; deployment deferred. Revised P2 retrieval implementation is complete locally.

## P3 - Grounded Question Answering

The development-only `POST /api/questions/answer` endpoint retrieves
authorized, current-version evidence from PostgreSQL full-text indexes.

The endpoint enforces tenant isolation, document ACLs, active document
status, and content-version consistency. Responses include source IDs,
document IDs, document versions, content versions, and excerpts.

Without a model provider, answers are extractive. An optional Azure OpenAI
chat-completions provider can generate answers from authorized evidence.
Generated citation IDs are validated; invalid citations or model failures
cause abstention.

Example request:

    POST /api/questions/answer
    Content-Type: application/json

    {"question":"What does the security policy require?"}

Optional environment variables:

    GroundedAnswers__Provider=AzureOpenAI
    GroundedAnswers__AzureOpenAI__Endpoint=<HTTPS endpoint>
    GroundedAnswers__AzureOpenAI__Deployment=<chat deployment>
    GroundedAnswers__AzureOpenAI__ApiKey=<secret>

No Azure resources are provisioned by P3.

Security limitations: Citation-ID validation does not prove factual
entailment. The endpoint currently uses a development identity and is
not exposed as a production-authenticated endpoint.

## P3 Implementation Evidence

**Status:** Complete locally; merged into `main`.

P3 extends the governed retrieval pipeline with development-only
grounded question answering.

### Implemented capabilities

- `POST /api/questions/answer` for grounded questions.
- PostgreSQL full-text evidence retrieval.
- Tenant-aware and principal-aware authorization filtering.
- Active-document and current-content-version enforcement.
- Citation records containing source IDs, document IDs, document
  versions, content versions, and source excerpts.
- Extractive fallback without external model dependencies.
- Optional Azure OpenAI chat-completions generation.
- Citation-ID validation and fail-closed abstention.
- HTTP integration tests covering authorized retrieval, tenant and
  ACL isolation, stale/deleted documents, invalid citations,
  missing evidence, and generator failures.

### Verified quality gate

- Unit tests: 42 passed.
- Integration tests: 145 passed.
- Failures: 0.
- P3 implementation commit: `df6cb1a`.
- P3 merge commit: `3104e20`.

### Security boundaries

The endpoint currently uses a server-derived development identity.
Production authentication and Microsoft Entra integration are not
implemented.

Citation-ID validation ensures that generated references correspond
to authorized retrieved evidence. It does not independently prove
that every factual claim is supported by that evidence.

The optional Azure OpenAI adapter is implemented but has not been
validated against a live Azure deployment. No Azure resources were
provisioned.

### Next milestone

**P4 — Agent Orchestration:** Complete; merged into `main`. See the
P4 Implementation Evidence section below for verified capabilities,
security boundaries, and remaining limitations.

---

## P4 Implementation Evidence

**Status:** Complete; validated, merged into `main`, and pushed to GitHub.

P4 introduces deterministic, application-owned orchestration over persisted
workflow state. Execution is controlled by application code rather than
delegating authoritative workflow transitions to an LLM or external agent.

### Implemented capabilities

- Bounded workflow plans with configurable step and retry limits.
- Explicit workflow transitions and persisted step outcomes.
- Retryable step failures, terminal failures, cancellation, and execution timeouts.
- Tenant/requester authorization before workflow reads and mutations.
- Development-only workflow HTTP endpoints; workflow routes are unavailable
  in Staging and Production.
- PostgreSQL `xmin` optimistic concurrency protection against competing
  workflow claims.
- Concurrency regression coverage verifying that a losing worker does not
  execute workflow steps.
- Recovery tests verifying persistence of cancellation and failure transitions.

### Verification

- Complete solution build passed.
- Unit, PostgreSQL integration, and architecture test suites passed.
- Integration tests: 106 passed.
- Architecture tests: 3 passed.
- Failures reported by the full validation script: 0.

### Boundaries and limitations

This milestone provides the orchestration foundation, not a production
multi-agent runtime. Model-driven Supervisor, Knowledge, and Policy agents,
production authentication, distributed crash recovery/resumption, typed
privileged tools, human approvals, and production observability are not
claimed as implemented.

The local API uses development identity. No Azure resources were provisioned.

### Next milestone

**P5 — Typed Tool Gateway:** Implemented allow-listed tool contracts,
deterministic authorization boundaries, and controlled tool execution.
## P5 Implementation Evidence — Typed Tool Gateway

P5 introduces an application-owned, in-process typed tool gateway.

Implemented and locally validated:

- Generic typed tool requests, results, and executor contracts.
- Explicit tool registration with duplicate-name rejection.
- Default-deny tool allow-list.
- Per-invocation caller identity and authorization-policy checks.
- Rejection of unregistered tools and incompatible input/output types.
- Configurable execution timeouts with cancellation propagation.
- Correlation IDs and structured audit-ready execution outcomes.
- Containment of tool exceptions without exposing exception messages.
- Unit tests for authorization, allow-list enforcement, registration,
  type mismatches, successful execution, failures, cancellation,
  timeouts, and audit outcomes.

P5 validation: 179 tests passed, 0 failed, 0 skipped.

Security and operational boundaries:

- The gateway does not dynamically execute shell commands or arbitrary
  model-generated code.
- No production tools are registered by this milestone.
- Authorization defaults to deny-all; a production authorization policy
  must be explicitly implemented and configured.
- The available null audit sink does not persist audit records.
- Timeouts depend on tool implementations honoring cancellation.
- Gateway integration into orchestrated workflow steps is future work.
- No Azure resources have been provisioned or deployed.

P6 Approval Gates: Complete; see the Phase 6 implementation evidence below.
## P6 Implementation Evidence - Approval Gates

**Status:** Complete locally; validated with 188 passing tests; merged into `main` and pushed to GitHub.

**Feature commit:** `408182f`
**Merge commit:** `35dfae3`

### Approval security controls

Phase 6 adds human-approval enforcement to the typed tool gateway.

- **Risk classification:** Tools are classified as Low, Medium, High, or Critical. Unknown tools default to Critical.
- **Approval requirements:** High- and Critical-risk actions require approval before execution.
- **Action binding:** Approval requests bind the tenant, requester, tool name, and serialized input digest.
- **Separation of duties:** A requester cannot approve their own action.
- **Lifecycle controls:** Approvals support pending, approved, denied, expired, and consumed states.
- **Replay prevention:** Approved actions are consumed atomically and cannot be reused.
- **Fail-closed execution:** Missing, invalid, expired, denied, or mismatched approvals prevent high-risk tool execution.
- **Regression coverage:** Tests cover missing approval, self-approval, tenant/requester isolation, payload tampering, denial, expiration, replay prevention, and unknown-risk classification.

**Current limitations:** The approval store is in-memory and is not durable across process restarts or distributed instances. The approval decision API accepts a supplied approver identity; authenticated approver authorization and tenant-scoped approver permissions are not yet implemented. Production deployment requires persistent approval storage, authenticated decision authorization, and verified integration of the approval gateway at every tool-execution entry point.

**Validation:** 188 tests passed (79 unit, 3 architecture, 106 integration).
## P7 — Answer Verification and Prompt-Injection Defense

**Status: Implemented and validated.**

P7 adds defense-in-depth controls to the grounded question-answering pipeline:

- **Generated-answer verification:** Citation IDs must reference authorized evidence, and generated claims must pass conservative lexical evidence checks. Unsupported generated answers cause abstention.
- **Prompt-injection screening:** Retrieved titles and excerpts are checked for common instruction-override, role-spoofing, secret-disclosure, command-execution, and data-exfiltration patterns.
- **Evidence quarantine:** Suspicious evidence is removed before it reaches the answer generator or answer composer. If no safe evidence remains, the endpoint abstains.
- **Adversarial regression coverage:** Tests verify filtering, authorized-source preservation, invalid citations, unsupported claims, and fail-closed behavior.

**Validation:** 211 tests passing — 102 unit, 3 architecture, and 106 integration.

**Security limitations:** Pattern-based injection detection is heuristic and can miss obfuscated, encoded, multilingual, or novel attacks. Lexical answer verification does not establish semantic entailment and can reject legitimate paraphrases or miss contradictions. Retrieved content remains untrusted; P7 does not independently authorize tool execution or replace existing tenant, ACL, and approval controls.
