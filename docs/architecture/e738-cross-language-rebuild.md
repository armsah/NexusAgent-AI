# Cross-language authoritative publication recovery

Verified acceptance executes the actual .NET document publisher
against an isolated PostgreSQL database and Kafka topic.

The publisher process is forcibly terminated after Kafka delivery
and before durable journal acknowledgement.

Recovery retransmits the identical event. Subsequent acknowledged
retries suppress additional transmission.

An independent bounded Kafka consumer compares exact keys and
payloads with the immutable PostgreSQL publication journal.

The verified publication envelope is exported and supplied to
two independent Java Kafka Streams rebuilds.

Acceptance verifies projection equivalence, tenant metrics,
event identity, duplicate suppression and isolated resource cleanup.

Production topic end offsets remain unchanged.

Prospective document publication and rebuild acceptance passed.

Legacy historical completeness remains unverified.
Historical production migration remains prohibited.

E738-04 remains PARTIAL pending legacy coverage resolution.
