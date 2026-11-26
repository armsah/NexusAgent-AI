package ai.nexusagent.events.streaming;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;

import java.time.Instant;
import java.util.Objects;
import java.util.UUID;

public final class ReconciliationAudit {

    public static final String STORE =
            "aggregate-reconciliation-audit-v1";

    private static final ObjectMapper JSON =
            new ObjectMapper();

    public enum Status {
        BUFFERED,
        RECONCILED,
        REJECTED
    }

    private ReconciliationAudit() {}

    public static String key(
            String aggregateKey,
            UUID eventId,
            Status status) {

        Objects.requireNonNull(aggregateKey);
        Objects.requireNonNull(eventId);
        Objects.requireNonNull(status);

        return aggregateKey + ":" + eventId
                + ":" + status.name();
    }

    public static String create(
            String tenant,
            String family,
            UUID aggregateId,
            UUID eventId,
            long sequence,
            long expectedSequence,
            Status status,
            Instant observedAt,
            Instant recordedAt,
            String reason) {

        Objects.requireNonNull(status);
        Objects.requireNonNull(recordedAt);

        if (sequence < 1 || expectedSequence < 1) {
            throw new IllegalArgumentException(
                    "Invalid reconciliation sequence");
        }

        ObjectNode audit = JSON.createObjectNode();

        audit.put("schemaVersion", 1);
        audit.put("tenantId", tenant);
        audit.put("aggregateType", family);
        audit.put("aggregateId", aggregateId.toString());
        audit.put("eventId", eventId.toString());
        audit.put("sequence", sequence);
        audit.put("expectedSequence", expectedSequence);
        audit.put("status", status.name());
        audit.put("recordedAt", recordedAt.toString());

        if (observedAt == null) {
            audit.putNull("firstObservedAt");
        } else {
            audit.put(
                    "firstObservedAt",
                    observedAt.toString());
        }

        if (reason != null) {
            audit.put("reason", reason);
        }

        return audit.toString();
    }
}
