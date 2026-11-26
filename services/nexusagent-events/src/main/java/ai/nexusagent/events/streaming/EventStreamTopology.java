package ai.nexusagent.events.streaming;

import ai.nexusagent.events.contracts.DocumentEvent;
import ai.nexusagent.events.contracts.WorkflowEvent;
import ai.nexusagent.events.contracts.EventCodec;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ObjectNode;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.Instant;
import java.util.HexFormat;
import java.util.UUID;

import org.apache.kafka.common.serialization.Serdes;
import org.apache.kafka.streams.Topology;
import org.apache.kafka.streams.StreamsBuilder;
import org.apache.kafka.streams.kstream.Consumed;
import org.apache.kafka.streams.kstream.Produced;
import org.apache.kafka.streams.kstream.Repartitioned;
import org.apache.kafka.streams.kstream.Named;
import org.apache.kafka.streams.kstream.KStream;
import org.apache.kafka.streams.state.Stores;
import org.apache.kafka.streams.processor.api.Processor;
import org.apache.kafka.streams.processor.api.ProcessorContext;
import org.apache.kafka.streams.processor.api.ProcessorSupplier;
import org.apache.kafka.streams.processor.api.Record;
import org.apache.kafka.streams.state.KeyValueStore;

public final class EventStreamTopology {
    private static final String HISTORY = "accepted-history-v1";
    private static final String IDENTITIES = "accepted-identities-v1";
    private static final String LATEST = "aggregate-latest-v1";
    private static final String PENDING = "aggregate-pending-v1";
    private static final long MAX_SEQUENCE_GAP = 1024;

    private EventStreamTopology() {}

    public static Topology build() {
        var serde = Serdes.String();
        var builder = new StreamsBuilder();

        for (String name : new String[]{
                HISTORY, IDENTITIES, LATEST, PENDING,
                GlobalIdentityProcessor.STORE}) {

            builder.addStateStore(
                    Stores.keyValueStoreBuilder(
                            Stores.persistentKeyValueStore(name),
                            serde, serde));
        }

        KStream<String, String> documents = builder.stream(
                StreamSettings.DOCUMENTS,
                Consumed.with(serde, serde));

        KStream<String, String> workflows = builder.stream(
                StreamSettings.WORKFLOWS,
                Consumed.with(serde, serde));

        var validated = documents.merge(workflows)
                .process(
                        ValidationProcessor::new,
                        Named.as("validate-source-events"));

        validated.filter((key, value) -> key.startsWith("failure:"))
                .selectKey((key, value) -> key.substring(8))
                .to(StreamSettings.DEAD_LETTERS,
                        Produced.with(serde, serde));

        var byIdentity = validated
                .filter((key, value) -> !key.startsWith("failure:"))
                .repartition(
                        Repartitioned.<String, String>as("event-identity-v1")
                                .withKeySerde(serde)
                                .withValueSerde(serde)
                                .withNumberOfPartitions(3));

        var registered = byIdentity.process(
                RegistryAdapter::new,
                Named.as("global-identity-registry"),
                GlobalIdentityProcessor.STORE);

        registered.filter((key, value) -> key.startsWith("failure:"))
                .selectKey((key, value) -> key.substring(8))
                .to(StreamSettings.DEAD_LETTERS,
                        Produced.with(serde, serde));

        var byAggregate = registered
                .filter((key, value) -> !key.startsWith("failure:"))
                .repartition(
                        Repartitioned.<String, String>as("aggregate-order-v1")
                                .withKeySerde(serde)
                                .withValueSerde(serde)
                                .withNumberOfPartitions(3));

        var projected = byAggregate.process(
                EventProcessor::new,
                Named.as("validate-and-project"),
                HISTORY, IDENTITIES, LATEST, PENDING);

        projected.filter((key, value) -> key.startsWith("failure:"))
                .selectKey((key, value) -> key.substring(8))
                .to(StreamSettings.DEAD_LETTERS,
                        Produced.with(serde, serde));

        projected.filter((key, value) -> !key.startsWith("failure:"))
                .to(StreamSettings.ANALYTICS,
                        Produced.with(serde, serde));

        return builder.build();
    }

