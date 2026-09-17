# NexusAgent AI — Data Classification and Handling Policy

**Project:** NexusAgent AI
**Phase:** P0 — Architecture and Governance Baseline
**Status:** Initial specification

## 1. Purpose

This document defines the data-classification and handling baseline for NexusAgent AI.

The policy governs data across:

- source-document ingestion;
- Azure Blob Storage / ADLS;
- Azure AI Search;
- PostgreSQL;
- model context;
- agent workflows;
- Tool Gateway requests and results;
- Service Bus messages;
- evaluation datasets;
- audit records;
- application logs;
- distributed traces; and
- operational telemetry.

The primary design rule is:

> Data access is determined by trusted application identity, authorization, tenant, and resource policy before data is exposed to an AI model.

An LLM is never an authorization boundary.

## 2. Classification Model

NexusAgent AI uses four application-level data classes.

| Class | Name         | Examples                                                                                                              | Default AI Handling                                               |
| ----- | ------------ | --------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| C0    | Public       | Public product documentation, public FAQs, intentionally public reference material                                    | May be retrieved and supplied to approved models                  |
| C1    | Internal     | Internal procedures, technical manuals, internal FAQs, non-sensitive operational documentation                        | Allowed only for authenticated and authorized enterprise users    |
| C2    | Confidential | Contracts, restricted policies, incident records, business-sensitive documents, customer-related business records     | Strict ACL/security trimming and minimized model context required |
| C3    | Restricted   | Credentials, access tokens, private keys, connection strings, privileged headers, highly sensitive regulated material | Must not be placed in model context by default                    |

Classification is independent from authorization.

For example, two C1 documents can have different authorized groups. Classification describes handling sensitivity; ACL and tenant metadata determine who can access a particular resource.

## 3. Core Data-Handling Rules

### 3.1 Identity Is Trusted Context

The effective:

- user identity;
- tenant identity;
- group membership;
- application role;
- workflow identity; and
- execution identity

must originate from authenticated application or platform context.

These values must never be accepted from model-generated arguments as authoritative identity information.

### 3.2 Authorization Occurs Before Retrieval

The retrieval layer must derive security filters server-side.

Unauthorized documents must be excluded before search results are assembled into model context.

The following pattern is prohibited:

1. retrieve all potentially relevant documents;
2. send them to the model;
3. instruct the model not to disclose unauthorized documents.

### 3.3 Retrieved Content Is Untrusted

Retrieved documents, search results, API responses, emails, tool results, and other externally sourced content are data, not system instructions.

Instructions embedded inside retrieved content must not override:

- system policy;
- application policy;
- authorization;
- risk controls;
- approval requirements;
- tool schemas; or
- execution boundaries.

### 3.4 Minimum Necessary Context

Only the evidence required for the current task should be supplied to a model.

Context assembly must enforce:

- authorization;
- tenant isolation;
- classification rules;
- token budgets;
- source selection;
- deduplication; and
- relevance constraints.

### 3.5 Secrets Are Not Model Context

The following must not be intentionally placed in prompts or retrieved model context:

- passwords;
- access tokens;
- refresh tokens;
- API keys;
- private keys;
- client secrets;
- database passwords;
- connection strings containing credentials;
- privileged HTTP headers;
- signing secrets; or
- equivalent authentication material.

Managed identities are preferred for Azure workload authentication.

Key Vault is used only where identity-based access cannot replace a secret.

## 4. Source Document Metadata

Each indexed document/chunk must preserve sufficient metadata for authorization, provenance, versioning, and evaluation.

The target logical metadata includes:

```text
id
documentId
documentVersion
title
sectionPath
content
contentVector
sourceUri
pageNumber
classification
tenantId
allowedGroupIds[]
effectiveFromUtc
effectiveToUtc
checksum
embeddingModel
ingestionVersion
```

The exact Azure AI Search schema is implemented in P5.

## 5. Tenant Isolation

Every tenant-scoped source must carry trusted tenant metadata.

The tenant used for retrieval is derived from authenticated application context.

Model-generated tenant identifiers are never authoritative.

A query from tenant A must not return tenant B content even when:

- semantic similarity is high;
- the user explicitly requests another tenant's content;
- retrieved content contains instructions to change tenant scope; or
- the model generates another tenant identifier.

