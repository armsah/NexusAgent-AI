package ai.nexusagent.events.streaming;

import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

final class ProductionRecoveryGuardTest {

    @Test
    void acceptsEstablishedApplication() {
        assertDoesNotThrow(() ->
                ProductionRecoveryGuard.requireApplication(
                        "nexusagent-events-v1"));
    }

    @Test
    void rejectsUnverifiedApplicationMigration() {
        assertThrows(IllegalStateException.class, () ->
                ProductionRecoveryGuard.requireApplication(
                        "nexusagent-events-v2"));
    }

    @Test
    void rejectsMissingCheckpoint() {
        assertThrows(IllegalStateException.class, () ->
                ProductionRecoveryGuard.requireContinuation(
                        List.of(new ProductionRecoveryGuard.Boundary(
                                0, 100, null)), true));
    }

    @Test
    void rejectsExpiredCheckpoint() {
        assertThrows(IllegalStateException.class, () ->
                ProductionRecoveryGuard.requireContinuation(
                        List.of(new ProductionRecoveryGuard.Boundary(
                                50, 100, 49L)), true));
    }

    @Test
    void rejectsCheckpointBeyondHistory() {
        assertThrows(IllegalStateException.class, () ->
                ProductionRecoveryGuard.requireContinuation(
                        List.of(new ProductionRecoveryGuard.Boundary(
                                0, 100, 101L)), true));
    }

    @Test
    void rejectsMissingDurableStores() {
        assertThrows(IllegalStateException.class, () ->
                ProductionRecoveryGuard.requireContinuation(
                        List.of(new ProductionRecoveryGuard.Boundary(
                                0, 100, 50L)), false));
    }

    @Test
    void rejectsEmptyPartitionInventory() {
        assertThrows(IllegalStateException.class, () ->
                ProductionRecoveryGuard.requireContinuation(
                        List.of(), true));
    }

    @Test
    void acceptsValidContinuation() {
        assertDoesNotThrow(() ->
                ProductionRecoveryGuard.requireContinuation(
                        List.of(
                            new ProductionRecoveryGuard.Boundary(
                                    0, 100, 50L),
                            new ProductionRecoveryGuard.Boundary(
                                    10, 80, 80L)),
                        true));
    }

    @Test
    void rejectsInvalidSourceBoundaries() {
        assertThrows(IllegalArgumentException.class, () ->
                new ProductionRecoveryGuard.Boundary(
                        100, 50, 75L));
    }

    @Test
    void acceptsCommittedBeginningBoundary() {
        assertDoesNotThrow(() ->
                ProductionRecoveryGuard.requireContinuation(
                        List.of(new ProductionRecoveryGuard.Boundary(
                                50, 100, 50L)), true));
    }

    @Test
    void oneMissingPartitionDeniesEntireContinuation() {
        assertThrows(IllegalStateException.class, () ->
                ProductionRecoveryGuard.requireContinuation(
                        List.of(
                            new ProductionRecoveryGuard.Boundary(
                                    0, 100, 50L),
                            new ProductionRecoveryGuard.Boundary(
                                    0, 100, null)),
                        true));
    }
}
