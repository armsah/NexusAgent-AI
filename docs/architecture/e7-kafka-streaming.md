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

### E7.2 - Durable approved-action continuation

- Atomic approval consumption and execution receipt creation.
- Gateway outcome persistence and transactional terminal workflow transitions.
- Automatic recovery of interrupted executions after five minutes.
- Ambiguous outcomes become Uncertain and are never automatically replayed.
- Premature completion and active-execution bypass protection.
- Four PostgreSQL acceptance scenarios passed.
- Lifecycle ordering, late completion fencing and recovery idempotency verified.
- Targeted integration and unit regressions passed.
- Cloud expenditure: EUR 0.

Scope: terminal approved-action continuation. Arbitrary multi-step plan
resumption and exactly-once external side effects are not guaranteed.
Approved-but-unused expiry and actual process-interruption acceptance
remain subject to additional verification.

### E7.2 - Interrupted execution and unused approval expiration

- Approved-but-unused expiration implemented and registered in the recovery worker.
- Database client terminated after committing a durable execution claim.
- Interrupted execution recovered as Uncertain without tool replay.
- Workflow failed closed with reconciliation reason.
- Unused approved workflow cancelled and approval expired.
- Repeated recovery produced no duplicate terminal lifecycle events.
- Recovery age accelerated for acceptance.
- Targeted regression tests passed.
- Cloud expenditure: EUR 0.

The interruption test exercises a PostgreSQL client, not a running
external tool process. Full external tool interruption acceptance
remains outstanding.

### E7.2 - Workflow-bound execution and continuation: CLOSED

Acceptance evidence:

- Actual isolated .NET process launched through the typed tool gateway.
- Workflow-bound approval consumed and durable execution receipt observed.
- Tool invocation confirmed through a persistent invocation marker.
- Executing process forcibly terminated before outcome persistence.
- Recovery deadline accelerated using database timestamps.
- Interrupted receipt recovered as Uncertain.
- Workflow transitioned to Failed with reconciliation reason.
- A second process attempted the same approved action.
- Replay rejected without another tool invocation.
- Repeated recovery produced no duplicate terminal lifecycle events.
- Approval, gateway and orchestration regressions passed.
- Cloud expenditure: EUR 0.

This closes the bounded terminal approved-action continuation requirement.
Recovery deliberately does not replay ambiguous external side effects.
The test invokes the production recovery function directly; hosted worker
scheduling and arbitrary multi-step plan resumption are separate concerns.

### E7.2 - Domain rejection lifecycle: CLOSED

- 18 dedicated domain rejection acceptance cases passed.
- Rejection permitted only from WaitingForApproval.
- Rejected workflows cannot resume, restart, complete, fail,
  cancel, request approval again or execute another step.
- Backwards rejection timestamps rejected without state mutation.
- Active steps prevent entry into approval waiting state.
- Completed steps permit approval waiting and rejection.
- Other terminal states cannot transition to Rejected.
- PostgreSQL rejection lifecycle and journal acceptance passed.
- Related unit and integration regressions passed.
- xUnit2031 analyzer warning corrected.
- Cloud expenditure: EUR 0.

### E7.2 - Publication crash-window acceptance: CLOSED

- Isolated PostgreSQL database with production migrations.
- Live Kafka broker acknowledgement verified.
- PostgreSQL publication acknowledgement deliberately rolled back.
- Restarted database context and Kafka producer.
- Production dispatcher republished the unacknowledged event.
- Original event identity and serialized payload preserved.
- Tenant and workflow partition key preserved.
- All three deliveries observed in the same Kafka partition.
- Delivery sequence verified as 1, 1, 2.
- Successor publication preserved aggregate ordering.
- PostgreSQL publication state reconciled.
- No further pending publication remained.
- Related E7 unit and integration regressions passed.
- Temporary PostgreSQL database removed.
- Cloud expenditure: EUR 0.

Delivery semantics are at-least-once across PostgreSQL and Kafka.
The acceptance test reproduces the acknowledgement failure boundary
using transaction rollback rather than an operating-system process kill.
Persistent consumer-side deduplication is an E7.3 requirement.

### E7.2 - Formal closure

Status: CLOSED (11/11 requirements).

