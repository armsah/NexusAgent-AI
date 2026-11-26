package ai.nexusagent.events.streaming;

import ai.nexusagent.events.contracts.EventCodec;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashSet;
import java.util.UUID;

public final class AuthoritativeMigrationInventory {

    private static final ObjectMapper JSON = new ObjectMapper();
    private static final EventCodec CODEC = new EventCodec();

    private AuthoritativeMigrationInventory() {}

    public static HistoricalCoverage validate(JsonNode inventory) {

        long total = count(inventory, "totalPublications");
        long nonStream = count(inventory, "nonStreamPublications");
        long unresolved = count(inventory, "unresolvedEvents");

        JsonNode events = inventory.get("events");

        if (events == null || !events.isArray()) {
            throw new IllegalArgumentException(
                    "Missing authoritative event inventory");
        }

        var identities = new HashSet<UUID>();
        long verified = 0;
        long conflicting = 0;

        for (JsonNode row : events) {

            try {
                UUID id = UUID.fromString(
                        row.required("eventId").asText());

                UUID aggregate = UUID.fromString(
                        row.required("aggregateId").asText());

                String tenant = row.required("tenantId").asText();
                String family = row.required("family").asText();

                long sequence = count(row, "sequence");

                JsonNode payload = row.required("payload");

                String canonical;

                if ("document".equals(family)) {

                    var event = CODEC.readDocument(
                            payload.toString());

                    canonical = CODEC.writeDocument(event);

                } else if ("workflow".equals(family)) {

                    var event = CODEC.readWorkflow(
                            payload.toString());

                    canonical = CODEC.writeWorkflow(event);

                } else {
                    throw new IllegalArgumentException(
                            "Unsupported event family");
                }

                JsonNode wire = JSON.readTree(canonical);

                String aggregateField =
                        "document".equals(family)
                                ? "documentId" : "workflowId";

                String sequenceField =
                        "document".equals(family)
                                ? "documentVersion" : "sequence";

                if (!id.toString().equals(
                            wire.required("eventId").asText())
                        || !aggregate.toString().equals(
                            wire.required(aggregateField).asText())
                        || !tenant.equals(
                            wire.required("tenantId").asText())
                        || sequence != count(wire, sequenceField)
                        || !row.required("eventType").asText()
                            .equals(wire.required("eventType").asText())
                        || !identities.add(id)) {

                    conflicting++;
                    continue;
                }

                StreamIdentity.aggregateKey(tenant, aggregate);

                verified++;

            } catch (Exception invalid) {
                conflicting++;
            }
        }

        // Database envelope validation is not equivalent to
        // independently verified Kafka history coverage.
        return new HistoricalCoverage(
                total,
                nonStream,
                verified,
                unresolved,
                conflicting,
                false,
                false);
    }

    private static long count(JsonNode node, String name) {

        JsonNode value = node.required(name);

        if (!value.isIntegralNumber()
                || !value.canConvertToLong()
                || value.longValue() < 0) {

            throw new IllegalArgumentException(
                    "Invalid inventory count: " + name);
        }

        return value.longValue();
    }

    public static void main(String[] args) throws Exception {

        if (args.length != 1) {
            throw new IllegalArgumentException(
                    "Usage: AuthoritativeMigrationInventory inventory.json");
        }

        JsonNode inventory = JSON.readTree(
                Files.readString(Path.of(args[0])));

        HistoricalCoverage coverage = validate(inventory);

        System.out.println(
                "AUTHORITATIVE_COVERAGE_JSON="
                        + JSON.writeValueAsString(coverage));

        coverage.requireAuthoritativeRebuild();
    }
}
