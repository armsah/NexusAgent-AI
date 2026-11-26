package ai.nexusagent.events;

import ai.nexusagent.events.contracts.*;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

import java.time.Instant;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.*;

class ContractTest {
    private final EventCodec codec = new EventCodec();

    private static final String VALID = """
        {
          "schemaVersion":1,
          "eventId":"10000000-0000-0000-0000-000000000001",
          "eventType":"document.created",
          "tenantId":"tenant-a",
          "documentId":"20000000-0000-0000-0000-000000000001",
          "documentVersion":1,
          "contentVersion":1,
          "occurredAt":"2026-01-01T00:00:00Z"
        }
        """;

    @Test
    void documentRoundTrip() {
        var event = codec.readDocument(VALID);
        assertEquals(event, codec.readDocument(codec.writeDocument(event)));
    }

    @ParameterizedTest
    @ValueSource(strings = {
        "document.created", "document.updated", "document.deleted"
    })
    void acceptsKnownDocumentTypes(String type) {
        assertEquals(type, codec.readDocument(
            VALID.replace("document.created", type)).eventType());
    }

    @Test
    void rejectsUnsupportedSchema() {
        assertThrows(IllegalArgumentException.class, () ->
            codec.readDocument(VALID.replace(
                "\"schemaVersion\":1", "\"schemaVersion\":2")));
    }

    @Test
    void rejectsUnknownEventType() {
        assertThrows(IllegalArgumentException.class, () ->
            codec.readDocument(VALID.replace(
                "document.created", "document.unknown")));
    }

    @Test
    void rejectsUnknownProperty() {
        assertThrows(IllegalArgumentException.class, () ->
            codec.readDocument(VALID.replace(
                "\"schemaVersion\":1",
                "\"schemaVersion\":1,\"secret\":\"forbidden\"")));
    }

    @Test
    void rejectsDuplicateProperty() {
        assertThrows(IllegalArgumentException.class, () ->
            codec.readDocument(VALID.replace(
                "\"schemaVersion\":1",
                "\"schemaVersion\":1,\"schemaVersion\":1")));
    }

    @Test
    void rejectsTrailingJson() {
        assertThrows(IllegalArgumentException.class, () ->
            codec.readDocument(VALID + "{}"));
    }

    @Test
    void rejectsEmptyTenant() {
        assertThrows(IllegalArgumentException.class, () ->
            codec.readDocument(VALID.replace("tenant-a", " ")));
    }

    @Test
    void rejectsZeroIdentifier() {
        assertThrows(IllegalArgumentException.class, () ->
            codec.readDocument(VALID.replace(
                "10000000-0000-0000-0000-000000000001",
                "00000000-0000-0000-0000-000000000000")));
    }

    @Test
    void rejectsInvalidDocumentVersion() {
        assertThrows(IllegalArgumentException.class, () ->
            codec.readDocument(VALID.replace(
                "\"documentVersion\":1", "\"documentVersion\":0")));
    }

    @Test
    void rejectsInvalidContentVersion() {
        assertThrows(IllegalArgumentException.class, () ->
            codec.readDocument(VALID.replace(
                "\"contentVersion\":1", "\"contentVersion\":2")));
    }

    @Test
    void rejectsNumericCoercion() {
        assertThrows(IllegalArgumentException.class, () ->
            codec.readDocument(VALID.replace(
                "\"documentVersion\":1", "\"documentVersion\":\"1\"")));
    }

    @Test
    void rejectsFractionalVersion() {
        assertThrows(IllegalArgumentException.class, () ->
            codec.readDocument(VALID.replace(
                "\"documentVersion\":1", "\"documentVersion\":1.5")));
    }

    @Test
    void rejectsOversizedInput() {
        assertThrows(IllegalArgumentException.class, () ->
            codec.readDocument(" ".repeat(16385)));
    }

    @Test
    void rejectsNullEvent() {
        assertThrows(IllegalArgumentException.class, () ->
            codec.readDocument("null"));
    }

    @Test
    void rejectsMissingTimestamp() {
        assertThrows(IllegalArgumentException.class, () ->
            codec.readDocument(VALID.replace(
                "\"2026-01-01T00:00:00Z\"", "null")));
    }

    @Test
    void workflowRoundTrip() {
        var event = new WorkflowEvent(
            1, UUID.randomUUID(), "tenant-a", UUID.randomUUID(),
            2, WorkflowEvent.Type.COMPLETED, Instant.EPOCH, 500L);

        assertEquals(event, codec.readWorkflow(codec.writeWorkflow(event)));
    }

    @Test
    void terminalDurationRequired() {
        assertThrows(IllegalArgumentException.class, () ->
            new WorkflowEvent(
                1, UUID.randomUUID(), "tenant-a", UUID.randomUUID(),
                2, WorkflowEvent.Type.COMPLETED, Instant.EPOCH, null));
    }

    @Test
    void rejectsNegativeDuration() {
        assertThrows(IllegalArgumentException.class, () ->
            new WorkflowEvent(
                1, UUID.randomUUID(), "tenant-a", UUID.randomUUID(),
                2, WorkflowEvent.Type.FAILED, Instant.EPOCH, -1L));
    }

    @Test
    void rejectsDurationOnStart() {
        assertThrows(IllegalArgumentException.class, () ->
            new WorkflowEvent(
                1, UUID.randomUUID(), "tenant-a", UUID.randomUUID(),
                1, WorkflowEvent.Type.STARTED, Instant.EPOCH, 1L));
    }

    @Test
    void rejectsInvalidSequence() {
        assertThrows(IllegalArgumentException.class, () ->
            new WorkflowEvent(
                1, UUID.randomUUID(), "tenant-a", UUID.randomUUID(),
                0, WorkflowEvent.Type.STARTED, Instant.EPOCH, null));
    }
}