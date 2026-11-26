package ai.nexusagent.events.streaming;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;

import org.apache.kafka.streams.KafkaStreams;
import org.apache.kafka.streams.StoreQueryParameters;
import org.apache.kafka.streams.state.QueryableStoreTypes;
import org.apache.kafka.streams.state.ReadOnlyKeyValueStore;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.UUID;

public final class ReconciliationAuditReader {

    private static final ObjectMapper JSON =
            new ObjectMapper();

    private final ReadOnlyKeyValueStore<String, String> store;

    public ReconciliationAuditReader(
            ReadOnlyKeyValueStore<String, String> store) {

        this.store = Objects.requireNonNull(store);
    }

    public static ReconciliationAuditReader local(
            KafkaStreams streams) {

        Objects.requireNonNull(streams);

        return new ReconciliationAuditReader(
                streams.store(
                        StoreQueryParameters.fromNameAndType(
                                ReconciliationAudit.STORE,
                                QueryableStoreTypes
                                        .<String, String>keyValueStore())));
    }

    public List<JsonNode> history(
            String tenant,
            String family,
            UUID aggregateId,
            UUID eventId) {

        Objects.requireNonNull(eventId);

        if (!"document".equals(family)
                && !"workflow".equals(family)) {

            throw new IllegalArgumentException(
                    "Unsupported aggregate family");
        }

        String aggregateKey = family + ":"
                + StreamIdentity.aggregateKey(
                        tenant, aggregateId);

        List<JsonNode> result = new ArrayList<>(3);

        for (var status : ReconciliationAudit.Status.values()) {

            String value = store.get(
                    ReconciliationAudit.key(
                            aggregateKey, eventId, status));

            if (value == null) {
                continue;
            }

            JsonNode audit;

            try {
                audit = JSON.readTree(value);
            } catch (Exception ex) {
                throw new IllegalStateException(
                        "Invalid stored audit record", ex);
            }

            if (!tenant.equals(
                        audit.path("tenantId").asText())
                    || !family.equals(
                        audit.path("aggregateType").asText())
                    || !aggregateId.toString().equals(
                        audit.path("aggregateId").asText())
                    || !eventId.toString().equals(
                        audit.path("eventId").asText())
                    || !status.name().equals(
                        audit.path("status").asText())
                    || audit.path("schemaVersion").asInt() != 1
                    || audit.has("payload")) {

                throw new IllegalStateException(
                        "Audit identity or schema mismatch");
            }

            result.add(audit);
        }

        return List.copyOf(result);
    }
}
