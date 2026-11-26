package ai.nexusagent.events;

import ai.nexusagent.events.analytics.EventAnalytics;
import ai.nexusagent.events.analytics.EventIntegrity;
import ai.nexusagent.events.contracts.DocumentEvent;
import ai.nexusagent.events.contracts.WorkflowEvent;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;
import java.util.UUID;

import static ai.nexusagent.events.contracts.WorkflowEvent.Type.*;
import static org.junit.jupiter.api.Assertions.*;

class EventIntegrityTest {
    private static final Instant FROM = Instant.EPOCH;
    private static final Instant TO = FROM.plusSeconds(100);

    private final UUID entity = UUID.randomUUID();
    private final EventAnalytics analytics = new EventAnalytics();

    private DocumentEvent document(
            String tenant, String type, int version) {

        return new DocumentEvent(
            1, UUID.randomUUID(), type, tenant,
            entity, version, 1, FROM.plusSeconds(version));
    }

    private WorkflowEvent workflow(
            String tenant, long sequence,
            WorkflowEvent.Type type, Long duration) {

        return new WorkflowEvent(
            1, UUID.randomUUID(), tenant, entity,
            sequence, type, FROM.plusSeconds(sequence), duration);
    }

    @Test
    void identicalDocumentDeliveryIsDeduplicated() {
        var event = document("a", "document.created", 1);
        assertEquals(List.of(event),
            EventIntegrity.documents(List.of(event, event)));
    }

    @Test
    void duplicateDocumentDoesNotInflateAnalytics() {
        var event = document("a", "document.created", 1);

        var metrics = analytics.documents(
            List.of(event, event, event), FROM, TO).get("a");

        assertEquals(1, metrics.created());
        assertEquals(1, metrics.activeDocuments());
    }

    @Test
    void identicalWorkflowDeliveryIsDeduplicated() {
        var event = workflow("a", 1, STARTED, null);
        assertEquals(List.of(event),
            EventIntegrity.workflows(List.of(event, event)));
    }

    @Test
    void duplicateWorkflowDoesNotInflateAnalytics() {
        var started = workflow("a", 1, STARTED, null);
        var completed = workflow("a", 2, COMPLETED, 500L);

        var metrics = analytics.workflows(
            List.of(started, started, completed, completed),
            FROM, TO).get("a");

        assertEquals(1, metrics.startedEvents());
        assertEquals(1, metrics.completed());
        assertEquals(500.0, metrics.averageTerminalDurationMillis());
    }

    @Test
    void rejectsConflictingDocumentEventIdentity() {
        var original = document("a", "document.created", 1);

        var conflicting = new DocumentEvent(
            1, original.eventId(), "document.updated", "a",
            entity, 2, 1, FROM.plusSeconds(2));

        assertThrows(IllegalArgumentException.class, () ->
            EventIntegrity.documents(List.of(original, conflicting)));
    }

    @Test
    void rejectsConflictingDocumentRevision() {
        assertThrows(IllegalArgumentException.class, () ->
            EventIntegrity.documents(List.of(
                document("a", "document.created", 1),
                document("a", "document.updated", 1))));
    }

    @Test
    void rejectsConflictingWorkflowSequence() {
        assertThrows(IllegalArgumentException.class, () ->
            EventIntegrity.workflows(List.of(
                workflow("a", 2, COMPLETED, 100L),
                workflow("a", 2, FAILED, 100L))));
    }

    @Test
    void rejectsConflictingWorkflowEventIdentity() {
        var original = workflow("a", 1, STARTED, null);

        var conflicting = new WorkflowEvent(
            1, original.eventId(), "b", entity,
            1, STARTED, original.occurredAt(), null);

        assertThrows(IllegalArgumentException.class, () ->
            EventIntegrity.workflows(List.of(original, conflicting)));
    }

    @Test
    void documentRevisionsAreTenantScoped() {
        assertEquals(2, EventIntegrity.documents(List.of(
            document("a", "document.created", 1),
            document("b", "document.created", 1))).size());
    }

    @Test
    void workflowSequencesAreTenantScoped() {
        assertEquals(2, EventIntegrity.workflows(List.of(
            workflow("a", 1, STARTED, null),
            workflow("b", 1, STARTED, null))).size());
    }

    @Test
    void ordersDocumentRevisionsDeterministically() {
        var first = document("a", "document.created", 1);
        var second = document("a", "document.updated", 2);
        var third = document("a", "document.deleted", 3);

        assertEquals(List.of(first, second, third),
            EventIntegrity.documents(List.of(third, first, second)));
    }

