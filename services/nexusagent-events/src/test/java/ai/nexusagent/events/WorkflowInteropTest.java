package ai.nexusagent.events;

import ai.nexusagent.events.contracts.EventCodec;
import ai.nexusagent.events.contracts.WorkflowEvent;
import ai.nexusagent.events.analytics.EventIntegrity;
import org.junit.jupiter.api.Test;

import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

class WorkflowInteropTest {
    private final EventCodec codec = new EventCodec();

    private WorkflowEvent read(String type) throws Exception {
        try (var stream = getClass().getResourceAsStream(
                "/contracts/workflow." + type + ".json")) {
            assertNotNull(stream);
            var event = codec.readWorkflow(
                new String(stream.readAllBytes(), StandardCharsets.UTF_8));
            assertEquals(type, event.eventType().name());
            assertEquals("tenant-e6", event.tenantId());
            assertEquals(1, event.schemaVersion());
            return event;
        }
    }

    @Test
    void approvedWorkflow() throws Exception {
        var start = read("STARTED");
        var pending = read("APPROVAL_REQUIRED");
        var approved = read("APPROVED");
        var completed = read("COMPLETED");

        assertEquals(Instant.parse("2026-01-01T10:00:01Z"), start.occurredAt());
        assertEquals(3000L, completed.durationMillis().longValue());

        EventIntegrity.completeWorkflowHistories(
            List.of(completed, pending, start, approved, start));

        assertEquals(4, EventIntegrity.workflows(
            List.of(completed, pending, start, approved, start)).size());
    }

    @Test
    void rejectedWorkflow() throws Exception {
        EventIntegrity.completeWorkflowHistories(
            List.of(read("STARTED"), read("APPROVAL_REQUIRED"), read("REJECTED")));
    }

    @Test
    void failedWorkflow() throws Exception {
        EventIntegrity.completeWorkflowHistories(
            List.of(read("STARTED"), read("FAILED")));
    }

    @Test
    void cancelledWorkflow() throws Exception {
        EventIntegrity.completeWorkflowHistories(
            List.of(read("STARTED"), read("CANCELLED")));
    }

    @Test
    void rejectsApprovalBypass() throws Exception {
        assertThrows(IllegalArgumentException.class, () ->
            EventIntegrity.completeWorkflowHistories(
                List.of(read("STARTED"), read("APPROVAL_REQUIRED"), read("COMPLETED"))));
    }
}