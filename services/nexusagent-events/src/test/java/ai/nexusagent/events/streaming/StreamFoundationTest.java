package ai.nexusagent.events.streaming;

import static org.junit.jupiter.api.Assertions.*;

import java.nio.file.Path;
import java.util.UUID;

import org.apache.kafka.common.serialization.Serdes;
import org.apache.kafka.streams.StreamsConfig;
import org.apache.kafka.streams.Topology;
import org.apache.kafka.streams.TopologyTestDriver;
import org.apache.kafka.streams.processor.api.Processor;
import org.apache.kafka.streams.processor.api.ProcessorContext;
import org.apache.kafka.streams.processor.api.ProcessorSupplier;
import org.apache.kafka.streams.processor.api.Record;
import org.apache.kafka.streams.state.KeyValueStore;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class StreamFoundationTest {
    @TempDir
    Path stateDirectory;

    @Test
    void configurationUsesExactlyOnceProcessing() {
        var settings = StreamSettings.create(
                "localhost:19092", "foundation-test", stateDirectory);

        assertEquals(StreamsConfig.EXACTLY_ONCE_V2,
                settings.get(StreamsConfig.PROCESSING_GUARANTEE_CONFIG));
        assertEquals("earliest", settings.get("auto.offset.reset"));
        assertEquals(false, settings.get("allow.auto.create.topics"));
        assertEquals("foundation-test",
                settings.get(StreamsConfig.APPLICATION_ID_CONFIG));
    }

    @Test
    void invalidConfigurationRejected() {
        assertThrows(IllegalArgumentException.class,
                () -> StreamSettings.create("", "test", stateDirectory));

        assertThrows(IllegalArgumentException.class,
                () -> StreamSettings.create("localhost:19092", "", stateDirectory));

        assertThrows(IllegalArgumentException.class,
                () -> StreamSettings.create("localhost:19092", "test", null));
    }

    @Test
    void partitionKeysMatchDotNetContract() {
        var id = UUID.fromString("f7000000-0000-0000-0000-000000000001");

        assertEquals("8:tenant-a:" + id,
                StreamIdentity.aggregateKey("tenant-a", id));

        assertNotEquals(
                StreamIdentity.aggregateKey("tenant-a", id),
                StreamIdentity.aggregateKey("tenant-b", id));

        assertDoesNotThrow(() -> StreamIdentity.requireMatchingKey(
                "8:tenant-a:" + id, "tenant-a", id));

        assertThrows(IllegalArgumentException.class,
                () -> StreamIdentity.requireMatchingKey(
                        "8:tenant-b:" + id, "tenant-a", id));
    }

    @Test
    void invalidAggregateIdentitiesRejected() {
        var id = UUID.randomUUID();

        assertThrows(IllegalArgumentException.class,
                () -> StreamIdentity.aggregateKey(" tenant", id));
        assertThrows(IllegalArgumentException.class,
                () -> StreamIdentity.aggregateKey("a\nb", id));
        assertThrows(IllegalArgumentException.class,
                () -> StreamIdentity.aggregateKey("a".repeat(257), id));
        assertThrows(IllegalArgumentException.class,
                () -> StreamIdentity.aggregateKey("tenant", new UUID(0, 0)));

        assertDoesNotThrow(
                () -> StreamIdentity.aggregateKey("a".repeat(256), id));
    }

    @Test
    void persistentStoreSuppressesRepeatedIdentity() {
        var serde = Serdes.String();

        ProcessorSupplier<String, String, String, String> supplier =
                () -> new Processor<>() {
                    private ProcessorContext<String, String> context;
                    private KeyValueStore<String, String> store;

                    @Override
                    public void init(ProcessorContext<String, String> context) {
                        this.context = context;
                        this.store = context.getStateStore(
                                StreamStateStores.DOCUMENT_IDENTITIES);
                    }

                    @Override
                    public void process(Record<String, String> record) {
                        if (store.get(record.key()) == null) {
                            store.put(record.key(), record.value());
                            context.forward(record);
                        }
                    }
                };

        var topology = new Topology()
                .addSource("source",
                        serde.deserializer(), serde.deserializer(),
                        StreamSettings.DOCUMENTS)
                .addProcessor("processor", supplier, "source")
                .addSink("sink", StreamSettings.ANALYTICS,
                        serde.serializer(), serde.serializer(), "processor");

        StreamStateStores.register(topology, "processor",
                StreamStateStores.DOCUMENT_IDENTITIES);

        var settings = StreamSettings.create(
                "localhost:19092", "state-test", stateDirectory);

        try (var driver = new TopologyTestDriver(topology, settings)) {
            var input = driver.createInputTopic(
                    StreamSettings.DOCUMENTS,
                    serde.serializer(), serde.serializer());

            var output = driver.createOutputTopic(
                    StreamSettings.ANALYTICS,
                    serde.deserializer(), serde.deserializer());

            input.pipeInput("event-1", "payload");
            input.pipeInput("event-1", "payload");
            input.pipeInput("event-2", "successor");

            assertEquals(2, output.readKeyValuesToList().size());

            var store = driver.<String, String>getKeyValueStore(
                    StreamStateStores.DOCUMENT_IDENTITIES);

            assertEquals("payload", store.get("event-1"));
            assertEquals("successor", store.get("event-2"));
        }
    }
}