Durable publication, workflow lifecycle sequencing, publication ordering,
tenant compatibility, approval association, expiration, workflow-bound
execution, continuation, rejection and publication recovery acceptance
have been implemented and verified.

Next stage: E7.3 - Java Kafka Streams processing.

### E7.3.1 - Kafka Streams foundation: CLOSED

- Kafka Streams, clients and test utilities aligned to 4.1.1.
- Resolved Kafka dependency versions verified.
- AutoOffsetResetStrategy runtime compatibility error corrected.
- Exactly-once-v2 Kafka processing configuration.
- Explicit topic configuration and earliest-offset replay.
- Cross-language tenant and aggregate partition-key validation.
- Persistent, changelog-enabled state-store definitions.
- Five dedicated foundation acceptance tests.
- Full Java regression: 77 passed.
- Failed, errored or skipped tests: zero.
- Cloud expenditure: EUR 0.

Replication factor one is restricted to local development.
Production processors, conflict handling, tenant analytics,
dead-letter routing and live state restoration remain open.
Foundation topology tests do not establish broker-level
exactly-once delivery or production deduplication.

### E7.3.2 - Production topology implementation: CLOSED

- Production document and workflow Kafka Streams topology.
- Strict event contract and aggregate-key validation.
- Persistent aggregate-scoped identity and sequence history.
- Duplicate suppression and conflicting revision rejection.
- Contiguous sequence and lifecycle enforcement.
- Tenant-isolated aggregate projections.
- Redacted dead-letter records with source coordinates.
- Opt-in Spring-managed Kafka Streams runtime.
- Exactly-once-v2 transactional processing configuration.
- Twelve dedicated production topology acceptance tests.
- Ambiguous null-payload test overload corrected.
- Full Java regression: 89 passed.
- Failures, errors and skips: zero.
- Cloud expenditure: EUR 0.

E7.3.2 production topology implementation: CLOSED.

Remaining mandatory acceptance:
- Cross-aggregate event identity conflict detection.
- Tenant-wide analytics counters and metrics.
- Broker-backed transactional acceptance.
- Changelog restoration with a fresh local state directory.
- Runtime interruption and duplicate replay acceptance.

E7.3 remains OPEN.
PostgreSQL remains authoritative.

### E7.3.3 - Global identity registry implementation checkpoint

- Globally scoped event identity keys.
- Length-prefixed SHA-256 identity fingerprints.
- Cross-aggregate, cross-tenant and cross-family conflict detection.
- Persistent identity registry processor.
- Redacted conflict dead-letter routing.
- Identical deliveries preserved for downstream sequence-gap recovery.
- Corrupted registry state fails closed.
- Eight dedicated acceptance tests.
- Full Java regression: 97 passed.
- Failures, errors and skips: zero.
- Cloud expenditure: EUR 0.

Status: OPEN pending production topology integration.

The identity registry processor must be connected through an event-ID
repartition boundary and followed by aggregate-key repartitioning.
The existing production topology remains aggregate-scoped until
that integration is completed and verified.

### E7.3.3 - Production global identity integration: CLOSED

- Global event identity validation integrated into production topology.
- Kafka-managed event-ID repartition boundary.
- Persistent global identity registry with changelog support.
- Kafka-managed aggregate-key repartition boundary.
- Three partitions per internal repartition topic.
- Cross-tenant, cross-aggregate and cross-family conflict rejection.
- Invalid source records cannot reserve event identities.
- Identical deliveries remain eligible for sequence-gap recovery.
- Accepted duplicate deliveries do not generate repeated projections.
- Original source coordinates preserved in redacted failure records.
- Seven additional production topology acceptance tests.
- Full Java regression: 104 passed.
- Failures, errors and skips: zero.
- Cloud expenditure: EUR 0.

Identity registration and aggregate acceptance occur in separate
Kafka Streams subtopologies. Each processing transaction is governed
by exactly-once-v2 configuration.

Global identity registration precedes aggregate lifecycle acceptance.
Identical retries remain permitted; conflicting identity reuse is rejected.

The topology uses a new internal repartition layout. Deployment against
previously populated application state requires an explicit migration
or a new application ID with complete source replay.

Live multi-partition processing, transactional interruption and
changelog restoration remain mandatory E7.3.7 acceptance.

E7.3.3 implementation and topology acceptance: CLOSED.

