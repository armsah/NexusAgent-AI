# ADR-002 — Azure AI Search for Enterprise Retrieval

**Project:** NexusAgent AI
**Status:** Accepted
**Date:** 2026-09-17
**Decision owners:** NexusAgent AI engineering
**Phase:** P0

## 1. Context

NexusAgent AI requires enterprise Retrieval-Augmented Generation (RAG) over heterogeneous internal knowledge including:

- policies;
- contracts;
- technical manuals;
- operating procedures;
- incident records;
- FAQs; and
- other enterprise documents.

The retrieval layer must support more than semantic similarity.

It must also support:

- lexical retrieval;
- vector retrieval;
- hybrid retrieval;
- metadata filtering;
- tenant isolation;
- document authorization metadata;
- document versioning;
- source provenance;
- semantic reranking where configured;
- stable citation metadata;
- retrieval evaluation; and
- operational observability.

PostgreSQL is already required by NexusAgent AI for durable workflow and governance state.

It would therefore be technically possible to add vector storage to PostgreSQL and use the database for both transactional state and RAG retrieval.

The architecture must decide whether enterprise retrieval should use that database-centric approach or a dedicated search platform.

## 2. Decision

NexusAgent AI will use **Azure AI Search** as the primary enterprise retrieval platform.

PostgreSQL will remain the authoritative relational store for durable workflow, governance, approval, audit metadata, release metadata, and related transactional state.

The responsibilities are separated as follows:

```text
PostgreSQL
    |
    +--> workflow state
    +--> workflow steps
    +--> approvals
    +--> tool proposals
    +--> idempotency records
    +--> audit metadata
    +--> release manifests
    +--> evaluation catalog metadata

Azure AI Search
    |
    +--> searchable document chunks
    +--> lexical retrieval
    +--> vector retrieval
    +--> hybrid retrieval
    +--> semantic ranking where configured
    +--> tenant/security metadata filtering
    +--> source/version metadata
```

Blob Storage / ADLS remains the storage location for source documents and larger document/evidence artifacts.

## 3. Decision Summary

The target retrieval architecture is:

```text
Enterprise documents
        |
        v
Blob Storage / ADLS
        |
        v
Ingestion + normalization
        |
        v
Versioning + metadata + ACL extraction
        |
        v
Chunking
        |
        v
Embedding generation
        |
        v
Azure AI Search
        |
        +--> text fields
        +--> vector fields
        +--> provenance
        +--> tenant metadata
        +--> authorization metadata
        +--> version metadata
```

Query path:

```text
Authenticated request
        |
        v
Trusted identity context
        |
        v
Application retrieval service
        |
        +--> derive tenant filter
        +--> derive authorization filter
        |
        v
Azure AI Search
        |
        +--> lexical retrieval
        +--> vector retrieval
        +--> hybrid ranking
        +--> semantic rerank where configured
        |
        v
Authorized evidence
        |
        v
Context budgeting / deduplication
        |
        v
Model
        |
        v
Grounded response + citations
```

## 4. Rationale

### 4.1 Enterprise Search Is a Distinct Workload

Transactional workflow persistence and enterprise information retrieval have different access patterns.

PostgreSQL is optimized in NexusAgent for durable application state and relational consistency.

The RAG workload requires specialized:

- full-text retrieval;
- vector similarity;
- hybrid search;
- ranking;
- filtering;
- search relevance tuning; and
- search-specific evaluation.

Separating these workloads provides a clearer architecture.

### 4.2 Hybrid Retrieval

NexusAgent requires both lexical and semantic/vector retrieval.

Lexical retrieval is important for exact enterprise terminology such as:

- policy identifiers;
- contract clauses;
- product names;
- incident IDs;
- acronyms;
- technical terms.

Vector retrieval is useful for semantically related language where exact terms differ.

The target architecture therefore combines both retrieval modes rather than assuming vector similarity alone is sufficient.

### 4.3 Authorization Metadata

Every searchable document/chunk carries authorization-related metadata.

The logical target includes:

```text
tenantId
allowedGroupIds[]
classification
effectiveFromUtc
effectiveToUtc
```

The application derives security filters from trusted requester context.

The model cannot broaden these filters.

### 4.4 Stable Provenance

Grounded responses require evidence that can be traced back to source material.

Search records therefore retain provenance such as:

```text
documentId
documentVersion
title
sectionPath
sourceUri
pageNumber
checksum
ingestionVersion
```

This supports:

- citations;
- auditability;
- stale-version analysis;
- evaluation;
- debugging.

### 4.5 Retrieval Evaluation

A dedicated retrieval layer enables NexusAgent to measure:

- Recall@K;
- Precision@K;
- MRR and/or NDCG;
- security-filter correctness;
- stale-version retrieval rate;
- zero-result behavior;
- retrieval latency.

