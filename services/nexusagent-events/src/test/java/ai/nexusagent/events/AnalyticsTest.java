package ai.nexusagent.events;

import ai.nexusagent.events.analytics.EventAnalytics;
import ai.nexusagent.events.contracts.*;
import org.junit.jupiter.api.Test;

import java.math.BigInteger;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.*;

class AnalyticsTest {
    private static final Instant FROM = Instant.EPOCH;
    private static final Instant TO = FROM.plusSeconds(100);
    private final EventAnalytics analytics = new EventAnalytics();

    private DocumentEvent document(
            String tenant, UUID id, String type, int version, Instant time) {
        return new DocumentEvent(
            1, UUID.randomUUID(), type, tenant, id, version, 1, time);
    }

    private WorkflowEvent workflow(
            String tenant, UUID id, long sequence,
            WorkflowEvent.Type type, Long duration) {
        return new WorkflowEvent(
            1, UUID.randomUUID(), tenant, id, sequence,
            type, FROM.plusSeconds(sequence), duration);
    }

    @Test
    void emptyDocuments() {
        assertTrue(analytics.documents(List.of(), FROM, TO).isEmpty());
    }

    @Test
    void emptyWorkflows() {
        assertTrue(analytics.workflows(List.of(), FROM, TO).isEmpty());
    }

    @Test
    void documentLifecycleAggregation() {
        UUID id = UUID.randomUUID();
        var events = List.of(
            document("a", id, "document.created", 1, FROM),
            document("a", id, "document.updated", 2, FROM.plusSeconds(1)),
            document("a", id, "document.deleted", 3, FROM.plusSeconds(2)));

        var result = analytics.documents(events, FROM, TO).get("a");

        assertEquals(1, result.created());
        assertEquals(1, result.updated());
        assertEquals(1, result.deleted());
        assertEquals(0, result.activeDocuments());
        assertEquals(1, result.distinctDocuments());
    }

    @Test
    void documentTenantsRemainSeparate() {
        UUID id = UUID.randomUUID();
        var events = List.of(
            document("a", id, "document.created", 1, FROM),
            document("b", id, "document.created", 1, FROM));

        var result = analytics.documents(events, FROM, TO);
        assertEquals(2, result.size());
        assertEquals(1, result.get("a").activeDocuments());
        assertEquals(1, result.get("b").activeDocuments());
    }

    @Test
    void usesHalfOpenTimeWindow() {
        var events = List.of(
            document("a", UUID.randomUUID(), "document.created", 1,
                FROM.minusSeconds(1)),
            document("a", UUID.randomUUID(), "document.created", 1, FROM),
            document("a", UUID.randomUUID(), "document.created", 1, TO));

        assertEquals(1, analytics.documents(
            events, FROM, TO).get("a").created());
    }

    @Test
    void rejectsInvalidWindow() {
        assertThrows(IllegalArgumentException.class, () ->
            analytics.documents(List.of(), TO, FROM));
        assertThrows(IllegalArgumentException.class, () ->
            analytics.workflows(List.of(), FROM, FROM));
    }

    @Test
    void resultsAreImmutable() {
        var result = analytics.documents(
            List.of(document("a", UUID.randomUUID(),
                "document.created", 1, FROM)), FROM, TO);

        assertThrows(UnsupportedOperationException.class, result::clear);
    }

    @Test
    void workflowCompletionAndFailureMetrics() {
        UUID first = UUID.randomUUID();
        UUID second = UUID.randomUUID();

        var events = List.of(
            workflow("a", first, 1, WorkflowEvent.Type.STARTED, null),
            workflow("a", first, 2, WorkflowEvent.Type.COMPLETED, 100L),
            workflow("a", second, 1, WorkflowEvent.Type.STARTED, null),
            workflow("a", second, 2, WorkflowEvent.Type.FAILED, 300L));

        var result = analytics.workflows(events, FROM, TO).get("a");

        assertEquals(2, result.workflows());
        assertEquals(2, result.startedEvents());
        assertEquals(1, result.completed());
        assertEquals(1, result.failed());
        assertEquals(2, result.terminalCount());
        assertEquals(200.0, result.averageTerminalDurationMillis());
        assertEquals(0.5, result.completionRate());
    }

    @Test
    void approvalAndNonTerminalMetrics() {
        UUID id = UUID.randomUUID();

        var result = analytics.workflows(List.of(
            workflow("a", id, 1, WorkflowEvent.Type.STARTED, null),
            workflow("a", id, 2,
                WorkflowEvent.Type.APPROVAL_REQUIRED, null)
        ), FROM, TO).get("a");

        assertEquals(1, result.approvalRequiredEvents());
        assertEquals(1, result.nonTerminal());
        assertEquals(0, result.terminalCount());
        assertEquals(0.0, result.completionRate());
        assertEquals(0.0, result.averageTerminalDurationMillis());
    }

    @Test
    void rejectionAndCancellationMetrics() {
        var result = analytics.workflows(List.of(
            workflow("a", UUID.randomUUID(), 3,
                WorkflowEvent.Type.REJECTED, 10L),
            workflow("a", UUID.randomUUID(), 2,
                WorkflowEvent.Type.CANCELLED, 20L)
        ), FROM, TO).get("a");

        assertEquals(1, result.rejected());
        assertEquals(1, result.cancelled());
        assertEquals(2, result.terminalCount());
        assertEquals(BigInteger.valueOf(30),
            result.terminalDurationMillis());
    }

    @Test
    void durationAggregationDoesNotOverflowLong() {
        var result = analytics.workflows(List.of(
            workflow("a", UUID.randomUUID(), 2,
                WorkflowEvent.Type.COMPLETED, Long.MAX_VALUE),
            workflow("a", UUID.randomUUID(), 2,
                WorkflowEvent.Type.COMPLETED, Long.MAX_VALUE)
        ), FROM, TO).get("a");

        assertEquals(BigInteger.valueOf(Long.MAX_VALUE)
            .multiply(BigInteger.TWO), result.terminalDurationMillis());
    }

    @Test
    void workflowTenantsRemainSeparate() {
        UUID id = UUID.randomUUID();

        var result = analytics.workflows(List.of(
            workflow("a", id, 1, WorkflowEvent.Type.STARTED, null),
            workflow("b", id, 2, WorkflowEvent.Type.COMPLETED, 100L)
        ), FROM, TO);

        assertEquals(1, result.get("a").nonTerminal());
        assertEquals(1, result.get("b").completed());
    }

    @Test
    void inputOrderDoesNotChangeAggregation() {
        UUID id = UUID.randomUUID();

        var events = new ArrayList<>(List.of(
            workflow("a", id, 1, WorkflowEvent.Type.STARTED, null),
            workflow("a", id, 2, WorkflowEvent.Type.COMPLETED, 100L)));

        var expected = analytics.workflows(events, FROM, TO);
        Collections.reverse(events);

        assertEquals(expected, analytics.workflows(events, FROM, TO));
    }

    @Test
    void repeatedAggregationDoesNotMutateInput() {
        var events = new ArrayList<>(List.of(
            workflow("a", UUID.randomUUID(), 1,
                WorkflowEvent.Type.STARTED, null)));

        var before = List.copyOf(events);

        assertEquals(
            analytics.workflows(events, FROM, TO),
            analytics.workflows(events, FROM, TO));

        assertEquals(before, events);
    }
}