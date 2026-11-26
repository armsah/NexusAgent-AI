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

class LiveTransactionalRecoveryTest {
    private static final java.util.Map<String, java.time.Instant>
            verifiedPendingObservations =
                    new java.util.concurrent.ConcurrentHashMap<>();


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
    @Timeout(value = 600, unit = TimeUnit.SECONDS)
    void restoresCommittedStateAfterBrokerInterruption()
            throws Exception {

        String suffix = UUID.randomUUID()
                .toString().replace("-", "");

        String appId = "nexus-e737-" + suffix;
        String tenant = "e737-tenant-" + suffix;

        UUID aggregate = UUID.randomUUID();
        UUID workflowId = UUID.randomUUID();

        UUID firstId = UUID.randomUUID();
        UUID secondId = UUID.randomUUID();
        UUID thirdId = UUID.randomUUID();
        UUID fourthId = UUID.randomUUID();

        UUID workflowStart = UUID.randomUUID();
        UUID workflowComplete = UUID.randomUUID();

        String key = StreamIdentity.aggregateKey(
                tenant, aggregate);

        String workflowKey = StreamIdentity.aggregateKey(
                tenant, workflowId);

        String first = document(
                tenant, aggregate, firstId,
                1, "document.created");

        String second = document(
                tenant, aggregate, secondId,
                2, "document.updated");

        String third = document(
                tenant, aggregate, thirdId,
                3, "document.updated");

        String fourth = document(
                tenant, aggregate, fourthId,
                4, "document.deleted");

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

        AtomicReference<Throwable> failure =
                new AtomicReference<>();

        Map<String, JsonNode> projections = new HashMap<>();

        Path firstDirectory = state.resolve("initial");
        Path restoredDirectory = state.resolve("restored");

        java.nio.file.Files.createDirectories(firstDirectory);

        assertFalse(java.nio.file.Files.exists(
                restoredDirectory));

        KafkaStreams streams = null;
        boolean brokerStopped = false;

        try (Admin admin = Admin.create(adminProperties);
             KafkaProducer<String, String> producer =
                     new KafkaProducer<>(producerProperties);
             KafkaConsumer<String, String> observer =
                     new KafkaConsumer<>(consumerProperties)) {

            Set<String> outputTopics = Set.of(
                    StreamSettings.ANALYTICS,
                    StreamSettings.TENANT_ANALYTICS,
                    StreamSettings.DEAD_LETTERS);

            var descriptions = admin.describeTopics(outputTopics)
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

            streams = startStreams(
                    appId, firstDirectory, failure);

            awaitPipeline(
                    producer, observer, tenant, failure);

            // Verify committed-input isolation before restoration.
            Properties transactionProperties = new Properties();
            transactionProperties.putAll(producerProperties);

            transactionProperties.put(
                    ProducerConfig.TRANSACTIONAL_ID_CONFIG,
                    appId + "-aborted-input");

            UUID abortedAggregate = UUID.randomUUID();
            UUID abortedId = UUID.randomUUID();

            try (KafkaProducer<String, String> transactional =
                         new KafkaProducer<>(transactionProperties)) {

                transactional.initTransactions();
                transactional.beginTransaction();

                send(transactional,
                        StreamSettings.DOCUMENTS,
                        StreamIdentity.aggregateKey(
                                tenant, abortedAggregate),
                        document(tenant, abortedAggregate,
                                abortedId, 1, "document.created"));

                transactional.abortTransaction();
            }

            send(producer, StreamSettings.DOCUMENTS,
                    key, first);

            send(producer, StreamSettings.WORKFLOWS,
                    workflowKey,
                    workflow(tenant, workflowId,
                            workflowStart, 1, "STARTED"));

            awaitMetrics(observer, tenant, projections,
                    failure, 2, 1, 1, 0);

            assertFalse(projections.containsKey(
                    abortedId.toString()));

            // Buffer sequence three while sequence two is missing.
            send(producer, StreamSettings.DOCUMENTS,
                    key, third);

            // Publish a barrier on the same source partition.
            // Its committed DLQ proves that source processing advanced.
            RecordMetadata barrier = send(
                    producer, StreamSettings.DOCUMENTS,
                    key, "{invalid-recovery-barrier");

            awaitFailure(observer,
                    new ExpectedFailure(
                            "INVALID_CONTRACT_OR_KEY", barrier),
                    "NO-SECRET", failure);

            // Confirm pending state actually exists in the production
            // persistent store before interrupting the broker.
            awaitPending(streams, thirdId);

            // Confirm all source and repartition offsets are committed
            // and stable before changing local state directories.
            awaitCommittedLagZero(admin, appId);

            assertFalse(projections.containsKey(
                    thirdId.toString()));

            System.out.println(
                    "RECOVERY: committed baseline and pending state PASSED");

            // Interrupt the actual local Kafka broker while Streams
            // remains running.
            brokerStopped = true;

            docker("stop", "--time", "10", "nexusagent-kafka");

            assertEquals("false",
                    docker("inspect", "-f",
                            "{{.State.Running}}",
                            "nexusagent-kafka").trim());

            Thread.sleep(2000);

            docker("start", "nexusagent-kafka");
            brokerStopped = false;

            awaitBroker(admin);

            assertNull(failure.get(),
                    "Unexpected failure during broker interruption");

            // Demonstrate the existing running client can resume
            // processing after broker recovery.
            UUID recoveryProbe = UUID.randomUUID();
            UUID recoveryEvent = UUID.randomUUID();

            String probeTenant = tenant + "-broker-recovery";

            send(producer, StreamSettings.DOCUMENTS,
                    StreamIdentity.aggregateKey(
                            probeTenant, recoveryProbe),
                    document(probeTenant, recoveryProbe,
                            recoveryEvent, 1, "document.created"));

            awaitProjection(
                    observer, probeTenant, recoveryEvent,
                    new HashMap<>(), failure);

            awaitCommittedLagZero(admin, appId);

            System.out.println(
                    "RECOVERY: actual broker stop/start PASSED");

            assertTrue(streams.close(Duration.ofSeconds(30)),
                    "Initial application shutdown timed out");

            streams = null;

            // The old directory is deliberately not reused.
            // Same application ID forces changelog restoration.
            assertFalse(java.nio.file.Files.exists(
                    restoredDirectory));

            java.nio.file.Files.createDirectories(
                    restoredDirectory);

            failure.set(null);

            streams = startStreams(
                    appId, restoredDirectory, failure);

            awaitPending(streams, thirdId);

            awaitRestoredMetrics(streams, tenant, 2);

            System.out.println(
                    "RECOVERY: fresh-directory changelog restoration PASSED");

            // Replay the already accepted event.
            send(producer, StreamSettings.DOCUMENTS,
                    key, first);

            // Reuse its identity for another aggregate.
            UUID conflictingAggregate = UUID.randomUUID();

            RecordMetadata conflict = send(
                    producer, StreamSettings.DOCUMENTS,
                    StreamIdentity.aggregateKey(
                            tenant, conflictingAggregate),
                    document(tenant, conflictingAggregate,
                            firstId, 1, "document.created"));

            // Verify the restored global identity registry.
            awaitFailure(observer,
                    new ExpectedFailure(
                            "GLOBAL_EVENT_ID_CONFLICT", conflict),
                    "NO-SECRET", failure);

            // Release the buffered third event by supplying
            // its missing predecessor.
            send(producer, StreamSettings.DOCUMENTS,
                    key, second);

            send(producer, StreamSettings.DOCUMENTS,
                    key, second);

            send(producer, StreamSettings.DOCUMENTS,
                    key, third);

            awaitMetrics(observer, tenant, projections,
                    failure, 4, 1, 1, 0);

            // Continue the restored document and workflow lifecycles.
            send(producer, StreamSettings.DOCUMENTS,
                    key, fourth);

            send(producer, StreamSettings.WORKFLOWS,
                    workflowKey,
                    workflow(tenant, workflowId,
                            workflowComplete, 2, "COMPLETED"));

            JsonNode finalMetrics = awaitMetrics(
                    observer, tenant, projections,
                    failure, 6, 0, 0, 1);

            assertEquals(1,
                    finalMetrics.path("documentsCreated").asLong());

            assertEquals(2,
                    finalMetrics.path("documentsUpdated").asLong());

            assertEquals(1,
                    finalMetrics.path("documentsDeleted").asLong());

            assertEquals(1,
                    finalMetrics.path("workflowsStarted").asLong());

            assertEquals(1,
                    finalMetrics.path("terminalWorkflows").asLong());

            assertEquals(1.0,
                    finalMetrics.path("completionRate").asDouble());

            awaitCommittedLagZero(admin, appId);

            // Drain any remaining committed output, ensuring
            // duplicates cannot escape after the final snapshot.
            Instant quietDeadline =
                    Instant.now().plusSeconds(3);

            while (Instant.now().isBefore(quietDeadline)) {
                for (var record :
                        observer.poll(Duration.ofMillis(250))) {

                    if (record.topic().equals(
                            StreamSettings.ANALYTICS)) {

                        collectProjection(
                                projections, tenant, record.value());

                    } else if (record.topic().equals(
                            StreamSettings.TENANT_ANALYTICS)
                            && tenant.equals(record.key())) {

                        JsonNode metrics =
                                JSON.readTree(record.value());

                        assertEquals(6,
                                metrics.path("acceptedEvents").asLong());
                    }
                }
            }

            assertEquals(Set.of(
                    firstId.toString(),
                    secondId.toString(),
                    thirdId.toString(),
                    fourthId.toString(),
                    workflowStart.toString(),
                    workflowComplete.toString()),
                    projections.keySet());

            assertEquals(2,
                    projections.get(secondId.toString())
                            .path("sequence").asLong());

            assertEquals(3,
                    projections.get(thirdId.toString())
                            .path("sequence").asLong());

            assertEquals(4,
                    projections.get(fourthId.toString())
                            .path("sequence").asLong());

            assertFalse(projections.containsKey(
                    abortedId.toString()));

            assertHealthy(streams, failure);

            System.out.println(
                    "RECOVERY: aborted transaction isolation PASSED");

            System.out.println(
                    "RECOVERY: restored global identity registry PASSED");

            System.out.println(
                    "RECOVERY: buffered sequence restoration PASSED");

            System.out.println(
                    "RECOVERY: tenant counters and duplicate suppression PASSED");

            System.out.println(
                    "LIVE TRANSACTIONAL RECOVERY ACCEPTANCE PASSED");

        } finally {
            try {
                if (brokerStopped) {
                    docker("start", "nexusagent-kafka");
                }
            } finally {
                try {
                    if (streams != null) {
                        assertTrue(streams.close(
                                Duration.ofSeconds(30)));
                    }
                } finally {
                    cleanupApplication(appId);
                }
            }
        }
    }