    @Test
    void ordersWorkflowSequencesDeterministically() {
        var first = workflow("a", 1, STARTED, null);
        var second = workflow("a", 2, COMPLETED, 100L);

        assertEquals(List.of(first, second),
            EventIntegrity.workflows(List.of(second, first)));
    }

    @Test
    void validApprovalLifecycle() {
        var events = List.of(
            workflow("a", 1, STARTED, null),
            workflow("a", 2, APPROVAL_REQUIRED, null),
            workflow("a", 3, APPROVED, null),
            workflow("a", 4, COMPLETED, 3000L));

        var shuffled = new ArrayList<>(events);
        Collections.reverse(shuffled);

        assertEquals(events,
            EventIntegrity.completeWorkflowHistories(shuffled));
    }

    @Test
    void validRejectionLifecycle() {
        var events = List.of(
            workflow("a", 1, STARTED, null),
            workflow("a", 2, APPROVAL_REQUIRED, null),
            workflow("a", 3, REJECTED, 2000L));

        assertEquals(events,
            EventIntegrity.completeWorkflowHistories(events));
    }

    @Test
    void validFailureLifecycle() {
        var events = List.of(
            workflow("a", 1, STARTED, null),
            workflow("a", 2, FAILED, 1000L));

        assertEquals(events,
            EventIntegrity.completeWorkflowHistories(events));
    }

    @Test
    void rejectsMissingStart() {
        assertThrows(IllegalArgumentException.class, () ->
            EventIntegrity.completeWorkflowHistories(List.of(
                workflow("a", 1, COMPLETED, 100L))));
    }

    @Test
    void rejectsSequenceGap() {
        assertThrows(IllegalArgumentException.class, () ->
            EventIntegrity.completeWorkflowHistories(List.of(
                workflow("a", 1, STARTED, null),
                workflow("a", 3, COMPLETED, 100L))));
    }

    @Test
    void rejectsApprovalWithoutRequest() {
        assertThrows(IllegalArgumentException.class, () ->
            EventIntegrity.completeWorkflowHistories(List.of(
                workflow("a", 1, STARTED, null),
                workflow("a", 2, APPROVED, null))));
    }

    @Test
    void rejectsCompletionWhileApprovalPending() {
        assertThrows(IllegalArgumentException.class, () ->
            EventIntegrity.completeWorkflowHistories(List.of(
                workflow("a", 1, STARTED, null),
                workflow("a", 2, APPROVAL_REQUIRED, null),
                workflow("a", 3, COMPLETED, 100L))));
    }

    @Test
    void rejectsEventsAfterTerminalState() {
        assertThrows(IllegalArgumentException.class, () ->
            EventIntegrity.completeWorkflowHistories(List.of(
                workflow("a", 1, STARTED, null),
                workflow("a", 2, COMPLETED, 100L),
                workflow("a", 3, FAILED, 200L))));
    }

    @Test
    void rejectsTimestampRegression() {
        var first = workflow("a", 1, STARTED, null);

        var second = new WorkflowEvent(
            1, UUID.randomUUID(), "a", entity, 2,
            COMPLETED, FROM, 100L);

        assertThrows(IllegalArgumentException.class, () ->
            EventIntegrity.completeWorkflowHistories(
                List.of(first, second)));
    }

    @Test
    void partialWindowDoesNotRequireCompleteHistory() {
        var completed = workflow("a", 5, COMPLETED, 500L);

        assertEquals(1, analytics.workflows(
            List.of(completed), FROM, TO).get("a").completed());
    }

    @Test
    void validatesConflictsBeforeWindowFiltering() {
        var first = document("a", "document.created", 1);

        var conflicting = new DocumentEvent(
            1, first.eventId(), "document.updated", "a",
            entity, 2, 1, TO.plusSeconds(1));

        assertThrows(IllegalArgumentException.class, () ->
            analytics.documents(
                List.of(first, conflicting), FROM, TO));
    }

    @Test
    void normalizedEventsAreImmutable() {
        var events = EventIntegrity.documents(List.of(
            document("a", "document.created", 1)));

        assertThrows(UnsupportedOperationException.class, events::clear);
    }

    @Test
    void rejectsNullEvent() {
        assertThrows(IllegalArgumentException.class, () ->
            EventIntegrity.documents(Arrays.asList(
                document("a", "document.created", 1), null)));
    }

    @Test
    void completeHistoriesAreValidatedSeparatelyByTenant() {
        var events = List.of(
            workflow("a", 1, STARTED, null),
            workflow("a", 2, COMPLETED, 100L),
            workflow("b", 1, STARTED, null),
            workflow("b", 2, CANCELLED, 100L));

        assertEquals(4,
            EventIntegrity.completeWorkflowHistories(events).size());
    }
}