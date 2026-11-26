package ai.nexusagent.events.streaming;

import org.junit.jupiter.api.Test;
import java.util.List;

import static org.junit.jupiter.api.Assertions.*;
import static ai.nexusagent.events.streaming.StreamInitializationPolicy.Mode.*;

final class StreamInitializationPolicyTest {

    private static final List<ProductionRecoveryGuard.Boundary> EMPTY =
            List.of(new ProductionRecoveryGuard.Boundary(
                    0, 0, null));

    private static final List<ProductionRecoveryGuard.Boundary> ESTABLISHED =
            List.of(new ProductionRecoveryGuard.Boundary(
                    0, 100, 100L));

    private static StreamInitializationPolicy.Evidence evidence(
            boolean approved,
            boolean internal,
            boolean offsets,
            boolean empty,
            boolean complete,
            boolean localEmpty) {

        return new StreamInitializationPolicy.Evidence(
                approved, internal, offsets,
                empty, complete, localEmpty);
    }

    @Test
    void initializesExplicitlyApprovedPristineEnvironment() {

        assertEquals(INITIALIZE_EMPTY,
                StreamInitializationPolicy.select(
                        evidence(true, false, false,
                                true, true, true),
                        EMPTY, false));
    }

    @Test
    void rejectsUnapprovedInitialization() {

        assertThrows(IllegalStateException.class, () ->
                StreamInitializationPolicy.select(
                        evidence(false, false, false,
                                true, true, true),
                        EMPTY, false));
    }

    @Test
    void rejectsExistingHistoryWithoutCheckpoints() {

        assertThrows(IllegalStateException.class, () ->
                StreamInitializationPolicy.select(
                        evidence(true, false, false,
                                false, true, true),
                        EMPTY, false));
    }

    @Test
    void rejectsPartialInternalState() {

        assertThrows(IllegalStateException.class, () ->
                StreamInitializationPolicy.select(
                        evidence(true, true, false,
                                true, true, true),
                        EMPTY, false));
    }

    @Test
    void rejectsCommittedOffsetsWithoutDurableStores() {

        assertThrows(IllegalStateException.class, () ->
                StreamInitializationPolicy.select(
                        evidence(true, false, true,
                                false, true, true),
                        ESTABLISHED, false));
    }

    @Test
    void continuesVerifiedEstablishedState() {

        assertEquals(CONTINUE_ESTABLISHED,
                StreamInitializationPolicy.select(
                        evidence(false, true, true,
                                false, true, false),
                        ESTABLISHED, true));
    }

    @Test
    void rejectsIncompleteInventory() {

        assertThrows(IllegalStateException.class, () ->
                StreamInitializationPolicy.select(
                        evidence(true, false, false,
                                true, false, true),
                        EMPTY, false));
    }

    @Test
    void rejectsUnverifiedLocalState() {

        assertThrows(IllegalStateException.class, () ->
                StreamInitializationPolicy.select(
                        evidence(true, false, false,
                                true, true, false),
                        EMPTY, false));
    }

    @Test
    void rejectsPreviouslyTruncatedSource() {

        assertThrows(IllegalStateException.class, () ->
                StreamInitializationPolicy.select(
                        evidence(true, false, false,
                                true, true, true),
                        List.of(new ProductionRecoveryGuard.Boundary(
                                100, 100, null)),
                        false));
    }

    @Test
    void rejectsContradictoryEmptyEvidence() {

        assertThrows(IllegalStateException.class, () ->
                StreamInitializationPolicy.select(
                        evidence(true, false, false,
                                true, true, true),
                        ESTABLISHED, false));
    }

    @Test
    void initializationStartsAtEarliest() {

        assertEquals("earliest",
                StreamInitializationPolicy.offsetReset(
                        INITIALIZE_EMPTY));
    }

    @Test
    void continuationNeverAutomaticallyResetsOffsets() {

        assertEquals("none",
                StreamInitializationPolicy.offsetReset(
                        CONTINUE_ESTABLISHED));
    }

    @Test
    void rejectsEmptyPartitionInventory() {

        assertThrows(IllegalStateException.class, () ->
                StreamInitializationPolicy.select(
                        evidence(true, false, false,
                                true, true, true),
                        List.of(), false));
    }
}
