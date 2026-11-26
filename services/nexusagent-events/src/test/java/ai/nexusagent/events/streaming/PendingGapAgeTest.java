package ai.nexusagent.events.streaming;

import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;

import static org.junit.jupiter.api.Assertions.*;

class PendingGapAgeTest {

    private static final Instant FIRST =
            Instant.parse("2026-01-01T00:00:00Z");

    private static final Duration THRESHOLD =
            Duration.ofMinutes(5);

    @Test
    void newGapIsNotOverdue() {

        var age = PendingGapAge.evaluate(
                FIRST, FIRST, THRESHOLD);

        assertEquals(0, age.ageMillis());
        assertFalse(age.overdue());
    }

    @Test
    void immediatelyBeforeThresholdIsNotOverdue() {

        var age = PendingGapAge.evaluate(
                FIRST,
                FIRST.plusMillis(299999),
                THRESHOLD);

        assertEquals(299999, age.ageMillis());
        assertFalse(age.overdue());
    }

    @Test
    void exactThresholdIsOverdue() {

        var age = PendingGapAge.evaluate(
                FIRST,
                FIRST.plusSeconds(300),
                THRESHOLD);

        assertEquals(300000, age.ageMillis());
        assertTrue(age.overdue());
    }

    @Test
    void oldGapRemainsOverdue() {

        var age = PendingGapAge.evaluate(
                FIRST,
                FIRST.plusSeconds(3600),
                THRESHOLD);

        assertEquals(3600000, age.ageMillis());
        assertTrue(age.overdue());
    }

    @Test
    void backwardsClockDoesNotProduceNegativeAge() {

        var age = PendingGapAge.evaluate(
                FIRST,
                FIRST.minusSeconds(10),
                THRESHOLD);

        assertEquals(0, age.ageMillis());
        assertFalse(age.overdue());
    }

    @Test
    void zeroThresholdRejected() {

        assertThrows(
                IllegalArgumentException.class,
                () -> PendingGapAge.evaluate(
                        FIRST, FIRST, Duration.ZERO));
    }

    @Test
    void negativeThresholdRejected() {

        assertThrows(
                IllegalArgumentException.class,
                () -> PendingGapAge.evaluate(
                        FIRST, FIRST, Duration.ofSeconds(-1)));
    }

    @Test
    void originalObservationTimestampPreserved() {

        var age = PendingGapAge.evaluate(
                FIRST,
                FIRST.plusSeconds(900),
                THRESHOLD);

        assertEquals(FIRST, age.firstObservedAt());
    }
}
