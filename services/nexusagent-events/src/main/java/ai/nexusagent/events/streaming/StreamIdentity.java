package ai.nexusagent.events.streaming;

import java.util.UUID;

public final class StreamIdentity {
    private StreamIdentity() {}

    public static String aggregateKey(String tenantId, UUID aggregateId) {
        if (tenantId == null || tenantId.isBlank()
                || tenantId.length() > 256
                || !tenantId.equals(tenantId.strip())
                || tenantId.chars().anyMatch(Character::isISOControl)
                || aggregateId == null
                || aggregateId.equals(new UUID(0, 0))) {
            throw new IllegalArgumentException("Invalid aggregate identity.");
        }

        return tenantId.length() + ":" + tenantId + ":" + aggregateId;
    }

    public static void requireMatchingKey(
            String actual, String tenantId, UUID aggregateId) {

        if (!aggregateKey(tenantId, aggregateId).equals(actual)) {
            throw new IllegalArgumentException("Partition key mismatch.");
        }
    }
}
