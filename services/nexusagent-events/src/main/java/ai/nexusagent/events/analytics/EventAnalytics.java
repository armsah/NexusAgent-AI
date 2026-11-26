package ai.nexusagent.events.analytics;

import ai.nexusagent.events.contracts.DocumentEvent;
import ai.nexusagent.events.contracts.WorkflowEvent;
import org.springframework.stereotype.Service;

import java.math.BigInteger;
import java.time.Instant;
import java.util.Collection;
import java.util.Comparator;
import java.util.Map;
import java.util.Objects;
import java.util.UUID;
import java.util.function.Function;
import java.util.function.Predicate;
import java.util.stream.Collectors;

@Service
public final class EventAnalytics {

    public record DocumentKey(String tenantId, UUID documentId) {}

    public record WorkflowKey(String tenantId, UUID workflowId) {}

    public record DocumentMetrics(
            long created,
            long updated,
            long deleted,
            long activeDocuments,
            long distinctDocuments) {}

    public record WorkflowMetrics(
            long workflows,
            long startedEvents,
            long approvalRequiredEvents,
            long completed,
            long failed,
            long rejected,
            long cancelled,
            long nonTerminal,
            BigInteger terminalDurationMillis) {

        public long terminalCount() {
            return completed + failed + rejected + cancelled;
        }

        public double averageTerminalDurationMillis() {
            return terminalCount() == 0 ? 0.0 :
                terminalDurationMillis.doubleValue() / terminalCount();
        }

        public double completionRate() {
            return terminalCount() == 0 ? 0.0 :
                (double) completed / terminalCount();
        }
    }

    public Map<String, DocumentMetrics> documents(
            Collection<DocumentEvent> events,
            Instant fromInclusive,
            Instant toExclusive) {

        validateWindow(fromInclusive, toExclusive);

        return events.stream()
            .filter(inWindow(DocumentEvent::occurredAt,
                fromInclusive, toExclusive))
            .collect(Collectors.collectingAndThen(
                Collectors.groupingBy(DocumentEvent::tenantId,
                    Collectors.collectingAndThen(
                        Collectors.toList(), this::summarizeDocuments)),
                Map::copyOf));
    }

    public Map<String, WorkflowMetrics> workflows(
            Collection<WorkflowEvent> events,
            Instant fromInclusive,
            Instant toExclusive) {

        validateWindow(fromInclusive, toExclusive);

        return events.stream()
            .filter(inWindow(WorkflowEvent::occurredAt,
                fromInclusive, toExclusive))
            .collect(Collectors.collectingAndThen(
                Collectors.groupingBy(WorkflowEvent::tenantId,
                    Collectors.collectingAndThen(
                        Collectors.toList(), this::summarizeWorkflows)),
                Map::copyOf));
    }

    private DocumentMetrics summarizeDocuments(
            Collection<DocumentEvent> events) {

        var counts = events.stream()
            .collect(Collectors.groupingBy(
                DocumentEvent::eventType, Collectors.counting()));

        var latest = events.stream().collect(Collectors.toMap(
            event -> new DocumentKey(event.tenantId(), event.documentId()),
            Function.identity(),
            java.util.function.BinaryOperator.maxBy(
                Comparator.comparingInt(DocumentEvent::documentVersion)
                    .thenComparing(DocumentEvent::occurredAt)
                    .thenComparing(DocumentEvent::eventId))));

        long active = latest.values().stream()
            .filter(event -> !event.eventType().equals("document.deleted"))
            .count();

        return new DocumentMetrics(
            counts.getOrDefault("document.created", 0L),
            counts.getOrDefault("document.updated", 0L),
            counts.getOrDefault("document.deleted", 0L),
            active, latest.size());
    }

    private WorkflowMetrics summarizeWorkflows(
            Collection<WorkflowEvent> events) {

        var latest = events.stream().collect(Collectors.toMap(
            event -> new WorkflowKey(event.tenantId(), event.workflowId()),
            Function.identity(),
            java.util.function.BinaryOperator.maxBy(
                Comparator.comparingLong(WorkflowEvent::sequence)
                    .thenComparing(WorkflowEvent::occurredAt)
                    .thenComparing(WorkflowEvent::eventId))));

        var states = latest.values().stream().collect(
            Collectors.groupingBy(
                WorkflowEvent::eventType, Collectors.counting()));

        long completed = states.getOrDefault(
            WorkflowEvent.Type.COMPLETED, 0L);
        long failed = states.getOrDefault(
            WorkflowEvent.Type.FAILED, 0L);
        long rejected = states.getOrDefault(
            WorkflowEvent.Type.REJECTED, 0L);
        long cancelled = states.getOrDefault(
            WorkflowEvent.Type.CANCELLED, 0L);

        BigInteger duration = latest.values().stream()
            .map(WorkflowEvent::durationMillis)
            .filter(Objects::nonNull)
            .map(BigInteger::valueOf)
            .reduce(BigInteger.ZERO, BigInteger::add);

        return new WorkflowMetrics(
            latest.size(),
            events.stream().filter(e ->
                e.eventType() == WorkflowEvent.Type.STARTED).count(),
            events.stream().filter(e ->
                e.eventType() == WorkflowEvent.Type.APPROVAL_REQUIRED).count(),
            completed, failed, rejected, cancelled,
            latest.size() - completed - failed - rejected - cancelled,
            duration);
    }

    private static void validateWindow(Instant from, Instant to) {
        if (from == null || to == null || !from.isBefore(to)) {
            throw new IllegalArgumentException("Invalid analytics window");
        }
    }

    private static <T> Predicate<T> inWindow(
            Function<T, Instant> timestamp, Instant from, Instant to) {

        return event -> !timestamp.apply(event).isBefore(from)
            && timestamp.apply(event).isBefore(to);
    }
}