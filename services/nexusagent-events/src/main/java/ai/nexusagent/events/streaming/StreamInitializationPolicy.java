package ai.nexusagent.events.streaming;

import java.util.Collection;
import java.util.Objects;

public final class StreamInitializationPolicy {

    private StreamInitializationPolicy() {}

    public enum Mode {
        INITIALIZE_EMPTY,
        CONTINUE_ESTABLISHED
    }

    public record Evidence(
            boolean initializationApproved,
            boolean anyInternalTopicsPresent,
            boolean anyCommittedOffsetsPresent,
            boolean allApplicationTopicsEmpty,
            boolean completeTopicInventory,
            boolean localStateEmpty) {}

    public static Mode select(
            Evidence evidence,
            Collection<ProductionRecoveryGuard.Boundary> boundaries,
            boolean durableStoresPresent) {

        Objects.requireNonNull(evidence);
        Objects.requireNonNull(boundaries);

        if (!evidence.completeTopicInventory() ||
                boundaries.isEmpty()) {
            throw new IllegalStateException(
                    "Incomplete initialization inventory");
        }

        if (evidence.anyInternalTopicsPresent() ||
                evidence.anyCommittedOffsetsPresent()) {

            ProductionRecoveryGuard.requireContinuation(
                    boundaries, durableStoresPresent);

            return Mode.CONTINUE_ESTABLISHED;
        }

        if (!evidence.initializationApproved()) {
            throw new IllegalStateException(
                    "Explicit initialization approval required");
        }

        if (!evidence.allApplicationTopicsEmpty()) {
            throw new IllegalStateException(
                    "Nonempty history requires verified migration");
        }

        if (!evidence.localStateEmpty()) {
            throw new IllegalStateException(
                    "Unverified local state prevents initialization");
        }

        for (var boundary : boundaries) {

            if (boundary.beginning() != 0 ||
                    boundary.end() != 0 ||
                    boundary.committed() != null) {

                throw new IllegalStateException(
                        "Source is not a pristine partition");
            }
        }

        return Mode.INITIALIZE_EMPTY;
    }

    public static String offsetReset(Mode mode) {

        return switch (Objects.requireNonNull(mode)) {
            case INITIALIZE_EMPTY -> "earliest";
            case CONTINUE_ESTABLISHED -> "none";
        };
    }
}
