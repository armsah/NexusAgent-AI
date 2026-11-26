package ai.nexusagent.events.streaming;

import java.time.Duration;
import java.time.Instant;
import java.util.Objects;

public final class PendingGapAge {

    private PendingGapAge() {}

    public record Observation(
            Instant firstObservedAt,
            long ageMillis,
            boolean overdue) {}

    public static Observation evaluate(
            Instant firstObservedAt,
            Instant now,
            Duration threshold) {

        Objects.requireNonNull(firstObservedAt);
        Objects.requireNonNull(now);
        Objects.requireNonNull(threshold);

        if (threshold.isNegative() || threshold.isZero()) {
            throw new IllegalArgumentException(
                    "Warning threshold must be positive");
        }

        Duration elapsed = now.isBefore(firstObservedAt)
                ? Duration.ZERO
                : Duration.between(firstObservedAt, now);

        return new Observation(
                firstObservedAt,
                elapsed.toMillis(),
                elapsed.compareTo(threshold) >= 0);
    }
}
