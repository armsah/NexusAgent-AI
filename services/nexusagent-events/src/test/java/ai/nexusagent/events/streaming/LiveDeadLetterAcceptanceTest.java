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

class LiveDeadLetterAcceptanceTest {

    private static AcceptanceTopicFixture acceptanceFixture;
    private static StreamTopics acceptanceTopics;

    @org.junit.jupiter.api.BeforeAll
    static void initializeAcceptanceTopics() throws Exception {

        acceptanceFixture =
                new AcceptanceTopicFixture(BOOTSTRAP);

        acceptanceTopics = acceptanceFixture.topics();
    }

    @org.junit.jupiter.api.AfterAll
    static void removeAcceptanceTopics() throws Exception {

        if (acceptanceFixture != null) {
            acceptanceFixture.close();
        }
    }


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
    void productionTopologyRejectsPoisonEventsWithoutLeakingData()
            throws Exception {

        String suffix = UUID.randomUUID()
                .toString().replace("-", "");

        String appId = "nexus-e736-" + suffix;
        String tenant = "e736-tenant-" + suffix;
        String secret = "PRIVATE-SENTINEL-" + suffix;

        UUID aggregate = UUID.randomUUID();
        UUID workflow = UUID.randomUUID();
        UUID originalId = UUID.randomUUID();

        String documentKey =
                StreamIdentity.aggregateKey(tenant, aggregate);

        String workflowKey =
                StreamIdentity.aggregateKey(tenant, workflow);

        Properties adminProperties = new Properties();

        adminProperties.put(
                AdminClientConfig.BOOTSTRAP_SERVERS_CONFIG,
                BOOTSTRAP);

        adminProperties.put(
                AdminClientConfig.REQUEST_TIMEOUT_MS_CONFIG,
                10000);

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
                ProducerConfig.DELIVERY_TIMEOUT_MS_CONFIG, 15000);

        producerProperties.put(
                ProducerConfig.REQUEST_TIMEOUT_MS_CONFIG, 10000);

        Properties consumerProperties = new Properties();

        consumerProperties.put(
                ConsumerConfig.BOOTSTRAP_SERVERS_CONFIG,
                BOOTSTRAP);

        consumerProperties.put(
                ConsumerConfig.GROUP_ID_CONFIG,
                appId + "-observer");

        consumerProperties.put(
                ConsumerConfig.KEY_DESERIALIZER_CLASS_CONFIG,
                StringDeserializer.class.getName());

        consumerProperties.put(
                ConsumerConfig.VALUE_DESERIALIZER_CLASS_CONFIG,
                StringDeserializer.class.getName());

        consumerProperties.put(
                ConsumerConfig.ENABLE_AUTO_COMMIT_CONFIG, false);

        consumerProperties.put(
                ConsumerConfig.ISOLATION_LEVEL_CONFIG,
                "read_committed");

        consumerProperties.put(
                ConsumerConfig.AUTO_OFFSET_RESET_CONFIG,
                "latest");

        AtomicReference<Throwable> runtimeFailure =
                new AtomicReference<>();

