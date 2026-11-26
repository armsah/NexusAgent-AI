package ai.nexusagent.events.streaming;

import com.fasterxml.jackson.databind.ObjectMapper;
import org.apache.kafka.clients.admin.Admin;
import org.apache.kafka.clients.admin.OffsetSpec;
import org.apache.kafka.clients.producer.KafkaProducer;
import org.apache.kafka.clients.producer.ProducerRecord;
import org.apache.kafka.common.TopicPartition;
import org.apache.kafka.common.serialization.StringSerializer;
import org.apache.kafka.streams.KafkaStreams;
import org.apache.kafka.streams.errors.InvalidStateStoreException;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.Timeout;
import org.junit.jupiter.api.io.TempDir;
import org.springframework.context.support.GenericApplicationContext;

import java.nio.file.Path;
import java.time.Duration;
import java.time.Instant;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.TimeUnit;

import static org.junit.jupiter.api.Assertions.*;

class LiveSpringGenerationRecoveryTest {

    private static final String BOOTSTRAP = "localhost:19092";
    private static final ObjectMapper JSON = new ObjectMapper();

    @TempDir
    Path state;

    @Test
    @Timeout(300)
    void restoresNonemptyStateThroughSpringLifecycle()
            throws Exception {

        var generation = StreamGeneration.PROSPECTIVE;
        var topics = generation.topics();
        String app = generation.applicationId();

        String tenant = "e738-operational-" + UUID.randomUUID();
        String registryPath = System.getenv(
                "NEXUS_ACCEPTANCE_TENANT_REGISTRY");

        assertNotNull(registryPath,
                "Explicit acceptance registry required");

        var registry = Path.of(registryPath);

        AcceptanceTenantRegistry.register(registry, tenant);

        UUID document = UUID.randomUUID();
        UUID event = UUID.randomUUID();

        String key = StreamIdentity.aggregateKey(tenant, document);

        var payload = JSON.createObjectNode();
        payload.put("schemaVersion", 1);
        payload.put("eventId", event.toString());
        payload.put("eventType", "document.created");
        payload.put("tenantId", tenant);
        payload.put("documentId", document.toString());
        payload.put("documentVersion", 1);
        payload.put("contentVersion", 1);
        payload.put("occurredAt", Instant.now().toString());

        String wire = payload.toString();

        try (var producer = new KafkaProducer<String, String>(
                Map.of(
                        "bootstrap.servers", BOOTSTRAP,
                        "acks", "all",
                        "enable.idempotence", true,
                        "allow.auto.create.topics", false),
                new StringSerializer(),
                new StringSerializer());
             var admin = Admin.create(Map.of(
                     "bootstrap.servers", BOOTSTRAP))) {

            String baseline;

            try (var context = startContext(
                    state.resolve("initial"))) {

                var runtime = context.getBean(StreamRuntime.class);

                awaitRunning(runtime);

                producer.send(new ProducerRecord<>(
                        topics.documents(), key, wire))
                        .get(20, TimeUnit.SECONDS);

                baseline = awaitSnapshot(runtime, tenant, 1);

                awaitCommitted(admin, app);

                System.out.println(
                        "SPRING_NONEMPTY_BASELINE_PASSED");
            }

            assertFalse(java.nio.file.Files.exists(
                    state.resolve("restored")));

            try (var context = startContext(
                    state.resolve("restored"))) {

                var runtime = context.getBean(StreamRuntime.class);

                awaitRunning(runtime);

                String restored = awaitSnapshot(runtime, tenant, 1);

                assertEquals(
                        JSON.readTree(baseline),
                        JSON.readTree(restored));

                producer.send(new ProducerRecord<>(
                        topics.documents(), key, wire))
                        .get(20, TimeUnit.SECONDS);

                awaitCommitted(admin, app);

                assertEquals(
                        JSON.readTree(baseline),
                        JSON.readTree(
                                awaitSnapshot(runtime, tenant, 1)));

                var exported = CurrentTenantSnapshotReader.read(
                        BOOTSTRAP,
                        generation,
                        topics.tenantAnalytics());

                assertTrue(exported.containsKey(tenant));

                var operational =
                        CurrentTenantSnapshotReader.readOperational(
                                BOOTSTRAP,
                                generation,
                                topics.tenantAnalytics(),
                                registry);

                assertFalse(operational.containsKey(tenant));

                for (var entry : exported.entrySet()) {
                    if (!AcceptanceTenantRegistry.read(registry)
                            .contains(entry.getKey())) {
                        assertEquals(entry.getValue(),
                                operational.get(entry.getKey()));
                    }
                }

                System.out.println(
                        "ACCEPTANCE_EXPORT_EXCLUSION_PASSED");

                assertEquals(1,
                        exported.get(tenant)
                                .path("acceptedEvents").asLong());

                assertEquals(1,
                        exported.get(tenant)
                                .path("activeDocuments").asLong());

                System.out.println(
                        "SPRING_RESTORATION_AND_DEDUPLICATION_PASSED");
            }
        }
    }

