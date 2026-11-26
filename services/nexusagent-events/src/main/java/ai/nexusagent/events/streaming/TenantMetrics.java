package ai.nexusagent.events.streaming;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;

import java.math.BigInteger;
import java.math.BigDecimal;
import java.math.RoundingMode;

public final class TenantMetrics {

    private static final ObjectMapper JSON = new ObjectMapper();

    private static final String[] COUNTERS = {
            "acceptedEvents",
            "documentEvents",
            "workflowEvents",
            "documentsCreated",
            "documentsUpdated",
            "documentsDeleted",
            "activeDocuments",
            "workflowsStarted",
            "workflowsCompleted",
            "workflowsFailed",
            "workflowsRejected",
            "workflowsCancelled",
            "nonTerminalWorkflows",
            "approvalRequests",
            "approvalsGranted",
            "approvalsRejected",
            "terminalWorkflows"
    };

    private TenantMetrics() {}

    public static ObjectNode empty(String tenant) {
        if (tenant == null || tenant.isBlank()) {
            throw new IllegalArgumentException("Tenant required");
        }

        ObjectNode metrics = JSON.createObjectNode();

        metrics.put("schemaVersion", 1);
        metrics.put("projectionType", "tenant-metrics");
        metrics.put("tenantId", tenant);

        for (String counter : COUNTERS) {
            metrics.put(counter, 0L);
        }

        metrics.put("terminalDurationMillis", BigInteger.ZERO);
        metrics.put("averageTerminalDurationMillis", BigDecimal.ZERO);
        metrics.put("completionRate", BigDecimal.ZERO);

        return metrics;
    }

    public static ObjectNode apply(
            ObjectNode previous,
            JsonNode event) {

        ObjectNode metrics = previous.deepCopy();

        String tenant = event.path("tenantId").asText();

        if (tenant.isBlank()
                || !tenant.equals(metrics.path("tenantId").asText())) {
            throw new IllegalArgumentException("Tenant mismatch");
        }

        String type = event.path("eventType").asText();

        increment(metrics, "acceptedEvents", 1);

        switch (type) {
            case "document.created" -> {
                increment(metrics, "documentEvents", 1);
                increment(metrics, "documentsCreated", 1);
                increment(metrics, "activeDocuments", 1);
            }

            case "document.updated" -> {
                increment(metrics, "documentEvents", 1);
                increment(metrics, "documentsUpdated", 1);
            }

            case "document.deleted" -> {
                increment(metrics, "documentEvents", 1);
                increment(metrics, "documentsDeleted", 1);
                increment(metrics, "activeDocuments", -1);
            }

            case "STARTED" -> {
                increment(metrics, "workflowEvents", 1);
                increment(metrics, "workflowsStarted", 1);
                increment(metrics, "nonTerminalWorkflows", 1);
            }

            case "APPROVAL_REQUIRED" -> {
                increment(metrics, "workflowEvents", 1);
                increment(metrics, "approvalRequests", 1);
            }

            case "APPROVED" -> {
                increment(metrics, "workflowEvents", 1);
                increment(metrics, "approvalsGranted", 1);
            }

            case "COMPLETED" -> {
                increment(metrics, "workflowsCompleted", 1);
                terminal(metrics, event);
            }

            case "FAILED" -> {
                increment(metrics, "workflowsFailed", 1);
                terminal(metrics, event);
            }

            case "REJECTED" -> {
                increment(metrics, "workflowsRejected", 1);
                increment(metrics, "approvalsRejected", 1);
                terminal(metrics, event);
            }

            case "CANCELLED" -> {
                increment(metrics, "workflowsCancelled", 1);
                terminal(metrics, event);
            }

            default -> throw new IllegalArgumentException(
                    "Unknown accepted event type");
        }

        validate(metrics);

        long terminalCount =
                metrics.path("terminalWorkflows").asLong();

        if (terminalCount > 0) {
            BigDecimal denominator =
                    BigDecimal.valueOf(terminalCount);

            metrics.put("averageTerminalDurationMillis",
                    new BigDecimal(
                            metrics.path("terminalDurationMillis")
                                    .bigIntegerValue())
                            .divide(denominator, 6,
                                    RoundingMode.HALF_UP));

            metrics.put("completionRate",
                    BigDecimal.valueOf(
                            metrics.path("workflowsCompleted").asLong())
                            .divide(denominator, 6,
                                    RoundingMode.HALF_UP));
        }

        metrics.put("lastEventId",
                event.path("eventId").asText());

        metrics.put("lastEventOccurredAt",
                event.path("occurredAt").asText());

        return metrics;
    }

