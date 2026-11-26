package ai.nexusagent.events.streaming;

import com.fasterxml.jackson.databind.ObjectMapper;

import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.*;

class ReconciliationAuditTest {

    private static final ObjectMapper JSON =
            new ObjectMapper();

    private static final UUID AGGREGATE =
            UUID.randomUUID();

    private static final UUID EVENT =
            UUID.randomUUID();

    private static final Instant FIRST =
            Instant.parse("2026-01-01T00:00:00Z");

    @Test
    void bufferedEventHasMissingPredecessorEvidence()
            throws Exception {

        var audit = JSON.readTree(
                ReconciliationAudit.create(
                        "tenant-a", "document",
                        AGGREGATE, EVENT,
                        3, 2,
                        ReconciliationAudit.Status.BUFFERED,
                        FIRST, FIRST,
                        "MISSING_PREDECESSOR"));

        assertEquals("BUFFERED",
                audit.path("status").asText());

        assertEquals(3,
                audit.path("sequence").asLong());

        assertEquals(2,
                audit.path("expectedSequence").asLong());

        assertEquals("MISSING_PREDECESSOR",
                audit.path("reason").asText());
    }

    @Test
    void reconciliationPreservesObservation()
            throws Exception {

        var audit = JSON.readTree(
                ReconciliationAudit.create(
                        "tenant-a", "document",
                        AGGREGATE, EVENT,
                        3, 3,
                        ReconciliationAudit.Status.RECONCILED,
                        FIRST, FIRST.plusSeconds(900),
                        null));

        assertEquals("RECONCILED",
                audit.path("status").asText());

        assertEquals(FIRST.toString(),
                audit.path("firstObservedAt").asText());

        assertFalse(audit.has("payload"));
    }

    @Test
    void rejectedSuccessorHasExplicitReason()
            throws Exception {

        var audit = JSON.readTree(
                ReconciliationAudit.create(
                        "tenant-a", "workflow",
                        AGGREGATE, EVENT,
                        3, 3,
                        ReconciliationAudit.Status.REJECTED,
                        FIRST, FIRST.plusSeconds(30),
                        "INVALID_WORKFLOW_TRANSITION"));

        assertEquals("REJECTED",
                audit.path("status").asText());

        assertEquals("INVALID_WORKFLOW_TRANSITION",
                audit.path("reason").asText());
    }

    @Test
    void tenantAuditKeysAreIsolated() {

        String first = ReconciliationAudit.key(
                "document:" + StreamIdentity.aggregateKey(
                        "tenant-a", AGGREGATE),
                EVENT,
                ReconciliationAudit.Status.BUFFERED);

        String second = ReconciliationAudit.key(
                "document:" + StreamIdentity.aggregateKey(
                        "tenant-b", AGGREGATE),
                EVENT,
                ReconciliationAudit.Status.BUFFERED);

        assertNotEquals(first, second);
    }

    @Test
    void transitionRecordsHaveDistinctKeys() {

        assertNotEquals(
                ReconciliationAudit.key(
                        "aggregate", EVENT,
                        ReconciliationAudit.Status.BUFFERED),
                ReconciliationAudit.key(
                        "aggregate", EVENT,
                        ReconciliationAudit.Status.RECONCILED));
    }

    @Test
    void legacyObservationIsExplicitlyUnknown()
            throws Exception {

        var audit = JSON.readTree(
                ReconciliationAudit.create(
                        "tenant-a", "document",
                        AGGREGATE, EVENT,
                        3, 3,
                        ReconciliationAudit.Status.RECONCILED,
                        null, FIRST, null));

        assertTrue(
                audit.path("firstObservedAt").isNull());
    }

    @Test
    void invalidSequenceRejected() {

        assertThrows(
                IllegalArgumentException.class,
                () -> ReconciliationAudit.create(
                        "tenant-a", "document",
                        AGGREGATE, EVENT,
                        0, 1,
                        ReconciliationAudit.Status.BUFFERED,
                        FIRST, FIRST, null));
    }
}
