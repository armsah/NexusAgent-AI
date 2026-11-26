package ai.nexusagent.events.streaming;

import org.junit.jupiter.api.Test;

import java.util.HashSet;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.*;

final class StreamGenerationTest {

    @Test
    void legacyTopicsRemainUnchanged() {

        assertEquals(
                StreamTopics.production(),
                StreamGeneration.LEGACY.topics());
    }

    @Test
    void prospectiveGenerationUsesDistinctTopics() {

        var legacy = new HashSet<>(
                StreamGeneration.LEGACY.topics().all());

        var prospective =
                StreamGeneration.PROSPECTIVE.topics().all();

        assertEquals(5, prospective.size());
        assertTrue(prospective.stream()
                .noneMatch(legacy::contains));
    }

    @Test
    void prospectiveApplicationIdentityIsDistinct() {

        assertNotEquals(
                StreamGeneration.LEGACY.applicationId(),
                StreamGeneration.PROSPECTIVE.applicationId());
    }

    @Test
    void rejectsUnknownGeneration() {

        assertThrows(IllegalArgumentException.class,
                () -> StreamGeneration.resolve("v99"));
    }

    @Test
    void rejectsCrossGenerationApplicationIdentity() {

        assertThrows(IllegalStateException.class,
                () -> StreamGeneration.PROSPECTIVE.requireBinding(
                        StreamGeneration.LEGACY.applicationId(),
                        StreamGeneration.PROSPECTIVE.topics()));
    }

    @Test
    void rejectsCrossGenerationTopicRouting() {

        assertThrows(IllegalStateException.class,
                () -> StreamGeneration.PROSPECTIVE.requireBinding(
                        StreamGeneration.PROSPECTIVE.applicationId(),
                        StreamGeneration.LEGACY.topics()));
    }

    @Test
    void acceptsExactProspectiveBinding() {

        var generation = StreamGeneration.PROSPECTIVE;

        assertEquals(generation,
                StreamGeneration.requireBindingFor(
                        generation.applicationId(),
                        generation.topics()));
    }

    @Test
    void acceptsExactLegacyBinding() {

        var generation = StreamGeneration.LEGACY;

        assertEquals(generation,
                StreamGeneration.requireBindingFor(
                        generation.applicationId(),
                        generation.topics()));
    }

    @Test
    void rejectsUnverifiedApplicationMigration() {

        assertThrows(IllegalStateException.class,
                () -> StreamGeneration.requireBindingFor(
                        "unverified-app",
                        StreamTopics.production()));
    }

    @Test
    void prospectiveTopologyUsesProspectiveTopics() {

        var generation = StreamGeneration.PROSPECTIVE;

        String description = EventStreamTopology
                .build(generation.topics())
                .describe()
                .toString();

        for (String topic : generation.topics().all()) {
            assertTrue(description.contains(topic), topic);
        }

        for (String topic : StreamTopics.production().all()) {
            assertFalse(description.contains(topic), topic);
        }
    }

    @Test
    void historicalCandidateIsPreserved() {

        var decision = HistoricalSnapshotDisposition.classify(
                StreamGeneration.LEGACY,
                "candidate",
                Set.of("candidate"));

        assertEquals(
                HistoricalSnapshotDisposition.Classification
                        .LEGACY_REVIEW_REQUIRED,
                decision.classification());

        assertTrue(decision.preserveOriginal());
        assertFalse(decision.eligibleForCurrentGeneration());
        assertFalse(decision.tombstoneAuthorized());
    }

    @Test
    void unrelatedLegacySnapshotIsPreserved() {

        var decision = HistoricalSnapshotDisposition.classify(
                StreamGeneration.LEGACY,
                "unrelated",
                Set.of("candidate"));

        assertTrue(decision.preserveOriginal());
        assertFalse(decision.eligibleForCurrentGeneration());
        assertFalse(decision.tombstoneAuthorized());
    }

    @Test
    void currentGenerationDoesNotUseTenantNameHeuristics() {

        var decision = HistoricalSnapshotDisposition.classify(
                StreamGeneration.PROSPECTIVE,
                "candidate",
                Set.of("candidate"));

        assertTrue(decision.eligibleForCurrentGeneration());
        assertTrue(decision.preserveOriginal());
        assertFalse(decision.tombstoneAuthorized());
    }

    @Test
    void rejectsHistoricalSnapshotImport() {

        assertThrows(IllegalStateException.class,
                () -> HistoricalSnapshotDisposition
                        .requireCurrentGeneration(
                                StreamGeneration.PROSPECTIVE,
                                StreamTopics.production()
                                        .tenantAnalytics()));
    }

    @Test
    void acceptsCurrentGenerationSnapshotSource() {

        assertDoesNotThrow(
                () -> HistoricalSnapshotDisposition
                        .requireCurrentGeneration(
                                StreamGeneration.PROSPECTIVE,
                                StreamGeneration.PROSPECTIVE
                                        .topics().tenantAnalytics()));
    }
}
