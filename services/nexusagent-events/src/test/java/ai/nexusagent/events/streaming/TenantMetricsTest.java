package ai.nexusagent.events.streaming;

import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;

import org.junit.jupiter.api.Test;

import java.math.BigInteger;
import java.util.UUID;

class TenantMetricsTest {

    private final ObjectMapper json = new ObjectMapper();

    private ObjectNode event(String tenant, String type, Long duration) {
        ObjectNode event = json.createObjectNode();

        event.put("tenantId", tenant);
        event.put("eventId", UUID.randomUUID().toString());
        event.put("aggregateId", UUID.randomUUID().toString());
        event.put("eventType", type);
        event.put("occurredAt", "2026-01-01T00:00:00Z");

        if (duration != null) {
            event.put("durationMillis", duration);
        }

        return event;
    }

    private ObjectNode apply(ObjectNode metrics, String type) {
        return TenantMetrics.apply(metrics,
                event(metrics.path("tenantId").asText(), type, null));
    }

    private ObjectNode terminal(
            ObjectNode metrics, String type, long duration) {

        return TenantMetrics.apply(metrics,
                event(metrics.path("tenantId").asText(), type, duration));
    }

    @Test
    void documentLifecycleCounters() {
        ObjectNode metrics = TenantMetrics.empty("tenant-a");

        metrics = apply(metrics, "document.created");
        metrics = apply(metrics, "document.created");
        metrics = apply(metrics, "document.updated");
        metrics = apply(metrics, "document.deleted");

        assertEquals(2, metrics.path("documentsCreated").asLong());
        assertEquals(1, metrics.path("documentsUpdated").asLong());
        assertEquals(1, metrics.path("documentsDeleted").asLong());
        assertEquals(1, metrics.path("activeDocuments").asLong());
        assertEquals(4, metrics.path("documentEvents").asLong());
        assertEquals(4, metrics.path("acceptedEvents").asLong());
    }

    @Test
    void workflowApprovalAndCompletionCounters() {
        ObjectNode metrics = TenantMetrics.empty("tenant-a");

        metrics = apply(metrics, "STARTED");
        metrics = apply(metrics, "APPROVAL_REQUIRED");
        metrics = apply(metrics, "APPROVED");
        metrics = terminal(metrics, "COMPLETED", 500);

        assertEquals(1, metrics.path("workflowsStarted").asLong());
        assertEquals(1, metrics.path("approvalRequests").asLong());
        assertEquals(1, metrics.path("approvalsGranted").asLong());
        assertEquals(1, metrics.path("workflowsCompleted").asLong());
        assertEquals(0, metrics.path("nonTerminalWorkflows").asLong());
        assertEquals(4, metrics.path("workflowEvents").asLong());
        assertEquals(1.0, metrics.path("completionRate").asDouble());
    }

    @Test
    void allTerminalOutcomesAndAverageDuration() {
        ObjectNode metrics = TenantMetrics.empty("tenant-a");

        String[] outcomes = {
                "COMPLETED", "FAILED", "REJECTED", "CANCELLED"
        };

        for (int i = 0; i < outcomes.length; i++) {
            metrics = apply(metrics, "STARTED");

            if (outcomes[i].equals("REJECTED")) {
                metrics = apply(metrics, "APPROVAL_REQUIRED");
            }

            metrics = terminal(metrics, outcomes[i], (i + 1) * 100L);
        }

        assertEquals(4, metrics.path("terminalWorkflows").asLong());
        assertEquals(1, metrics.path("workflowsCompleted").asLong());
        assertEquals(1, metrics.path("workflowsFailed").asLong());
        assertEquals(1, metrics.path("workflowsRejected").asLong());
        assertEquals(1, metrics.path("workflowsCancelled").asLong());
        assertEquals(1, metrics.path("approvalsRejected").asLong());
        assertEquals(0, metrics.path("nonTerminalWorkflows").asLong());

        assertEquals(1000,
                metrics.path("terminalDurationMillis").asLong());

        assertEquals(250.0,
                metrics.path("averageTerminalDurationMillis").asDouble());

        assertEquals(0.25,
                metrics.path("completionRate").asDouble());
    }

