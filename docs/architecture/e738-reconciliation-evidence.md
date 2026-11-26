# E738-03 — Auditable missing-predecessor reconciliation

Status: CLOSED for the implemented reconciliation scope.

## Implementation

- Persistent changelog-backed reconciliation audit store.
- BUFFERED, RECONCILED and REJECTED transition records.
- First committed transition records are preserved.
- Original pending observation timestamps are retained.
- Rejected buffered successors receive explicit audit and DLQ evidence.
- Corrected successors can progress with new event identities.
- Accepted history and identity records are not discarded.
- Tenant-scoped operational audit reader uses at most three key lookups.
- Raw event payloads are excluded from audit records.

## Acceptance

- Previous targeted regression: 49/49 passed.
- Live Kafka recovery and reconciliation acceptance: 1/1 passed.
- Final reconciliation integration and model acceptance: 12/12 passed.
- Zero failures, errors or skipped tests in these executions.
- Unresolved gaps, successful reconciliation, rejected successors,
  duplicate suppression and tenant isolation verified.
- Full regression deferred to E7.4.
- Cloud expenditure: EUR 0.

## Operational access

ReconciliationAuditReader.local(streams).history(
    tenant, family, aggregateId, eventId)

The reader is an internal Java interface, not a public REST endpoint.
It must be invoked behind trusted authorization and routed to the
appropriate Kafka Streams state owner. An empty local result does not
prove cluster-wide absence.

## Migration and retention

Existing pending records may lack historical BUFFERED audit entries.
Such history must not be fabricated. Store migration, source retention,
rebuild safety and changelog lifecycle are governed by E738-04.
