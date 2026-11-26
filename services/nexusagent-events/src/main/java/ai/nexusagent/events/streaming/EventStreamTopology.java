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
import org.apache.kafka.streams.processor.api.Processor;
import org.apache.kafka.streams.processor.api.ProcessorContext;
import org.apache.kafka.streams.processor.api.ProcessorSupplier;
import org.apache.kafka.streams.processor.api.Record;
import org.apache.kafka.streams.state.KeyValueStore;

public final class EventStreamTopology {
    private static final String HISTORY = "accepted-history-v1";
    private static final String IDENTITIES = "accepted-identities-v1";
    private static final String LATEST = "aggregate-latest-v1";

    private EventStreamTopology() {}

    public static Topology build() {
        var serde = Serdes.String();

        ProcessorSupplier<String, String, String, String> supplier =
                EventProcessor::new;

        var topology = new Topology()
                .addSource("documents",
                        serde.deserializer(), serde.deserializer(),
                        StreamSettings.DOCUMENTS)
                .addSource("workflows",
                        serde.deserializer(), serde.deserializer(),
                        StreamSettings.WORKFLOWS)
                .addProcessor("validate-and-project", supplier,
                        "documents", "workflows")
                .addSink("analytics", StreamSettings.ANALYTICS,
                        serde.serializer(), serde.serializer(),
                        "validate-and-project")
                .addSink("dead-letter", StreamSettings.DEAD_LETTERS,
                        serde.serializer(), serde.serializer(),
                        "validate-and-project");

        return StreamStateStores.register(topology,
                "validate-and-project", HISTORY, IDENTITIES, LATEST);
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

        @Override
        public void init(ProcessorContext<String, String> context) {
            this.context = context;
            history = context.getStateStore(HISTORY);
            identities = context.getStateStore(IDENTITIES);
            latest = context.getStateStore(LATEST);
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

            var metadata = context.recordMetadata().orElseThrow();

            ObjectNode failure = json.createObjectNode();
            failure.put("schemaVersion", 1);
            failure.put("reason", reason);
            failure.put("sourceTopic", metadata.topic());
            failure.put("sourcePartition", metadata.partition());
            failure.put("sourceOffset", metadata.offset());

            String failureKey = metadata.topic() + ":"
                    + metadata.partition() + ":" + metadata.offset();

            context.forward(
                    new Record<>(failureKey,
                            failure.toString(), record.timestamp()),
                    "dead-letter");
        }

        @Override
        public void process(Record<String, String> record) {
            Event event;

            try {
                String topic = context.recordMetadata()
                        .orElseThrow().topic();

                event = decode(topic, record.value());

                StreamIdentity.requireMatchingKey(
                        record.key(), event.tenant(), event.aggregate());

            } catch (Rejected ex) {
                reject(record, ex.getMessage());
                return;
            } catch (Exception ex) {
                reject(record, "INVALID_CONTRACT_OR_KEY");
                return;
            }

            String aggregateKey = event.family() + ":" + record.key();

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
                    new Record<>(aggregateKey, serialized, record.timestamp()),
                    "analytics");
        }
    }
}
