package ai.nexusagent.events.streaming;

import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;

import org.apache.kafka.clients.admin.Admin;
import org.apache.kafka.clients.admin.AdminClientConfig;
import org.apache.kafka.clients.admin.NewTopic;
import org.apache.kafka.clients.consumer.ConsumerConfig;
import org.apache.kafka.clients.consumer.KafkaConsumer;
import org.apache.kafka.clients.producer.KafkaProducer;
import org.apache.kafka.clients.producer.ProducerConfig;
import org.apache.kafka.clients.producer.ProducerRecord;
import org.apache.kafka.clients.producer.RecordMetadata;
import org.apache.kafka.common.TopicPartition;
import org.apache.kafka.common.serialization.StringDeserializer;
import org.apache.kafka.common.serialization.StringSerializer;
import org.apache.kafka.streams.KafkaStreams;
import org.apache.kafka.streams.StreamsConfig;
import org.apache.kafka.streams.errors.StreamsUncaughtExceptionHandler;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.Timeout;
import org.junit.jupiter.api.io.TempDir;

import java.nio.file.Path;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Properties;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

class LiveTransactionCrashTest {

    private static final ObjectMapper JSON = new ObjectMapper();

    private static final String BOOTSTRAP =
            System.getenv().getOrDefault(
                    "NEXUS_TEST_KAFKA_BOOTSTRAP",
                    "localhost:19092");

    @TempDir
    Path state;

    private record ExpectedFailure(
            String reason,
            RecordMetadata metadata) {}

