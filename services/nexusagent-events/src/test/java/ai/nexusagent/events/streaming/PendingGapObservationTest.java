package ai.nexusagent.events.streaming;

import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.time.Duration;

import static org.junit.jupiter.api.Assertions.*;

class PendingGapObservationTest {

    private static final Instant FIRST =
            Instant.parse("2026-01-01T00:00:00Z");

    @Test
    void observationSurvivesSerialization() {

        String stored = PendingGapObservation.stamp(
                "{\"payload\":\"event\"}", FIRST);

        assertEquals(
                FIRST,
                PendingGapObservation.observedAt(stored)
                        .orElseThrow());
    }

    @Test
    void duplicateDoesNotResetAge() {

        String stored = PendingGapObservation.stamp(
                "{}", FIRST);

        String replayed = PendingGapObservation.stamp(
                stored, FIRST.plusSeconds(900));

        assertEquals(
                FIRST,
                PendingGapObservation.observedAt(replayed)
                        .orElseThrow());
    }

    @Test
    void restoredObservationRemainsOverdue() {

        String stored = PendingGapObservation.stamp(
                "{}", FIRST);

        Instant restored =
                PendingGapObservation.observedAt(stored)
                        .orElseThrow();

        assertTrue(PendingGapAge.evaluate(
                restored,
                FIRST.plusSeconds(301),
                Duration.ofMinutes(5)).overdue());
    }

    @Test
    void independentObservationsRemainIndependent() {

        String first = PendingGapObservation.stamp(
                "{\"tenant\":\"a\"}", FIRST);

        String second = PendingGapObservation.stamp(
                "{\"tenant\":\"b\"}", FIRST.plusSeconds(200));

        assertTrue(PendingGapAge.evaluate(
                PendingGapObservation.observedAt(first)
                        .orElseThrow(),
                FIRST.plusSeconds(301),
                Duration.ofMinutes(5)).overdue());

        assertFalse(PendingGapAge.evaluate(
                PendingGapObservation.observedAt(second)
                        .orElseThrow(),
                FIRST.plusSeconds(301),
                Duration.ofMinutes(5)).overdue());
    }

    @Test
    void legacyEnvelopeHasUnknownAge() {

        assertTrue(
                PendingGapObservation.observedAt("{}")
                        .isEmpty());
    }

    @Test
    void malformedTimestampRejected() {

        assertThrows(
                IllegalStateException.class,
                () -> PendingGapObservation.observedAt(
                        "{\"firstObservedAtEpochMillis\":\"bad\"}"));
    }

    @Test
    void negativeTimestampRejected() {

        assertThrows(
                IllegalStateException.class,
                () -> PendingGapObservation.observedAt(
                        "{\"firstObservedAtEpochMillis\":-1}"));
    }

    @Test
    void originalPayloadPreserved() {

        String stored = PendingGapObservation.stamp(
                "{\"payload\":\"original-event\"}", FIRST);

        assertTrue(stored.contains(
                "\"payload\":\"original-event\""));
    }
}
