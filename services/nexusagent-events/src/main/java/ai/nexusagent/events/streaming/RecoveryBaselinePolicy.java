package ai.nexusagent.events.streaming;

import java.util.Objects;
import java.util.UUID;

/**
 * A prospective baseline is not proof of complete historical coverage.
 */
public final class RecoveryBaselinePolicy {

    private RecoveryBaselinePolicy() {}

    public enum Operation {
        ISOLATED_PROSPECTIVE_REBUILD,
        PRODUCTION_BASELINE_MIGRATION,
        COMPLETE_HISTORICAL_REBUILD,
        SYNTHETIC_SNAPSHOT_RECONCILIATION
    }

    public record Evidence(
            UUID baselineId,
            long unresolvedLegacyEvents,
            long unclassifiedBrokerRecords,
            boolean prospectiveHistoryVerified,
            boolean baselineStateVerified,
            boolean sourceBoundariesVerified,
            boolean isolatedTargetVerified,
            boolean productionPreservationVerified,
            boolean historicalCoverageComplete,
            boolean syntheticProvenanceVerified,
            boolean authoritativeTenantInventoryComplete,
            boolean reconciliationApproved,
            boolean migrationApproved) {

        public Evidence {
            Objects.requireNonNull(baselineId);

            if (baselineId.equals(new UUID(0, 0))) {
                throw new IllegalArgumentException(
                        "Empty baseline identity");
            }

            if (unresolvedLegacyEvents < 0 ||
                    unclassifiedBrokerRecords < 0) {
                throw new IllegalArgumentException(
                        "Negative evidence counts");
            }

            if (historicalCoverageComplete &&
                    (unresolvedLegacyEvents != 0 ||
                     unclassifiedBrokerRecords != 0)) {
                throw new IllegalArgumentException(
                        "Contradictory historical evidence");
            }
        }
    }

    public static boolean permits(
            Operation operation,
            Evidence evidence) {

        Objects.requireNonNull(operation);
        Objects.requireNonNull(evidence);

        boolean safeIsolation =
                evidence.prospectiveHistoryVerified() &&
                evidence.sourceBoundariesVerified() &&
                evidence.isolatedTargetVerified() &&
                evidence.productionPreservationVerified();

        return switch (operation) {

            case ISOLATED_PROSPECTIVE_REBUILD ->
                    safeIsolation;

            case PRODUCTION_BASELINE_MIGRATION ->
                    safeIsolation &&
                    evidence.baselineStateVerified() &&
                    evidence.unclassifiedBrokerRecords() == 0 &&
                    evidence.migrationApproved();

            case COMPLETE_HISTORICAL_REBUILD ->
                    safeIsolation &&
                    evidence.baselineStateVerified() &&
                    evidence.historicalCoverageComplete() &&
                    evidence.unresolvedLegacyEvents() == 0 &&
                    evidence.unclassifiedBrokerRecords() == 0 &&
                    evidence.migrationApproved();

            case SYNTHETIC_SNAPSHOT_RECONCILIATION ->
                    safeIsolation &&
                    evidence.baselineStateVerified() &&
                    evidence.syntheticProvenanceVerified() &&
                    evidence.authoritativeTenantInventoryComplete() &&
                    evidence.reconciliationApproved();
        };
    }

    public static void require(
            Operation operation,
            Evidence evidence) {

        if (!permits(operation, evidence)) {
            throw new IllegalStateException(
                    "Recovery operation denied: " + operation);
        }
    }
}
