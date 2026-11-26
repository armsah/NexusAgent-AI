package ai.nexusagent.events.streaming;

import org.apache.kafka.clients.admin.Admin;
import org.apache.kafka.clients.admin.AdminClientConfig;
import org.apache.kafka.clients.admin.NewTopic;
import org.apache.kafka.clients.admin.OffsetSpec;
import org.apache.kafka.common.TopicPartition;

import java.time.Duration;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.TimeUnit;

final class AcceptanceTopicFixture implements AutoCloseable {

    private final Admin admin;

    private final StreamTopics topics =
            StreamTopics.acceptance(UUID.randomUUID());

    private final Map<TopicPartition, Long> productionOffsets;

    AcceptanceTopicFixture(String bootstrap) throws Exception {

        admin = Admin.create(Map.of(
                AdminClientConfig.BOOTSTRAP_SERVERS_CONFIG,
                bootstrap));

        productionOffsets = productionEndOffsets();

        var created = new ArrayList<String>();

        try {

            for (String topic : topics.all()) {

                String cleanup = topic.equals(
                        topics.tenantAnalytics())
                        ? "compact" : "delete";

                var definition = new NewTopic(
                        topic, 3, (short) 1)
                        .configs(Map.of(
                                "cleanup.policy", cleanup,
                                "retention.ms", "-1",
                                "retention.bytes", "-1"));

                admin.createTopics(List.of(definition))
                        .all().get(30, TimeUnit.SECONDS);

                created.add(topic);
            }

            AcceptanceBrokerReadiness.await(
                    admin,
                    topics.all(),
                    Duration.ofSeconds(90));

        } catch (Exception ex) {

            try {
                if (!created.isEmpty()) {
                    admin.deleteTopics(created)
                            .all().get(30, TimeUnit.SECONDS);
                }
            } finally {
                admin.close(Duration.ofSeconds(5));
            }

            throw ex;
        }
    }

    StreamTopics topics() {
        return topics;
    }

    private Map<TopicPartition, Long> productionEndOffsets()
            throws Exception {

        var descriptions = admin.describeTopics(
                StreamTopics.production().all())
                .allTopicNames().get(30, TimeUnit.SECONDS);

        var request =
                new HashMap<TopicPartition, OffsetSpec>();

        for (var description : descriptions.values()) {

            for (var partition : description.partitions()) {

                request.put(
                        new TopicPartition(
                                description.name(),
                                partition.partition()),
                        OffsetSpec.latest());
            }
        }

        var offsets = admin.listOffsets(request)
                .all().get(30, TimeUnit.SECONDS);

        var result = new HashMap<TopicPartition, Long>();

        offsets.forEach((partition, offset) ->
                result.put(partition, offset.offset()));

        return Map.copyOf(result);
    }

    @Override
    public void close() throws Exception {

        try {

            admin.deleteTopics(topics.all())
                    .all().get(30, TimeUnit.SECONDS);

            long deadline = System.nanoTime()
                    + TimeUnit.SECONDS.toNanos(60);

            while (true) {

                var existing = admin.listTopics()
                        .names().get(15, TimeUnit.SECONDS);

                if (topics.all().stream()
                        .noneMatch(existing::contains)) {

                    break;
                }

                if (System.nanoTime() >= deadline) {

                    throw new IllegalStateException(
                            "Acceptance topic cleanup incomplete");
                }

                Thread.sleep(500);
            }

            if (!productionOffsets.equals(
                    productionEndOffsets())) {

                throw new IllegalStateException(
                        "Production topic offsets changed during acceptance");
            }

        } finally {
            admin.close(Duration.ofSeconds(5));
        }
    }
}
