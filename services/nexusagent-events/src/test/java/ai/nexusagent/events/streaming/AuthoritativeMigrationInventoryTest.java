package ai.nexusagent.events.streaming;

import com.fasterxml.jackson.databind.ObjectMapper;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

class AuthoritativeMigrationInventoryTest {

    private final ObjectMapper json = new ObjectMapper();

    @Test
    void actualHistoricalGapDeniesMigration() throws Exception {

        var inventory = json.readTree("""
                {
                  "totalPublications":159,
                  "nonStreamPublications":37,
                  "unresolvedEvents":122,
                  "events":[]
                }
                """);

        var coverage =
                AuthoritativeMigrationInventory.validate(inventory);

        assertEquals(122, coverage.unresolvedEvents());

        assertThrows(
                IllegalStateException.class,
                coverage::requireAuthoritativeRebuild);
    }

    @Test
    void omittedHistoricalRowsAreRejected() throws Exception {

        var inventory = json.readTree("""
                {
                  "totalPublications":159,
                  "nonStreamPublications":37,
                  "unresolvedEvents":100,
                  "events":[]
                }
                """);

        assertThrows(
                IllegalArgumentException.class,
                () -> AuthoritativeMigrationInventory.validate(inventory));
    }

    @Test
    void malformedEnvelopeIsAccountedAsConflict() throws Exception {

        var inventory = json.readTree("""
                {
                  "totalPublications":1,
                  "nonStreamPublications":0,
                  "unresolvedEvents":0,
                  "events":[{}]
                }
                """);

        var coverage =
                AuthoritativeMigrationInventory.validate(inventory);

        assertEquals(1, coverage.conflictingEvents());
        assertFalse(coverage.permitsAuthoritativeRebuild());
    }

    @Test
    void databaseWireEnvelopeDoesNotAloneAuthorizeMigration()
            throws Exception {

        var inventory = json.readTree("""
                {
                  "totalPublications":1,
                  "nonStreamPublications":0,
                  "unresolvedEvents":0,
                  "events":[{
                    "family":"document",
                    "eventId":"11111111-1111-4111-8111-111111111111",
                    "tenantId":"migration-test",
                    "aggregateId":"22222222-2222-4222-8222-222222222222",
                    "sequence":1,
                    "eventType":"document.created",
                    "payload":{
                      "schemaVersion":1,
                      "eventId":"11111111-1111-4111-8111-111111111111",
                      "eventType":"document.created",
                      "tenantId":"migration-test",
                      "documentId":"22222222-2222-4222-8222-222222222222",
                      "documentVersion":1,
                      "contentVersion":1,
                      "occurredAt":"2026-01-01T00:00:00Z"
                    }
                  }]
                }
                """);

        var coverage =
                AuthoritativeMigrationInventory.validate(inventory);

        assertEquals(1, coverage.independentlyVerifiedEvents());
        assertEquals(0, coverage.conflictingEvents());
        assertFalse(coverage.permitsAuthoritativeRebuild());
    }
}
