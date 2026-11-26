package ai.nexusagent.events.streaming;

import org.apache.kafka.clients.admin.Admin;
import org.apache.kafka.clients.admin.OffsetSpec;
import org.apache.kafka.common.TopicPartition;

import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.TimeUnit;

public final class PristineCheckpointInitializer {

    private PristineCheckpointInitializer() {}

    private static void requireEqual(long expected, long actual) {
        if (expected != actual) {
            throw new IllegalStateException(
                    "Checkpoint verification failed");
        }
    }

    public static void initializePristineCheckpoints(
            String bootstrap,
            String app,
            StreamTopics topics) throws Exception {

        try (var admin = Admin.create(Map.of(
                "bootstrap.servers", bootstrap))) {

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

            // A genuinely new application must initialize normally.
            if (!available.containsAll(names)) {
                return;
            }

            var group = admin.describeConsumerGroups(
                    java.util.List.of(app))
                    .describedGroups().get(app)
                    .get(15, TimeUnit.SECONDS);

            if (!group.members().isEmpty()) {
                throw new IllegalStateException(
                        "Active consumer group prevents initialization");
            }

            var descriptions = admin.describeTopics(names)
                    .allTopicNames().get(15, TimeUnit.SECONDS);

            var earliest = new HashMap<TopicPartition, OffsetSpec>();
            var latest = new HashMap<TopicPartition, OffsetSpec>();

            descriptions.forEach((name, description) ->
                    description.partitions().forEach(partition -> {
                        var tp = new TopicPartition(
                                name, partition.partition());

                        earliest.put(tp, OffsetSpec.earliest());
                        latest.put(tp, OffsetSpec.latest());
                    }));

            var beginning = admin.listOffsets(earliest)
                    .all().get(15, TimeUnit.SECONDS);

            var end = admin.listOffsets(latest)
                    .all().get(15, TimeUnit.SECONDS);

            var committed = admin.listConsumerGroupOffsets(app)
                    .partitionsToOffsetAndMetadata()
                    .get(15, TimeUnit.SECONDS);

            var missing = new HashMap<TopicPartition,
                    org.apache.kafka.clients.consumer.OffsetAndMetadata>();

            for (var partition : earliest.keySet()) {

                if (committed.containsKey(partition)) {
                    continue;
                }

                if (beginning.get(partition).offset() != 0
                        || end.get(partition).offset() != 0) {

                    throw new IllegalStateException(
                            "Missing checkpoint on nonempty partition: "
                                    + partition);
                }

                missing.put(partition,
                        new org.apache.kafka.clients.consumer
                                .OffsetAndMetadata(0));
            }

            if (!missing.isEmpty()) {
                admin.alterConsumerGroupOffsets(app, missing)
                        .all().get(30, TimeUnit.SECONDS);

                var verified = admin.listConsumerGroupOffsets(app)
                        .partitionsToOffsetAndMetadata()
                        .get(15, TimeUnit.SECONDS);

                for (var partition : missing.keySet()) {
                    requireEqual(0L,
                            verified.get(partition).offset());
                }

                for (var entry : committed.entrySet()) {
                    requireEqual(entry.getValue().offset(),
                            verified.get(entry.getKey()).offset());
                }
            }

            System.out.println(
                    "PRISTINE_CHECKPOINTS_INITIALIZED="
                            + missing.size());
        }
    }


}