### E7.3.4 - Aggregate sequencing and lifecycle validation: CLOSED

- Corrected ordering risk introduced by event-ID repartitioning.
- Persistent changelog-enabled pending sequence buffer.
- Maximum forward sequence gap of 1024 revisions.
- Automatic contiguous successor processing.
- Duplicate buffered delivery suppression.
- Conflicting buffered revision rejection.
- Invalid successors cannot advance aggregate state.
- Tenant-isolated pending sequences.
- Terminal workflow and deleted-document lifecycle protection.
- Eight additional topology acceptance tests.
- Full Java regression: 112 passed.
- Failures, errors and skips: zero.
- Cloud expenditure: EUR 0.

A missing predecessor prevents subsequent revisions from advancing.
Pending revisions remain durable until the predecessor arrives.
Excessive sequence gaps are routed to the dead-letter topic.

Live distributed ordering, interruption and state restoration
remain subject to E7.3.7 acceptance.

Pending sequence age monitoring and operational reconciliation
remain required before production readiness.

### E7.3.5 - Persistent tenant-wide analytics: CLOSED

Implementation:

- Dedicated tenant-key repartition boundary with three partitions.
- Persistent changelog-enabled tenant metrics state.
- Persistent accepted-projection identity registry.
- Tenant-wide document creation, update, deletion and active counts.
- Workflow start, completion, failure, rejection and cancellation counts.
- Non-terminal workflow counts.
- Approval request, approval grant and rejection counts.
- Arbitrary-precision cumulative terminal workflow duration.
- Average terminal duration and completion rate.
- Counter overflow, underflow and consistency protection.
- Duplicate accepted projections cannot increment counters twice.
- Rejected source events cannot affect tenant metrics.
- Aggregate sequence buffering respected before tenant aggregation.
- Compacted tenant snapshot topic: nexusagent.tenant-analytics.v1.
- Reproducible local topic provisioning.
- Existing aggregate analytics topic remains unchanged.

Metric semantics:

- Metrics are cumulative across accepted event history.
- Completion rate equals completed workflows divided by all terminal workflows.
- Average terminal duration includes all terminal workflow outcomes.
- Both rates are zero before the first terminal workflow.
- Last-event metadata describes processing order, not maximum event time.
- Tenant snapshots are derived projections; PostgreSQL remains authoritative.
- Each accepted aggregate projection contributes once to tenant metrics.
- Tenant metrics and their identity registry are transactionally coordinated
  under Kafka Streams exactly-once-v2 configuration.

Acceptance:

- Twelve dedicated metric-model tests.
- Seven production topology acceptance tests.
- Full Java regression: 131 passed.
- Failures, errors and skips: zero.
- Live Kafka tenant analytics topic provisioning: PASSED.
- Topic partition count: 3.
- Topic cleanup policy: compact.
- Repeatable topic initialization: PASSED.
- Cloud expenditure: EUR 0.

Operational limitations:

- Live transactional tenant aggregation and fresh-directory changelog
  restoration remain mandatory E7.3.7 acceptance.
- Complete cumulative metrics require complete source history or a
  verified state migration.
- Persistent identity and history retention must be governed by an
  explicit operational retention policy.
- Missing-predecessor monitoring and reconciliation remain required.

E7.3.5 implementation and topology acceptance: CLOSED.

### E7.3.6 - End-to-end dead-letter acceptance: CLOSED

Live broker acceptance:

- Actual production Kafka Streams topology.
- Exactly-once-v2 configuration.
- Read-committed output observation.
- Eleven rejected input records verified.
- Malformed JSON, null, whitespace and oversized payload rejection.
- Invalid aggregate key and unknown contract field rejection.
- Cross-aggregate and cross-family event identity conflict rejection.
- Accepted sequence conflict and excessive sequence gap rejection.
- Invalid workflow transition rejection.
- Original source topic, partition and offset verified.
- Strict five-field dead-letter schema verified.
- Sensitive sentinel values excluded from dead-letter output.
- Raw payloads, tenant identifiers and exception details excluded.
- Valid document and workflow successors processed after poison records.
- Tenant analytics continuity verified.
- Acceptance application internal topics removed.
- Acceptance consumer groups removed.
- Existing application topics preserved.

Corrective implementation:

- Initial regression encountered GroupNotEmptyException during cleanup.
- Consumer-group membership release is asynchronous.
- Added bounded retries for groups awaiting membership release.
- Missing groups are handled idempotently.
- Unexpected cleanup failures remain fatal.
- Residual acceptance resources from the failed run were removed.
- Final broker resource discovery verified cleanup.

Final acceptance:

- Full Java regression: 132 passed.
- Failures, errors and skips: zero.
- Live dead-letter acceptance: PASSED.
- Acceptance internal topic cleanup: PASSED.
- Acceptance consumer group cleanup: PASSED.
- Cloud expenditure: EUR 0.

Operational boundaries:

- Synthetic records remain in shared application topics.
- Compacted synthetic tenant snapshots require explicit reconciliation.
- Live broker interruption and changelog restoration remain E7.3.7.
- Retention governance and missing-predecessor reconciliation remain open.
- Exactly-once external side effects are not established by this test.

E7.3.6 end-to-end dead-letter acceptance: CLOSED.

### E7.3.7 - Live transactional recovery and restoration: CLOSED

Verified recovery acceptance:

- Actual production topology with exactly-once-v2 configuration.
- Aborted Kafka producer transaction excluded from accepted projections.
- Read-committed observation of aggregate and tenant outputs.
- Persistent pending-sequence state verified before interruption.
- Actual Kafka Docker container stop/start while Streams remained active.
- Existing Streams application resumed processing after broker recovery.
- Committed consumer offsets verified before application restart.
- Same application ID restarted with a fresh local state directory.
- Pending sequence state restored from Kafka changelog.
- Tenant metrics restored from Kafka changelog.
- Restored global event identity conflict detection verified.
- Previously accepted event replay did not duplicate projections.
- Missing predecessor released the restored buffered successor.
- Restored document and workflow lifecycle validation continued.
- Final aggregate projections and cumulative tenant counters verified.
- Acceptance internal topics and consumer groups removed.
- Required application topics preserved.

Regression:

- Full Java regression: 133 passed.
- Failures, errors and skips: zero.
- Live dead-letter acceptance: PASSED.
- Live transactional recovery acceptance: PASSED.
- Broker interruption and recovery: PASSED.
- Fresh-directory state restoration: PASSED.
- Duplicate suppression after restoration: PASSED.
- Cloud expenditure: EUR 0.

Scope and limitations:

- Single-broker local Kafka deployment.
- Broker interruption exercised after a committed baseline.
- Application restart uses orderly shutdown.
- Abrupt process termination during an open Streams transaction is not
  established by this acceptance.
- Multi-broker failover and external exactly-once side effects are not
  established by this acceptance.
- Synthetic input and output records remain in shared topics.
- Operational retention, missing-predecessor reconciliation and
  synthetic snapshot cleanup remain consolidated acceptance obligations.

E7.3.7 broker recovery and changelog restoration acceptance: CLOSED.

### E7.3.8 - Consolidated streaming acceptance: OPEN

- Consolidated Java regression: 133 passed.
- Failures, errors and skips: zero.
- Required production implementation files verified.
- E7.3.1 through E7.3.7 closure evidence verified.
- Live dead-letter and recovery acceptance rerun successfully.
- Required topic configuration verified.
- Acceptance internal topics and consumer groups absent.
- Mandatory operational acceptance register created:
  docs/architecture/e7-streaming-acceptance.md.
- Six outstanding operational gates remain OPEN.
- Cloud expenditure: EUR 0.

## E738-01 closure evidence

- Separate JVM executed the production Kafka Streams topology.
- Producer interceptor blocked acknowledged analytics output
  before transaction completion.
- Read-uncommitted observer verified the physical output record.
- Read-committed observer verified isolation.
- Probe JVM forcibly terminated without orderly shutdown.
- Same application ID restarted with fresh local state.
- Uncommitted processing recovered through Kafka replay.
- Exactly one committed projection per accepted event verified.
- Duplicate input replay did not increment tenant metrics.
- Valid successor processing and cumulative counters verified.
- Acceptance internal topics and consumer groups removed.
- Full Java regression: 134 passed.
- Failures, errors and skips: zero.
- Cloud expenditure: EUR 0.

E738-01: CLOSED.

Remaining mandatory E7.3.8 gates: E738-02 through E738-06.
