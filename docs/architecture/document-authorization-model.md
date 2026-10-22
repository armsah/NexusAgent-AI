# Document Authorization Model

**Phase:** P0 — Architecture and Governance
**Status:** Design baseline; implementation and Azure verification pending.

## 1. Security Objective

NexusAgent AI must prevent unauthorized document retrieval, cross-tenant
information disclosure, and privilege escalation through retrieved content.

Authorization is enforced by trusted application services, never by an LLM.

## 2. Identity Model

Every authenticated request has a server-validated security context:

- tenantId: Microsoft Entra tenant or application tenant identifier.
- subjectId: validated user or workload identity.
- groupIds: trusted group memberships resolved from authorized identity sources.
- correlationId: identifier for tracing and audit.
- permissions: application-authorized capabilities.

Client-supplied tenant IDs, group IDs, and permissions are not authoritative.

P1 currently uses a fixed development identity. Production identity
validation and managed identity integration remain future work.

## 3. Document Metadata

Every indexed document must include:

| Field | Purpose |
|---|---|
| documentId | Stable document identifier |
| tenantId | Authoritative tenant ownership |
| versionId | Immutable document version |
| contentHash | Integrity and change detection |
| allowedSubjects | Authorized user identifiers |
| allowedGroups | Authorized group identifiers |
| classification | Information sensitivity |
| lifecycleStatus | Active, superseded, or deleted |
| sourceUri | Canonical source reference |
| indexedAt | Indexing timestamp |

Document chunks inherit their source document's tenant and ACL restrictions.

Chunk-specific restrictions may narrow access but must never broaden it.

## 4. Authorization Invariants

1. Deny access when the request identity is missing or invalid.
2. Deny access when tenant ownership does not match.
3. Deny access when authorization metadata is missing or malformed.
4. Deny access unless the requester has an explicit matching entitlement.
5. Apply authorization filters before retrieved content reaches the model.
6. Never trust an LLM-generated authorization decision.
7. Never accept client-supplied ACL filters as authoritative.
8. Exclude deleted, superseded, or inactive document versions.
9. Apply the same restrictions to lexical, vector, and hybrid retrieval.
10. Revalidate authorization before privileged downstream actions.

## 5. Retrieval Decision

A passage is eligible only when all conditions hold:

- Request identity is authenticated and authorized.
- Passage tenant matches the request's trusted tenant.
- Source document is active.
- Source version is the current authorized version.
- Requester matches an allowed subject or trusted group.
- Passage restrictions do not deny access.
- Source content passes applicable classification controls.

Unknown or conflicting metadata results in denial.

## 6. Update and Deletion Semantics

Document updates create new immutable versions.

Superseded versions must be excluded from retrieval.

Deletion must invalidate retrieval eligibility before or independently
of asynchronous physical cleanup.

Indexing retries must be idempotent.

Failures must not reactivate deleted or unauthorized content.

## 7. Trust Boundaries

Untrusted:
- User prompts.
- Uploaded documents.
- Retrieved passages.
- Model-generated output.
- Tool arguments proposed by agents.

Trusted enforcement:
- Authentication middleware.
- Application authorization services.
- Document metadata validation.
- Retrieval filter construction.
- Workflow state transitions.
- Tool Gateway policy checks.

## 8. Required Security Tests

| Test | Expected result |
|---|---|
| Authorized user retrieves active document | Allow |
| Unauthorized user requests document | Deny |
| User requests another tenant's document | Deny |
| Document has missing ACL metadata | Deny |
| Document has malformed ACL metadata | Deny |
| Deleted document appears in search results | Exclude |
| Superseded version appears in search results | Exclude |
| Prompt attempts to override ACL policy | Deny |
| Client supplies forged tenant identifier | Ignore or reject |
| Chunk ACL is broader than source ACL | Reject |

## 9. Implementation Mapping

P1: Persist document metadata, versions, and ingestion events.

P2: Implement authorization-aware Azure AI Search indexing and retrieval.

P3: Ensure only authorized evidence reaches grounded generation.

P7: Validate adversarial retrieval and prompt-injection resistance.

## 10. P0 Acceptance

The authorization model is documented and reviewed.

Runtime enforcement and live Azure security tests are not claimed at P0.
