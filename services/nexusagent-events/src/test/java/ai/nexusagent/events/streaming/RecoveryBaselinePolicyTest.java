package ai.nexusagent.events.streaming;

import org.junit.jupiter.api.Test;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.*;
import static ai.nexusagent.events.streaming.RecoveryBaselinePolicy.Operation.*;

final class RecoveryBaselinePolicyTest {

    private static final UUID ID = UUID.randomUUID();

    private static RecoveryBaselinePolicy.Evidence evidence(
            long unresolved,
            long malformed,
            boolean prospective,
            boolean baseline,
            boolean boundaries,
            boolean isolated,
            boolean preserved,
            boolean historical,
            boolean synthetic,
            boolean tenants,
            boolean reconcile,
            boolean migrate) {

        return new RecoveryBaselinePolicy.Evidence(
                ID, unresolved, malformed,
                prospective, baseline, boundaries,
                isolated, preserved, historical,
                synthetic, tenants, reconcile, migrate);
    }

    private static RecoveryBaselinePolicy.Evidence current() {

        return evidence(
                122, 38, true, false,
                true, true, true, false,
                false, false, false, false);
    }

    @Test
    void permitsVerifiedProspectiveRebuild() {
        assertTrue(RecoveryBaselinePolicy.permits(
                ISOLATED_PROSPECTIVE_REBUILD, current()));
    }

    @Test
    void rejectsUnverifiedBaselineMigration() {
        assertThrows(IllegalStateException.class, () ->
                RecoveryBaselinePolicy.require(
                        PRODUCTION_BASELINE_MIGRATION, current()));
    }

    @Test
    void rejectsIncompleteHistoricalRebuild() {
        assertFalse(RecoveryBaselinePolicy.permits(
                COMPLETE_HISTORICAL_REBUILD, current()));
    }

    @Test
    void rejectsUnverifiedSyntheticReconciliation() {
        assertFalse(RecoveryBaselinePolicy.permits(
                SYNTHETIC_SNAPSHOT_RECONCILIATION, current()));
    }

    @Test
    void rejectsContradictoryHistoricalEvidence() {
        assertThrows(IllegalArgumentException.class, () ->
                evidence(
                        122, 0, true, true,
                        true, true, true, true,
                        true, true, true, true));
    }

    @Test
    void rejectsNegativeCounts() {
        assertThrows(IllegalArgumentException.class, () ->
                evidence(
                        -1, 0, true, true,
                        true, true, true, false,
                        true, true, true, true));
    }

    @Test
    void rejectsUnverifiedBoundaries() {
        var value = evidence(
                0, 0, true, true,
                false, true, true, true,
                true, true, true, true);

        for (var operation :
                RecoveryBaselinePolicy.Operation.values()) {
            assertFalse(RecoveryBaselinePolicy.permits(
                    operation, value));
        }
    }

    @Test
    void rejectsNonIsolatedTarget() {
        var value = evidence(
                0, 0, true, true,
                true, false, true, true,
                true, true, true, true);

        for (var operation :
                RecoveryBaselinePolicy.Operation.values()) {
            assertFalse(RecoveryBaselinePolicy.permits(
                    operation, value));
        }
    }

    @Test
    void rejectsUnverifiedProductionPreservation() {
        var value = evidence(
                0, 0, true, true,
                true, true, false, true,
                true, true, true, true);

        for (var operation :
                RecoveryBaselinePolicy.Operation.values()) {
            assertFalse(RecoveryBaselinePolicy.permits(
                    operation, value));
        }
    }

    @Test
    void permitsExplicitlyApprovedVerifiedBaseline() {
        var value = evidence(
                122, 0, true, true,
                true, true, true, false,
                false, false, false, true);

        assertTrue(RecoveryBaselinePolicy.permits(
                PRODUCTION_BASELINE_MIGRATION, value));

        assertFalse(RecoveryBaselinePolicy.permits(
                COMPLETE_HISTORICAL_REBUILD, value));
    }

    @Test
    void rejectsUnclassifiedBrokerRecords() {
        var value = evidence(
                122, 38, true, true,
                true, true, true, false,
                false, false, false, true);

        assertFalse(RecoveryBaselinePolicy.permits(
                PRODUCTION_BASELINE_MIGRATION, value));
    }

    @Test
    void rejectsMigrationWithoutApproval() {
        var value = evidence(
                0, 0, true, true,
                true, true, true, true,
                true, true, true, false);

        assertFalse(RecoveryBaselinePolicy.permits(
                PRODUCTION_BASELINE_MIGRATION, value));

        assertFalse(RecoveryBaselinePolicy.permits(
                COMPLETE_HISTORICAL_REBUILD, value));
    }

    @Test
    void rejectsSyntheticReconciliationWithoutTenantInventory() {
        var value = evidence(
                122, 0, true, true,
                true, true, true, false,
                true, false, true, false);

        assertFalse(RecoveryBaselinePolicy.permits(
                SYNTHETIC_SNAPSHOT_RECONCILIATION, value));
    }

    @Test
    void rejectsSyntheticReconciliationWithoutProvenance() {
        var value = evidence(
                122, 0, true, true,
                true, true, true, false,
                false, true, true, false);

        assertFalse(RecoveryBaselinePolicy.permits(
                SYNTHETIC_SNAPSHOT_RECONCILIATION, value));
    }

    @Test
    void rejectsSyntheticReconciliationWithoutApproval() {
        var value = evidence(
                122, 0, true, true,
                true, true, true, false,
                true, true, false, false);

        assertFalse(RecoveryBaselinePolicy.permits(
                SYNTHETIC_SNAPSHOT_RECONCILIATION, value));
    }

    @Test
    void permitsVerifiedAndApprovedReconciliation() {
        var value = evidence(
                122, 0, true, true,
                true, true, true, false,
                true, true, true, false);

        assertTrue(RecoveryBaselinePolicy.permits(
                SYNTHETIC_SNAPSHOT_RECONCILIATION, value));
    }

    @Test
    void permitsCompleteVerifiedHistoricalRebuild() {
        var value = evidence(
                0, 0, true, true,
                true, true, true, true,
                true, true, true, true);

        assertTrue(RecoveryBaselinePolicy.permits(
                COMPLETE_HISTORICAL_REBUILD, value));
    }
}