        try (Admin admin = Admin.create(adminProperties);
             KafkaProducer<String, String> producer =
                     new KafkaProducer<>(producerProperties);
             KafkaConsumer<String, String> observer =
                     new KafkaConsumer<>(consumerProperties)) {

            Set<String> outputs = Set.of(
                    acceptanceTopics.deadLetters(),
                    acceptanceTopics.analytics(),
                    acceptanceTopics.tenantAnalytics());

            var descriptions = admin.describeTopics(outputs)
                    .allTopicNames().get(20, TimeUnit.SECONDS);

            List<TopicPartition> partitions = new ArrayList<>();

            descriptions.forEach((topic, description) ->
                    description.partitions().forEach(partition ->
                            partitions.add(new TopicPartition(
                                    topic, partition.partition()))));

            observer.assign(partitions);
            observer.seekToEnd(partitions);

            for (TopicPartition partition : partitions) {
                observer.position(partition);
            }

            Properties streamProperties = StreamSettings.create(
                    BOOTSTRAP, appId, state);

            streamProperties.put(
                    StreamsConfig.COMMIT_INTERVAL_MS_CONFIG, 100);

            // This acceptance application processes newly published
            // test events rather than rebuilding historical metrics.
            streamProperties.put(
                    ConsumerConfig.AUTO_OFFSET_RESET_CONFIG,
                    "latest");

            KafkaStreams streams = new KafkaStreams(
                    EventStreamTopology.build(acceptanceTopics), streamProperties);

            streams.setUncaughtExceptionHandler(error -> {
                runtimeFailure.compareAndSet(null, error);

                return StreamsUncaughtExceptionHandler
                        .StreamThreadExceptionResponse
                        .SHUTDOWN_CLIENT;
            });

            try {
                streams.start();

                awaitRunning(streams, runtimeFailure);

                // Wait until a valid probe traverses every production
                // repartition boundary before injecting poison events.
                awaitPipeline(
                        producer, observer, tenant,
                        runtimeFailure);

                Map<String, ExpectedFailure> expected =
                        new HashMap<>();

                Map<String, JsonNode> projections =
                        new HashMap<>();

                Set<String> observedFailures = new HashSet<>();

                String first = document(
                        tenant, aggregate, originalId,
                        1, "document.created");

                send(producer, acceptanceTopics.documents(),
                        documentKey, first);

                awaitProjection(
                        observer, tenant, originalId,
                        projections, runtimeFailure);

                expect(expected, "INVALID_CONTRACT_OR_KEY",
                        send(producer, acceptanceTopics.documents(),
                                documentKey,
                                "{\"secret\":\"" + secret));

                expect(expected, "EMPTY_PAYLOAD",
                        send(producer, acceptanceTopics.documents(),
                                documentKey, null));

                expect(expected, "EMPTY_PAYLOAD",
                        send(producer, acceptanceTopics.documents(),
                                documentKey, "   "));

                expect(expected, "PAYLOAD_TOO_LARGE",
                        send(producer, acceptanceTopics.documents(),
                                documentKey,
                                secret.repeat(1000)));

                expect(expected, "INVALID_CONTRACT_OR_KEY",
                        send(producer, acceptanceTopics.documents(),
                                "wrong-key-" + secret,
                                document(tenant, aggregate,
                                        UUID.randomUUID(), 2,
                                        "document.updated")));

                ObjectNode unknownField =
                        (ObjectNode) JSON.readTree(
                                document(tenant, aggregate,
                                        UUID.randomUUID(), 2,
                                        "document.updated"));

                unknownField.put("privateCredential", secret);

                expect(expected, "INVALID_CONTRACT_OR_KEY",
                        send(producer, acceptanceTopics.documents(),
                                documentKey, unknownField.toString()));

                UUID other = UUID.randomUUID();

                expect(expected, "GLOBAL_EVENT_ID_CONFLICT",
                        send(producer, acceptanceTopics.documents(),
                                StreamIdentity.aggregateKey(
                                        tenant, other),
                                document(tenant, other, originalId,
                                        1, "document.created")));

                expect(expected, "GLOBAL_EVENT_ID_CONFLICT",
                        send(producer, acceptanceTopics.workflows(),
                                workflowKey,
                                workflow(tenant, workflow,
                                        originalId, 1, "STARTED")));

                expect(expected, "SEQUENCE_CONFLICT",
                        send(producer, acceptanceTopics.documents(),
                                documentKey,
                                document(tenant, aggregate,
                                        UUID.randomUUID(), 1,
                                        "document.created")));

                expect(expected, "SEQUENCE_GAP_LIMIT_EXCEEDED",
                        send(producer, acceptanceTopics.documents(),
                                documentKey,
                                document(tenant, aggregate,
                                        UUID.randomUUID(), 2048,
                                        "document.updated")));

                UUID workflowStart = UUID.randomUUID();

                send(producer, acceptanceTopics.workflows(),
                        workflowKey,
                        workflow(tenant, workflow,
                                workflowStart, 1, "STARTED"));

                // Collect failures before sending subsequent valid
                // revisions, avoiding ambiguity about arrival order.
                Instant deadline = Instant.now().plusSeconds(60);

                while (Instant.now().isBefore(deadline)
                        && (observedFailures.size() < expected.size()
                        || !projections.containsKey(
                                workflowStart.toString()))) {

                    assertHealthy(streams, runtimeFailure);

                    for (var record :
                            observer.poll(Duration.ofMillis(250))) {

                        if (record.topic().equals(
                                acceptanceTopics.deadLetters())) {

                            ExpectedFailure failure =
                                    expected.get(record.key());

                            if (failure == null) {
                                continue;
                            }

                            verifyFailure(
                                    record.value(), failure, secret);

                            assertTrue(observedFailures.add(
                                    record.key()),
                                    "Duplicate committed DLQ record");

                        } else if (record.topic().equals(
                                acceptanceTopics.analytics())) {

                            collectProjection(
                                    projections, tenant,
                                    record.value());
                        }
                    }
                }

                assertEquals(expected.keySet(), observedFailures,
                        "Missing committed dead-letter records");

                assertTrue(projections.containsKey(
                        workflowStart.toString()));

                // A lifecycle-invalid event must not poison the
                // aggregate or prevent a valid replacement.
                RecordMetadata invalidTransition = send(
                        producer, acceptanceTopics.workflows(),
                        workflowKey,
                        workflow(tenant, workflow,
                                UUID.randomUUID(), 2, "APPROVED"));

                ExpectedFailure transitionFailure =
                        new ExpectedFailure(
                                "INVALID_WORKFLOW_TRANSITION",
                                invalidTransition);

                awaitFailure(observer, transitionFailure,
                        secret, runtimeFailure);

                UUID updateId = UUID.randomUUID();
                UUID completedId = UUID.randomUUID();

                String update = document(
                        tenant, aggregate, updateId,
                        2, "document.updated");

                send(producer, acceptanceTopics.documents(),
                        documentKey, update);

                send(producer, acceptanceTopics.documents(),
                        documentKey, update);

                send(producer, acceptanceTopics.workflows(),
                        workflowKey,
                        workflow(tenant, workflow,
                                completedId, 2, "COMPLETED"));

                boolean finalMetricsObserved = false;

                Instant finalDeadline =
                        Instant.now().plusSeconds(60);

                while (Instant.now().isBefore(finalDeadline)
                        && (!projections.containsKey(
                                updateId.toString())
                        || !projections.containsKey(
                                completedId.toString())
                        || !finalMetricsObserved)) {

                    assertHealthy(streams, runtimeFailure);

                    for (var record :
                            observer.poll(Duration.ofMillis(250))) {

                        if (record.topic().equals(
                                acceptanceTopics.analytics())) {

                            collectProjection(
                                    projections, tenant,
                                    record.value());

                        } else if (record.topic().equals(
                                acceptanceTopics.tenantAnalytics())
                                && tenant.equals(record.key())) {

                            JsonNode metrics =
                                    JSON.readTree(record.value());

                            if (metrics.path("documentsUpdated")
                                    .asLong() == 1
                                    && metrics.path(
                                            "workflowsCompleted")
                                            .asLong() == 1) {

                                assertEquals(0,
                                        metrics.path(
                                                "nonTerminalWorkflows")
                                                .asLong());

                                finalMetricsObserved = true;
                            }
                        }
                    }
                }

                assertTrue(projections.containsKey(
                        updateId.toString()),
                        "Valid document successor missing");

                assertTrue(projections.containsKey(
                        completedId.toString()),
                        "Valid workflow successor missing");

                assertTrue(finalMetricsObserved,
                        "Tenant analytics did not recover");

                assertEquals(2, projections.get(
                        updateId.toString()).path("sequence").asLong());

                assertEquals(2, projections.get(
                        completedId.toString())
                        .path("sequence").asLong());

                assertHealthy(streams, runtimeFailure);

                System.out.println(
                        "LIVE DLQ ACCEPTANCE: "
                                + (expected.size() + 1)
                                + " rejected records verified");

                System.out.println(
                        "LIVE DLQ ACCEPTANCE: source coordinates, "
                                + "redaction and valid successors PASSED");

            } finally {
                assertTrue(streams.close(Duration.ofSeconds(30)),
                        "Acceptance streams shutdown timed out");
            }

        } finally {
            cleanupApplication(appId);
        }
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

            send(producer, acceptanceTopics.documents(),
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
                            acceptanceTopics.tenantAnalytics())
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
                        acceptanceTopics.analytics())) {

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
                        acceptanceTopics.deadLetters())
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