P8 can compare improved retrieval against the earlier P5/P7 baseline.

### 4.6 Independent Scaling

Search workload can scale independently from workflow persistence.

A retrieval spike should not inherently require the same scaling strategy as:

- workflow writes;
- approval records;
- audit metadata;
- idempotency records.

### 4.7 Clear Operational Ownership

The separation creates explicit responsibilities:

```text
PostgreSQL
= transactional workflow/governance state

Azure AI Search
= derived searchable knowledge representation

Blob / ADLS
= source documents and large artifacts
```

The search index can be rebuilt from authoritative source documents and ingestion metadata.

It is not treated as the sole authoritative source of enterprise documents.

## 5. Logical Search Schema

The target logical chunk schema is:

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

The physical Azure AI Search schema is implemented in P5.

P0 intentionally does not fix:

- vector dimensions;
- analyzer configuration;
- semantic configuration names;
- scoring profiles;
- exact field attributes;
- Search SKU;
- embedding deployment.

Those decisions depend on the implementation and selected model capabilities.

## 6. Retrieval Security Boundary

The retrieval sequence must preserve:

```text
Authenticated user
      |
      v
Server-derived identity
      |
      v
Server-derived tenant/group filter
      |
      v
Azure AI Search query
      |
      v
Authorized results only
      |
      v
Model context
```

The following architecture is prohibited:

```text
Search all documents
      |
      v
Send unauthorized + authorized content to model
      |
      v
Ask model not to reveal unauthorized content
```

The LLM is not an authorization filter.

## 7. Tenant Isolation

Tenant scope originates from trusted application context.

A model-generated tenant identifier must not determine retrieval scope.

For tenant-scoped content:

```text
requesterTenantId
        |
        v
trusted application context
        |
        v
retrieval filter
        |
        v
matching tenant content only
```

Cross-tenant leakage is treated as a security failure, not merely a retrieval-quality regression.

## 8. Document Authorization

Group/document authorization metadata is attached during ingestion.

The target retrieval request combines relevance criteria with deterministic security constraints.

Conceptually:

```text
Relevance:
    lexical + vector + semantic ranking

AND

Security:
    tenantId == requesterTenant
    AND requester authorized by document metadata
    AND document is effective/current where required
```

Security filtering is not optional when the source requires authorization.

## 9. Document Versioning

The index must distinguish source versions.

Conceptually:

```text
documentId = POL-001
documentVersion = 7
checksum = ...
ingestionVersion = ...
```

If version 8 replaces version 7, retrieval behavior must not silently treat both versions as equally current when business rules require the current version.

The ingestion/retrieval implementation must support stale-version detection and evaluation.

## 10. Citation Support

Search results must return enough metadata to construct stable evidence references.

A citation should ultimately identify or resolve to:

```text
document
document version
section/page or equivalent location
retrieved chunk/evidence
source reference
```

The model should not invent citation identifiers.

Citation validity is verified against the evidence supplied to the workflow.

## 11. Query Processing

The target query path may include:

```text
original query
      |
      v
query preparation/rewrite
      |
      v
security-filter construction
      |
      v
hybrid retrieval
      |
      v
semantic rerank
      |
      v
deduplication
      |
      v
context budgeting
      |
      v
grounded generation
```

Query rewriting must not broaden authorization.

The original query must remain available or referenced appropriately for evaluation and audit subject to the data-handling policy.

## 12. Search Index as Derived State

Azure AI Search is treated as a derived retrieval representation.

Authoritative source content remains in controlled source storage.

This allows:

```text
Source documents
      +
ingestion configuration
      +
embedding configuration
      +
index schema/configuration
      |
      v
Rebuild search index
```

The implementation must record sufficient version information to make index behavior reproducible.

## 13. PostgreSQL Role

PostgreSQL is not eliminated from RAG-related workflows.

It may store structured metadata such as:

- ingestion job state;
- source-document catalog metadata;
- workflow evidence references;
- evaluation run metadata;
- release manifests;
- audit references.

However, it is not the primary enterprise full-text/vector retrieval engine for NexusAgent.

## 14. Alternatives Considered

### Alternative A — PostgreSQL + Vector Extension as the Only Retrieval Engine

Use PostgreSQL for:

- workflow state;
- relational data;
- document chunks;
- vector embeddings;
- similarity search;
- metadata filters.

**Advantages**

- fewer Azure services;
- potentially lower development-environment complexity;
- one primary persistence technology;
- transactional integration can be simpler.

**Not selected as the primary design because**

- it combines transactional workflow and enterprise-search workloads;
- NexusAgent specifically requires strong hybrid enterprise retrieval;
- search relevance becomes more application/database engineered;
- dedicated search evaluation and tuning become less explicit;
- independent search scaling is reduced;
- it provides weaker portfolio evidence for Azure enterprise search architecture.

