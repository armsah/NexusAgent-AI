package ai.nexusagent.events.streaming;

import com.fasterxml.jackson.databind.ObjectMapper;

import org.apache.kafka.streams.processor.api.Processor;
import org.apache.kafka.streams.processor.api.ProcessorContext;
import org.apache.kafka.streams.processor.api.Record;
import org.apache.kafka.streams.state.KeyValueStore;

import java.util.UUID;

public final class GlobalIdentityProcessor
        implements Processor<String, String, String, String> {

    public static final String STORE = "global-event-identities-v1";

    private final ObjectMapper json = new ObjectMapper();

    private ProcessorContext<String, String> context;
    private KeyValueStore<String, String> registry;

    @Override
    public void init(ProcessorContext<String, String> context) {
        this.context = context;
        this.registry = context.getStateStore(STORE);
    }

    @Override
    public void process(Record<String, String> record) {
        var envelope = parse(record.value());

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
            var failure = json.createObjectNode();

            failure.put("schemaVersion", 1);
            failure.put("reason", "GLOBAL_EVENT_ID_CONFLICT");
            failure.put("sourceTopic",
                    envelope.path("sourceTopic").asText());
            failure.put("sourcePartition",
                    envelope.path("sourcePartition").asInt());
            failure.put("sourceOffset",
                    envelope.path("sourceOffset").asLong());

            String failureKey =
                    envelope.path("sourceTopic").asText() + ":"
                    + envelope.path("sourcePartition").asInt() + ":"
                    + envelope.path("sourceOffset").asLong();

            context.forward(
                    new Record<>(failureKey,
                            failure.toString(), record.timestamp()),
                    "identity-dead-letter");

            return;
        }

        if (decision == GlobalEventIdentity.Decision.NEW) {
            registry.put(expectedKey, fingerprint);
        }

        // Identical deliveries must continue to aggregate validation.
        // A previous delivery may have been rejected for a sequence gap.
        context.forward(
                new Record<>(aggregateKey,
                        record.value(), record.timestamp()),
                "identity-accepted");
    }

    private com.fasterxml.jackson.databind.JsonNode parse(String value) {
        try {
            return json.readTree(value);
        } catch (Exception ex) {
            throw new IllegalStateException(
                    "Invalid internal event envelope", ex);
        }
    }
}