    private static KafkaStreams startStreams(
            String appId, Path directory,
            AtomicReference<Throwable> failure)
            throws Exception {

        Properties properties = StreamSettings.create(
                BOOTSTRAP, appId, directory);

        properties.put(
                StreamsConfig.COMMIT_INTERVAL_MS_CONFIG, 100);

        properties.put(
                ConsumerConfig.AUTO_OFFSET_RESET_CONFIG,
                "latest");

        properties.put(
                ConsumerConfig.ISOLATION_LEVEL_CONFIG,
                "read_committed");

        KafkaStreams streams = new KafkaStreams(
                EventStreamTopology.build(), properties);

        streams.setUncaughtExceptionHandler(error -> {
            failure.compareAndSet(null, error);

            return StreamsUncaughtExceptionHandler
                    .StreamThreadExceptionResponse
                    .SHUTDOWN_CLIENT;
        });

        try {
            streams.start();
            awaitRunning(streams, failure);
            return streams;

        } catch (Exception | AssertionError ex) {
            streams.close(Duration.ofSeconds(30));
            throw ex;
        }
    }

    private static String docker(String... arguments)
            throws Exception {

        List<String> command = new ArrayList<>();

        command.add("docker");
        command.addAll(List.of(arguments));

        Process process = new ProcessBuilder(command)
                .redirectErrorStream(true)
                .start();

        if (!process.waitFor(45, TimeUnit.SECONDS)) {
            process.destroyForcibly();
            fail("Docker command timed out");
        }

        String output = new String(
                process.getInputStream().readAllBytes(),
                java.nio.charset.StandardCharsets.UTF_8);

        assertEquals(0, process.exitValue(), output);

        return output;
    }

