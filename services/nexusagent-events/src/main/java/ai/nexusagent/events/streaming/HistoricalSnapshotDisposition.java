package ai.nexusagent.events.streaming;

import java.util.Objects;
import java.util.Set;

public final class HistoricalSnapshotDisposition {

    private HistoricalSnapshotDisposition() {}

    public enum Classification {
        CURRENT_GENERATION,
        LEGACY_UNVERIFIED,
        LEGACY_REVIEW_REQUIRED
    }

    public record Decision(
            Classification classification,
            boolean preserveOriginal,
            boolean eligibleForCurrentGeneration,
            boolean tombstoneAuthorized) {}

    public static Decision classify(
            StreamGeneration generation,
            String tenantId,
            Set<String> reviewCandidates) {

        Objects.requireNonNull(generation);
        Objects.requireNonNull(tenantId);
        Objects.requireNonNull(reviewCandidates);

        if (tenantId.isBlank()) {
            throw new IllegalArgumentException(
                    "Tenant identity required");
        }

        if (generation == StreamGeneration.PROSPECTIVE) {

            return new Decision(
                    Classification.CURRENT_GENERATION,
                    true,
                    true,
                    false);
        }

        return new Decision(
                reviewCandidates.contains(tenantId)
                        ? Classification.LEGACY_REVIEW_REQUIRED
                        : Classification.LEGACY_UNVERIFIED,
                true,
                false,
                false);
    }

    public static void requireCurrentGeneration(
            StreamGeneration generation,
            String sourceTopic) {

        Objects.requireNonNull(generation);

        if (generation != StreamGeneration.PROSPECTIVE ||
                !generation.topics().tenantAnalytics()
                        .equals(sourceTopic)) {

            throw new IllegalStateException(
                    "Historical snapshot cannot enter current generation");
        }
    }
}
