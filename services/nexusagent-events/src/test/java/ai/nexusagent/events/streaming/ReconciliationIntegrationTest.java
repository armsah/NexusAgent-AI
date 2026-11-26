package ai.nexusagent.events.streaming;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;

import org.apache.kafka.common.serialization.StringDeserializer;
import org.apache.kafka.common.serialization.StringSerializer;
import org.apache.kafka.streams.TopologyTestDriver;
import org.apache.kafka.streams.TestInputTopic;
import org.apache.kafka.streams.TestOutputTopic;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.nio.file.Path;
import java.time.Instant;
import java.util.List;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.*;

class ReconciliationIntegrationTest {

    @TempDir
    Path stateDirectory;

    private static final ObjectMapper JSON =
            new ObjectMapper();

    private static final Instant OCCURRED =
            Instant.parse("2026-01-01T00:00:00Z");

    private TopologyTestDriver driver;

    private TestInputTopic<String, String> documents;
    private TestOutputTopic<String, String> analytics;
    private TestOutputTopic<String, String> failures;

    private ReconciliationAuditReader reader;

    private final UUID aggregate = UUID.randomUUID();

    @BeforeEach
    void setup() {

        driver = new TopologyTestDriver(
                EventStreamTopology.build(),
                StreamSettings.create(
                        "localhost:19092",
                        "reconciliation-integration",
                        stateDirectory));

        documents = driver.createInputTopic(
                StreamSettings.DOCUMENTS,
                new StringSerializer(),
                new StringSerializer());

        analytics = driver.createOutputTopic(
                StreamSettings.ANALYTICS,
                new StringDeserializer(),
                new StringDeserializer());

        failures = driver.createOutputTopic(
                StreamSettings.DEAD_LETTERS,
                new StringDeserializer(),
                new StringDeserializer());

        reader = new ReconciliationAuditReader(
                driver.getKeyValueStore(
                        ReconciliationAudit.STORE));
    }

    @AfterEach
    void cleanup() {
        if (driver != null) {
            driver.close();
        }
    }

    private String document(
            String tenant,
            UUID event,
            int sequence,
            String type) {

        var node = JSON.createObjectNode();

        node.put("schemaVersion", 1);
        node.put("tenantId", tenant);
        node.put("eventId", event.toString());
        node.put("documentId", aggregate.toString());
        node.put("documentVersion", sequence);
        node.put("contentVersion", sequence);
        node.put("eventType", type);
        node.put("occurredAt", OCCURRED.toString());

        return node.toString();
    }

    private void send(
            String tenant,
            UUID event,
            int sequence,
            String type) {

        documents.pipeInput(
                StreamIdentity.aggregateKey(
                        tenant, aggregate),
                document(tenant, event, sequence, type));
    }

    private List<JsonNode> history(
            String tenant,
            UUID event) {

        return reader.history(
                tenant, "document", aggregate, event);
    }

    @Test
    void unresolvedGapRemainsAuditableWithoutProjection() {

        UUID successor = UUID.randomUUID();

        send("tenant-a", successor, 3,
                "document.updated");

        assertTrue(analytics.isEmpty());
        assertTrue(failures.isEmpty());

        var audit = history("tenant-a", successor);

        assertEquals(1, audit.size());
        assertEquals("BUFFERED",
                audit.getFirst().path("status").asText());

        assertEquals(1,
                audit.getFirst()
                        .path("expectedSequence").asLong());

        assertTrue(history("tenant-b", successor).isEmpty());
    }

    @Test
    void reconciliationPreservesHistoryAndSuppressesDuplicates() {

        UUID successor = UUID.randomUUID();

        send("tenant-a", successor, 3,
                "document.updated");

        var original = history(
                "tenant-a", successor).getFirst();

        send("tenant-a", successor, 3,
                "document.updated");

        assertEquals(
                List.of(original),
                history("tenant-a", successor));

        send("tenant-a", UUID.randomUUID(), 1,
                "document.created");

        send("tenant-a", UUID.randomUUID(), 2,
                "document.updated");

        assertEquals(3,
                analytics.readValuesToList().size());

        var audit = history("tenant-a", successor);

        assertEquals(2, audit.size());
        assertEquals(original, audit.getFirst());

        assertEquals("RECONCILED",
                audit.getLast().path("status").asText());

        assertEquals(
                original.path("firstObservedAt"),
                audit.getLast().path("firstObservedAt"));

        send("tenant-a", successor, 3,
                "document.updated");

        assertTrue(analytics.isEmpty());
        assertTrue(failures.isEmpty());

        assertEquals(audit,
                history("tenant-a", successor));
    }

    @Test
    void invalidBufferedSuccessorIsAuditedAndDoesNotEraseHistory()
            throws Exception {

        UUID first = UUID.randomUUID();
        UUID second = UUID.randomUUID();
        UUID invalid = UUID.randomUUID();

        // A second creation is invalid after creation.
        send("tenant-a", invalid, 3,
                "document.created");

        var original = history(
                "tenant-a", invalid).getFirst();

        send("tenant-a", first, 1,
                "document.created");

        send("tenant-a", second, 2,
                "document.updated");

        assertEquals(2,
                analytics.readValuesToList().size());

        assertFalse(failures.isEmpty());

        var rejection = JSON.readTree(
                failures.readValue());

        var audit = history("tenant-a", invalid);

        assertEquals(2, audit.size());
        assertEquals(original, audit.getFirst());

        var rejected = audit.getLast();

        assertEquals("REJECTED",
                rejected.path("status").asText());

        assertFalse(
                rejected.path("reason").asText().isBlank());

        assertEquals(
                rejection.path("reason").asText(),
                rejected.path("reason").asText());

        assertEquals(
                original.path("firstObservedAt"),
                rejected.path("firstObservedAt"));

        assertTrue(failures.isEmpty());

        // A corrected successor uses a new event identity.
        UUID corrected = UUID.randomUUID();

        send("tenant-a", corrected, 3,
                "document.updated");

        var projection = JSON.readTree(
                analytics.readValue());

        assertEquals(corrected.toString(),
                projection.path("eventId").asText());

        assertEquals(3,
                projection.path("acceptedEvents").asLong());

        // Replaying accepted history must not re-project it.
        send("tenant-a", first, 1,
                "document.created");

        send("tenant-a", second, 2,
                "document.updated");

        assertTrue(analytics.isEmpty());
        assertTrue(failures.isEmpty());

        assertEquals(audit,
                history("tenant-a", invalid));
    }

    @Test
    void anotherTenantCannotResolveOrReadPendingAudit() {

        UUID pending = UUID.randomUUID();

        send("tenant-a", pending, 2,
                "document.updated");

        send("tenant-b", UUID.randomUUID(), 1,
                "document.created");

        assertEquals(1,
                analytics.readValuesToList().size());

        assertEquals(1,
                history("tenant-a", pending).size());

        assertTrue(
                history("tenant-b", pending).isEmpty());

        send("tenant-a", UUID.randomUUID(), 1,
                "document.created");

        assertEquals(2,
                analytics.readValuesToList().size());

        assertEquals(2,
                history("tenant-a", pending).size());

        assertTrue(
                history("tenant-b", pending).isEmpty());
    }

    @Test
    void auditReaderRejectsInvalidFamily() {

        assertThrows(
                IllegalArgumentException.class,
                () -> reader.history(
                        "tenant-a", "unknown",
                        aggregate, UUID.randomUUID()));
    }
}
