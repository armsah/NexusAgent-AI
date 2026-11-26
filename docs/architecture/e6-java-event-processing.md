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
| E6.5 | Functional transformations and analytics | OPEN |
| E6.6 | Deduplication and ordering semantics | OPEN |
| E6.7 | Cross-language contract acceptance | OPEN |
| E6.8 | Regression, documentation and GitHub merge | OPEN |

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