    @Test
    @Timeout(value = 360, unit = TimeUnit.SECONDS)
    void killedJvmCannotCommitPartialProjection()
            throws Exception {

        String suffix = UUID.randomUUID()
                .toString().replace("-", "");

        String appId = "nexus-e738-crash-" + suffix;
        String tenant = "e738-crash-" + suffix;

        UUID aggregate = UUID.randomUUID();
        UUID eventId = UUID.randomUUID();
        UUID successorId = UUID.randomUUID();

        String key = StreamIdentity.aggregateKey(
                tenant, aggregate);

        String event = document(
                tenant, aggregate, eventId,
                1, "document.created");

        Path marker = state.resolve("transaction-open");
        Path initial = state.resolve("initial");
        Path restored = state.resolve("restored");

        java.nio.file.Files.createDirectories(initial);
        java.nio.file.Files.createDirectories(restored);

        Properties adminProperties = new Properties();

        adminProperties.put(
                AdminClientConfig.BOOTSTRAP_SERVERS_CONFIG,
                BOOTSTRAP);

        Properties producerProperties = new Properties();

        producerProperties.put(
                ProducerConfig.BOOTSTRAP_SERVERS_CONFIG,
                BOOTSTRAP);

        producerProperties.put(
                ProducerConfig.KEY_SERIALIZER_CLASS_CONFIG,
                StringSerializer.class.getName());

        producerProperties.put(
                ProducerConfig.VALUE_SERIALIZER_CLASS_CONFIG,
                StringSerializer.class.getName());

        producerProperties.put(
                ProducerConfig.ACKS_CONFIG, "all");

        producerProperties.put(
                ProducerConfig.ENABLE_IDEMPOTENCE_CONFIG, true);

        producerProperties.put(
                ProducerConfig.DELIVERY_TIMEOUT_MS_CONFIG, 30000);

        producerProperties.put(
                ProducerConfig.REQUEST_TIMEOUT_MS_CONFIG, 10000);

        Process child = null;

        AtomicReference<Throwable> failure =
                new AtomicReference<>();

        try (Admin admin = Admin.create(adminProperties);
             KafkaProducer<String, String> producer =
                     new KafkaProducer<>(producerProperties);
             KafkaConsumer<String, String> committed =
                     observer(appId, "read_committed");
             KafkaConsumer<String, String> uncommitted =
                     observer(appId, "read_uncommitted")) {

            assignOutputs(admin, committed);
            assignOutputs(admin, uncommitted);

            child = launch(
                    appId, initial, eventId, marker, true);

            awaitFile(child, initial.resolve("probe-ready"), 45);

            // Establish committed source and repartition offsets
            // before introducing the crash-target event.
            awaitPipeline(
                    producer, committed, tenant, failure);

            send(producer, StreamSettings.DOCUMENTS,
                    key, event);

            awaitFile(child, marker, 30);

            assertTrue(child.isAlive());

            String coordinate =
                    java.nio.file.Files.readString(marker);

            assertTrue(coordinate.startsWith(
                    StreamSettings.ANALYTICS + ":"));

            // Prove the target output physically reached Kafka.
            boolean rawOutputFound = false;

            Instant rawDeadline =
                    Instant.now().plusSeconds(5);

            while (Instant.now().isBefore(rawDeadline)
                    && !rawOutputFound) {

                for (var record :
                        uncommitted.poll(Duration.ofMillis(100))) {

                    if (record.topic().equals(
                            StreamSettings.ANALYTICS)
                            && record.value() != null
                            && record.value().contains(
                                    eventId.toString())) {

                        String actual = record.topic() + ":"
                                + record.partition() + ":"
                                + record.offset();

                        assertEquals(coordinate, actual);
                        rawOutputFound = true;
                    }
                }
            }

            assertTrue(rawOutputFound,
                    "Uncommitted physical output not found");

            // The same output must not be visible to committed readers.
            Instant visibilityDeadline =
                    Instant.now().plusSeconds(1);

            while (Instant.now().isBefore(visibilityDeadline)) {
                for (var record :
                        committed.poll(Duration.ofMillis(100))) {

                    assertFalse(record.value() != null
                            && record.value().contains(
                                    eventId.toString()),
                            "Partial transaction became visible");
                }
            }

            System.out.println(
                    "CRASH: acknowledged uncommitted output verified");

            child.destroyForcibly();

            assertTrue(child.waitFor(15, TimeUnit.SECONDS),
                    "Crash JVM did not terminate");

            assertFalse(child.isAlive());
            assertNotEquals(0, child.exitValue());

            child = null;

            System.out.println(
                    "CRASH: separate JVM forcibly terminated");

            // Same application ID, new local state directory.
            // Kafka must resolve the abandoned transaction and
            // replay the uncommitted processing boundary.
            child = launch(
                    appId, restored, eventId, marker, false);

            awaitFile(child, restored.resolve("probe-ready"), 90);

            Map<String, JsonNode> projections = new HashMap<>();

            awaitProjection(
                    committed, tenant, eventId,
                    projections, failure);

            assertEquals(1, projections.size());

            // Replay accepted identity and then advance the aggregate.
            send(producer, StreamSettings.DOCUMENTS,
                    key, event);

            send(producer, StreamSettings.DOCUMENTS,
                    key, document(
                            tenant, aggregate, successorId,
                            2, "document.updated"));

            boolean metricsVerified = false;

            Instant deadline =
                    Instant.now().plusSeconds(60);

            while (Instant.now().isBefore(deadline)
                    && (!projections.containsKey(
                            successorId.toString())
                        || !metricsVerified)) {

                assertTrue(child.isAlive(),
                        "Recovery JVM terminated unexpectedly");

                for (var record :
                        committed.poll(Duration.ofMillis(250))) {

                    if (record.topic().equals(
                            StreamSettings.ANALYTICS)) {

                        collectProjection(
                                projections, tenant,
                                record.value());

                    } else if (record.topic().equals(
                            StreamSettings.TENANT_ANALYTICS)
                            && tenant.equals(record.key())) {

                        JsonNode metrics =
                                JSON.readTree(record.value());

                        long count = metrics.path(
                                "acceptedEvents").asLong();

                        assertTrue(count <= 2);

                        if (count == 2) {
                            assertEquals(1,
                                    metrics.path(
                                            "documentsCreated")
                                            .asLong());

                            assertEquals(1,
                                    metrics.path(
                                            "documentsUpdated")
                                            .asLong());

                            assertEquals(1,
                                    metrics.path(
                                            "activeDocuments")
                                            .asLong());

                            metricsVerified = true;
                        }

                    } else if (record.topic().equals(
                            StreamSettings.DEAD_LETTERS)) {

                        // No valid crash-target event should be rejected.
                        assertFalse(record.value() != null
                                && record.value().contains(
                                        eventId.toString()));
                    }
                }
            }

            assertTrue(metricsVerified);

            assertEquals(Set.of(
                    eventId.toString(),
                    successorId.toString()),
                    projections.keySet());

            assertEquals(1,
                    projections.get(eventId.toString())
                            .path("sequence").asLong());

            assertEquals(2,
                    projections.get(successorId.toString())
                            .path("sequence").asLong());

            // Observe additional committed output to detect replay
            // duplicates after the final expected projection.
            Instant quietDeadline =
                    Instant.now().plusSeconds(3);

            while (Instant.now().isBefore(quietDeadline)) {
                for (var record :
                        committed.poll(Duration.ofMillis(250))) {

                    if (record.topic().equals(
                            StreamSettings.ANALYTICS)) {

                        collectProjection(
                                projections, tenant,
                                record.value());

                    } else if (record.topic().equals(
                            StreamSettings.TENANT_ANALYTICS)
                            && tenant.equals(record.key())) {

                        assertEquals(2,
                                JSON.readTree(record.value())
                                        .path("acceptedEvents")
                                        .asLong());
                    }
                }
            }

            assertEquals(2, projections.size());

            System.out.println(
                    "CRASH: committed replay and duplicate suppression PASSED");

            System.out.println(
                    "CRASH: aggregate and tenant state consistency PASSED");

            System.out.println(
                    "E738-01 FORCED JVM TRANSACTION RECOVERY PASSED");

        } finally {
            try {
                if (child != null && child.isAlive()) {
                    child.destroy();

                    if (!child.waitFor(20, TimeUnit.SECONDS)) {
                        child.destroyForcibly();

                        assertTrue(child.waitFor(
                                15, TimeUnit.SECONDS));
                    }
                }
            } finally {
                cleanupApplication(appId);
            }
        }
    }