    private static void awaitBroker(Admin admin)
            throws Exception {

        Instant deadline = Instant.now().plusSeconds(60);

        while (Instant.now().isBefore(deadline)) {
            try {
                admin.describeCluster().nodes()
                        .get(5, TimeUnit.SECONDS);

                return;

            } catch (java.util.concurrent.ExecutionException
                     | java.util.concurrent.TimeoutException ex) {

                Thread.sleep(500);
            }
        }

        fail("Broker did not recover");
    }

    private static void awaitPending(
            KafkaStreams streams, UUID eventId)
            throws Exception {

        Instant deadline = Instant.now().plusSeconds(45);

        while (Instant.now().isBefore(deadline)) {
            try {
                var store = streams.store(
                        org.apache.kafka.streams.StoreQueryParameters
                                .fromNameAndType(
                                        "aggregate-pending-v1",
                                        org.apache.kafka.streams.state
                                                .QueryableStoreTypes
                                                .<String, String>keyValueStore()));

                try (var iterator = store.all()) {
                    while (iterator.hasNext()) {
                        var entry = iterator.next();

                        if (entry.value != null
                                && entry.value.contains(
                                        eventId.toString())) {

                            var observed = PendingGapObservation
                                    .observedAt(entry.value)
                                    .orElseThrow();

                            var previous = verifiedPendingObservations
                                    .putIfAbsent(eventId.toString(), observed);

                            if (previous != null) {
                                org.junit.jupiter.api.Assertions.assertEquals(
                                        previous, observed,
                                        "Pending observation changed after restoration");
                            }

                            org.junit.jupiter.api.Assertions.assertTrue(
                                    PendingGapAge.evaluate(
                                            observed,
                                            observed.plusSeconds(301),
                                            java.time.Duration.ofMinutes(5))
                                            .overdue());


                            return;
                        }
                    }
                }

            } catch (org.apache.kafka.streams.errors
                     .InvalidStateStoreException ex) {
                // Store restoration is still in progress.
            }

            Thread.sleep(200);
        }

        fail("Expected buffered event not found in persistent state");
    }

