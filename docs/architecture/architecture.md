# NexusAgent AI — Architecture Specification

**Project:** NexusAgent AI
**Phase:** P0 — Architecture and Governance Baseline
**Status:** Initial specification
**Architecture style:** Governed enterprise agentic RAG with deterministic control boundaries

## 1. Architecture Objective

NexusAgent AI is a governed enterprise agentic RAG and knowledge-automation platform on Microsoft Azure.

The architecture separates probabilistic AI reasoning from deterministic software authority.

AI components may:

- interpret requests;
- retrieve and synthesize evidence;
- propose workflow steps;
- interpret policy context;
- propose typed actions; and
- assist with verification.

AI components may not independently:

- authenticate users;
- grant authorization;
- broaden tenant scope;
- bypass document ACLs;
- approve sensitive operations;
- execute arbitrary commands;
- obtain unrestricted credentials; or
- override deterministic policy.

The application control plane remains authoritative for workflow state, authorization, risk, approvals, idempotency, execution, and auditability.

---

## 2. Architectural Principles

### 2.1 Deterministic Control Plane

Business-critical workflow behavior is implemented through explicit application code and persisted state.

Conversation history is not the authoritative workflow state.

### 2.2 AI Behind Ports

Model, retrieval, messaging, persistence, approval, and tool integrations are accessed through application interfaces.

Domain and Application layers must not depend directly on Azure SDKs or model-specific frameworks.

### 2.3 Authorization Before AI Context

Tenant, role, group, and document authorization are applied before retrieved enterprise content reaches a model.

### 2.4 Typed Tool Boundary

No generic execution capability is exposed to an agent.

Every executable business operation has a named typed contract and server-side implementation.

### 2.5 Human Governance

L2 actions require explicit human approval bound to the exact proposal.

### 2.6 Durable Asynchronous Workflows

Long-running workflows are persisted and processed asynchronously.

### 2.7 Observable and Reproducible AI

Consequential behavior is associated with model, prompt, retrieval, tool, policy, dataset, and application versions.

---

## 3. System Context

### 3.1 Actors and External Systems

```text
+--------------------+
| Enterprise User    |
| Entra-authenticated|
+---------+----------+
          |
          v
+---------------------------------------------------+
|                 NexusAgent AI                     |
|                                                   |
| Governed RAG + agent orchestration + tool control |
+----+--------------+---------------+---------------+
     |              |               |
     v              v               v
+---------+    +-----------+    +------------------+
| Entra ID|    | Enterprise|    | Business /       |
|         |    | Knowledge |    | Internal Systems |
+---------+    | Sources   |    +------------------+
               +-----------+
                     |
                     v
              +---------------+
              | Blob / ADLS   |
              +---------------+

Additional platform dependencies:

NexusAgent AI
    |
    +--> Microsoft Foundry / Azure OpenAI
    +--> Azure AI Search
    +--> Azure Service Bus
    +--> PostgreSQL
    +--> Azure Monitor / Application Insights
    +--> Key Vault where required
```

### 3.2 Human Roles

**Enterprise User**

Submits knowledge questions and workflow requests.

**Approver**

Reviews sensitive L2 action proposals.

**Administrator / Operator**

Operates releases, evaluations, infrastructure, observability, and recovery procedures.

**Source-System Owner**

Owns enterprise knowledge or business systems integrated with the platform.

---

## 4. Container Architecture