    private KafkaConsumer<String, String> observer(
            String appId, String isolation) {

        Properties properties = new Properties();

        properties.put(
                ConsumerConfig.BOOTSTRAP_SERVERS_CONFIG,
                BOOTSTRAP);

        properties.put(
                ConsumerConfig.GROUP_ID_CONFIG,
                appId + "-observer");

        properties.put(
                ConsumerConfig.KEY_DESERIALIZER_CLASS_CONFIG,
                StringDeserializer.class.getName());

        properties.put(
                ConsumerConfig.VALUE_DESERIALIZER_CLASS_CONFIG,
                StringDeserializer.class.getName());

        properties.put(
                ConsumerConfig.ENABLE_AUTO_COMMIT_CONFIG, false);

        properties.put(
                ConsumerConfig.ISOLATION_LEVEL_CONFIG,
                isolation);

        properties.put(
                ConsumerConfig.AUTO_OFFSET_RESET_CONFIG,
                "latest");

        return new KafkaConsumer<>(properties);
    }

    private static void assignOutputs(
            Admin admin,
            KafkaConsumer<String, String> consumer)
            throws Exception {

        var descriptions = admin.describeTopics(Set.of(
                StreamSettings.ANALYTICS,
                StreamSettings.TENANT_ANALYTICS,
                StreamSettings.DEAD_LETTERS))
                .allTopicNames().get(20, TimeUnit.SECONDS);

        List<TopicPartition> partitions = new ArrayList<>();

        descriptions.forEach((topic, description) ->
                description.partitions().forEach(partition ->
                        partitions.add(new TopicPartition(
                                topic, partition.partition()))));

        consumer.assign(partitions);
        consumer.seekToEnd(partitions);

        for (TopicPartition partition : partitions) {
            consumer.position(partition);
        }
    }

    private Process launch(
            String appId, Path directory,
            UUID eventId, Path marker,
            boolean crashMode) throws Exception {

        String executable = Path.of(
                System.getProperty("java.home"),
                "bin", "java.exe").toString();

        String classpath = System.getProperty(
                "surefire.test.class.path",
                System.getProperty("java.class.path"));

        Path log = directory.resolve("probe.log");

        return new ProcessBuilder(
                executable,
                "-cp", classpath,
                TransactionCrashProbe.class.getName(),
                BOOTSTRAP,
                appId,
                directory.toAbsolutePath().toString(),
                eventId.toString(),
                marker.toAbsolutePath().toString(),
                Boolean.toString(crashMode))
                .redirectErrorStream(true)
                .redirectOutput(log.toFile())
                .start();
    }

    private static void awaitFile(
            Process process, Path file, int seconds)
            throws Exception {

        Instant deadline =
                Instant.now().plusSeconds(seconds);

        while (Instant.now().isBefore(deadline)) {
            assertTrue(process.isAlive(),
                    "Probe JVM exited before marker: " + file);

            if (java.nio.file.Files.exists(file)
                    && java.nio.file.Files.size(file) > 0) {
                return;
            }

            Thread.sleep(100);
        }

        fail("Probe marker timeout: " + file);
    }

