package ai.nexusagent.events.streaming;

import org.junit.jupiter.api.Test;

import java.util.Collections;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.*;

class StreamTopicsTest {

    @Test
    void productionNamesRemainUnchanged() {

        var topics = StreamTopics.production();

        assertEquals(StreamSettings.DOCUMENTS,
                topics.documents());

        assertEquals(StreamSettings.WORKFLOWS,
                topics.workflows());

        assertEquals(StreamSettings.ANALYTICS,
                topics.analytics());

        assertEquals(StreamSettings.DEAD_LETTERS,
                topics.deadLetters());

        assertEquals(StreamSettings.TENANT_ANALYTICS,
                topics.tenantAnalytics());
    }

    @Test
    void acceptanceTopicsAreDisjointFromProduction() {

        var isolated = StreamTopics.acceptance(
                UUID.randomUUID());

        assertTrue(Collections.disjoint(
                isolated.all(),
                StreamTopics.production().all()));
    }

    @Test
    void separateRunsAreIsolated() {

        assertTrue(Collections.disjoint(
                StreamTopics.acceptance(
                        UUID.randomUUID()).all(),
                StreamTopics.acceptance(
                        UUID.randomUUID()).all()));
    }

    @Test
    void cleanupOwnershipRejectsProduction() {

        UUID run = UUID.randomUUID();

        var topics = StreamTopics.acceptance(run);

        assertFalse(topics.ownsAcceptanceTopic(
                run, StreamSettings.DOCUMENTS));

        assertTrue(topics.ownsAcceptanceTopic(
                run, topics.documents()));
    }

    @Test
    void cleanupOwnershipRejectsOtherRuns() {

        UUID run = UUID.randomUUID();

        assertFalse(
                StreamTopics.acceptance(run)
                        .ownsAcceptanceTopic(
                                run,
                                StreamTopics.acceptance(
                                        UUID.randomUUID())
                                        .documents()));
    }

    @Test
    void duplicateNamesRejected() {

        assertThrows(
                IllegalArgumentException.class,
                () -> new StreamTopics(
                        "same", "same", "a", "b", "c"));
    }

    @Test
    void invalidNamesRejected() {

        assertThrows(
                IllegalArgumentException.class,
                () -> new StreamTopics(
                        "invalid topic", "a", "b", "c", "d"));
    }
}