    private static void terminal(
            ObjectNode metrics, JsonNode event) {

        JsonNode duration = event.get("durationMillis");

        if (duration == null
                || !duration.isIntegralNumber()
                || !duration.canConvertToLong()
                || duration.asLong() < 0) {
            throw new IllegalArgumentException(
                    "Invalid terminal workflow duration");
        }

        increment(metrics, "workflowEvents", 1);
        increment(metrics, "terminalWorkflows", 1);
        increment(metrics, "nonTerminalWorkflows", -1);

        BigInteger total = metrics.path("terminalDurationMillis")
                .bigIntegerValue()
                .add(duration.bigIntegerValue());

        metrics.put("terminalDurationMillis", total);
    }

    private static void increment(
            ObjectNode metrics, String name, long delta) {

        JsonNode current = metrics.get(name);

        if (current == null
                || !current.isIntegralNumber()
                || !current.canConvertToLong()) {
            throw new IllegalStateException(
                    "Invalid persistent counter: " + name);
        }

        long next = Math.addExact(current.asLong(), delta);

        if (next < 0) {
            throw new IllegalStateException(
                    "Negative persistent counter: " + name);
        }

        metrics.put(name, next);
    }

    private static void validate(ObjectNode metrics) {
        for (String counter : COUNTERS) {
            JsonNode value = metrics.get(counter);

            if (value == null
                    || !value.isIntegralNumber()
                    || !value.canConvertToLong()
                    || value.asLong() < 0) {
                throw new IllegalStateException(
                        "Corrupted counter: " + counter);
            }
        }

        long documentsCreated =
                metrics.path("documentsCreated").asLong();

        long documentsDeleted =
                metrics.path("documentsDeleted").asLong();

        long activeDocuments =
                metrics.path("activeDocuments").asLong();

        if (documentsCreated - documentsDeleted != activeDocuments) {
            throw new IllegalStateException(
                    "Document counter invariant violated");
        }

        long started = metrics.path("workflowsStarted").asLong();
        long terminal = metrics.path("terminalWorkflows").asLong();
        long active = metrics.path("nonTerminalWorkflows").asLong();

        if (started - terminal != active) {
            throw new IllegalStateException(
                    "Workflow counter invariant violated");
        }

        long terminalSum = Math.addExact(
                Math.addExact(
                        metrics.path("workflowsCompleted").asLong(),
                        metrics.path("workflowsFailed").asLong()),
                Math.addExact(
                        metrics.path("workflowsRejected").asLong(),
                        metrics.path("workflowsCancelled").asLong()));

        if (terminalSum != terminal) {
            throw new IllegalStateException(
                    "Terminal outcome invariant violated");
        }

        if (Math.addExact(
                metrics.path("documentEvents").asLong(),
                metrics.path("workflowEvents").asLong())
                != metrics.path("acceptedEvents").asLong()) {

            throw new IllegalStateException(
                    "Accepted event counter invariant violated");
        }

        JsonNode duration = metrics.get("terminalDurationMillis");

        if (duration == null
                || !duration.isIntegralNumber()
                || duration.bigIntegerValue().signum() < 0) {
            throw new IllegalStateException(
                    "Invalid cumulative duration");
        }
    }
}
