package ai.nexusagent.events.streaming;

import org.junit.jupiter.api.Test;

import java.util.Set;

import static org.junit.jupiter.api.Assertions.*;

class SyntheticSnapshotPolicyTest {

    private static final String SYNTHETIC =
            "e737-tenant-1234567890abcdef1234567890abcdef";

    @Test
    void verifiedSyntheticTenantCanBeReconciled() {

        assertEquals(
                SyntheticSnapshotPolicy.Decision
                        .APPROVED_FOR_RECONCILIATION,
                SyntheticSnapshotPolicy.classify(
                        SYNTHETIC,
                        Set.of(SYNTHETIC),
                        Set.of(),
                        true));
    }

    @Test
    void prefixAloneDoesNotAuthorizeReconciliation() {

        assertEquals(
                SyntheticSnapshotPolicy.Decision.PRESERVE,
                SyntheticSnapshotPolicy.classify(
                        SYNTHETIC,
                        Set.of(),
                        Set.of(),
                        true));
    }

    @Test
    void authoritativeTenantAlwaysPreserved() {

        assertEquals(
                SyntheticSnapshotPolicy.Decision.PRESERVE,
                SyntheticSnapshotPolicy.classify(
                        SYNTHETIC,
                        Set.of(SYNTHETIC),
                        Set.of(SYNTHETIC),
                        true));
    }

    @Test
    void incompleteInventoryRequiresReview() {

        assertEquals(
                SyntheticSnapshotPolicy.Decision.REVIEW_REQUIRED,
                SyntheticSnapshotPolicy.classify(
                        SYNTHETIC,
                        Set.of(SYNTHETIC),
                        Set.of(),
                        false));
    }

    @Test
    void unrelatedTenantPreserved() {

        assertEquals(
                SyntheticSnapshotPolicy.Decision.PRESERVE,
                SyntheticSnapshotPolicy.classify(
                        "customer-production",
                        Set.of(SYNTHETIC),
                        Set.of(),
                        true));
    }

    @Test
    void blankTenantRejected() {

        assertThrows(
                IllegalArgumentException.class,
                () -> SyntheticSnapshotPolicy.classify(
                        " ",
                        Set.of(),
                        Set.of(),
                        true));
    }
}