    private static String document(
            String tenant, UUID aggregate, UUID eventId,
            int sequence, String type) {

        ObjectNode event = JSON.createObjectNode();

        event.put("schemaVersion", 1);
        event.put("eventId", eventId.toString());
        event.put("tenantId", tenant);
        event.put("documentId", aggregate.toString());
        event.put("documentVersion", sequence);
        event.put("contentVersion", sequence);
        event.put("eventType", type);
        event.put("occurredAt", "2026-01-01T00:00:00Z");

        return event.toString();
    }

    private static String workflow(
            String tenant, UUID aggregate, UUID eventId,
            long sequence, String type) {

        ObjectNode event = JSON.createObjectNode();

        event.put("schemaVersion", 1);
        event.put("eventId", eventId.toString());
        event.put("tenantId", tenant);
        event.put("workflowId", aggregate.toString());
        event.put("sequence", sequence);
        event.put("eventType", type);
        event.put("occurredAt", "2026-01-01T00:00:00Z");

        if (Set.of("COMPLETED", "FAILED",
                "REJECTED", "CANCELLED").contains(type)) {

            event.put("durationMillis", 0L);

        } else {
            event.putNull("durationMillis");
        }

        return event.toString();
    }

    private static RecordMetadata send(
            KafkaProducer<String, String> producer,
            String topic, String key, String value)
            throws Exception {

        return producer.send(
                new ProducerRecord<>(topic, key, value))
                .get(20, TimeUnit.SECONDS);
    }

    private static String coordinate(RecordMetadata metadata) {
        return metadata.topic() + ":"
                + metadata.partition() + ":"
                + metadata.offset();
    }

    private static void expect(
            Map<String, ExpectedFailure> expected,
            String reason, RecordMetadata metadata) {

        assertNull(expected.put(
                coordinate(metadata),
                new ExpectedFailure(reason, metadata)));
    }

    private static void verifyFailure(
            String value, ExpectedFailure expected,
            String secret) throws Exception {

        JsonNode failure = JSON.readTree(value);

        assertEquals(1, failure.path("schemaVersion").asInt());

        assertEquals(expected.reason(),
                failure.path("reason").asText());

        assertEquals(expected.metadata().topic(),
                failure.path("sourceTopic").asText());

        assertEquals(expected.metadata().partition(),
                failure.path("sourcePartition").asInt());

        assertEquals(expected.metadata().offset(),
                failure.path("sourceOffset").asLong());

        Set<String> actualFields = new HashSet<>();

        failure.fieldNames().forEachRemaining(actualFields::add);

        assertEquals(Set.of(
                "schemaVersion",
                "reason",
                "sourceTopic",
                "sourcePartition",
                "sourceOffset"), actualFields);

        assertFalse(value.contains(secret));
        assertFalse(failure.has("payload"));
        assertFalse(failure.has("tenantId"));
        assertFalse(failure.has("eventId"));
        assertFalse(failure.has("stackTrace"));
        assertFalse(failure.has("exception"));
        assertFalse(failure.has("key"));
    }

    private static void collectProjection(
            Map<String, JsonNode> projections,
            String tenant, String value) throws Exception {

        JsonNode projection = JSON.readTree(value);

        if (!tenant.equals(
                projection.path("tenantId").asText())) {
            return;
        }

        String eventId = projection.path("eventId").asText();

        assertNull(projections.put(eventId, projection),
                "Duplicate committed aggregate projection");
    }

    private static void awaitRunning(
            KafkaStreams streams,
            AtomicReference<Throwable> failure)
            throws Exception {

        Instant deadline = Instant.now().plusSeconds(45);

        while (Instant.now().isBefore(deadline)) {
            assertNull(failure.get(),
                    "Kafka Streams startup failure");

            if (streams.state() == KafkaStreams.State.RUNNING) {
                return;
            }

            Thread.sleep(200);
        }

        fail("Kafka Streams did not reach RUNNING");
    }

    private static void assertHealthy(
            KafkaStreams streams,
            AtomicReference<Throwable> failure) {

        assertNull(failure.get(),
                "Unexpected Kafka Streams runtime failure");

        assertTrue(
                streams.state() == KafkaStreams.State.RUNNING
                        || streams.state()
                        == KafkaStreams.State.REBALANCING,
                "Unexpected streams state: " + streams.state());
    }

