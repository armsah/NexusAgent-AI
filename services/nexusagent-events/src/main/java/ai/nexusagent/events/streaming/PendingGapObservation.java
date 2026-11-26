package ai.nexusagent.events.streaming;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;

import java.time.Instant;
import java.util.Optional;

public final class PendingGapObservation {

    private static final ObjectMapper JSON = new ObjectMapper();

    private static final String FIELD =
            "firstObservedAtEpochMillis";

    private PendingGapObservation() {}

    public static String stamp(String envelope, Instant now) {
        ObjectNode value = parse(envelope);

        if (!value.has(FIELD)) {
            value.put(FIELD, now.toEpochMilli());
        } else {
            observedAt(value);
        }

        return value.toString();
    }

    public static Optional<Instant> observedAt(String envelope) {
        return observedAt(parse(envelope));
    }

    private static Optional<Instant> observedAt(JsonNode value) {
        JsonNode timestamp = value.get(FIELD);

        if (timestamp == null) {
            return Optional.empty();
        }

        if (!timestamp.isIntegralNumber()
                || !timestamp.canConvertToLong()
                || timestamp.asLong() < 0) {

            throw new IllegalStateException(
                    "Invalid persisted gap observation");
        }

        return Optional.of(
                Instant.ofEpochMilli(timestamp.asLong()));
    }

    private static ObjectNode parse(String envelope) {
        try {
            JsonNode value = JSON.readTree(envelope);

            if (!(value instanceof ObjectNode object)) {
                throw new IllegalStateException(
                        "Invalid pending envelope");
            }

            return object;

        } catch (Exception ex) {
            throw new IllegalStateException(
                    "Cannot read pending observation", ex);
        }
    }
}
