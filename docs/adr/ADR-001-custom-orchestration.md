# ADR-001 — Application-Owned Agent Orchestration

**Project:** NexusAgent AI
**Status:** Accepted
**Date:** 2026-09-17
**Decision owners:** NexusAgent AI engineering
**Phase:** P0

## 1. Context

NexusAgent AI requires multi-step agentic workflows involving:

- task classification;
- planning;
- authorized knowledge retrieval;
- specialist-agent delegation;
- policy interpretation;
- typed action proposals;
- deterministic risk evaluation;
- human approval;
- privileged tool execution;
- post-action verification;
- retries;
- cancellation;
- deadlines;
- dead-letter handling; and
- complete auditability.

These workflows can affect external or business systems. Therefore, workflow state and authorization-sensitive transitions are part of the application's deterministic control plane.

Microsoft Foundry can provide agent capabilities and may provide hosted or workflow-oriented agent functionality depending on the selected runtime, region, and service capabilities.

NexusAgent AI must remain deployable even when a particular Foundry multi-agent/workflow capability is preview, region-dependent, changes its runtime model, or is otherwise unsuitable as the authoritative business workflow engine.

## 2. Decision

NexusAgent AI will implement and own the authoritative agent/workflow state machine in the ASP.NET Core application and worker layer.

The application control plane will own:

- workflow identity;
- workflow status;
- current step;
- permitted state transitions;
- requester and tenant context;
- release identity;
- step budgets;
- token budgets;
- deadlines;
- cancellation;
- evidence references;
- tool-proposal lifecycle;
- risk state;
- approval state;
- execution state;
- verification state; and
- terminal outcomes.

Durable workflow state will be persisted in PostgreSQL.

Long-running workflow steps will use durable asynchronous processing through Azure Service Bus where required.

Microsoft Foundry / Azure OpenAI will be accessed behind application abstractions for model/agent capabilities.

Foundry Agent Service may later be integrated as an optional runtime or accelerator, but it will not become the sole authoritative source for business-critical workflow state or deterministic authorization decisions.

## 3. Decision Summary

```text
Authoritative workflow control
        =
NexusAgent ASP.NET Core / Workers
        +
PostgreSQL durable state
        +
Service Bus asynchronous execution
```

Model/agent runtime:

```text
Application
    |
    v
Application abstraction
    |
    +--> Microsoft Foundry / Azure OpenAI
    |
    +--> optional future Foundry agent runtime
```

The model runtime assists reasoning.

The application owns authority.

## 4. Rationale

### 4.1 Deterministic State Transitions

Business-critical transitions must be explicitly represented and tested.

For example:

```text
ProposalCreated
      |
      v
RiskEvaluated
      |
      +--> PolicyBlocked
      |
      +--> AwaitingApproval
                |
                +--> Rejected
                |
                +--> Approved
                          |
                          v
                      Executing
                          |
                          v
                      Verifying
                          |
                   +------+------+
                   |             |
               Completed       Failed
```

An LLM conversation must not implicitly determine whether an approved or privileged operation is executable.

### 4.2 Authorization Must Remain Application-Controlled

The model cannot:

- grant authorization;
- change the authenticated requester;
- broaden tenant scope;
- bypass document ACLs;
- approve an L2 action; or
- override deterministic policy.

Application-owned orchestration makes these boundaries explicit.

### 4.3 Human Approval Requires Durable State

L2 actions require approval bound to the exact proposal.

The control plane must durably associate:

- workflow;
- requester;
- tool name;
- arguments hash;
- risk level;
- approver;
- expiration;
- approval status; and
- execution state.

This state must survive process restarts and asynchronous execution.

### 4.4 Idempotency Requires Deterministic Control

Retries must not duplicate business side effects.

The application therefore needs durable operation IDs, execution records, and workflow state independent of model conversational state.

### 4.5 Failure Recovery

The architecture must recover from:

- process restart;
- model timeout;
- Search failure;
- Service Bus redelivery;
- worker failure;
- Tool Gateway failure;
- approval expiration; and
- transient Azure dependency failures.

Explicit persisted state provides a deterministic recovery point.

### 4.6 Portability

Business workflow logic should not require a rewrite when:

- the model deployment changes;
- a Foundry runtime changes;
- an agent capability is unavailable in the selected region;
- a preview capability changes;
- another approved model/runtime is introduced.

### 4.7 Testability

Application-owned orchestration allows deterministic tests for:

- state transitions;
- invalid transitions;
- cancellation;
- expiration;
- risk decisions;
- approval requirements;
- retry behavior;
- duplicate message handling;
- idempotency;
- failure recovery.

These tests do not require an LLM to determine whether core business invariants hold.

## 5. Workflow Ownership Boundary

### Application-owned

The application is authoritative for:

```text
workflowId
requesterObjectId
tenantId
releaseId
status
currentStep
riskLevel
expiresAtUtc
cancellationRequested
approval state
tool proposal state
execution state
verification outcome
```

### AI-assisted

AI may assist with:

```text
task interpretation
planning suggestions
query rewriting
knowledge synthesis
policy interpretation
tool selection proposal
candidate tool arguments
verification reasoning
```

### AI-prohibited authority

AI may not independently determine:

```text
authenticated identity
tenant authorization
document ACL bypass
effective business authorization
approval validity
execution identity
idempotency state
arbitrary resource scope
policy override
```

## 6. Target Application Boundary

Conceptual dependency:

```text
NexusAgent.Application
        |
        +--> IModelClient
        +--> IRetriever
        +--> IWorkflowRepository
        +--> IApprovalStore
        +--> IToolGateway
        +--> IMessagePublisher
        +--> IAuditWriter
```

Infrastructure provides implementations.

```text
NexusAgent.Infrastructure
        |
        +--> Foundry / Azure OpenAI
        +--> Azure AI Search
        +--> PostgreSQL
        +--> Azure Service Bus
        +--> Blob Storage
```

The Application layer does not depend on concrete Azure SDKs.

## 7. Durable Execution Model

Short bounded operations may execute synchronously.

Long-running workflows use:

```text
API
 |
 v
Persist workflow
 |
 v
Commit state
 |
 v
Publish durable work
 |
 v
Service Bus
 |
 v
Worker
 |
 v
Load workflow
 |
 v
Validate current state
 |
 v
Execute bounded step
 |
 v
Persist transition/result
```

Where state change and event publication must be atomic from the business perspective, a transactional outbox or equivalent consistency mechanism will be used.

Consumers use idempotency/inbox controls where duplicate delivery could affect state.

## 8. Agent Representation

The logical agent model includes:

### Supervisor

Plans and delegates bounded workflow steps.

Cannot execute business tools directly.

### Knowledge Agent

Retrieves authorized evidence and synthesizes cited responses.

Cannot bypass retrieval security filters.

### Policy Agent

Interprets policy context.

Cannot grant authorization or override deterministic policy.

### Action Agent

Produces typed action proposals.

Cannot execute privileged operations directly.

### Verification Agent

Checks evidence, proposals, citations, and post-action results.

Cannot override policy rejection or missing approval.

These may initially be implemented as application orchestration responsibilities rather than requiring five independently deployed services.

## 9. Alternatives Considered

### Alternative A — Hosted Agent Runtime Owns the Entire Workflow

A hosted agent service owns planning, conversational state, delegation, tool invocation, and workflow progression.

**Advantages**

- potentially less custom orchestration code;
- faster prototyping;
- native agent-runtime features.

**Rejected as the authoritative design because**

- business-critical state becomes coupled to the runtime;
- preview/region availability may affect deployability;
- deterministic approval and execution transitions become less explicit;
- portability decreases;
- failure recovery and replay behavior become more runtime-dependent;
- portfolio evidence for state-machine engineering becomes weaker.

