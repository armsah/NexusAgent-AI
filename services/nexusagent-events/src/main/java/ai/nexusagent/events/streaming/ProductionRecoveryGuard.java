package ai.nexusagent.events.streaming;

import org.apache.kafka.clients.admin.Admin;
import org.apache.kafka.clients.admin.ListTopicsOptions;
import org.apache.kafka.clients.admin.OffsetSpec;
import org.apache.kafka.common.TopicPartition;

import java.util.*;
import java.util.concurrent.TimeUnit;

public final class ProductionRecoveryGuard {

    public static final String PRODUCTION_APPLICATION =
            "nexusagent-events-v1";

    private ProductionRecoveryGuard() {}

    public record Boundary(
            long beginning,
            long end,
            Long committed) {

        public Boundary {
            if (beginning < 0 || end < beginning) {
                throw new IllegalArgumentException(
                        "Invalid source boundaries");
            }
        }
    }

    public static void requireApplication(String applicationId) {

        if (!PRODUCTION_APPLICATION.equals(applicationId)) {
            throw new IllegalStateException(
                    "Unverified production application migration denied");
        }
    }

    public static void requireContinuation(
            Collection<Boundary> boundaries,
            boolean durableStoresPresent) {

        Objects.requireNonNull(boundaries);

        if (boundaries.isEmpty()) {
            throw new IllegalStateException(
                    "Missing source partition inventory");
        }

        if (!durableStoresPresent) {
            throw new IllegalStateException(
                    "Durable state history is incomplete");
        }

        for (Boundary boundary : boundaries) {

            if (boundary.committed() == null) {
                throw new IllegalStateException(
                        "Missing committed source checkpoint");
            }

            if (boundary.committed() < boundary.beginning()) {
                throw new IllegalStateException(
                        "Committed checkpoint precedes retained history");
            }

            if (boundary.committed() > boundary.end()) {
                throw new IllegalStateException(
                        "Committed checkpoint exceeds source history");
            }
        }
    }

    public static void verify(
            String bootstrap,
            String applicationId,
            StreamTopics topics) {

        requireApplication(applicationId);

        if (!StreamTopics.production().equals(topics)) {
            throw new IllegalArgumentException(
                    "Production guard requires production topics");
        }

        try (var admin = Admin.create(Map.of(
                "bootstrap.servers", bootstrap,
                "default.api.timeout.ms", 30000))) {

            var names = admin.listTopics(
                    new ListTopicsOptions().listInternal(true))
                    .names().get(30, TimeUnit.SECONDS);

            boolean storesPresent =
                    StreamRetentionPolicy.DURABLE_STORES.stream()
                            .allMatch(store -> names.contains(
                                    applicationId + "-" +
                                    store + "-changelog"));

            var descriptions = admin.describeTopics(
                    List.of(topics.documents(), topics.workflows()))
                    .allTopicNames().get(30, TimeUnit.SECONDS);

            var partitions = new HashSet<TopicPartition>();

            descriptions.forEach((name, description) ->
                    description.partitions().forEach(partition ->
                            partitions.add(new TopicPartition(
                                    name, partition.partition()))));

            var earliest = new HashMap<TopicPartition, OffsetSpec>();
            var latest = new HashMap<TopicPartition, OffsetSpec>();

            for (var partition : partitions) {
                earliest.put(partition, OffsetSpec.earliest());
                latest.put(partition, OffsetSpec.latest());
            }

            var beginning = admin.listOffsets(earliest)
                    .all().get(30, TimeUnit.SECONDS);

            var end = admin.listOffsets(latest)
                    .all().get(30, TimeUnit.SECONDS);

            var committed = admin.listConsumerGroupOffsets(
                    applicationId)
                    .partitionsToOffsetAndMetadata()
                    .get(30, TimeUnit.SECONDS);

            var boundaries = new ArrayList<Boundary>();

            for (var partition : partitions) {

                var checkpoint = committed.get(partition);

                boundaries.add(new Boundary(
                        beginning.get(partition).offset(),
                        end.get(partition).offset(),
                        checkpoint == null
                                ? null
                                : checkpoint.offset()));
            }

            requireContinuation(boundaries, storesPresent);

        } catch (InterruptedException ex) {

            Thread.currentThread().interrupt();

            throw new IllegalStateException(
                    "Production recovery verification interrupted", ex);

        } catch (java.util.concurrent.ExecutionException |
                 java.util.concurrent.TimeoutException ex) {

            throw new IllegalStateException(
                    "Production recovery evidence unavailable", ex);
        }
    }
}
