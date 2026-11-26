# E6 - Java functional event processing

## Objective

Implement a substantive Java analytics service without duplicating
the authoritative C# orchestration and PostgreSQL persistence layers.

## Closure register

| Step | Scope | Status |
|---|---|---|
| E6.1 | Baseline and event-contract audit | CLOSED |
| E6.2 | Reproducible Java and Maven foundation | CLOSED |
| E6.3 | Spring Boot service foundation | CLOSED |
| E6.4 | Immutable versioned event contracts | CLOSED |
| E6.5 | Functional transformations and analytics | CLOSED |
| E6.6 | Deduplication and ordering semantics | CLOSED |
| E6.7 | Cross-language contract acceptance | CLOSED |
| E6.8 | Regression, documentation and GitHub merge | READY FOR MERGE |

## E6.1 acceptance

Baseline: ad797e612ed4a581a0cfeaee629fe65d6aae97a7.

Reviewed the exported C# event and outbox source files.

Existing document events:

- document.created
- document.updated
- document.deleted

The existing record contains EventId, EventType, TenantId,
DocumentId, DocumentVersion, ContentVersion and OccurredAt.

PublishedDocumentEvent adds publication metadata.
OccurredAt and PublishedAt have different meanings.

PostgreSQL publication deduplicates by event ID.
The outbox uses leased claims, retry scheduling and terminal failure.

Java must not independently acknowledge the C# ingestion outbox,
mutate authoritative workflow state or replace PostgreSQL.

Workflow analytics requires a separate versioned event contract.
Actual producer serialization compatibility must be tested in E6.7.

Kafka transport and durable analytics projections remain E7 and E8.
Trusted identity and external API authorization remain E10.

Java 21 LTS is available locally.
Maven is not currently on PATH.
No cloud resources were provisioned.
## E6.2 acceptance

- Java 21 enforced by Maven Enforcer.
- Maven 3.9.11 pinned and installed locally.
- Distribution SHA512 verified against Apache checksum.
- Maven wrapper generated with distribution SHA256.
- JUnit and build plugin versions pinned.
- Wrapper clean verify succeeded.
- Tests: 1 passed, zero failed, zero skipped.
- Cloud expenditure: EUR 0.

## E6.3 acceptance

Spring Boot 3.5.8 context, executable JAR and non-web startup verified.

- Java regression: 3 passed.
- Failed, errors and skipped: zero.
- Cloud expenditure: EUR 0.

## E6.4 acceptance

Document and workflow records implemented with explicit schema version 1.
Strict JSON codec rejects unknown fields, duplicate keys, malformed
identifiers, unsupported versions, oversized inputs and invalid durations.
Document schema version is a new transport-contract field.
Actual C# producer adaptation and serialization acceptance remain E6.7.

- Java regression: 26 passed.
- Failed, errors and skipped: zero.
- Cloud expenditure: EUR 0.

## E6.5 acceptance

Pure windowed transformations implemented with Java Stream API,
immutable records, grouping collectors and deterministic latest-state
selection. Document lifecycle counts, workflow terminal outcomes,
approval counts, completion rate and average duration implemented.

Window semantics: from-inclusive and to-exclusive. Metrics describe
events and latest observed entities inside the selected window, not
the complete authoritative system state.

Duration totals use BigInteger to avoid long overflow.

Duplicate delivery, conflicting identities and sequence validation
remain mandatory E6.6 acceptance requirements. These batch functions
must not yet be connected directly to an at-least-once transport.

- Java regression: 40 passed.
- Failed, errors and skipped: zero.
- Cloud expenditure: EUR 0.

## E6.6 acceptance

- Identical event deliveries deduplicated before analytics.
- Conflicting event identities rejected.
- Conflicting document revisions and workflow sequences rejected.
- Tenant-scoped revision and sequence identities.
- Deterministic ordering and immutable normalized collections.
- Explicit complete-history validation for workflow transitions.
- Missing starts, sequence gaps and timestamp regressions rejected.
- Approval bypass and post-terminal transitions rejected.
- Partial-window analytics remain distinct from complete-history validation.
- Java regression: 66 passed, zero failures, errors or skips.
- Cloud expenditure: EUR 0.

Deduplication operates over the supplied batch. Durable deduplication,
cross-batch state, late-arrival policy and broker replay handling remain
E7/E8 responsibilities. Complete-history validation is not implicitly
applied to partial-window analytics.

The proposed workflow lifecycle requires C# producer compatibility
verification during E6.7.


### E6.7 dependency security acceptance

- Spring Boot upgraded to 3.5.16.
- Jackson BOM aligned to 2.23.0.
- Log4j aligned to 2.26.1.
- Unused Mockito dependencies excluded.
- Java regression: 66 passed, zero failures, errors or skipped tests.
- OSV audit: 53 resolved dependencies, zero reported vulnerabilities.
- NuGet audit: eight projects, zero reported vulnerable packages.
- Audit date: 2026-11-26.
- Results reflect advisory database coverage at audit time.
- Cross-language acceptance remains pending.

### E6.7 document wire interoperability

- Versioned C# document serialization boundary implemented.
- Three actual .NET-generated lifecycle fixtures consumed by Java.
- Identity, tenant, timestamp, revision, deduplication and analytics verified.
- PostgreSQL remains authoritative.
- Broker publication and durable workflow event sequencing are E7 responsibilities.

### E6.7 workflow wire acceptance

- Versioned C# workflow lifecycle serialization implemented.
- Seven C#-generated workflow event fixtures consumed by Java.
- Approval, rejection, completion, failure and cancellation histories verified.
- Invalid duration and approval bypass rejected.
- Fourteen new C# contract tests and six new Java interoperability tests.
- E6.7 closes the serialization and functional consumer boundary.
- Workflow events are transport contracts, not persisted domain transitions.
- Durable event generation, transactional sequencing and Kafka delivery remain required in E7.

### E6.8 final regression acceptance

- C# Release build passed.
- C# tests: 353 passed.
- Python tests: 151 passed.
- Java tests: 72 passed.
- Combined regression: 576 passed.
- Failures, errors and skipped tests: zero.
- Ten C#-generated event fixtures reproduced without changes.
- Inference liveness and PostgreSQL availability verified.
- E6.7 dependency security acceptance retained.
- Cloud expenditure: EUR 0.
- PostgreSQL remains authoritative.
- Durable event production, transactional sequencing and Kafka delivery remain E7 scope.
- Durable NoSQL projections remain E8 scope.
