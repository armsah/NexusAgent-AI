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

        reason("GLOBAL_EVENT_ID_CONFLICT");
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

        assertTrue(analytics.isEmpty());
        assertTrue(failures.isEmpty());

        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        2, "document.updated"));

        assertEquals(2, projection().get("sequence").asInt());
        assertEquals(3, projection().get("sequence").asInt());

        documents.pipeInput(key(tenant), third);

        assertTrue(analytics.isEmpty());
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

        assertTrue(failures.isEmpty());
        assertTrue(analytics.isEmpty());
    }

    @Test
    void globalIdentityRejectsCrossTenantConflict() throws Exception {
        UUID id = UUID.randomUUID();

        documents.pipeInput(key("tenant-a"),
                document(id, "tenant-a", 1, "document.created"));

        assertEquals("tenant-a",
                projection().get("tenantId").asText());

        documents.pipeInput(key("tenant-b"),
                document(id, "tenant-b", 1, "document.created"));

        reason("GLOBAL_EVENT_ID_CONFLICT");
        assertTrue(analytics.isEmpty());

        documents.pipeInput(key("tenant-b"),
                document(UUID.randomUUID(), "tenant-b",
                        1, "document.created"));

        assertEquals("tenant-b",
                projection().get("tenantId").asText());

        assertTrue(failures.isEmpty());
    }

    @Test
    void globalIdentityRejectsCrossAggregateConflict() throws Exception {
        UUID id = UUID.randomUUID();

        documents.pipeInput(key(tenant),
                document(id, tenant, 1, "document.created"));

        projection();

        UUID other = UUID.randomUUID();

        String payload = document(id, tenant, 1, "document.created")
                .replace(aggregate.toString(), other.toString());

        documents.pipeInput(
                StreamIdentity.aggregateKey(tenant, other),
                payload);

        reason("GLOBAL_EVENT_ID_CONFLICT");
        assertTrue(analytics.isEmpty());
    }

    @Test
    void globalIdentityRejectsCrossFamilyConflict() throws Exception {
        UUID id = UUID.randomUUID();

        documents.pipeInput(key(tenant),
                document(id, tenant, 1, "document.created"));

        projection();

        workflows.pipeInput(key(tenant),
                workflow(id, 1, "STARTED"));

        reason("GLOBAL_EVENT_ID_CONFLICT");
        assertTrue(analytics.isEmpty());
    }

    @Test
    void malformedRecordCannotReserveGlobalIdentity() throws Exception {
        UUID id = UUID.randomUUID();

        String valid = document(id, tenant, 1, "document.created");

        documents.pipeInput(key("wrong-tenant"), valid);

        reason("INVALID_CONTRACT_OR_KEY");

        documents.pipeInput(key(tenant), valid);

        assertEquals(1, projection().get("sequence").asInt());
        assertTrue(failures.isEmpty());
    }

    @Test
    void topologyContainsBothRepartitionBoundaries() {
        String description = EventStreamTopology.build()
                .describe().toString();

        assertTrue(description.contains(
                "event-identity-v1-repartition"));

        assertTrue(description.contains(
                "aggregate-order-v1-repartition"));

        assertTrue(description.contains(
                GlobalIdentityProcessor.STORE));
    }

    @Test
    void identityConflictPreservesOriginalSourceCoordinates()
            throws Exception {

        UUID id = UUID.randomUUID();

        documents.pipeInput(key(tenant),
                document(id, tenant, 1, "document.created"));

        projection();

        workflows.pipeInput(key(tenant),
                workflow(id, 1, "STARTED"));

        JsonNode failure = json.readTree(failures.readValue());

        assertEquals("GLOBAL_EVENT_ID_CONFLICT",
                failure.path("reason").asText());

        assertEquals(StreamSettings.WORKFLOWS,
                failure.path("sourceTopic").asText());

        assertTrue(failure.path("sourcePartition").asInt() >= 0);
        assertTrue(failure.path("sourceOffset").asLong() >= 0);
        assertFalse(failure.has("payload"));
        assertFalse(failure.has("tenantId"));
    }

    @Test
    void duplicateAfterRepartitionProducesOnlyOneProjection()
            throws Exception {

        String payload = document(
                UUID.randomUUID(), tenant, 1, "document.created");

        for (int i = 0; i < 5; i++) {
            documents.pipeInput(key(tenant), payload);
        }

        assertEquals(1, projection().get("acceptedEvents").asInt());
        assertTrue(analytics.isEmpty());
        assertTrue(failures.isEmpty());
    }

    @Test
    void reversedDocumentDeliveryDrainsInSequence() throws Exception {
        String first = document(UUID.randomUUID(), tenant,
                1, "document.created");

        String second = document(UUID.randomUUID(), tenant,
                2, "document.updated");

        String third = document(UUID.randomUUID(), tenant,
                3, "document.updated");

        documents.pipeInput(key(tenant), third);
        documents.pipeInput(key(tenant), second);
        documents.pipeInput(key(tenant), third);

        assertTrue(analytics.isEmpty());
        assertTrue(failures.isEmpty());

        documents.pipeInput(key(tenant), first);

        for (int i = 1; i <= 3; i++) {
            assertEquals(i, projection().path("sequence").asInt());
        }

        assertTrue(analytics.isEmpty());
        assertTrue(failures.isEmpty());
    }

    @Test
    void reversedWorkflowDeliveryPreservesApprovalLifecycle()
            throws Exception {

        String[] types = {
                "STARTED", "APPROVAL_REQUIRED",
                "APPROVED", "COMPLETED"
        };

        for (int i = 3; i >= 0; i--) {
            workflows.pipeInput(key(tenant),
                    workflow(UUID.randomUUID(), i + 1, types[i]));
        }

        for (int i = 0; i < types.length; i++) {
            JsonNode output = projection();

            assertEquals(i + 1, output.path("sequence").asInt());
            assertEquals(types[i], output.path("eventType").asText());
        }

        assertTrue(analytics.isEmpty());
        assertTrue(failures.isEmpty());
    }

    @Test
    void excessiveSequenceGapRejected() throws Exception {
        workflows.pipeInput(key(tenant),
                workflow(UUID.randomUUID(), 1026, "STARTED"));

        reason("SEQUENCE_GAP_LIMIT_EXCEEDED");
        assertTrue(analytics.isEmpty());
    }

    @Test
    void conflictingBufferedSequenceRejected() throws Exception {
        String second = document(UUID.randomUUID(), tenant,
                2, "document.updated");

        documents.pipeInput(key(tenant), second);

        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        2, "document.updated"));

        reason("BUFFERED_SEQUENCE_CONFLICT");

        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        1, "document.created"));

        assertEquals(1, projection().path("sequence").asInt());
        assertEquals(2, projection().path("sequence").asInt());
        assertTrue(failures.isEmpty());
    }

    @Test
    void bufferedInvalidTransitionCannotAdvanceSequence()
            throws Exception {

        workflows.pipeInput(key(tenant),
                workflow(UUID.randomUUID(), 2, "APPROVED"));

        workflows.pipeInput(key(tenant),
                workflow(UUID.randomUUID(), 1, "STARTED"));

        assertEquals(1, projection().path("sequence").asInt());
        reason("INVALID_WORKFLOW_TRANSITION");
        assertTrue(analytics.isEmpty());

        workflows.pipeInput(key(tenant),
                workflow(UUID.randomUUID(), 2, "APPROVAL_REQUIRED"));

        assertEquals(2, projection().path("sequence").asInt());
        assertTrue(failures.isEmpty());
    }

    @Test
    void pendingSequencesRemainTenantIsolated() throws Exception {
        documents.pipeInput(key("tenant-a"),
                document(UUID.randomUUID(), "tenant-a",
                        2, "document.updated"));

        documents.pipeInput(key("tenant-b"),
                document(UUID.randomUUID(), "tenant-b",
                        1, "document.created"));

        assertEquals("tenant-b",
                projection().path("tenantId").asText());

        assertTrue(analytics.isEmpty());

        documents.pipeInput(key("tenant-a"),
                document(UUID.randomUUID(), "tenant-a",
                        1, "document.created"));

        assertEquals(1, projection().path("sequence").asInt());

        JsonNode successor = projection();

        assertEquals("tenant-a", successor.path("tenantId").asText());
        assertEquals(2, successor.path("sequence").asInt());

        assertTrue(failures.isEmpty());
    }

    @Test
    void terminalWorkflowCannotBeReopened() throws Exception {
        workflows.pipeInput(key(tenant),
                workflow(UUID.randomUUID(), 1, "STARTED"));

        projection();

        workflows.pipeInput(key(tenant),
                workflow(UUID.randomUUID(), 2, "COMPLETED"));

        assertTrue(projection().path("terminal").asBoolean());

        workflows.pipeInput(key(tenant),
                workflow(UUID.randomUUID(), 3, "APPROVAL_REQUIRED"));

        reason("INVALID_WORKFLOW_TRANSITION");
        assertTrue(analytics.isEmpty());
    }

    @Test
    void deletedDocumentCannotBeUpdated() throws Exception {
        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        1, "document.created"));

        projection();

        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        2, "document.deleted"));

        assertFalse(projection().path("active").asBoolean());

        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        3, "document.updated"));

        reason("INVALID_DOCUMENT_TRANSITION");
        assertTrue(analytics.isEmpty());
    }

    private org.apache.kafka.streams.TestOutputTopic<String, String>
            tenantOutput() {

        return driver.createOutputTopic(
                StreamSettings.TENANT_ANALYTICS,
                org.apache.kafka.common.serialization.Serdes.String()
                        .deserializer(),
                org.apache.kafka.common.serialization.Serdes.String()
                        .deserializer());
    }

    @Test
    void tenantMetricsAggregateMultipleDocuments() throws Exception {
        var output = tenantOutput();

        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        1, "document.created"));

        UUID other = UUID.randomUUID();

        String second = document(UUID.randomUUID(), tenant,
                1, "document.created")
                .replace(aggregate.toString(), other.toString());

        documents.pipeInput(
                StreamIdentity.aggregateKey(tenant, other), second);

        var snapshots = output.readKeyValuesToList();

        assertEquals(2, snapshots.size());
        assertEquals(tenant, snapshots.get(1).key);

        JsonNode metrics = json.readTree(snapshots.get(1).value);

        assertEquals(2, metrics.path("documentsCreated").asLong());
        assertEquals(2, metrics.path("activeDocuments").asLong());
        assertEquals(2, metrics.path("acceptedEvents").asLong());
    }

    @Test
    void tenantMetricsExcludeDuplicatesAndRejectedRecords()
            throws Exception {

        var output = tenantOutput();

        String original = document(UUID.randomUUID(), tenant,
                1, "document.created");

        documents.pipeInput(key(tenant), original);
        documents.pipeInput(key(tenant), original);
        documents.pipeInput(key(tenant), original);
        documents.pipeInput(key(tenant), "invalid-json");

        var snapshots = output.readValuesToList();

        assertEquals(1, snapshots.size());

        JsonNode metrics = json.readTree(snapshots.getFirst());

        assertEquals(1, metrics.path("acceptedEvents").asLong());
        assertEquals(1, metrics.path("activeDocuments").asLong());
    }

    @Test
    void tenantMetricsRemainIsolated() throws Exception {
        var output = tenantOutput();

        documents.pipeInput(key("tenant-a"),
                document(UUID.randomUUID(), "tenant-a",
                        1, "document.created"));

        documents.pipeInput(key("tenant-b"),
                document(UUID.randomUUID(), "tenant-b",
                        1, "document.created"));

        documents.pipeInput(key("tenant-a"),
                document(UUID.randomUUID(), "tenant-a",
                        2, "document.updated"));

        var snapshots = output.readKeyValuesToList();

        assertEquals(3, snapshots.size());

        assertEquals("tenant-a", snapshots.get(0).key);
        assertEquals("tenant-b", snapshots.get(1).key);
        assertEquals("tenant-a", snapshots.get(2).key);

        assertEquals(1, json.readTree(snapshots.get(1).value)
                .path("acceptedEvents").asLong());

        assertEquals(2, json.readTree(snapshots.get(2).value)
                .path("acceptedEvents").asLong());
    }

    @Test
    void tenantMetricsCombineDocumentAndWorkflowEvents()
            throws Exception {

        var output = tenantOutput();

        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        1, "document.created"));

        workflows.pipeInput(key(tenant),
                workflow(UUID.randomUUID(), 1, "STARTED"));

        workflows.pipeInput(key(tenant),
                workflow(UUID.randomUUID(), 2, "COMPLETED"));

        var snapshots = output.readValuesToList();

        assertEquals(3, snapshots.size());

        JsonNode metrics = json.readTree(snapshots.getLast());

        assertEquals(3, metrics.path("acceptedEvents").asLong());
        assertEquals(1, metrics.path("activeDocuments").asLong());
        assertEquals(1, metrics.path("workflowsCompleted").asLong());
        assertEquals(0, metrics.path("nonTerminalWorkflows").asLong());
        assertEquals(1.0, metrics.path("completionRate").asDouble());
    }

    @Test
    void tenantMetricsFollowBufferedAggregateOrder()
            throws Exception {

        var output = tenantOutput();

        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        3, "document.deleted"));

        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        2, "document.updated"));

        assertTrue(output.isEmpty());

        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        1, "document.created"));

        var snapshots = output.readValuesToList();

        assertEquals(3, snapshots.size());

        JsonNode metrics = json.readTree(snapshots.getLast());

        assertEquals(1, metrics.path("documentsCreated").asLong());
        assertEquals(1, metrics.path("documentsUpdated").asLong());
        assertEquals(1, metrics.path("documentsDeleted").asLong());
        assertEquals(0, metrics.path("activeDocuments").asLong());
        assertEquals(3, metrics.path("acceptedEvents").asLong());
    }

    @Test
    void tenantMetricsPersistInStateStore() throws Exception {
        documents.pipeInput(key(tenant),
                document(UUID.randomUUID(), tenant,
                        1, "document.created"));

        var store = driver.<String, String>getKeyValueStore(
                TenantAnalyticsProcessor.METRICS_STORE);

        JsonNode persisted = json.readTree(store.get(tenant));

        assertEquals(1, persisted.path("activeDocuments").asLong());
        assertEquals(tenant, persisted.path("tenantId").asText());
    }

    @Test
    void tenantTopologyContainsRepartitionAndPersistentStores() {
        String topology = EventStreamTopology.build()
                .describe().toString();

        assertTrue(topology.contains("tenant-analytics-v1-repartition"));
        assertTrue(topology.contains(
                TenantAnalyticsProcessor.METRICS_STORE));
        assertTrue(topology.contains(
                TenantAnalyticsProcessor.IDENTITIES_STORE));
        assertTrue(topology.contains(StreamSettings.TENANT_ANALYTICS));
    }
}
