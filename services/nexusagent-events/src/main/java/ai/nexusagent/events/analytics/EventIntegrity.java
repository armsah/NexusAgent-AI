package ai.nexusagent.events.analytics;

import ai.nexusagent.events.contracts.DocumentEvent;
import ai.nexusagent.events.contracts.WorkflowEvent;

import java.util.Collection;
import java.util.Comparator;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.UUID;
import java.util.function.Function;
import java.util.stream.Collectors;

public final class EventIntegrity {
    private EventIntegrity() {}

    private record DocumentRevision(
        String tenantId, UUID documentId, int version) {}

    private record WorkflowSequence(
        String tenantId, UUID workflowId, long sequence) {}

    private record WorkflowKey(
        String tenantId, UUID workflowId) {}

    private static <T, K> T identical(T left, T right) {
        if (!left.equals(right)) {
            throw new IllegalArgumentException(
                "Conflicting event identity or revision");
        }
        return left;
    }

    private static <T, K> Collection<T> unique(
            Collection<T> events, Function<T, K> key) {

        Objects.requireNonNull(events, "Events are required");

        if (events.stream().anyMatch(Objects::isNull)) {
            throw new IllegalArgumentException("Null event");
        }

        return events.stream()
            .collect(Collectors.toUnmodifiableMap(
                key, Function.identity(), EventIntegrity::identical))
            .values();
    }

    public static List<DocumentEvent> documents(
            Collection<DocumentEvent> events) {

        var identities = unique(events, DocumentEvent::eventId);

        return unique(identities, event -> new DocumentRevision(
                event.tenantId(),
                event.documentId(),
                event.documentVersion()))
            .stream()
            .sorted(Comparator
                .comparing(DocumentEvent::tenantId)
                .thenComparing(DocumentEvent::documentId)
                .thenComparingInt(DocumentEvent::documentVersion))
            .toList();
    }

    public static List<WorkflowEvent> workflows(
            Collection<WorkflowEvent> events) {

        var identities = unique(events, WorkflowEvent::eventId);

        return unique(identities, event -> new WorkflowSequence(
                event.tenantId(),
                event.workflowId(),
                event.sequence()))
            .stream()
            .sorted(Comparator
                .comparing(WorkflowEvent::tenantId)
                .thenComparing(WorkflowEvent::workflowId)
                .thenComparingLong(WorkflowEvent::sequence))
            .toList();
    }

    public static List<WorkflowEvent> completeWorkflowHistories(
            Collection<WorkflowEvent> events) {

        var ordered = workflows(events);

        Map<WorkflowKey, List<WorkflowEvent>> histories =
            ordered.stream().collect(Collectors.groupingBy(
                event -> new WorkflowKey(
                    event.tenantId(), event.workflowId())));

        histories.values().forEach(EventIntegrity::validateHistory);

        return ordered;
    }

    private static void validateHistory(List<WorkflowEvent> history) {
        WorkflowEvent previous = null;
        long expectedSequence = 1;

        for (WorkflowEvent current : history) {
            if (current.sequence() != expectedSequence++) {
                throw new IllegalArgumentException(
                    "Incomplete workflow sequence");
            }

            if (previous == null) {
                if (current.eventType() != WorkflowEvent.Type.STARTED) {
                    throw new IllegalArgumentException(
                        "Workflow must begin with STARTED");
                }
            } else {
                if (current.occurredAt().isBefore(previous.occurredAt())) {
                    throw new IllegalArgumentException(
                        "Workflow timestamp regression");
                }

                if (!allowed(previous.eventType(), current.eventType())) {
                    throw new IllegalArgumentException(
                        "Invalid workflow transition");
                }
            }

            previous = current;
        }
    }

    private static boolean allowed(
            WorkflowEvent.Type previous,
            WorkflowEvent.Type next) {

        return switch (previous) {
            case STARTED, APPROVED -> switch (next) {
                case APPROVAL_REQUIRED, COMPLETED, FAILED, CANCELLED -> true;
                default -> false;
            };

            case APPROVAL_REQUIRED -> switch (next) {
                case APPROVED, REJECTED, FAILED, CANCELLED -> true;
                default -> false;
            };

            case COMPLETED, FAILED, REJECTED, CANCELLED -> false;
        };
    }
}