    private static final class ValidationProcessor
            implements Processor<String, String, String, String> {

        private final EventProcessor decoder = new EventProcessor();
        private final ObjectMapper json = new ObjectMapper();

        private ProcessorContext<String, String> context;

        @Override
        public void init(ProcessorContext<String, String> context) {
            this.context = context;
        }

        @Override
        public void process(Record<String, String> record) {
            var metadata = context.recordMetadata().orElseThrow();

            Event event;

            try {
                event = decoder.decode(metadata.topic(), record.value());

                StreamIdentity.requireMatchingKey(
                        record.key(), event.tenant(), event.aggregate());

            } catch (Rejected ex) {
                failure(record, ex.getMessage());
                return;
            } catch (Exception ex) {
                failure(record, "INVALID_CONTRACT_OR_KEY");
                return;
            }

            ObjectNode envelope = json.createObjectNode();

            envelope.put("eventId", event.eventId().toString());
            envelope.put("family", event.family());
            envelope.put("aggregateKey", record.key());
            envelope.put("payload", event.canonical());
            envelope.put("sourceTopic", metadata.topic());
            envelope.put("sourcePartition", metadata.partition());
            envelope.put("sourceOffset", metadata.offset());

            context.forward(new Record<>(
                    event.eventId().toString(),
                    envelope.toString(),
                    record.timestamp()));
        }

        private void failure(
                Record<String, String> record, String reason) {

            var metadata = context.recordMetadata().orElseThrow();

            ObjectNode failure = json.createObjectNode();
            failure.put("schemaVersion", 1);
            failure.put("reason", reason);
            failure.put("sourceTopic", metadata.topic());
            failure.put("sourcePartition", metadata.partition());
            failure.put("sourceOffset", metadata.offset());

            context.forward(new Record<>(
                    "failure:" + metadata.topic() + ":"
                            + metadata.partition() + ":" + metadata.offset(),
                    failure.toString(),
                    record.timestamp()));
        }
    }

    private static final class RegistryAdapter
            implements Processor<String, String, String, String> {

        private final ObjectMapper json = new ObjectMapper();

        private ProcessorContext<String, String> context;
        private KeyValueStore<String, String> registry;

        @Override
        public void init(ProcessorContext<String, String> context) {
            this.context = context;
            registry = context.getStateStore(GlobalIdentityProcessor.STORE);
        }

        @Override
        public void process(Record<String, String> record) {
            JsonNode envelope = readEnvelope(record.value());

            String eventId = envelope.path("eventId").asText();
            String family = envelope.path("family").asText();
            String aggregateKey = envelope.path("aggregateKey").asText();
            String payload = envelope.path("payload").asText();

            String expectedKey = GlobalEventIdentity.registryKey(
                    UUID.fromString(eventId));

            if (!expectedKey.equals(record.key())) {
                throw new IllegalStateException(
                        "Identity repartition invariant violated");
            }

            String fingerprint = GlobalEventIdentity.fingerprint(
                    family, aggregateKey, payload);

            var decision = GlobalEventIdentity.classify(
                    registry.get(expectedKey), fingerprint);

            if (decision == GlobalEventIdentity.Decision.CONFLICT) {
                ObjectNode failure = json.createObjectNode();

                failure.put("schemaVersion", 1);
                failure.put("reason", "GLOBAL_EVENT_ID_CONFLICT");
                failure.put("sourceTopic",
                        envelope.path("sourceTopic").asText());
                failure.put("sourcePartition",
                        envelope.path("sourcePartition").asInt());
                failure.put("sourceOffset",
                        envelope.path("sourceOffset").asLong());

                context.forward(new Record<>(
                        "failure:" + envelope.path("sourceTopic").asText()
                                + ":" + envelope.path("sourcePartition").asInt()
                                + ":" + envelope.path("sourceOffset").asLong(),
                        failure.toString(),
                        record.timestamp()));

                return;
            }

            if (decision == GlobalEventIdentity.Decision.NEW) {
                registry.put(expectedKey, fingerprint);
            }

            context.forward(new Record<>(
                    family + ":" + aggregateKey,
                    record.value(),
                    record.timestamp()));
        }
    }

    private static JsonNode readEnvelope(String value) {
        try {
            JsonNode envelope = new ObjectMapper().readTree(value);

            if (envelope == null || !envelope.isObject()) {
                throw new IllegalStateException("Invalid envelope");
            }

            return envelope;

        } catch (Exception ex) {
            throw new IllegalStateException(
                    "Invalid internal event envelope", ex);
        }
    }

    private record Event(
            String family,
            UUID eventId,
            String tenant,
            UUID aggregate,
            long sequence,
            String type,
            Instant occurredAt,
            int contentVersion,
            Long durationMillis,
            String canonical) {}

    private static final class Rejected extends RuntimeException {
        Rejected(String reason) {
            super(reason);
        }
    }

