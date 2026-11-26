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