    private static void awaitRestoredMetrics(
            KafkaStreams streams, String tenant,
            long expectedEvents) throws Exception {

        Instant deadline = Instant.now().plusSeconds(45);

        while (Instant.now().isBefore(deadline)) {
            try {
                var store = streams.store(
                        org.apache.kafka.streams.StoreQueryParameters
                                .fromNameAndType(
                                        TenantAnalyticsProcessor.METRICS_STORE,
                                        org.apache.kafka.streams.state
                                                .QueryableStoreTypes
                                                .<String, String>keyValueStore()));

                String value = store.get(tenant);

                if (value != null) {
                    JsonNode metrics = JSON.readTree(value);

                    if (metrics.path("acceptedEvents").asLong()
                            == expectedEvents) {

                        assertEquals(1,
                                metrics.path("activeDocuments").asLong());

                        assertEquals(1,
                                metrics.path("nonTerminalWorkflows").asLong());

                        return;
                    }
                }

            } catch (org.apache.kafka.streams.errors
                     .InvalidStateStoreException ex) {
                // Store restoration is still in progress.
            }

            Thread.sleep(200);
        }

        fail("Expected restored tenant metrics missing");
    }

    private static void awaitCommittedLagZero(
            Admin admin, String appId) throws Exception {

        Instant deadline = Instant.now().plusSeconds(45);
        int stable = 0;

        while (Instant.now().isBefore(deadline)) {

            var offsets = admin.listConsumerGroupOffsets(appId)
                    .partitionsToOffsetAndMetadata()
                    .get(10, TimeUnit.SECONDS);

            if (offsets.isEmpty()) {
                Thread.sleep(250);
                continue;
            }

            Map<TopicPartition,
                    org.apache.kafka.clients.admin.OffsetSpec>
                    requests = new HashMap<>();

            offsets.keySet().forEach(partition ->
                    requests.put(partition,
                            org.apache.kafka.clients.admin
                                    .OffsetSpec.latest()));

            var latest = admin.listOffsets(requests)
                    .all().get(10, TimeUnit.SECONDS);

            boolean caughtUp = offsets.entrySet().stream()
                    .allMatch(entry ->
                            entry.getValue().offset()
                                    >= latest.get(entry.getKey()).offset());

            if (caughtUp) {
                stable++;

                if (stable >= 4) {
                    return;
                }
            } else {
                stable = 0;
            }

            Thread.sleep(250);
        }

        fail("Application offsets did not reach committed baseline");
    }

    private static JsonNode awaitMetrics(
            KafkaConsumer<String, String> observer,
            String tenant,
            Map<String, JsonNode> projections,
            AtomicReference<Throwable> failure,
            long expectedEvents,
            long activeDocuments,
            long nonTerminal,
            long completed) throws Exception {

        Instant deadline = Instant.now().plusSeconds(60);

        JsonNode matching = null;

        while (Instant.now().isBefore(deadline)) {
            assertNull(failure.get());

            for (var record :
                    observer.poll(Duration.ofMillis(250))) {

                if (record.topic().equals(
                        StreamSettings.ANALYTICS)) {

                    collectProjection(
                            projections, tenant, record.value());

                } else if (record.topic().equals(
                        StreamSettings.TENANT_ANALYTICS)
                        && tenant.equals(record.key())) {

                    JsonNode metrics =
                            JSON.readTree(record.value());

                    long count =
                            metrics.path("acceptedEvents").asLong();

                    assertTrue(count <= expectedEvents,
                            "Unexpected tenant counter increment");

                    if (count == expectedEvents) {

                        assertEquals(activeDocuments,
                                metrics.path("activeDocuments").asLong());

                        assertEquals(nonTerminal,
                                metrics.path(
                                        "nonTerminalWorkflows").asLong());

                        assertEquals(completed,
                                metrics.path(
                                        "workflowsCompleted").asLong());

                        matching = metrics;
                    }
                }
            }

            if (matching != null
                    && projections.size() == expectedEvents) {

                return matching;
            }
        }

        throw new AssertionError(
                "Expected tenant metrics not observed: "
                        + expectedEvents
                        + ", projections=" + projections.size());
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

        Instant deadline = Instant.now().plusSeconds(120);

        while (Instant.now().isBefore(deadline)) {
            assertNull(failure.get(),
                    "Kafka Streams startup failure");

            if (streams.state() == KafkaStreams.State.RUNNING) {
                return;
            }

            Thread.sleep(200);
        }

        fail("Kafka Streams did not reach RUNNING; final state=" + streams.state());
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
