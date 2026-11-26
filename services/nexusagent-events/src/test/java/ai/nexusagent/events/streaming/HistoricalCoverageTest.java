package ai.nexusagent.events.streaming;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

class HistoricalCoverageTest {

    @Test
    void completeVerifiedHistoryPermitsRebuild() {

        var coverage = new HistoricalCoverage(
                159, 37, 122, 0, 0, true, true);

        assertTrue(coverage.permitsAuthoritativeRebuild());

        assertDoesNotThrow(
                coverage::requireAuthoritativeRebuild);
    }

    @Test
    void actualLegacyMetadataGapBlocksRebuild() {

        var coverage = new HistoricalCoverage(
                159, 37, 0, 122, 0, true, false);

        assertFalse(coverage.permitsAuthoritativeRebuild());

        assertThrows(
                IllegalStateException.class,
                coverage::requireAuthoritativeRebuild);
    }

    @Test
    void retainedKafkaHistoryAloneIsInsufficient() {

        var coverage = new HistoricalCoverage(
                122, 0, 122, 0, 0, true, false);

        assertFalse(coverage.permitsAuthoritativeRebuild());
    }

    @Test
    void incompleteSourceInventoryBlocksRebuild() {

        var coverage = new HistoricalCoverage(
                122, 0, 122, 0, 0, false, true);

        assertFalse(coverage.permitsAuthoritativeRebuild());
    }

    @Test
    void unresolvedEventBlocksRebuild() {

        var coverage = new HistoricalCoverage(
                122, 0, 121, 1, 0, true, true);

        assertFalse(coverage.permitsAuthoritativeRebuild());
    }

    @Test
    void conflictingEventBlocksRebuild() {

        var coverage = new HistoricalCoverage(
                122, 0, 121, 0, 1, true, true);

        assertFalse(coverage.permitsAuthoritativeRebuild());
    }

    @Test
    void emptyHistoryCannotCertifyMigration() {

        var coverage = new HistoricalCoverage(
                0, 0, 0, 0, 0, true, true);

        assertFalse(coverage.permitsAuthoritativeRebuild());
    }

    @Test
    void nonStreamEventsCannotCertifyMigration() {

        var coverage = new HistoricalCoverage(
                37, 37, 0, 0, 0, true, true);

        assertFalse(coverage.permitsAuthoritativeRebuild());
    }

    @Test
    void silentlyOmittedPublicationsAreRejected() {

        assertThrows(
                IllegalArgumentException.class,
                () -> new HistoricalCoverage(
                        159, 37, 100, 0, 0, true, true));
    }

    @Test
    void negativeCountsAreRejected() {

        assertThrows(
                IllegalArgumentException.class,
                () -> new HistoricalCoverage(
                        159, 37, -1, 123, 0, true, true));
    }

    @Test
    void overflowCannotProduceValidCoverage() {

        assertThrows(
                ArithmeticException.class,
                () -> new HistoricalCoverage(
                        Long.MAX_VALUE,
                        Long.MAX_VALUE,
                        1, 0, 0, true, true));
    }

    @Test
    void completeCoverageStillRequiresSafeMigrationEvidence() {

        var coverage = new HistoricalCoverage(
                122, 0, 122, 0, 0, true, true);

        assertThrows(
                RuntimeException.class,
                () -> coverage.requireSafeMigration(
                        new StreamRetentionPolicy.RebuildEvidence(
                                true, true, false, true, true)));
    }

    @Test
    void completeCoverageAndSafeEvidencePermitMigration() {

        var coverage = new HistoricalCoverage(
                122, 0, 122, 0, 0, true, true);

        assertDoesNotThrow(
                () -> coverage.requireSafeMigration(
                        new StreamRetentionPolicy.RebuildEvidence(
                                true, true, true, true, true)));
    }

    @Test
    void assertedSafeMigrationCannotOverrideHistoricalGap() {

        var coverage = new HistoricalCoverage(
                159, 37, 0, 122, 0, true, false);

        assertThrows(
                IllegalStateException.class,
                () -> coverage.requireSafeMigration(
                        new StreamRetentionPolicy.RebuildEvidence(
                                true, true, true, true, true)));
    }
}
