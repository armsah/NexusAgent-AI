package ai.nexusagent.events.streaming;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import org.apache.kafka.clients.admin.Admin;
import org.apache.kafka.clients.consumer.*;
import org.apache.kafka.clients.producer.*;
import org.apache.kafka.common.TopicPartition;
import org.apache.kafka.common.serialization.StringDeserializer;
import org.apache.kafka.common.serialization.StringSerializer;
import org.apache.kafka.streams.KafkaStreams;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.nio.file.Path;
import java.time.Duration;
import java.util.*;
import java.util.concurrent.TimeUnit;

import static org.junit.jupiter.api.Assertions.*;

final class LiveDocumentRebuildTest {

    private static final String BOOTSTRAP = "localhost:19092";

    private static final ObjectMapper JSON = new ObjectMapper();

    @TempDir
    Path state;

    record Envelope(String key, String payload) {}

    record Rebuild(
            List<JsonNode> projections,
            JsonNode tenantMetrics) {}

    @Test
    void independentlyRebuiltDocumentHistoryProducesEquivalentState()
            throws Exception {

        String tenant = "e738-rebuild-" + UUID.randomUUID();

        UUID document = UUID.randomUUID();
        UUID created = UUID.randomUUID();
        UUID updated = UUID.randomUUID();
        UUID deleted = UUID.randomUUID();

        String key = StreamIdentity.aggregateKey(
                tenant, document);

        var first = new Envelope(key, envelope(
                tenant, document, created,
                "document.created", 1, 1,
                "2026-01-01T00:00:00Z"));

        var second = new Envelope(key, envelope(
                tenant, document, updated,
                "document.updated", 2, 2,
                "2026-01-01T00:00:01Z"));

        var third = new Envelope(key, envelope(
                tenant, document, deleted,
                "document.deleted", 3, 2,
                "2026-01-01T00:00:02Z"));

        List<Envelope> history = List.of(
                first, first, second, second, third);

        Rebuild original = rebuild(history, tenant);
        Rebuild reconstructed = rebuild(history, tenant);

        assertEquals(original.projections(),
                reconstructed.projections());

        assertEquals(original.tenantMetrics(),
                reconstructed.tenantMetrics());

        assertEquals(3, reconstructed.projections().size());

        JsonNode metrics = reconstructed.tenantMetrics();

        assertEquals(3, metrics.path("acceptedEvents").asLong(-1));
        assertEquals(3, metrics.path("documentEvents").asLong(-1));
        assertEquals(1, metrics.path("documentsCreated").asLong(-1));
        assertEquals(1, metrics.path("documentsUpdated").asLong(-1));
        assertEquals(1, metrics.path("documentsDeleted").asLong(-1));
        assertEquals(0, metrics.path("activeDocuments").asLong(-1));

        System.out.println(
                "ISOLATED_DOCUMENT_REBUILD_PASSED");
    }

    @Test
    void rebuildsVerifiedDotnetPublicationHistory()
            throws Exception {

        String inventoryPath =
                System.getenv("NEXUS_VERIFIED_DOCUMENT_HISTORY");

        org.junit.jupiter.api.Assumptions.assumeTrue(
                inventoryPath != null && !inventoryPath.isBlank(),
                "Explicit cross-language acceptance inventory required");

        JsonNode inventory = JSON.readTree(
                Path.of(inventoryPath).toFile());

        assertTrue(inventory.path("acknowledged").asBoolean());
        assertTrue(inventory.path("exactPayloadMatch").asBoolean());
        assertTrue(inventory.path("boundedHistoryComplete").asBoolean());
        assertEquals(2, inventory.path("records").asInt());

        String payload = inventory.path("payload").asText();
        String key = inventory.path("key").asText();

        JsonNode event = JSON.readTree(payload);

        String tenant = event.path("tenantId").asText();

        assertEquals(
                inventory.path("eventId").asText(),
                event.path("eventId").asText());

        assertEquals(
                StreamIdentity.aggregateKey(
                        tenant,
                        UUID.fromString(
                                event.path("documentId").asText())),
                key);

        var envelope = new Envelope(key, payload);

        List<Envelope> history = List.of(envelope, envelope);

        Rebuild original = rebuild(history, tenant, 1);
        Rebuild reconstructed = rebuild(history, tenant, 1);

        assertEquals(original.projections(),
                reconstructed.projections());

        assertEquals(original.tenantMetrics(),
                reconstructed.tenantMetrics());

        assertEquals(1, reconstructed.projections().size());

        var projection = reconstructed.projections().getFirst();

        assertEquals(event.path("eventId").asText(),
                projection.path("eventId").asText());

        assertEquals(event.path("documentId").asText(),
                projection.path("aggregateId").asText());

        var metrics = reconstructed.tenantMetrics();

        assertEquals(1, metrics.path("acceptedEvents").asLong(-1));
        assertEquals(1, metrics.path("documentEvents").asLong(-1));
        assertEquals(1, metrics.path("documentsCreated").asLong(-1));
        assertEquals(1, metrics.path("activeDocuments").asLong(-1));

        System.out.println(
                "CROSS_LANGUAGE_AUTHORITATIVE_REBUILD_PASSED");
    }

