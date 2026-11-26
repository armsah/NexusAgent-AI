package ai.nexusagent.events.streaming;

import static org.junit.jupiter.api.Assertions.*;

import java.nio.file.Path;
import java.util.UUID;

import com.fasterxml.jackson.databind.ObjectMapper;

import org.apache.kafka.common.serialization.Serdes;
import org.apache.kafka.streams.Topology;
import org.apache.kafka.streams.TopologyTestDriver;
import org.apache.kafka.streams.processor.api.ProcessorSupplier;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class GlobalEventIdentityTest {

    @TempDir
    Path state;

    @Test
    void eventIdentityIndependentOfAggregate() {
        UUID id = UUID.randomUUID();

        assertEquals(id.toString(),
                GlobalEventIdentity.registryKey(id));

        assertThrows(IllegalArgumentException.class,
                () -> GlobalEventIdentity.registryKey(new UUID(0, 0)));
    }

    @Test
    void identicalCanonicalEventsHaveIdenticalFingerprints() {
        String first = GlobalEventIdentity.fingerprint(
                "document", "tenant-a:aggregate-a", "{}");

        String second = GlobalEventIdentity.fingerprint(
                "document", "tenant-a:aggregate-a", "{}");

        assertEquals(first, second);
        assertEquals(64, first.length());

        assertEquals(GlobalEventIdentity.Decision.NEW,
                GlobalEventIdentity.classify(null, first));

        assertEquals(GlobalEventIdentity.Decision.DUPLICATE,
                GlobalEventIdentity.classify(first, second));
    }

    @Test
    void conflictingPayloadDetected() {
        String first = GlobalEventIdentity.fingerprint(
                "document", "aggregate", "{\"version\":1}");

        String second = GlobalEventIdentity.fingerprint(
                "document", "aggregate", "{\"version\":2}");

        assertEquals(GlobalEventIdentity.Decision.CONFLICT,
                GlobalEventIdentity.classify(first, second));
    }

    @Test
    void crossAggregateAndCrossTenantConflictsDetected() {
        String first = GlobalEventIdentity.fingerprint(
                "document", "tenant-a:aggregate-a", "{}");

        for (String key : new String[]{
                "tenant-a:aggregate-b",
                "tenant-b:aggregate-a"}) {

            String second = GlobalEventIdentity.fingerprint(
                    "document", key, "{}");

            assertEquals(GlobalEventIdentity.Decision.CONFLICT,
                    GlobalEventIdentity.classify(first, second));
        }
    }

    @Test
    void crossFamilyConflictDetected() {
        String document = GlobalEventIdentity.fingerprint(
                "document", "aggregate", "{}");

        String workflow = GlobalEventIdentity.fingerprint(
                "workflow", "aggregate", "{}");

        assertEquals(GlobalEventIdentity.Decision.CONFLICT,
                GlobalEventIdentity.classify(document, workflow));
    }

    @Test
    void lengthPrefixPreventsAmbiguousIdentityEncoding() {
        assertNotEquals(
                GlobalEventIdentity.fingerprint(
                        "document", "ab", "c"),
                GlobalEventIdentity.fingerprint(
                        "document", "a", "bc"));
    }

    @Test
    void corruptedRegistryFailsClosed() {
        String valid = GlobalEventIdentity.fingerprint(
                "workflow", "aggregate", "{}");

        assertThrows(IllegalStateException.class,
                () -> GlobalEventIdentity.classify("corrupt", valid));
    }

    @Test
    void persistentRegistryRoutesConflictsAndPreservesRetry()
            throws Exception {

        var serde = Serdes.String();

        ProcessorSupplier<String, String, String, String> supplier =
                GlobalIdentityProcessor::new;

        var topology = new Topology()
                .addSource("identity-input",
                        serde.deserializer(), serde.deserializer(),
                        "identity-test-input")
                .addProcessor("identity-check", supplier,
                        "identity-input")
                .addSink("identity-accepted",
                        "identity-test-accepted",
                        serde.serializer(), serde.serializer(),
                        "identity-check")
                .addSink("identity-dead-letter",
                        "identity-test-failures",
                        serde.serializer(), serde.serializer(),
                        "identity-check");

        StreamStateStores.register(
                topology, "identity-check",
                GlobalIdentityProcessor.STORE);

        var properties = StreamSettings.create(
                "localhost:19092", "global-identity-test", state);

        var json = new ObjectMapper();

        try (var driver = new TopologyTestDriver(topology, properties)) {

            var input = driver.createInputTopic(
                    "identity-test-input",
                    serde.serializer(), serde.serializer());

            var accepted = driver.createOutputTopic(
                    "identity-test-accepted",
                    serde.deserializer(), serde.deserializer());

            var failures = driver.createOutputTopic(
                    "identity-test-failures",
                    serde.deserializer(), serde.deserializer());

            String eventId = UUID.randomUUID().toString();

            var envelope = json.createObjectNode();

            envelope.put("eventId", eventId);
            envelope.put("family", "document");
            envelope.put("aggregateKey", "tenant-a:aggregate-a");
            envelope.put("payload", "{\"private\":\"sensitive\"}");
            envelope.put("sourceTopic", "documents");
            envelope.put("sourcePartition", 0);
            envelope.put("sourceOffset", 1);

            String original = envelope.toString();

            input.pipeInput(eventId, original);
            input.pipeInput(eventId, original);

            assertEquals(2, accepted.readKeyValuesToList().size());

            envelope.put("aggregateKey", "tenant-b:aggregate-b");
            envelope.put("sourceOffset", 2);

            input.pipeInput(eventId, envelope.toString());

            String failure = failures.readValue();

            assertEquals("GLOBAL_EVENT_ID_CONFLICT",
                    json.readTree(failure).path("reason").asText());

            assertFalse(failure.contains("sensitive"));
            assertTrue(accepted.isEmpty());

            var registry = driver.<String, String>getKeyValueStore(
                    GlobalIdentityProcessor.STORE);

            assertEquals(
                    GlobalEventIdentity.fingerprint(
                            "document", "tenant-a:aggregate-a",
                            "{\"private\":\"sensitive\"}"),
                    registry.get(eventId));

            input.pipeInput(eventId, original);

            assertEquals("tenant-a:aggregate-a",
                    accepted.readKeyValue().key);

            assertTrue(failures.isEmpty());
        }
    }
}
