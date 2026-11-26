package ai.nexusagent.events.contracts;

import java.time.Instant;
import java.util.UUID;

public record WorkflowEvent(
        int schemaVersion,
        UUID eventId,
        String tenantId,
        UUID workflowId,
        long sequence,
        Type eventType,
        Instant occurredAt,
        Long durationMillis) {

    public enum Type {
        STARTED,
        APPROVAL_REQUIRED,
        APPROVED,
        REJECTED,
        COMPLETED,
        FAILED,
        CANCELLED
    }

    public WorkflowEvent {
        EventRules.version(schemaVersion);
        EventRules.identifier(eventId, "eventId");
        EventRules.identifier(workflowId, "workflowId");
        EventRules.tenant(tenantId);

        if (sequence < 1 || eventType == null || occurredAt == null) {
            throw new IllegalArgumentException("Invalid workflow event");
        }

        boolean terminal = switch (eventType) {
            case REJECTED, COMPLETED, FAILED, CANCELLED -> true;
            default -> false;
        };

        if (terminal) {
            if (durationMillis == null || durationMillis < 0) {
                throw new IllegalArgumentException("Terminal duration required");
            }
        } else if (durationMillis != null) {
            throw new IllegalArgumentException("Unexpected duration");
        }
    }
}