Cross-tenant isolation must be covered by automated security and adversarial tests.

## 6. Document Authorization

Authorization metadata is attached during ingestion and used during retrieval.

The target authorization model supports:

- tenant restrictions;
- Entra-derived group restrictions;
- role restrictions where required;
- document classification;
- document validity/effective dates; and
- application-specific business rules.

The retrieval service is responsible for applying these controls.

The Knowledge Agent cannot disable, replace, or broaden them.

## 7. Document Versioning

Every indexed source must be traceable to a specific source version.

At minimum, the system records:

- document ID;
- document version;
- checksum;
- ingestion version;
- embedding model/version;
- indexing timestamp or equivalent provenance; and
- source URI/reference.

When a document changes, the system must be able to distinguish the new representation from stale indexed content.

Evaluation must measure stale-version retrieval where applicable.

## 8. Storage Handling

### 8.1 Blob Storage / ADLS

Blob Storage / ADLS is the system of record for:

- source documents;
- normalized/parsed document artifacts where required;
- evaluation datasets;
- replay artifacts; and
- large evidence artifacts.

Large source content should not be duplicated into PostgreSQL without a specific application requirement.

### 8.2 PostgreSQL

PostgreSQL stores structured durable state including:

- workflows;
- workflow steps;
- approvals;
- tool proposals;
- tool idempotency records;
- release manifests;
- audit metadata;
- evaluation catalog records; and
- immutable references/hashes to larger artifacts.

### 8.3 Azure AI Search

Azure AI Search contains the retrieval representation of authorized enterprise knowledge.

Search data includes:

- searchable content;
- vector representation;
- source provenance;
- version metadata;
- tenant metadata;
- authorization metadata; and
- classification metadata.

Search is not the authoritative identity provider or business authorization engine.

### 8.4 Service Bus

Messages should contain identifiers and minimum workflow data required by the consumer.

Large documents and sensitive raw payloads should be referenced through controlled storage where practical rather than copied into queue messages.

## 9. Model Context Handling

Model requests are constructed from explicit context categories.

### Trusted instruction context

May contain:

- system behavior;
- application policy;
- tool descriptions;
- output schemas;
- workflow constraints.

### Trusted identity/policy context

May contain only the minimum information required for the operation and must be generated by the application.

### Untrusted evidence context

May contain:

- authorized document chunks;
- search results;
- tool results;
- external API results.

Untrusted evidence must be structurally separated from trusted instructions.

Retrieved instructions do not become trusted because they appear in an enterprise document.

## 10. Tool Gateway Data Handling

The Tool Gateway is a privileged trust boundary.

Tool requests use typed contracts.

Before execution, the gateway validates:

- schema;
- field constraints;
- requester context;
- tenant/resource scope;
- authorization;
- risk level;
- approval requirement;
- approval binding;
- idempotency; and
- destination allow-list where applicable.

The gateway must not accept model-generated identity information as a replacement for trusted workflow identity.

Sensitive tool output must be minimized before being returned to an agent/model.

## 11. Approval Data

An approval record must be bound to the exact action being authorized.

The record should include:

- approval ID;
- workflow ID;
- requester;
- tool name;
- arguments hash;
- risk level;
- approver;
- approval status;
- creation time;
- expiration time; and
- execution state.

Changing the tool or arguments after approval invalidates the authorization.

Expired or already-consumed approvals must not authorize execution.

## 12. Audit Data

Consequential operations require durable audit metadata.

The audit trail should allow an operator to determine:

- who requested the workflow;
- which tenant was involved;
- which release was active;
- what evidence was used;
- what policy decision occurred;
- which action was proposed;
- whether approval was required;
- who approved or rejected it;
- what executor performed the operation;
- what result was observed; and
- which correlation/trace identifiers connect the operation.

Security-relevant audit records should be append-oriented.

## 13. Logging and Telemetry

Logs and traces follow data-minimization principles.

### Permitted operational metadata

Examples include:

- workflow ID;
- operation ID;
- correlation ID;
- trace/span ID;
- agent name/version;
- model deployment identifier;
- prompt/release version;
- retrieval latency;
- model latency;
- token counts;
- tool name;
- policy outcome;
- approval state;
- status code;
- error category.

