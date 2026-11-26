package ai.nexusagent.events.streaming;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.Timeout;
import org.junit.jupiter.api.io.TempDir;

import org.apache.kafka.clients.admin.Admin;
import org.apache.kafka.clients.admin.OffsetSpec;
import org.apache.kafka.common.TopicPartition;
import org.apache.kafka.streams.KafkaStreams;

import java.nio.file.Path;
import java.time.Duration;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.TimeUnit;

import static org.junit.jupiter.api.Assertions.*;

class LiveOperationalGenerationTest {

    @TempDir
    Path state;

    @Test
    @Timeout(300)
    void initializesOrContinuesProspectiveRuntime()
            throws Exception {

        String bootstrap = "localhost:19092";
        var generation = StreamGeneration.PROSPECTIVE;
        var topics = generation.topics();
        String app = generation.applicationId();

        BrokerRetentionGuard.verify(bootstrap, app, topics);

        PristineCheckpointInitializer.initializePristineCheckpoints(bootstrap, app, topics);

        var firstMode = ProductionRecoveryGuard.verifyStartup(
                bootstrap, app, topics, true, true);

        runRuntime(bootstrap, app, topics,
                state.resolve("initial"), firstMode);

        var nextMode = ProductionRecoveryGuard.verifyStartup(
                bootstrap, app, topics, false, true);

        assertEquals(
                StreamInitializationPolicy.Mode.CONTINUE_ESTABLISHED,
                nextMode);

        runRuntime(bootstrap, app, topics,
                state.resolve("restored"), nextMode);

        assertNotNull(CurrentTenantSnapshotReader.read(
                bootstrap, generation, topics.tenantAnalytics()));

        assertThrows(IllegalStateException.class,
                () -> CurrentTenantSnapshotReader.read(
                        bootstrap,
                        StreamGeneration.LEGACY,
                        StreamGeneration.LEGACY
                                .topics().tenantAnalytics()));

        System.out.println(
                "LIVE_OPERATIONAL_GENERATION_ACCEPTANCE_PASSED");
    }

    private static void runRuntime(
            String bootstrap,
            String app,
            StreamTopics topics,
            Path directory,
            StreamInitializationPolicy.Mode mode)
            throws Exception {

        var settings = StreamSettings.create(
                bootstrap, app, directory);

        settings.put("auto.offset.reset",
                StreamInitializationPolicy.offsetReset(mode));

        settings.put("commit.interval.ms", 100);

        var streams = new KafkaStreams(
                EventStreamTopology.build(topics), settings);

        try (var admin = Admin.create(Map.of(
                "bootstrap.servers", bootstrap))) {

            streams.start();

            long deadline = System.nanoTime()
                    + Duration.ofSeconds(120).toNanos();

            while (System.nanoTime() < deadline) {

                if (streams.state() == KafkaStreams.State.ERROR) {
                    fail("Operational Streams runtime failed");
                }

                if (streams.state() == KafkaStreams.State.RUNNING
                        && checkpointsComplete(admin, app, topics)) {
                    return;
                }

                Thread.sleep(500);
            }

            fail("Operational checkpoints incomplete");

        } finally {
            assertTrue(streams.close(Duration.ofSeconds(30)));
        }
    }

    private static boolean checkpointsComplete(
            Admin admin,
            String app,
            StreamTopics topics) throws Exception {

        var names = java.util.List.of(
                topics.documents(),
                topics.workflows(),
                app + "-event-identity-v1-repartition",
                app + "-aggregate-order-v1-repartition",
                app + "-tenant-analytics-v1-repartition");

        var available = admin.listTopics(
                new org.apache.kafka.clients.admin.ListTopicsOptions()
                        .listInternal(true))
                .names().get(15, TimeUnit.SECONDS);

        if (!available.containsAll(names)) {
            return false;
        }

        var descriptions = admin.describeTopics(names)
                .allTopicNames().get(15, TimeUnit.SECONDS);

        var requested = new HashMap<TopicPartition, OffsetSpec>();

        descriptions.forEach((name, description) ->
                description.partitions().forEach(partition ->
                        requested.put(new TopicPartition(
                                name, partition.partition()),
                                OffsetSpec.latest())));

        var end = admin.listOffsets(requested)
                .all().get(15, TimeUnit.SECONDS);

        var committed = admin.listConsumerGroupOffsets(app)
                .partitionsToOffsetAndMetadata()
                .get(15, TimeUnit.SECONDS);

        return !requested.isEmpty()
                && requested.keySet().stream().allMatch(
                        partition -> committed.containsKey(partition)
                                && committed.get(partition).offset()
                                == end.get(partition).offset());
    }
}
