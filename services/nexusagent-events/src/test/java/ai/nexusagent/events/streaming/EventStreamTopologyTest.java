package ai.nexusagent.events.streaming;

import static org.junit.jupiter.api.Assertions.*;

import java.nio.file.Path;
import java.util.UUID;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.JsonNode;

import org.apache.kafka.common.serialization.Serdes;
import org.apache.kafka.streams.TopologyTestDriver;
import org.apache.kafka.streams.TestInputTopic;
import org.apache.kafka.streams.TestOutputTopic;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class EventStreamTopologyTest {
    @TempDir
    Path state;

    private TopologyTestDriver driver;
    private TestInputTopic<String, String> documents;
    private TestInputTopic<String, String> workflows;
    private TestOutputTopic<String, String> analytics;
    private TestOutputTopic<String, String> failures;

    private final ObjectMapper json = new ObjectMapper();

    private final UUID aggregate = UUID.randomUUID();
    private final String tenant = "tenant-a";

    @BeforeEach
    void setup() {
        var serde = Serdes.String();

        driver = new TopologyTestDriver(
                EventStreamTopology.build(),
                StreamSettings.create(
                        "localhost:19092", "topology-test", state));

        documents = driver.createInputTopic(
                StreamSettings.DOCUMENTS,
                serde.serializer(), serde.serializer());

        workflows = driver.createInputTopic(
                StreamSettings.WORKFLOWS,
                serde.serializer(), serde.serializer());

        analytics = driver.createOutputTopic(
                StreamSettings.ANALYTICS,
                serde.deserializer(), serde.deserializer());

        failures = driver.createOutputTopic(
                StreamSettings.DEAD_LETTERS,
                serde.deserializer(), serde.deserializer());
    }

    @AfterEach
    void close() {
        if (driver != null) {
            driver.close();
        }
    }

    private String key(String tenantId) {
        return StreamIdentity.aggregateKey(tenantId, aggregate);
    }

    private String document(
            UUID eventId, String tenantId,
            int sequence, String type) {

        var node = json.createObjectNode();
        node.put("schemaVersion", 1);
        node.put("eventId", eventId.toString());
        node.put("tenantId", tenantId);
        node.put("documentId", aggregate.toString());
        node.put("documentVersion", sequence);
        node.put("contentVersion", sequence);
        node.put("eventType", type);
        node.put("occurredAt", "2026-01-01T00:00:00Z");
        return node.toString();
    }

    private String workflow(
            UUID eventId, long sequence, String type) {

        var node = json.createObjectNode();
        node.put("schemaVersion", 1);
        node.put("eventId", eventId.toString());
        node.put("tenantId", tenant);
        node.put("workflowId", aggregate.toString());
        node.put("sequence", sequence);
        node.put("eventType", type);
        node.put("occurredAt", "2026-01-01T00:00:00Z");

        switch (type) {
            case "COMPLETED", "FAILED", "CANCELLED", "REJECTED" ->
                    node.put("durationMillis", 0);
            default -> node.putNull("durationMillis");
        }

        return node.toString();
    }

    private JsonNode projection() throws Exception {
        return json.readTree(analytics.readValue());
    }

    private void reason(String expected) throws Exception {
        assertEquals(expected,
                json.readTree(failures.readValue())
                        .get("reason").asText());
    }

    @Test
    void documentLifecycleAndDuplicateSuppression() throws Exception {
        String created = document(UUID.randomUUID(), tenant,
                1, "document.created");

        documents.pipeInput(key(tenant), created);
        documents.pipeInput(key(tenant), created);

        assertEquals(1, projection().get("sequence").asInt());
        assertTrue(analytics.isEmpty());

        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        2, "document.updated"));

        assertEquals(2, projection().get("acceptedEvents").asInt());

        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        3, "document.deleted"));

        assertFalse(projection().get("active").asBoolean());
        assertTrue(failures.isEmpty());
    }

    @Test
    void conflictingIdentityRejected() throws Exception {
        UUID id = UUID.randomUUID();

        documents.pipeInput(key(tenant),
                document(id, tenant, 1, "document.created"));

        projection();

        documents.pipeInput(key(tenant),
                document(id, tenant, 2, "document.updated"));

        reason("EVENT_ID_CONFLICT");
        assertTrue(analytics.isEmpty());
    }

    @Test
    void conflictingSequenceRejected() throws Exception {
        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        1, "document.created"));

        projection();

        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        1, "document.created"));

        reason("SEQUENCE_CONFLICT");
        assertTrue(analytics.isEmpty());
    }

    @Test
    void sequenceGapDoesNotPoisonRetry() throws Exception {
        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        1, "document.created"));

        projection();

        String third = document(UUID.randomUUID(), tenant,
                3, "document.updated");

        documents.pipeInput(key(tenant), third);
        reason("SEQUENCE_GAP_OR_REGRESSION");

        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        2, "document.updated"));

        assertEquals(2, projection().get("sequence").asInt());

        documents.pipeInput(key(tenant), third);

        assertEquals(3, projection().get("sequence").asInt());
        assertTrue(failures.isEmpty());
    }

    @Test
    void tenantIsolation() throws Exception {
        documents.pipeInput(key("tenant-a"),
                document(UUID.randomUUID(), "tenant-a",
                        1, "document.created"));

        documents.pipeInput(key("tenant-b"),
                document(UUID.randomUUID(), "tenant-b",
                        1, "document.created"));

        assertEquals("tenant-a",
                projection().get("tenantId").asText());

        assertEquals("tenant-b",
                projection().get("tenantId").asText());

        assertTrue(failures.isEmpty());
    }

    @Test
    void workflowApprovalLifecycle() throws Exception {
        String[] states = {
                "STARTED", "APPROVAL_REQUIRED",
                "APPROVED", "COMPLETED"
        };

        for (int i = 0; i < states.length; i++) {
            workflows.pipeInput(key(tenant),
                    workflow(UUID.randomUUID(), i + 1, states[i]));

            JsonNode result = projection();

            assertEquals(states[i], result.get("eventType").asText());
            assertEquals(i + 1, result.get("sequence").asInt());
            assertEquals(i == 3, result.get("terminal").asBoolean());
        }

        assertTrue(failures.isEmpty());
    }

    @Test
    void invalidWorkflowTransitionRejected() throws Exception {
        workflows.pipeInput(key(tenant),
                workflow(UUID.randomUUID(), 1, "STARTED"));

        projection();

        workflows.pipeInput(key(tenant),
                workflow(UUID.randomUUID(), 2, "APPROVED"));

        reason("INVALID_WORKFLOW_TRANSITION");
        assertTrue(analytics.isEmpty());
    }

    @Test
    void malformedPayloadRedacted() throws Exception {
        documents.pipeInput(key(tenant), "{secret-invalid-payload");

        String failure = failures.readValue();

        assertFalse(failure.contains("secret-invalid-payload"));
        assertEquals("INVALID_CONTRACT_OR_KEY",
                json.readTree(failure).get("reason").asText());

        assertTrue(analytics.isEmpty());
    }

    @Test
    void incorrectPartitionKeyRejected() throws Exception {
        documents.pipeInput(key("tenant-b"),
                document(UUID.randomUUID(), tenant,
                        1, "document.created"));

        reason("INVALID_CONTRACT_OR_KEY");
        assertTrue(analytics.isEmpty());
    }

    @Test
    void oversizedPayloadRejected() throws Exception {
        documents.pipeInput(key(tenant), "x".repeat(16385));
        reason("PAYLOAD_TOO_LARGE");
        assertTrue(analytics.isEmpty());
    }

    @Test
    void nullPayloadRejected() throws Exception {
        documents.pipeInput(key(tenant), (String) null);
        reason("EMPTY_PAYLOAD");
        assertTrue(analytics.isEmpty());
    }

    @Test
    void initialSequenceMustBeOne() throws Exception {
        workflows.pipeInput(key(tenant),
                workflow(UUID.randomUUID(), 2, "STARTED"));

        reason("INITIAL_SEQUENCE_REQUIRED");
        assertTrue(analytics.isEmpty());
    }
}