```text
                         +----------------------+
                         | Enterprise User      |
                         | Browser / API Client |
                         +----------+-----------+
                                    |
                                    v
                    +-------------------------------+
                    | Front Door / WAF              |
                    | Production-reference edge     |
                    +---------------+---------------+
                                    |
                                    v
                    +-------------------------------+
                    | API Management                |
                    | API governance / quotas       |
                    +---------------+---------------+
                                    |
                                    v
+-------------------------------------------------------------------+
| Azure Container Apps                                               |
|                                                                    |
| +----------------------+        +-------------------------------+  |
| | ASP.NET Core         |        | Orchestration Service         |  |
| | Agent Gateway API    +------->| Workflow state machine        |  |
| |                      |        | Agent delegation              |  |
| | Auth / authz         |        | Budgets / deadlines           |  |
| | validation           |        | Proposal lifecycle            |  |
| | correlation          |        +---------------+---------------+  |
| +----------------------+                        |                  |
|                                               |                  |
|                         +---------------------+ |                  |
|                         |                       |                  |
|                         v                       v                  |
|               +----------------+       +----------------------+   |
|               | Workers        |       | Tool Gateway         |   |
|               | Service Bus    |       | Typed execution      |   |
|               | processors     |       | boundary             |   |
|               +-------+--------+       +----------+-----------+   |
|                       |                           |               |
+-----------------------|---------------------------|---------------+
                        |                           |
              +---------+---------+                 |
              |                   |                 |
              v                   v                 v
       +-------------+      +-------------+   +------------------+
       | Service Bus |      | PostgreSQL  |   | Allow-listed     |
       |             |      |             |   | Business Systems |
       +-------------+      +-------------+   +------------------+

Orchestration / workers
       |
       +-------------> Azure AI Search
       |
       +-------------> Foundry / Azure OpenAI
       |
       +-------------> Blob / ADLS

All application components
       |
       +-------------> OpenTelemetry
                              |
                              v
                    Application Insights /
                       Azure Monitor
```

---

## 5. Major Component Responsibilities

### 5.1 Front Door / WAF

Production-reference responsibilities:

- public ingress;
- TLS;
- WAF controls;
- routing;
- optional geographic strategy.

It is not required for the initial low-cost local development path.

### 5.2 API Management

Responsibilities:

- external API governance;
- quotas;
- rate limits;
- versioning;
- consumer boundary.

API Management complements but does not replace authorization inside the application.

### 5.3 ASP.NET Core Agent Gateway

Responsibilities:

- authenticate request;
- authorize API operation;
- establish trusted requester context;
- establish tenant context;
- validate request;
- create correlation identifiers;
- enforce request-level rate limits;
- start/resume workflow;
- return synchronous response or asynchronous workflow reference.

The gateway does not provide privileged business-tool execution.

### 5.4 Orchestration Service

Responsibilities:

- own workflow state machine;
- classify work;
- delegate specialist responsibilities;
- enforce step budgets;
- enforce token budgets;
- enforce deadlines;
- support cancellation;
- manage evidence references;
- manage tool-proposal lifecycle;
- persist consequential workflow state.

The orchestration implementation belongs to the application rather than depending on preview-only hosted orchestration.

### 5.5 Workers

Responsibilities:

- process durable asynchronous work;
- consume Service Bus messages;
- execute bounded workflow steps;
- respect cancellation/deadlines;
- implement retry policy;
- use durable inbox/idempotency where required;
- route poison messages to DLQ;
- preserve trace context.

### 5.6 Model Adapter

Responsibilities:

- provide application-level model interface;
- call approved Microsoft Foundry / Azure OpenAI deployment;
- enforce timeout/cancellation;
- collect token/latency metadata;
- normalize provider response;
- support configuration-driven model deployment replacement.

The model SDK remains in Infrastructure.

### 5.7 Retrieval Adapter

Responsibilities:

- derive retrieval request from trusted application state;
- apply tenant/security filters;
- perform hybrid lexical/vector retrieval;
- invoke semantic reranking where configured;
- return evidence with stable provenance;
- expose retrieval metrics.

Azure AI Search remains an Infrastructure implementation behind an application interface.

### 5.8 Tool Gateway

The Tool Gateway is the privileged action boundary.

Responsibilities:

- deserialize typed request;
- validate schema;
- reject unknown/invalid fields;
- establish trusted execution context;
- enforce authorization;
- evaluate deterministic risk policy;
- validate approval;
- enforce argument-hash binding;
- enforce idempotency;
- restrict egress/destinations;
- execute named tool;
- persist audit result.

No arbitrary command execution endpoint exists.

### 5.9 PostgreSQL

Stores durable structured state including:

- workflows;
- workflow steps;
- approvals;
- tool proposals;
- tool execution/idempotency records;
- release manifests;
- audit metadata;
- evaluation catalog metadata;
- outbox/inbox records where required.

### 5.10 Azure Service Bus

Provides:

- durable asynchronous jobs;
- retry handling;
- controlled concurrency;
- DLQ;
- re-drive;
- optional ordering/session behavior where needed.

### 5.11 Blob Storage / ADLS

Stores:

- source documents;
- normalized artifacts;
- evaluation datasets;
- replay artifacts;
- large evidence artifacts.

### 5.12 Azure AI Search

Provides enterprise retrieval using:

- lexical search;
- vector search;
- metadata filtering;
- tenant/security filtering;
- semantic ranking where supported/configured.

Authorization decisions remain application-controlled.

### 5.13 Application Insights / Azure Monitor

Provides operational evidence for:

- API traces;
- worker traces;
- retrieval spans;
- model spans;
- Tool Gateway spans;
- errors;
- latency;
- token usage;
- cost attribution;
- workflow metrics;
- safety metrics.

---

## 6. Application Dependency Direction

Target logical structure:

```text
NexusAgent.Domain
        ^
        |
NexusAgent.Application
        ^
        |
NexusAgent.Infrastructure
        ^
        |
+-----------------------+
| Api / Workers /       |
| ToolGateway           |
+-----------------------+
```

More precisely:

```text
Domain
  - entities
  - value objects
  - invariants
  - deterministic policies

Application
  - use cases
  - commands/queries
  - workflow behavior
  - ports/interfaces

Infrastructure
  - EF Core/PostgreSQL
  - Azure AI Search SDK
  - Azure OpenAI / Foundry integration
  - Service Bus SDK
  - Blob SDK
  - telemetry implementations

Api
  - HTTP transport
  - authentication
  - request/response DTOs
  - composition root

Workers
  - asynchronous transport
  - composition root

ToolGateway
  - privileged action HTTP/application boundary
  - tool implementations
  - composition root
```

Forbidden dependencies:

```text
Domain -> Azure SDK
Domain -> model framework
Application -> ASP.NET controllers
Application -> concrete Azure SDK
Model -> generic shell execution
Model -> arbitrary SQL
Model -> arbitrary ARM
Model -> arbitrary Terraform
Model -> arbitrary HTTP executor
```

---

## 7. Application Ports

The target Application layer exposes abstractions conceptually equivalent to:

```text
IModelClient
IRetriever
IWorkflowRepository
IApprovalStore
IToolGateway
IAuditWriter
IMessagePublisher
IArtifactStore
IReleaseManifestProvider
```

Exact C# contracts are implemented in later phases.

P0 defines the boundary, not the final method signatures.

---

## 8. Agent Architecture

### 8.1 Supervisor

Responsibilities:

- classify task;
- plan bounded steps;
- delegate work;
- enforce workflow budgets.

Hard boundary:

```text
Cannot execute business tools directly.
```

### 8.2 Knowledge Agent

Responsibilities:

- request authorized retrieval;
- synthesize evidence;
- identify missing evidence;
- construct cited knowledge response.

Hard boundary:

```text
Cannot bypass tenant or document ACL filters.
```

### 8.3 Policy Agent

Responsibilities:

- interpret domain policy;
- explain policy implications;
- assist risk evaluation.

Hard boundary:

```text
Cannot grant authorization.
Cannot override deterministic risk policy.
```

### 8.4 Action Agent

Responsibilities:

- create typed action proposal;
- describe expected outcome;
- provide candidate arguments.

Hard boundary:

```text
Proposal only.
No unrestricted credentials.
No direct business-system execution.
```

### 8.5 Verification Agent

Responsibilities:

- inspect evidence;
- validate citations;
- examine proposal consistency;
- assess post-action result.

Hard boundary:

```text
Cannot override denied policy.
Cannot manufacture approval.
Cannot execute a rejected proposal.
```

---

## 9. Governed Request-to-Answer Flow

For read-only grounded Q&A:

```text
User
  |
  v
Agent Gateway
  |
  +--> authenticate
  +--> establish tenant/groups/roles
  +--> validate request
  |
  v
Orchestration
  |
  v
Knowledge workflow
  |
  v
Retriever
  |
  +--> server-derived ACL/security filter
  |
  v
Azure AI Search
  |
  v
Authorized evidence only
  |
  v
Context assembly
  |
  v
Model adapter
  |
  v
Citation verification
  |
  +--> valid -> response
  |
  +--> insufficient/invalid -> safe insufficient-evidence path
```

The model never receives unauthorized search results.

---

## 10. Governed Request-to-Action Flow

For an action-bearing workflow:

```text
1. User request
       |
       v
2. Authentication + authorization
       |
       v
3. Durable workflow created
       |
       v
4. Authorized evidence retrieval
       |
       v
5. Supervisor / specialist processing
       |
       v
6. Typed action proposal
       |
       v
7. Deterministic server validation
       |
       v
8. Deterministic risk classification
       |
       +---- prohibited ----> PolicyBlocked
       |
       +---- L0/L1 ---------> no execution
       |
       +---- L2 ------------> AwaitingApproval
       |                           |
       |                           v
       |                     Human approval
       |                           |
       |                  +--------+--------+
       |                  |                 |
       |               reject            approve
       |                  |                 |
       |                  v                 v
       |              Rejected       Revalidate exact
       |                             proposal/hash/expiry
       |                                   |
       +---- permitted L3 -----------------+
                                           |
                                           v
                                    Tool Gateway
                                           |
                              +------------+------------+
                              | authorization           |
                              | idempotency             |
                              | egress restrictions     |
                              | typed execution         |
                              +------------+------------+
                                           |
                                           v
                                      Verification
                                           |
                                  +--------+--------+
                                  |                 |
                               success            failure
                                  |                 |
                                  v                 v
                              Completed       Failed/Escalated
```

---

## 11. Trust Boundaries

### TB-01 — User to Public API

Untrusted input crosses into NexusAgent.

Controls:

- Entra authentication;
- authorization;
- validation;
- rate limiting;
- request limits;
- correlation.

### TB-02 — Application to Model

Application-controlled instructions and authorized evidence cross into a probabilistic AI system.

Controls:

- context minimization;
- instruction/data separation;
- no credentials;
- output validation;
- time/token budgets.

### TB-03 — Source Content to Retrieval Context

Potentially malicious or poisoned document content enters the AI workflow.

Controls:

- ACL trimming before context;
- untrusted-data treatment;
- prompt-injection defenses;
- provenance;
- citation verification.

### TB-04 — Agent Proposal to Tool Gateway

Probabilistic model output approaches a privileged execution boundary.

Controls:

- typed contracts;
- schema validation;
- trusted identity;
- authorization;
- deterministic risk policy;
- approval;
- idempotency;
- destination restrictions.

### TB-05 — Tool Gateway to Business System

An authorized mutation crosses into an external/business system.

Controls:

- least-privilege identity;
- named operation;
- resource scope;
- operation ID;
- audit;
- post-action verification.

### TB-06 — API/Workers to Messaging

Asynchronous work crosses a durable transport boundary.

Controls:

- explicit message contracts;
- trace propagation;
- idempotent consumer;
- retry budget;
- DLQ.

### TB-07 — Application to Persistence

Security-meaningful workflow state crosses into durable storage.

Controls:

- encrypted transport;
- schema constraints;
- migrations;
- least privilege;
- append-oriented audit semantics.

---

## 12. RAG Architecture

The target RAG pipeline is:

```text
Source document
      |
      v
Ingestion
      |
      v
Normalize + version
      |
      v
Metadata / ACL extraction
      |
      v
Structure-aware chunking
      |
      v
Embedding generation
      |
      v
Azure AI Search indexing
      |
===============================
           Query path
===============================
      |
Original query
      |
      v
Intent/query preparation
      |
      v
Server-derived security filter
      |
      v
Hybrid retrieval
(BM25 + vector)
      |
      v
Semantic rerank
(where configured)
      |
      v
Deduplication
      |
      v
Token-budgeted context
      |
      v
Grounded generation
      |
      v
Citation verification
```

Original user queries are retained or referenced appropriately for audit/evaluation subject to data-handling policy.

---

## 13. Target Search Document Model

Logical representation:

```text
DocumentChunk
- id
- documentId
- documentVersion
- title
- sectionPath
- content
- contentVector
- sourceUri
- pageNumber
- classification
- tenantId
- allowedGroupIds[]
- effectiveFromUtc
- effectiveToUtc
- checksum
- embeddingModel
- ingestionVersion
```

P5 defines the actual Azure AI Search index schema.

---

## 14. Durable Workflow Model

Target logical workflow state:

```text
AgentWorkflow
- workflowId
- requesterObjectId
- tenantId
- releaseId
- status
- currentStep
- riskLevel
- expiresAtUtc
- cancellationRequested
```

Workflow steps:

