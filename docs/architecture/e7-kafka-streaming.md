# E7 - Kafka Streams and event-driven integration

## Closure register

| Step | Deliverable | Status |
|---|---|---|
| E7.1 | Kafka infrastructure and runtime acceptance | CLOSED |
| E7.2 | Durable C# event production and sequencing | OPEN |
| E7.3 | Java Kafka Streams processing | OPEN |
| E7.4 | End-to-end acceptance and GitHub merge | OPEN |

## Architecture

PostgreSQL remains the authoritative transactional store.

Kafka provides asynchronous event transport. Java consumes
versioned document and workflow events and produces derived
analytics. Kafka does not replace authoritative application state.

Target producer semantics: transactional outbox with at-least-once
delivery and stable event identifiers.

Target consumer semantics: partition-local ordering, durable
deduplication, conflict detection, replay and explicit failure routing.

## E7.1 acceptance

- Date: 2026-11-26.
- Apache Kafka 4.1.1 in KRaft mode.
- Image ID: sha256:0bc1bb2478f45b6cea78864df86acdc11e8df2c5172477819a4d12942cbe5d40.
- Immutable image reference: apache/kafka@sha256:0bc1bb2478f45b6cea78864df86acdc11e8df2c5172477819a4d12942cbe5d40.
- Four explicitly provisioned application topics.
- Three partitions per application topic.
- Automatic topic creation disabled.
- Host listener: 127.0.0.1:19092.
- Container listener: kafka:9092 on the Compose network.
- Persistent named Docker volume.
- Publication and consumption verified.
- Broker restart and persistent replay verified.
- Temporary acceptance topic deleted.
- Cloud expenditure: EUR 0.

## Local operation

Run infra/kafka/Initialize-Kafka.ps1 to start and provision Kafka.

Run infra/kafka/Test-Kafka.ps1 for broker acceptance.

Stop the broker without deleting persisted data:

    docker compose -p nexusagent-kafka -f infra/kafka/compose.yaml stop

## Scope and limitations

Single-node Kafka is a local development configuration.
Replication factor one does not provide broker high availability.
PLAINTEXT is restricted to the local development environment.
Production TLS, authentication, authorization and deployment
hardening remain required before production acceptance.

Broker-level replay is verified. Application-level publication,
stream processing and durable deduplication remain E7.2-E7.3.
No cloud resources have been provisioned.
## E7.2 transport implementation

- Confluent.Kafka 2.12.0.
- Idempotent producer and acknowledged persistent delivery.
- Deterministic tenant and aggregate partition keys.
- Explicit topic allowlist and event identity validation.
- Bounded payload and publication timeout.
- Twelve targeted transport tests passed.
- PostgreSQL transactional integration remains open.
- Cross-system delivery semantics remain at-least-once.
- Cloud expenditure: EUR 0.

### E7.2 document publication integration

- Existing PostgreSQL document publisher integrated with Kafka.
- Kafka enabled explicitly through NEXUS_KAFKA_BOOTSTRAP_SERVERS.
- Existing worker acknowledgment and retry mechanism retained.
- Versioned document wire codec reused.
- Live Kafka transport delivery test passed.
- Delivery remains at-least-once.
- Full PostgreSQL-to-Kafka failure recovery acceptance remains open.
- Workflow lifecycle persistence remains open.
- Cloud expenditure: EUR 0.

### E7.2 workflow journal foundation

- PostgreSQL durable workflow event journal implemented.
- Unique event identity and aggregate sequence constraints.
- JSON payload identity constraints.
- Transaction-required C# append operation.
- Duplicate event, duplicate sequence and rollback SQL acceptance passed.
- Release build and targeted regression passed.
- Lifecycle integration and ordered dispatch remain open.
- Cloud expenditure: EUR 0.

### E7.2 automatic workflow lifecycle capture

- PostgreSQL trigger captures status transitions transactionally.
- Migration contains journal and trigger provisioning.
- Sequence allocation serialized by workflow row updates.
- Unchanged status does not emit duplicate events.
- Lifecycle sequence, terminal duration and atomic rollback acceptance passed.
- Historical workflows without a captured STARTED event are not backfilled.
- Migration execution through EF, tenant compatibility, rejection semantics and ordered dispatch require further acceptance.
- Cloud expenditure: EUR 0.

### E7.2 migration acceptance

- EF Core migration applied successfully.
- Migration history registration verified through PostgreSQL.
- PowerShell native argument quoting issue corrected.
- Atomic lifecycle and rollback acceptance repeated.
- Targeted regression passed.
- Fresh-database migration acceptance remains required.
- Cloud expenditure: EUR 0.

### E7.2 durable ordered workflow dispatch