    @Test
    void durationAccumulationDoesNotOverflowLong() {
        ObjectNode metrics = TenantMetrics.empty("tenant-a");

        for (int i = 0; i < 2; i++) {
            metrics = apply(metrics, "STARTED");
            metrics = terminal(metrics, "COMPLETED", Long.MAX_VALUE);
        }

        assertEquals(
                BigInteger.valueOf(Long.MAX_VALUE)
                        .multiply(BigInteger.TWO),
                metrics.path("terminalDurationMillis").bigIntegerValue());
    }

    @Test
    void tenantMismatchRejectedWithoutMutation() {
        ObjectNode metrics = TenantMetrics.empty("tenant-a");

        assertThrows(IllegalArgumentException.class,
                () -> TenantMetrics.apply(metrics,
                        event("tenant-b", "STARTED", null)));

        assertEquals(0, metrics.path("acceptedEvents").asLong());
    }

    @Test
    void invalidTerminalDurationRejectedWithoutMutation() {
        ObjectNode metrics = apply(
                TenantMetrics.empty("tenant-a"), "STARTED");

        assertThrows(IllegalArgumentException.class,
                () -> terminal(metrics, "COMPLETED", -1));

        assertThrows(IllegalArgumentException.class,
                () -> apply(metrics, "COMPLETED"));

        assertEquals(1, metrics.path("nonTerminalWorkflows").asLong());
        assertEquals(0, metrics.path("terminalWorkflows").asLong());
    }

    @Test
    void counterUnderflowRejected() {
        ObjectNode metrics = TenantMetrics.empty("tenant-a");

        assertThrows(IllegalStateException.class,
                () -> apply(metrics, "document.deleted"));

        assertThrows(IllegalStateException.class,
                () -> terminal(metrics, "FAILED", 100));

        assertEquals(0, metrics.path("acceptedEvents").asLong());
    }

    @Test
    void unknownEventTypeRejected() {
        ObjectNode metrics = TenantMetrics.empty("tenant-a");

        assertThrows(IllegalArgumentException.class,
                () -> apply(metrics, "UNKNOWN"));

        assertEquals(0, metrics.path("acceptedEvents").asLong());
    }

    @Test
    void emptyMetricsHaveDefinedRates() {
        ObjectNode metrics = TenantMetrics.empty("tenant-a");

        assertEquals(0.0, metrics.path("completionRate").asDouble());
        assertEquals(0.0,
                metrics.path("averageTerminalDurationMillis").asDouble());
    }

    @Test
    void previousSnapshotRemainsImmutable() {
        ObjectNode original = TenantMetrics.empty("tenant-a");
        ObjectNode next = apply(original, "document.created");

        assertEquals(0, original.path("acceptedEvents").asLong());
        assertEquals(1, next.path("acceptedEvents").asLong());
    }

    @Test
    void mixedDocumentAndWorkflowMetrics() {
        ObjectNode metrics = TenantMetrics.empty("tenant-a");

        metrics = apply(metrics, "document.created");
        metrics = apply(metrics, "STARTED");
        metrics = apply(metrics, "document.updated");
        metrics = terminal(metrics, "COMPLETED", 200);

        assertEquals(4, metrics.path("acceptedEvents").asLong());
        assertEquals(2, metrics.path("documentEvents").asLong());
        assertEquals(2, metrics.path("workflowEvents").asLong());
        assertEquals(1, metrics.path("activeDocuments").asLong());
        assertEquals(1, metrics.path("workflowsCompleted").asLong());
    }

    @Test
    void repeatedApprovalCyclesAreCounted() {
        ObjectNode metrics = TenantMetrics.empty("tenant-a");

        metrics = apply(metrics, "STARTED");

        for (int i = 0; i < 3; i++) {
            metrics = apply(metrics, "APPROVAL_REQUIRED");
            metrics = apply(metrics, "APPROVED");
        }

        metrics = terminal(metrics, "COMPLETED", 900);

        assertEquals(3, metrics.path("approvalRequests").asLong());
        assertEquals(3, metrics.path("approvalsGranted").asLong());
        assertEquals(8, metrics.path("acceptedEvents").asLong());
    }
}