Hosted agent capabilities remain usable behind the application boundary where appropriate.

### Alternative B — Conversation History as Workflow State

Store conversation history and infer workflow status from previous model messages.

**Advantages**

- minimal workflow implementation;
- suitable for simple chat prototypes.

**Rejected because**

- state transitions are implicit;
- authorization-sensitive state becomes ambiguous;
- restart/replay behavior is difficult to prove;
- approval binding is unsafe;
- deterministic testing is weak;
- idempotent execution is difficult to guarantee.

### Alternative C — Fully Synchronous Orchestration

Keep the entire workflow inside a single HTTP request.

**Advantages**

- simple request model;
- minimal messaging infrastructure.

**Rejected for long-running workflows because**

- HTTP lifetime becomes coupled to agent execution;
- dependency delays increase timeout risk;
- recovery after process failure is poor;
- approval workflows cannot remain open safely;
- retry/DLQ patterns are unavailable.

Synchronous execution remains acceptable for bounded read-only Q&A paths.

## 10. Consequences

### Positive

- deterministic business workflow behavior;
- explicit authorization boundaries;
- durable human approval;
- testable state transitions;
- reliable idempotency;
- improved replay/recovery;
- reduced dependence on preview-only functionality;
- easier model/runtime replacement;
- stronger auditability;
- clearer separation between reasoning and authority.

### Negative

- additional orchestration code;
- persistence schema required;
- explicit state-transition management required;
- Service Bus processing complexity;
- outbox/inbox patterns add implementation work;
- application team owns more workflow reliability concerns.

These costs are accepted because NexusAgent is intended to demonstrate production-grade governed agentic engineering rather than a conversational prototype.

## 11. Implementation Constraints

The implementation must preserve the following:

1. Workflow state is persisted outside the LLM.
2. Model output cannot directly transition privileged workflow states.
3. Approval state is deterministic.
4. Tool execution is routed through the Tool Gateway.
5. L2 actions cannot bypass approval.
6. Worker retries are idempotent.
7. Long-running work is recoverable.
8. Model/runtime adapters remain replaceable.
9. Foundry-specific SDKs do not leak into Domain/Application.
10. Trace context follows asynchronous workflow execution.

## 12. Validation

The decision will be validated progressively.

### P1

Demonstrate:

- explicit application boundaries;
- workflow domain model;
- PostgreSQL persistence;
- deterministic state-transition tests.

### P6

Demonstrate:

- model integration behind an application abstraction;
- deployment/model replacement through configuration.

### P9

Demonstrate:

- Supervisor;
- Knowledge/Policy responsibilities;
- durable multi-step workflow;
- workflow traces.

### P11

Demonstrate:

- deterministic risk state;
- persisted approval state;
- L2 approval enforcement.

### P12

Demonstrate:

- Service Bus asynchronous execution;
- retry;
- DLQ;
- re-drive;
- duplicate-delivery safety.

### P19

Demonstrate:

- restart/failure recovery;
- load behavior;
- rollback/fallback;
- portfolio benchmark.

## 13. Reconsideration Triggers

This ADR may be revisited if a future hosted orchestration platform can demonstrably satisfy all of the following without weakening application control:

- deterministic state-transition ownership;
- durable approval semantics;
- exact proposal/approval binding;
- idempotent execution;
- replay/recovery;
- full auditability;
- required regional availability;
- production support requirements;
- acceptable portability;
- application-enforced authorization boundaries.

Even if the runtime changes, authentication, authorization, risk, approval, and privileged execution remain deterministic responsibilities.

## 14. Result

NexusAgent AI will use an **application-owned durable orchestration state machine** as its authoritative business control plane.

Microsoft Foundry / Azure OpenAI provides AI capabilities behind abstractions.

Hosted agent capabilities may accelerate reasoning or specialist-agent execution, but they do not replace deterministic application ownership of authorization-sensitive workflow state.