    private static final class EventProcessor
            implements Processor<String, String, String, String> {

        private final EventCodec codec = new EventCodec();
        private final ObjectMapper json = new ObjectMapper();

        private ProcessorContext<String, String> context;
        private KeyValueStore<String, String> history;
        private KeyValueStore<String, String> identities;
        private KeyValueStore<String, String> latest;
        private KeyValueStore<String, String> pending;
        private boolean draining;

        @Override
        public void init(ProcessorContext<String, String> context) {
            this.context = context;
            history = context.getStateStore(HISTORY);
            identities = context.getStateStore(IDENTITIES);
            latest = context.getStateStore(LATEST);
            pending = context.getStateStore(PENDING);
        }

        private Event decode(String topic, String value) throws Exception {
            if (value == null || value.isBlank()) {
                throw new Rejected("EMPTY_PAYLOAD");
            }

            if (value.getBytes(StandardCharsets.UTF_8).length > 16384) {
                throw new Rejected("PAYLOAD_TOO_LARGE");
            }

            if (StreamSettings.DOCUMENTS.equals(topic)) {
                DocumentEvent e = codec.readDocument(value);

                return new Event(
                        "document", e.eventId(), e.tenantId(),
                        e.documentId(), e.documentVersion(),
                        e.eventType(), e.occurredAt(),
                        e.contentVersion(), null,
                        codec.writeDocument(e));
            }

            if (StreamSettings.WORKFLOWS.equals(topic)) {
                WorkflowEvent e = codec.readWorkflow(value);

                return new Event(
                        "workflow", e.eventId(), e.tenantId(),
                        e.workflowId(), e.sequence(),
                        e.eventType().name(), e.occurredAt(),
                        0, e.durationMillis(),
                        codec.writeWorkflow(e));
            }

            throw new Rejected("UNKNOWN_TOPIC");
        }

        private static String digest(String value) {
            try {
                return HexFormat.of().formatHex(
                        MessageDigest.getInstance("SHA-256")
                                .digest(value.getBytes(StandardCharsets.UTF_8)));
            } catch (java.security.NoSuchAlgorithmException ex) {
                throw new IllegalStateException(ex);
            }
        }

        private JsonNode readState(String value) {
            try {
                return json.readTree(value);
            } catch (Exception ex) {
                throw new IllegalStateException("Invalid persisted state", ex);
            }
        }

        private static boolean workflowTransition(String from, String to) {
            return switch (from) {
                case "STARTED", "APPROVED" ->
                        switch (to) {
                            case "APPROVAL_REQUIRED", "COMPLETED",
                                 "FAILED", "CANCELLED" -> true;
                            default -> false;
                        };
                case "APPROVAL_REQUIRED" ->
                        switch (to) {
                            case "APPROVED", "REJECTED",
                                 "FAILED", "CANCELLED" -> true;
                            default -> false;
                        };
                default -> false;
            };
        }

        private void validateSequence(Event event, JsonNode previous) {
            if (previous == null) {
                if (event.sequence() != 1) {
                    throw new Rejected("INITIAL_SEQUENCE_REQUIRED");
                }

                if (event.family().equals("workflow")
                        && !event.type().equals("STARTED")) {
                    throw new Rejected("INVALID_INITIAL_STATE");
                }

                if (event.family().equals("document")
                        && !event.type().equals("document.created")) {
                    throw new Rejected("INVALID_INITIAL_STATE");
                }

                return;
            }

            long sequence = previous.get("sequence").asLong();

            if (event.sequence() != sequence + 1) {
                throw new Rejected("SEQUENCE_GAP_OR_REGRESSION");
            }

            if (event.occurredAt().isBefore(
                    Instant.parse(previous.get("occurredAt").asText()))) {
                throw new Rejected("TIMESTAMP_REGRESSION");
            }

            String previousType = previous.get("eventType").asText();

            if (event.family().equals("workflow")) {
                if (!workflowTransition(previousType, event.type())) {
                    throw new Rejected("INVALID_WORKFLOW_TRANSITION");
                }
            } else {
                if (previousType.equals("document.deleted")
                        || event.type().equals("document.created")) {
                    throw new Rejected("INVALID_DOCUMENT_TRANSITION");
                }

                int previousContent = previous.get("contentVersion").asInt();

                if (event.contentVersion() < previousContent) {
                    throw new Rejected("CONTENT_VERSION_REGRESSION");
                }
            }
        }

        private void reject(
                Record<String, String> record,
                String reason) {

            JsonNode envelope = readEnvelope(record.value());

            ObjectNode failure = json.createObjectNode();
            failure.put("schemaVersion", 1);
            failure.put("reason", reason);
            failure.put("sourceTopic",
                    envelope.path("sourceTopic").asText());
            failure.put("sourcePartition",
                    envelope.path("sourcePartition").asInt());
            failure.put("sourceOffset",
                    envelope.path("sourceOffset").asLong());

            String failureKey = "failure:"
                    + envelope.path("sourceTopic").asText() + ":"
                    + envelope.path("sourcePartition").asInt() + ":"
                    + envelope.path("sourceOffset").asLong();

            context.forward(new Record<>(
                    failureKey, failure.toString(), record.timestamp()));
        }

        @Override
        public void process(Record<String, String> record) {
            JsonNode envelope = readEnvelope(record.value());
            Event event;

            try {
                event = decode(
                        envelope.path("sourceTopic").asText(),
                        envelope.path("payload").asText());

                StreamIdentity.requireMatchingKey(
                        envelope.path("aggregateKey").asText(),
                        event.tenant(), event.aggregate());

            } catch (Exception ex) {
                throw new IllegalStateException(
                        "Validated envelope invariant violated", ex);
            }

            String aggregateKey = event.family() + ":"
                    + envelope.path("aggregateKey").asText();

            if (!aggregateKey.equals(record.key())) {
                throw new IllegalStateException(
                        "Aggregate repartition invariant violated");
            }

            String identityKey = aggregateKey + ":" + event.eventId();

            String sequenceKey = aggregateKey + ":" + event.sequence();

            String fingerprint = digest(event.canonical());

            String existingIdentity = identities.get(identityKey);

            if (existingIdentity != null) {
                if (!existingIdentity.equals(fingerprint)) {
                    reject(record, "EVENT_ID_CONFLICT");
                }
                return;
            }

            String existingSequence = history.get(sequenceKey);

            if (existingSequence != null) {
                if (!existingSequence.equals(fingerprint)) {
                    reject(record, "SEQUENCE_CONFLICT");
                }
                return;
            }

            String prior = latest.get(aggregateKey);
            JsonNode previous = prior == null ? null : readState(prior);

            long expected = previous == null
                    ? 1 : previous.path("sequence").asLong() + 1;

            if (event.sequence() > expected) {
                if (event.sequence() - expected > MAX_SEQUENCE_GAP) {
                    reject(record, "SEQUENCE_GAP_LIMIT_EXCEEDED");
                    return;
                }

                String buffered = pending.get(sequenceKey);

                if (buffered != null) {
                    JsonNode oldEnvelope = readEnvelope(buffered);

                    if (!oldEnvelope.path("payload").asText()
                            .equals(event.canonical())) {
                        reject(record, "BUFFERED_SEQUENCE_CONFLICT");
                    }

                    return;
                }

                pending.put(sequenceKey, record.value());
                return;
            }

            try {
                validateSequence(event, previous);
            } catch (Rejected ex) {
                reject(record, ex.getMessage());
                return;
            }

            ObjectNode projection = json.createObjectNode();
            projection.put("schemaVersion", 1);
            projection.put("tenantId", event.tenant());
            projection.put("aggregateType", event.family());
            projection.put("aggregateId", event.aggregate().toString());
            projection.put("eventId", event.eventId().toString());
            projection.put("sequence", event.sequence());
            projection.put("eventType", event.type());
            projection.put("occurredAt", event.occurredAt().toString());
            projection.put("contentVersion", event.contentVersion());

            if (event.durationMillis() != null) {
                projection.put("durationMillis", event.durationMillis());
            }

            long acceptedEvents = previous == null
                    ? 1 : previous.get("acceptedEvents").asLong() + 1;

            projection.put("acceptedEvents", acceptedEvents);

            if (event.family().equals("document")) {
                projection.put("active",
                        !event.type().equals("document.deleted"));
            } else {
                projection.put("terminal", switch (event.type()) {
                    case "COMPLETED", "FAILED", "CANCELLED",
                         "REJECTED" -> true;
                    default -> false;
                });
            }

            String serialized = projection.toString();

            // State and output participate in the Kafka Streams transaction.
            // Unexpected persistence failures must propagate.
            identities.put(identityKey, fingerprint);
            history.put(sequenceKey, fingerprint);
            latest.put(aggregateKey, serialized);

            context.forward(
                    new Record<>(aggregateKey, serialized, record.timestamp()));

            // Drain contiguous successors already persisted by this task.
            // State mutations and outputs remain transactionally coordinated.
            drainPending(aggregateKey, record.timestamp());
        }

        private void drainPending(String aggregateKey, long timestamp) {
            if (draining) {
                return;
            }

            draining = true;

            try {
                for (long i = 0; i < MAX_SEQUENCE_GAP; i++) {
                    JsonNode current = readState(latest.get(aggregateKey));

                    long next = current.path("sequence").asLong() + 1;
                    String key = aggregateKey + ":" + next;
                    String buffered = pending.get(key);

                    if (buffered == null) {
                        return;
                    }

                    pending.delete(key);

                    process(new Record<>(
                            aggregateKey, buffered, timestamp));

                    JsonNode after = readState(latest.get(aggregateKey));

                    if (after.path("sequence").asLong() != next) {
                        // Invalid successor was routed to the DLQ.
                        // Do not advance past a rejected revision.
                        return;
                    }
                }
            } finally {
                draining = false;
            }
        }
    }
}