```text
WorkflowStep
- stepId
- workflowId
- agentName
- inputHash
- evidenceRefs[]
- modelDeployment
- promptVersion
- toolProposalId
- outcome
- startedAtUtc
- completedAtUtc
```

The exact persistence model and EF Core mapping are implemented beginning in P1.

---

## 15. Workflow State Principles

State transitions must be explicit.

Conceptual workflow states may include:

```text
Created
Planning
RetrievingEvidence
Generating
ProposalCreated
AwaitingApproval
Approved
Executing
Verifying
Completed

Safety / failure states:
Rejected
PolicyBlocked
Expired
Cancelled
Failed
```

The final state machine is implemented and tested as application code.

---

## 16. Reliability Architecture

### Timeouts

Every external network operation must be bounded.

### Cancellation

Cancellation tokens propagate through application and infrastructure operations where supported.

### Retry

Retry only transient operations with bounded attempts and total duration.

### Circuit Breaking

Repeated model/search/tool failures should eventually open a circuit rather than amplify dependency failure.

### Bulkheads

Concurrency must be bounded to protect:

- model quotas;
- PostgreSQL connections;
- Tool Gateway dependencies;
- worker processing capacity.

### Idempotency

Business-affecting operations use durable operation IDs.

### Outbox

State transitions that require durable message publication use transactional outbox or equivalent consistency handling.

### Inbox

Consumers use durable idempotency/inbox semantics where duplicate processing can affect state.

### DLQ

Poison asynchronous work is isolated and later inspected/re-driven through controlled procedures.

---

## 17. Identity Architecture

Planned identity separation:

```text
API identity
  - application configuration
  - workflow message send
  - no privileged tool execution

Orchestrator identity
  - model access
  - search access
  - workflow persistence
  - no privileged business execution

Ingestion identity
  - source Blob read
  - Search indexing rights
  - no tool rights

Tool Gateway identity
  - only allow-listed business resources/actions

GitHub OIDC deployment identity
  - environment-scoped deployment permissions
  - no runtime identity reuse
```

More granular Tool Gateway identities may be introduced by risk domain.

---

## 18. Network Architecture

### Development Topology

Initial implementation prioritizes a low-cost development environment using public service endpoints where necessary, protected by:

- Entra identity;
- managed identities;
- least privilege;
- service firewall restrictions where practical;
- TLS.

### Production-Reference Topology

P18 adds/documented production-reference networking including appropriate:

- VNet integration;
- private endpoints;
- private DNS;
- controlled egress;
- private paths to Storage;
- PostgreSQL;
- Key Vault;
- Search/AI resources where supported.

Preview or region-dependent networking features must not become mandatory dependencies without verification.

---

## 19. Observability Architecture

Every workflow receives correlation/trace context.

Target trace:

```text
HTTP request
   |
   v
Agent Gateway span
   |
   v
Orchestration span
   |
   +--> Retrieval span
   |       |
   |       +--> Azure AI Search
   |
   +--> Model span
   |
   +--> Service Bus send
           |
           v
       Worker consume
           |
           v
       Proposal / approval
           |
           v
       Tool Gateway
           |
           v
       Business operation
           |
           v
       Verification
```

Telemetry must correlate operational behavior with:

- workflow ID;
- release ID;
- agent;
- dependency;
- model deployment;
- token counts;
- policy outcome;
- tool action;
- approval;
- verification outcome.

Sensitive raw data is minimized according to the data-classification policy.

---

## 20. LLMOps Architecture

A consequential workflow is associated with a release manifest conceptually containing:

```yaml
agentRelease: 1.0.0
applicationCommit: <git-sha>
modelDeployment: <approved-deployment>
systemPromptVersion: <version>
retrievalConfigVersion: <version>
embeddingModelVersion: <version>
searchIndexVersion: <version>
toolCatalogVersion: <version>
policyBundleVersion: <version>
evaluationDatasetVersion: <version>
evaluationRunId: <id>
releasedAtUtc: <timestamp>
```

P14 establishes evaluation gates.

Later releases are compared against an approved baseline before promotion.

---

## 21. Deployment Architecture

Target Azure resources:

```text
Resource Group: NexusAgent development environment

├── Container Apps Environment
│   ├── Agent Gateway
│   ├── Orchestration service
│   ├── Workers
│   └── Tool Gateway
│
├── Azure Container Registry
├── Azure Database for PostgreSQL Flexible Server
├── Storage Account
├── Azure AI Search
├── Microsoft Foundry / Azure OpenAI resources
├── Azure Service Bus
├── Key Vault
├── Application Insights
└── Log Analytics Workspace
```

Production-reference network resources are introduced separately in P18.

---

## 22. Infrastructure as Code Structure

Target Terraform structure:

```text
infra/
├── bootstrap/
│   ├── state/
│   └── github-oidc/
│
├── modules/
│   ├── resource-group/
│   ├── managed-identity/
│   ├── container-app-env/
│   ├── container-app/
│   ├── service-bus/
│   ├── postgres/
│   ├── storage/
│   ├── key-vault/
│   ├── ai-search/
│   ├── foundry-model/
│   ├── monitor/
│   ├── private-endpoint/
│   └── private-dns/
│
└── environments/
    ├── nexusagent-dev/
    └── production-reference/
```

Terraform implementation starts in P2 and expands through P18.

---

## 23. Security Architecture Invariants

The following invariants must remain true throughout P0–P19:

1. Model output is untrusted.
2. Authentication is deterministic.
3. Authorization is deterministic.
4. Tenant scope is derived from trusted context.
5. Document security filtering occurs before model context.
6. Retrieved instructions do not become system instructions.
7. Secrets are not intentionally supplied to models.
8. No generic privileged execution tool exists.
9. Tool arguments are validated server-side.
10. Tool execution uses least-privilege identity.
11. L2 execution requires exact valid approval.
12. Approval cannot be reused for changed arguments.
13. Business-affecting retries are idempotent.
14. Consequential operations are auditable.
15. Dependency failure cannot broaden authority.

---

## 24. Key Architecture Decisions

Two initial architecture decisions are documented separately:

### ADR-001 — Application-Owned Agent Orchestration

NexusAgent owns the business workflow state machine in ASP.NET Core/workers rather than making preview or region-dependent Foundry workflow orchestration a mandatory control-plane dependency.

### ADR-002 — Azure AI Search for Enterprise Retrieval

Azure AI Search is the primary enterprise RAG retrieval platform rather than relying exclusively on PostgreSQL vector storage.

Additional ADRs will be introduced when implementation requires a consequential architectural choice.

---

## 25. Phase-to-Architecture Mapping

| Phase | Architectural capability                                        |
| ----- | --------------------------------------------------------------- |
| P0    | Architecture, governance and quality boundaries                 |
| P1    | Domain/Application/Infrastructure/API boundaries and PostgreSQL |
| P2    | Azure infrastructure baseline                                   |
| P3    | Entra, managed identities and OIDC                              |
| P4    | Document ingestion/versioning                                   |
| P5    | Azure AI Search + ACL retrieval                                 |
| P6    | Model abstraction and Foundry/Azure OpenAI                      |
| P7    | Grounded Q&A and stable citations                               |
| P8    | Query rewrite/rerank/context budgets                            |
| P9    | Supervisor + Knowledge/Policy orchestration                     |
| P10   | Tool Gateway                                                    |
| P11   | Risk engine + human approval                                    |
| P12   | Service Bus durability/DLQ                                      |
| P13   | Verification                                                    |
| P14   | Evaluation/LLMOps gate                                          |
| P15   | Prompt-injection/security suite                                 |
| P16   | End-to-end observability                                        |
| P17   | Cost/token attribution                                          |
| P18   | Production-reference private networking                         |
| P19   | Load/failure/rollback benchmark                                 |

---

## 26. Architecture Exit Criteria

The architecture is considered sufficiently specified for P0 when:

- system context is explicit;
- major containers and responsibilities are explicit;
- agent responsibilities and hard boundaries are explicit;
- trust boundaries are identified;
- retrieval authorization occurs before model context;
- Tool Gateway is established as the privileged boundary;
- human approval is established for L2 actions;
- durable workflow state is application-owned;
- dependency direction prevents Azure/model SDK leakage into Domain/Application;
- identity separation is defined;
- observability correlation is defined;
- LLMOps release attribution is defined; and
- implementation phases map to architectural capabilities.

Detailed implementation begins in P1 without changing these security invariants unless a later ADR explicitly documents the change.