PostgreSQL vector retrieval could still be useful in another system with different constraints.

### Alternative B — Vector-Only Retrieval

Use an embedding/vector store without strong lexical retrieval.

**Advantages**

- conceptually simple semantic retrieval;
- useful for semantically similar language.

**Rejected because**

enterprise knowledge frequently contains exact identifiers and terminology where lexical matching is valuable.

Examples include:

```text
POL-SEC-042
INC-2026-00173
contract clause 7.4
product SKU
technical error code
internal acronym
```

Hybrid retrieval is therefore preferred.

### Alternative C — Model Context Without Search

Provide entire documents or large document sets directly to the model.

**Rejected because**

- context is bounded;
- authorization becomes harder to control;
- cost increases;
- irrelevant content increases;
- provenance is weaker;
- retrieval evaluation is unavailable;
- large enterprise corpora do not fit this approach.

## 15. Consequences

### Positive

- explicit enterprise search architecture;
- hybrid lexical/vector retrieval;
- security metadata filtering;
- semantic ranking where configured;
- dedicated relevance tuning;
- independent scaling;
- strong provenance;
- measurable retrieval quality;
- clearer separation of transactional and retrieval workloads.

### Negative

- additional Azure service;
- additional Terraform configuration;
- index schema management;
- ingestion/index synchronization complexity;
- search-specific operational cost;
- search-specific monitoring;
- eventual consistency between source changes and index state.

These costs are accepted because enterprise retrieval is a core capability of NexusAgent AI.

## 16. Failure Behavior

If Azure AI Search is unavailable:

```text
Search unavailable
      |
      v
No authorized evidence
      |
      v
Do not fabricate grounded answer
      |
      +--> bounded retry where appropriate
      |
      +--> deterministic dependency error/fallback
```

Search failure must not cause the application to:

- remove authorization filters;
- use another tenant's cached evidence;
- fabricate citations;
- treat model prior knowledge as retrieved enterprise evidence.

## 17. Evaluation Requirements

Retrieval evaluation must eventually include:

```text
Recall@K
Precision@K
MRR and/or NDCG
security-filter correctness
stale-version retrieval rate
latency
zero-result behavior
```

Evaluation results must identify relevant versions including:

```text
evaluationDatasetVersion
embeddingModelVersion
searchIndexVersion
retrievalConfigVersion
applicationCommit/release
```

## 18. Observability Requirements

Search operations must expose sufficient telemetry to diagnose:

- latency;
- failures;
- throttling where observable;
- result count;
- zero-result rate;
- retrieval configuration/version;
- workflow correlation;
- downstream grounded-answer behavior.

Sensitive query/document data follows the P0 data-classification policy and is not logged indiscriminately.

## 19. Implementation Mapping

### P4 — Ingestion

Implement:

- source-document ingestion;
- versioning;
- checksums;
- metadata extraction;
- structure-aware chunking;
- reproducibility.

### P5 — Search

Implement:

- Azure AI Search index;
- vector field;
- metadata fields;
- tenant/group ACL metadata;
- hybrid retrieval;
- semantic ranking where configured;
- security trimming.

### P6 — Model Adapter

Connect retrieved evidence to the model abstraction.

### P7 — Grounded Q&A

Implement:

- context assembly;
- citations;
- insufficient-evidence behavior.

### P8 — Retrieval Improvement

Implement and measure:

- query rewriting;
- reranking;
- deduplication;
- context budgeting;
- baseline comparison.

### P14 — Evaluation

Add retrieval and grounded-answer quality to the versioned evaluation gate.

### P16 — Observability

Expose search latency and retrieval behavior in end-to-end traces.

## 20. Reconsideration Triggers

This ADR may be revisited if measured requirements show that another retrieval architecture provides a materially better fit for:

- security;
- relevance;
- latency;
- availability;
- operational complexity;
- cost;
- regional requirements.

Any replacement must continue to satisfy:

- server-side authorization filtering;
- tenant isolation;
- hybrid retrieval requirements;
- stable provenance;
- version awareness;
- reproducible evaluation;
- model-independent retrieval abstraction.

The Application layer must remain insulated from the concrete retrieval provider through `IRetriever` or equivalent.

## 21. Result

NexusAgent AI will use **Azure AI Search as its primary enterprise retrieval platform**.

Azure AI Search owns the derived searchable representation and relevance-oriented retrieval behavior.

PostgreSQL owns transactional workflow and governance state.

Blob Storage / ADLS owns source documents and large artifacts.

This separation preserves clear system responsibilities while supporting authorization-aware hybrid RAG, provenance, evaluation, and independent operational scaling.