    private static String envelope(
            String tenant,
            UUID document,
            UUID event,
            String type,
            int version,
            int contentVersion,
            String occurredAt) throws Exception {

        var value = JSON.createObjectNode();

        value.put("schemaVersion", 1);
        value.put("eventId", event.toString());
        value.put("eventType", type);
        value.put("tenantId", tenant);
        value.put("documentId", document.toString());
        value.put("documentVersion", version);
        value.put("contentVersion", contentVersion);
        value.put("occurredAt", occurredAt);

        return JSON.writeValueAsString(value);
    }

    private Rebuild rebuild(
            List<Envelope> history,
            String tenant) throws Exception {

        return rebuild(history, tenant, 3);
    }

    private Rebuild rebuild(
            List<Envelope> history,
            String tenant,
            int expectedEvents) throws Exception {

        String application =
                "nexus-e738-rebuild-" + UUID.randomUUID();

        try (var fixture = new AcceptanceTopicFixture(BOOTSTRAP)) {

            var topics = fixture.topics();

            try {

                try (var producer =
                        new KafkaProducer<String, String>(
                                Map.of(
                                    ProducerConfig.BOOTSTRAP_SERVERS_CONFIG,
                                    BOOTSTRAP,
                                    ProducerConfig.ACKS_CONFIG, "all",
                                    ProducerConfig.ENABLE_IDEMPOTENCE_CONFIG,
                                    true),
                                new StringSerializer(),
                                new StringSerializer())) {

                    for (var event : history) {

                        producer.send(new ProducerRecord<>(
                                topics.documents(),
                                event.key(),
                                event.payload()))
                                .get(15, TimeUnit.SECONDS);
                    }
                }

                assertEquals(history, readSource(topics.documents()));

                var properties = StreamSettings.create(
                        BOOTSTRAP,
                        application,
                        state.resolve(application));

                properties.put("commit.interval.ms", 100);

                try (var streams = new KafkaStreams(
                        EventStreamTopology.build(topics),
                        properties)) {

                    streams.start();

                    long deadline = System.nanoTime()
                            + TimeUnit.SECONDS.toNanos(120);

                    while (System.nanoTime() < deadline) {

                        if (streams.state() == KafkaStreams.State.ERROR) {
                            fail("Kafka Streams entered ERROR");
                        }

                        var projections = readBounded(
                                topics.analytics());

                        var snapshots = readBounded(
                                topics.tenantAnalytics());

                        var dlq = readBounded(
                                topics.deadLetters());

                        assertTrue(dlq.isEmpty(),
                                "Unexpected dead-letter records");

                        if (projections.size() >= expectedEvents &&
                                !snapshots.isEmpty()) {

                            var metrics = JSON.readTree(
                                    snapshots.getLast().payload());

                            if (metrics.path("acceptedEvents")
                                    .asLong(-1) == expectedEvents) {

                                streams.close(Duration.ofSeconds(30));

                                var finalProjections =
                                        readBounded(topics.analytics());

                                var finalSnapshots =
                                        readBounded(topics.tenantAnalytics());

                                assertEquals(expectedEvents, finalProjections.size());

                                assertTrue(readBounded(
                                        topics.deadLetters()).isEmpty());

                                var values = new ArrayList<JsonNode>();

                                for (var item : finalProjections) {

                                    var value = JSON.readTree(
                                            item.payload());

                                    assertEquals(tenant,
                                            value.path("tenantId").asText());

                                    values.add(value);
                                }

                                values.sort(Comparator.comparingLong(
                                        value -> value.path("sequence")
                                                .asLong()));

                                assertEquals(
                                        java.util.stream.LongStream.rangeClosed(1, expectedEvents).boxed().toList(),
                                        values.stream()
                                                .map(value ->
                                                    value.path("sequence")
                                                        .asLong())
                                                .toList());

                                return new Rebuild(
                                        List.copyOf(values),
                                        JSON.readTree(
                                            finalSnapshots.getLast()
                                                .payload()));
                            }
                        }

                        Thread.sleep(500);
                    }

                    throw new AssertionError(
                            "Isolated rebuild timed out");
                }

            } finally {
                cleanupApplication(application);
            }
        }
    }