    private GenericApplicationContext startContext(Path directory) {

        var configuration = Map.of(
                "NEXUS_STREAM_ENABLED", "true",
                "NEXUS_STREAM_GENERATION", "v2",
                "NEXUS_KAFKA_BOOTSTRAP_SERVERS", BOOTSTRAP,
                "NEXUS_STREAM_STATE_DIR", directory.toString(),
                "NEXUS_STREAM_INITIALIZE_EMPTY", "false");

        var context = new GenericApplicationContext();

        context.registerBean(
                StreamRuntime.class,
                () -> new StreamRuntime(configuration));

        try {
            context.refresh();
            return context;
        } catch (RuntimeException ex) {
            context.close();
            throw ex;
        }
    }

    private static void awaitRunning(StreamRuntime runtime)
            throws Exception {

        long deadline = System.nanoTime()
                + Duration.ofSeconds(90).toNanos();

        while (System.nanoTime() < deadline) {

            var current = runtime.runtimeState();

            if (current == KafkaStreams.State.RUNNING) {
                assertTrue(runtime.isRunning());
                return;
            }

            if (current == KafkaStreams.State.ERROR) {
                fail("Spring Streams runtime entered ERROR");
            }

            Thread.sleep(250);
        }

        fail("Spring Streams runtime did not become ready");
    }

    private static String awaitSnapshot(
            StreamRuntime runtime,
            String tenant,
            long expected) throws Exception {

        long deadline = System.nanoTime()
                + Duration.ofSeconds(60).toNanos();

        while (System.nanoTime() < deadline) {
            try {
                String snapshot = runtime.tenantSnapshot(tenant);

                if (snapshot != null) {
                    var value = JSON.readTree(snapshot);

                    long accepted =
                            value.path("acceptedEvents").asLong();

                    assertTrue(accepted <= expected);

                    if (accepted == expected) {
                        return snapshot;
                    }
                }
            } catch (InvalidStateStoreException ex) {
                // Wait for state restoration.
            }

            Thread.sleep(250);
        }

        throw new AssertionError(
                "Expected tenant snapshot not available");
    }

    private static void awaitCommitted(
            Admin admin,
            String app) throws Exception {

        var topics = StreamGeneration.PROSPECTIVE.topics();

        var names = java.util.List.of(
                topics.documents(),
                topics.workflows(),
                app + "-event-identity-v1-repartition",
                app + "-aggregate-order-v1-repartition",
                app + "-tenant-analytics-v1-repartition");

        var descriptions = admin.describeTopics(names)
                .allTopicNames().get(15, TimeUnit.SECONDS);

        var requests = new HashMap<TopicPartition, OffsetSpec>();

        descriptions.forEach((name, description) ->
                description.partitions().forEach(partition ->
                        requests.put(
                                new TopicPartition(
                                        name, partition.partition()),
                                OffsetSpec.latest())));

        long deadline = System.nanoTime()
                + Duration.ofSeconds(90).toNanos();

        int stable = 0;

        while (System.nanoTime() < deadline) {

            var committed = admin.listConsumerGroupOffsets(app)
                    .partitionsToOffsetAndMetadata()
                    .get(15, TimeUnit.SECONDS);

            var latest = admin.listOffsets(requests)
                    .all().get(15, TimeUnit.SECONDS);

            boolean complete = requests.keySet().stream()
                    .allMatch(partition ->
                            committed.containsKey(partition)
                            && committed.get(partition).offset()
                            == latest.get(partition).offset());

            stable = complete ? stable + 1 : 0;

            if (stable >= 8) {
                return;
            }

            Thread.sleep(500);
        }

        fail("Committed operational baseline not reached");
    }
}