- Workflow Kafka background worker registered.
- PostgreSQL SKIP LOCKED claims and aggregate sequence ordering.
- Broker acknowledgment precedes PostgreSQL acknowledgment.
- Failed publication retains events with bounded retry backoff.
- Fresh isolated database provisioned with pgvector before EF migration.
- Broker connection failure and ordered recovery acceptance passed.
- Full unit and architecture regression passed.
- Atomic lifecycle acceptance repeated.
- Temporary database cleanup verified.
- Delivery remains at-least-once.
- Cloud expenditure: EUR 0.

### E7.2 tenant compatibility

- Event tenant capacity aligned to 256 characters across C#, Java and PostgreSQL.
- Targeted C# and Java regression passed.
- Remaining producer acceptance stays OPEN.


### E7.2 publication safeguards

- Existing database tenant-capacity migration applied.
- Database publication ordering safeguard installed.
- Workflow publication quarantined after ten failed attempts.
- Quarantined predecessors block subsequent workflow events.
- Targeted regression passed.
- PostgreSQL remains authoritative.
- Cloud expenditure: EUR 0.
- E7.2 remains open pending broker-side ordering, rejection and crash-window acceptance.

### E7.2 document predecessor ordering - CLOSED

- Claim-time predecessor exclusion implemented.
- Ordering scoped by tenant, document and revision.
- Existing lease fencing and SKIP LOCKED retained.
- Quarantined predecessors block subsequent revisions.
- Partial predecessor index migration applied successfully.
- Two isolated PostgreSQL ordering tests passed.
- Sixteen outbox integration regression tests passed.
- Initial shared-database test failures resolved through database isolation and timestamp adjustment.
- No failed or skipped tests in final targeted acceptance.
- Delayed duplicate delivery remains an at-least-once consideration.
- Cloud expenditure: EUR 0.

### E7.2 rejection lifecycle foundation - CLOSED

- Rejected terminal domain status introduced.
- Existing enum values preserved.
- Rejection restricted to WaitingForApproval.
- Transactional REJECTED lifecycle event implemented.
- PostgreSQL migration applied.
- Workflow regression passed.
- Invalid database transition rejected.
- STARTED, APPROVAL_REQUIRED and REJECTED sequence verified.
- Terminal duration of 4000 milliseconds verified.
- Repeated status update produces no duplicate event.
- Atomic rollback verified.
- Application approval association remains OPEN.
- Dedicated domain rejection acceptance remains OPEN.
- Cloud expenditure: EUR 0.

### E7.2 approval association and atomic decision handling - CLOSED

- Optional durable workflow association with foreign-key integrity.
- Unique pending approval per associated workflow.
- Atomic approval creation and WaitingForApproval transition.
- Atomic approval decision, workflow transition and lifecycle journal.
- Tenant and requester association validated.
- Missing tenant context and self-approval rejected for linked decisions.
- Repeated decisions rejected.
- Failed workflow transition rolls back approval decision.
- Standalone approval behavior preserved.
- Workflow-bound approvals cannot be consumed through the standalone gateway.
- Dedicated PostgreSQL approval acceptance passed.
- Targeted unit and integration regression passed.
- Cloud expenditure: EUR 0.

Remaining requirements:
- Expired workflow approval resolution.
- Workflow-bound tool execution and continuation.
- Dedicated domain rejection tests.
- Crash-window recovery acceptance.
- Trusted approver authentication (E10).

### E7.2 expired approval resolution - CLOSED

- Pending expired approvals resolved automatically by a hosted worker.
- Associated waiting workflows transition atomically to Cancelled.
- Cancellation lifecycle events recorded transactionally.
- Bounded batches and SKIP LOCKED support competing workers.
- Late approval and denial cannot override expiration.
- Concurrent expiration-versus-decision tests passed.
- Repeated expiration produces no duplicate cancellation.
- SQL rollback and actual hosted worker acceptance passed.
- Approval regression passed.
- Cloud expenditure: EUR 0.


### E7.2 - Workflow-bound gateway acceptance

**Status: CLOSED**

- Workflow-scoped approval consumption implemented in PostgreSQL.
- Standalone approval isolation preserved.
- Workflow, tenant, requester and approved action binding enforced.
- Database-clock validation and stale approval rejection implemented.
- Concurrent consumption permits only one authorized invocation.
- Replay rejected after database context restart.
- Build, targeted integration acceptance and 30 unit tests passed.
- Cloud expenditure: EUR 0.

Remaining E7.2 requirements:
- Durable orchestration continuation after approval.
- Interrupted tool execution recovery and ambiguous outcome handling.
- Dedicated rejection lifecycle domain tests.
- Kafka publication crash-window recovery.

Single-use authorization does not guarantee exactly-once external side effects.
Ambiguous external actions must not be automatically retried.
