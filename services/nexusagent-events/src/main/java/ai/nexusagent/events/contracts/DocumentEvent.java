package ai.nexusagent.events.contracts;

import java.time.Instant;
import java.util.Set;
import java.util.UUID;

public record DocumentEvent(
        int schemaVersion,
        UUID eventId,
        String eventType,
        String tenantId,
        UUID documentId,
        int documentVersion,
        int contentVersion,
        Instant occurredAt) {

    private static final Set<String> TYPES = Set.of(
        "document.created", "document.updated", "document.deleted");

    public DocumentEvent {
        EventRules.version(schemaVersion);
        EventRules.identifier(eventId, "eventId");
        EventRules.identifier(documentId, "documentId");
        EventRules.tenant(tenantId);

        if (eventType == null || !TYPES.contains(eventType)) {
            throw new IllegalArgumentException("Unsupported document event");
        }

        if (documentVersion < 1 || contentVersion < 1
                || contentVersion > documentVersion) {
            throw new IllegalArgumentException("Invalid document versions");
        }

        if (occurredAt == null) {
            throw new IllegalArgumentException("Occurrence time is required");
        }
    }
}