    private static List<Envelope> readSource(String topic)
            throws Exception {

        return readBounded(topic);
    }

    private static List<Envelope> readBounded(String topic)
            throws Exception {

        try (var consumer = new KafkaConsumer<String, String>(
                Map.of(
                    ConsumerConfig.BOOTSTRAP_SERVERS_CONFIG,
                    BOOTSTRAP,
                    ConsumerConfig.GROUP_ID_CONFIG,
                    "e738-reader-" + UUID.randomUUID(),
                    ConsumerConfig.ENABLE_AUTO_COMMIT_CONFIG,
                    false,
                    ConsumerConfig.ISOLATION_LEVEL_CONFIG,
                    "read_committed"),
                new StringDeserializer(),
                new StringDeserializer())) {

            var partitions = consumer.partitionsFor(
                    topic, Duration.ofSeconds(15))
                    .stream()
                    .map(info -> new TopicPartition(
                            topic, info.partition()))
                    .toList();

            consumer.assign(partitions);

            var beginning = consumer.beginningOffsets(partitions);
            var end = consumer.endOffsets(partitions);

            consumer.seekToBeginning(partitions);

            var result = new ArrayList<Envelope>();

            long deadline = System.nanoTime()
                    + TimeUnit.SECONDS.toNanos(30);

            while (true) {

                boolean complete = true;

                for (var partition : partitions) {

                    if (consumer.position(partition)
                            < end.get(partition)) {

                        complete = false;
                    }
                }

                if (complete) {
                    return result;
                }

                if (System.nanoTime() >= deadline) {
                    throw new IllegalStateException(
                            "Bounded Kafka read timed out");
                }

                for (var record :
                        consumer.poll(Duration.ofMillis(250))) {

                    var partition = new TopicPartition(
                            record.topic(), record.partition());

                    if (record.offset() >= beginning.get(partition)
                            && record.offset() < end.get(partition)) {

                        result.add(new Envelope(
                                record.key(), record.value()));
                    }
                }
            }
        }
    }

    private static void cleanupApplication(String application)
            throws Exception {

        try (var admin = Admin.create(Map.of(
                "bootstrap.servers", BOOTSTRAP))) {

            long deadline = System.nanoTime()
                    + TimeUnit.SECONDS.toNanos(90);

            while (System.nanoTime() < deadline) {

                var owned = admin.listTopics(
                        new org.apache.kafka.clients.admin
                                .ListTopicsOptions()
                                .listInternal(true))
                        .names().get(15, TimeUnit.SECONDS)
                        .stream()
                        .filter(name ->
                                name.startsWith(application + "-"))
                        .toList();

                var groups = admin.listConsumerGroups()
                        .all().get(15, TimeUnit.SECONDS)
                        .stream()
                        .filter(group ->
                                group.groupId().equals(application))
                        .toList();

                if (owned.isEmpty() && groups.isEmpty()) {
                    return;
                }

                if (!owned.isEmpty()) {
                    admin.deleteTopics(owned)
                            .all().get(30, TimeUnit.SECONDS);
                }

                if (!groups.isEmpty()) {

                    try {
                        admin.deleteConsumerGroups(
                                List.of(application))
                                .all().get(15, TimeUnit.SECONDS);
                    } catch (java.util.concurrent.ExecutionException ex) {

                        if (!(ex.getCause() instanceof
                                org.apache.kafka.common.errors
                                        .GroupNotEmptyException)) {
                            throw ex;
                        }
                    }
                }

                Thread.sleep(500);
            }

            fail("Application resource cleanup incomplete");
        }
    }
}