### Data requiring minimization or redaction

Avoid recording raw:

- prompts;
- document contents;
- retrieved confidential passages;
- tool payloads;
- tool responses;
- authorization headers;
- personal data;
- secrets; and
- credentials.

Where diagnostic capture is necessary, it must be explicitly controlled and access-restricted.

## 14. Evaluation Data

Evaluation datasets are versioned and reproducible.

Each dataset should record or reference:

- dataset version;
- case ID;
- input/query;
- expected evidence;
- expected authorization behavior;
- expected answer characteristics;
- expected tool behavior where applicable;
- classification/safety category; and
- content hash.

Evaluation datasets containing enterprise-sensitive material follow the same classification and authorization requirements as production source data.

A test dataset is not automatically public or safe merely because it is used for evaluation.

## 15. Adversarial Test Data

The security evaluation suite includes cases for:

- direct prompt injection;
- indirect prompt injection inside retrieved documents;
- cross-tenant retrieval attempts;
- unauthorized document requests;
- data-exfiltration requests;
- tool privilege escalation;
- forged identity/tenant arguments;
- approval replay;
- modified arguments after approval; and
- canary-data disclosure.

Synthetic canary markers may be introduced into controlled test datasets to detect unintended disclosure.

Canary material must not be a real credential.

## 16. Retention and Deletion

Retention periods are not fixed in P0 because production retention requirements depend on enterprise legal, regulatory, operational, and contractual requirements.

The implementation must therefore make retention explicit and configurable for:

- source documents;
- parsed artifacts;
- workflow records;
- approval records;
- audit records;
- model-related telemetry;
- evaluation artifacts;
- Service Bus dead-letter messages; and
- operational logs/traces.

The system must avoid indefinite retention by accident.

High-volume telemetry and evidence should be archived or deleted according to documented retention policy.

## 17. Encryption and Transport

Data must use encryption in transit and at rest through the applicable Azure service capabilities.

Production-reference architecture will use controlled network paths and private endpoints for sensitive PaaS services where supported and justified.

Development may initially use public endpoints with strict identity and firewall controls before the production-reference private networking phase.

## 18. Environment Separation

Development, test/evaluation, and production-reference environments must be logically separated.

Production credentials and privileged identities must not be reused for local development or evaluation.

Evaluation and adversarial testing must not intentionally execute destructive operations against production systems.

## 19. Data Handling by Autonomy Level

| Autonomy                | Data Behavior                                                                                       |
| ----------------------- | --------------------------------------------------------------------------------------------------- |
| L0 — Observe            | Authorized retrieval and read-only processing                                                       |
| L1 — Recommend          | May construct a structured proposal using authorized data; no external mutation                     |
| L2 — Approved execution | Sensitive action data may be processed only after deterministic validation and valid human approval |
| L3 — Bounded autonomy   | Only explicitly pre-approved low-risk actions within configured data/resource boundaries            |

Autonomy does not broaden data access.

An L3 action receives no additional document or tenant permissions merely because automatic execution is permitted.

## 20. Required Verification

The implementation must eventually demonstrate automated or auditable verification of the following:

- unauthorized documents are removed before model context assembly;
- tenant filters cannot be overridden through model output;
- model-generated identity values cannot replace authenticated identity;
- C3 secrets are not intentionally passed to the model;
- retrieved instructions cannot create an arbitrary tool execution path;
- modified approved arguments invalidate approval;
- expired approvals cannot execute;
- telemetry does not intentionally expose credentials;
- evaluation datasets are version identifiable; and
- consequential actions retain sufficient audit metadata for reconstruction.

## 21. Phase Mapping

This P0 policy establishes requirements implemented progressively in later phases:

- **P3:** identity and managed identity controls;
- **P4:** document versioning and metadata;
- **P5:** search ACL/security metadata and security trimming;
- **P7–P8:** controlled context assembly and citations;
- **P10:** Tool Gateway validation and execution boundary;
- **P11:** approval and risk enforcement;
- **P12:** durable messaging and DLQ handling;
- **P14:** versioned evaluation datasets;
- **P15:** prompt-injection and exfiltration testing;
- **P16:** telemetry and trace controls;
- **P17:** token/cost attribution; and
- **P18:** production-reference private networking.