    private static void awaitPipeline(
            KafkaProducer<String, String> producer,
            KafkaConsumer<String, String> observer,
            String tenant,
            AtomicReference<Throwable> failure)
            throws Exception {

        String probeTenant = tenant + "-probe";
        Instant deadline = Instant.now().plusSeconds(45);

        while (Instant.now().isBefore(deadline)) {
            assertNull(failure.get());

            UUID aggregate = UUID.randomUUID();

            send(producer, StreamSettings.DOCUMENTS,
                    StreamIdentity.aggregateKey(
                            probeTenant, aggregate),
                    document(probeTenant, aggregate,
                            UUID.randomUUID(), 1,
                            "document.created"));

            Instant attempt = Instant.now().plusSeconds(3);

            while (Instant.now().isBefore(attempt)) {
                for (var record :
                        observer.poll(Duration.ofMillis(250))) {

                    if (record.topic().equals(
                            StreamSettings.TENANT_ANALYTICS)
                            && probeTenant.equals(record.key())) {
                        return;
                    }
                }
            }
        }

        fail("Production topology readiness probe timed out");
    }

    private static void awaitProjection(
            KafkaConsumer<String, String> observer,
            String tenant, UUID eventId,
            Map<String, JsonNode> projections,
            AtomicReference<Throwable> failure)
            throws Exception {

        Instant deadline = Instant.now().plusSeconds(45);

        while (Instant.now().isBefore(deadline)) {
            assertNull(failure.get());

            for (var record :
                    observer.poll(Duration.ofMillis(250))) {

                if (record.topic().equals(
                        StreamSettings.ANALYTICS)) {

                    collectProjection(
                            projections, tenant, record.value());
                }
            }

            if (projections.containsKey(eventId.toString())) {
                return;
            }
        }

        fail("Expected initial projection not observed");
    }

    private static void awaitFailure(
            KafkaConsumer<String, String> observer,
            ExpectedFailure expected,
            String secret,
            AtomicReference<Throwable> failure)
            throws Exception {

        Instant deadline = Instant.now().plusSeconds(45);

        while (Instant.now().isBefore(deadline)) {
            assertNull(failure.get());

            for (var record :
                    observer.poll(Duration.ofMillis(250))) {

                if (record.topic().equals(
                        StreamSettings.DEAD_LETTERS)
                        && coordinate(expected.metadata())
                                .equals(record.key())) {

                    verifyFailure(
                            record.value(), expected, secret);

                    return;
                }
            }
        }

        fail("Expected lifecycle dead-letter record missing");
    }

    private static void cleanupApplication(String appId)
            throws Exception {

        Properties properties = new Properties();

        properties.put(
                AdminClientConfig.BOOTSTRAP_SERVERS_CONFIG,
                BOOTSTRAP);

        try (Admin admin = Admin.create(properties)) {
            Set<String> names = admin.listTopics()
                    .names().get(20, TimeUnit.SECONDS);

            List<String> internalTopics = names.stream()
                    .filter(name -> name.startsWith(appId + "-"))
                    .toList();

            if (!internalTopics.isEmpty()) {
                admin.deleteTopics(internalTopics)
                        .all().get(30, TimeUnit.SECONDS);
            }

            var groups = admin.listConsumerGroups()
                    .all().get(20, TimeUnit.SECONDS);

            List<String> testGroups = groups.stream()
                    .map(group -> group.groupId())
                    .filter(group -> group.equals(appId)
                            || group.equals(appId + "-observer"))
                    .toList();

            for (String group : testGroups) {
                Instant deadline = Instant.now().plusSeconds(75);

                while (true) {
                    try {
                        admin.deleteConsumerGroups(List.of(group))
                                .all().get(15, TimeUnit.SECONDS);

                        break;

                    } catch (java.util.concurrent.ExecutionException ex) {
                        Throwable cause = ex.getCause();

                        if (cause instanceof
                                org.apache.kafka.common.errors
                                        .GroupIdNotFoundException) {
                            break;
                        }

                        if (!(cause instanceof
                                org.apache.kafka.common.errors
                                        .GroupNotEmptyException)) {
                            throw ex;
                        }

                        if (!Instant.now().isBefore(deadline)) {
                            throw new IllegalStateException(
                                    "Acceptance consumer group "
                                            + "did not become empty",
                                    ex);
                        }

                        Thread.sleep(1000);
                    }
                }
            }

            var remainingGroups = admin.listConsumerGroups()
                    .all().get(20, TimeUnit.SECONDS);

            assertTrue(remainingGroups.stream()
                    .noneMatch(group ->
                            group.groupId().equals(appId)
                            || group.groupId().equals(
                                    appId + "-observer")),
                    "Acceptance consumer groups remain");
        }
    }
}
