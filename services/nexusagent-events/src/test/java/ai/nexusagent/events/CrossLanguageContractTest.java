package ai.nexusagent.events;

import ai.nexusagent.events.contracts.DocumentEvent;
import ai.nexusagent.events.contracts.EventCodec;
import ai.nexusagent.events.analytics.EventIntegrity;
import ai.nexusagent.events.analytics.EventAnalytics;
import org.junit.jupiter.api.Test;

import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.List;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.*;

class CrossLanguageContractTest {
    private final EventCodec codec = new EventCodec();

    private DocumentEvent read(String type) throws Exception {
        try (var stream = getClass().getResourceAsStream(
                "/contracts/document." + type + ".json")) {
            assertNotNull(stream);
            return codec.readDocument(
                new String(stream.readAllBytes(), StandardCharsets.UTF_8));
        }
    }

    @Test
    void consumesActualDotNetProducerEvents() throws Exception {
        var created = read("created");
        var updated = read("updated");
        var deleted = read("deleted");

        assertEquals(1, created.schemaVersion());
        assertEquals("tenant-e6", created.tenantId());
        assertEquals(UUID.fromString(
            "10000000-0000-0000-0000-000000000001"), created.documentId());
        assertEquals(created.documentId(), updated.documentId());
        assertEquals(created.documentId(), deleted.documentId());
        assertEquals(Instant.parse("2026-01-01T10:00:01Z"), created.occurredAt());
        assertEquals("document.created", created.eventType());
        assertEquals("document.updated", updated.eventType());
        assertEquals("document.deleted", deleted.eventType());
        assertEquals(1, created.documentVersion());
        assertEquals(2, updated.documentVersion());
        assertEquals(3, deleted.documentVersion());
        assertEquals(2, deleted.contentVersion());

        assertEquals(List.of(created, updated, deleted),
            EventIntegrity.documents(List.of(deleted, created, updated, created)));

        var metrics = new EventAnalytics().documents(
            List.of(deleted, created, updated, created),
            Instant.parse("2026-01-01T00:00:00Z"),
            Instant.parse("2026-01-02T00:00:00Z")).get("tenant-e6");

        assertNotNull(metrics);
        assertEquals(1, metrics.created());
        assertEquals(1, metrics.updated());
        assertEquals(1, metrics.deleted());
        assertEquals(0, metrics.activeDocuments());
    }
}