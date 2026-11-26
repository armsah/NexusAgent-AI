package ai.nexusagent.events.streaming;

import org.junit.jupiter.api.Test;

import java.util.UUID;

import static org.junit.jupiter.api.Assertions.*;

class BrokerRetentionGuardTest {

    private static final String BOOTSTRAP =
            System.getenv().getOrDefault(
                    "NEXUS_TEST_KAFKA_BOOTSTRAP",
                    "localhost:19092");

    @Test
    void verifiesProtectedSourceTopics() throws Exception {

        try (var fixture =
                     new AcceptanceTopicFixture(BOOTSTRAP)) {

            assertDoesNotThrow(() ->
                    BrokerRetentionGuard.verify(
                            BOOTSTRAP,
                            "retention-" + UUID.randomUUID(),
                            fixture.topics()));
        }
    }

    @Test
    void rejectsMissingSourceTopics() {

        assertThrows(IllegalStateException.class, () ->
                BrokerRetentionGuard.verify(
                        BOOTSTRAP,
                        "retention-" + UUID.randomUUID(),
                        StreamTopics.acceptance(
                                UUID.randomUUID())));
    }
}
