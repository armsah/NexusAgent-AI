package ai.nexusagent.events.streaming;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;

import org.apache.kafka.streams.processor.api.Processor;
import org.apache.kafka.streams.processor.api.ProcessorContext;
import org.apache.kafka.streams.processor.api.Record;
import org.apache.kafka.streams.state.KeyValueStore;

import java.util.UUID;

public final class TenantAnalyticsProcessor
        implements Processor<String, String, String, String> {

    public static final String METRICS_STORE =
            "tenant-metrics-v1";

    public static final String IDENTITIES_STORE =
            "tenant-metric-identities-v1";

    private final ObjectMapper json = new ObjectMapper();

    private ProcessorContext<String, String> context;

    private KeyValueStore<String, String> metrics;
    private KeyValueStore<String, String> identities;

    @Override
    public void init(ProcessorContext<String, String> context) {
        this.context = context;
        metrics = context.getStateStore(METRICS_STORE);
        identities = context.getStateStore(IDENTITIES_STORE);
    }

    public static String tenantKey(String projection) {
        JsonNode event = parse(projection);

        String tenant = event.path("tenantId").asText();

        if (tenant.isBlank()) {
            throw new IllegalStateException(
                    "Accepted projection missing tenant");
        }

        return tenant;
    }

    @Override
    public void process(Record<String, String> record) {
        JsonNode event = parse(record.value());

        String tenant = tenantKey(record.value());

        if (!tenant.equals(record.key())) {
            throw new IllegalStateException(
                    "Tenant repartition invariant violated");
        }

        String eventId = GlobalEventIdentity.registryKey(
                UUID.fromString(event.path("eventId").asText()));

        String type = event.path("eventType").asText();

        String family = type.startsWith("document.")
                ? "document" : "workflow";

        String aggregateId = event.path("aggregateId").asText();

        UUID aggregate = UUID.fromString(aggregateId);

        String aggregateKey =
                StreamIdentity.aggregateKey(tenant, aggregate);

        String identityKey = tenant.length() + ":"
                + tenant + ":" + eventId;

        String fingerprint = GlobalEventIdentity.fingerprint(
                family, aggregateKey, record.value());

        var decision = GlobalEventIdentity.classify(
                identities.get(identityKey), fingerprint);

        if (decision == GlobalEventIdentity.Decision.DUPLICATE) {
            return;
        }

        if (decision == GlobalEventIdentity.Decision.CONFLICT) {
            throw new IllegalStateException(
                    "Conflicting accepted projection identity");
        }

        String previous = metrics.get(tenant);

        ObjectNode current;

        if (previous == null) {
            current = TenantMetrics.empty(tenant);
        } else {
            JsonNode stored = parse(previous);

            if (!(stored instanceof ObjectNode object)) {
                throw new IllegalStateException(
                        "Invalid tenant metrics state");
            }

            current = object;
        }

        ObjectNode next = TenantMetrics.apply(current, event);

        String serialized = next.toString();

        identities.put(identityKey, fingerprint);
        metrics.put(tenant, serialized);

        context.forward(new Record<>(
                tenant, serialized, record.timestamp()));
    }

    private static JsonNode parse(String value) {
        try {
            JsonNode parsed = new ObjectMapper().readTree(value);

            if (parsed == null || !parsed.isObject()) {
                throw new IllegalStateException(
                        "Invalid analytics object");
            }

            return parsed;

        } catch (Exception ex) {
            throw new IllegalStateException(
                    "Invalid internal analytics record", ex);
        }
    }
}
