package ai.nexusagent.events.streaming;

import org.apache.kafka.clients.admin.Admin;
import org.apache.kafka.clients.admin.AdminClientConfig;
import org.apache.kafka.common.config.ConfigResource;

import java.time.Duration;
import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.TimeUnit;

public final class BrokerRetentionGuard {

    private BrokerRetentionGuard() {}

    public static void verify(
            String bootstrap,
            String applicationId,
            StreamTopics topics) {

        try (Admin admin = Admin.create(Map.of(
                AdminClientConfig.BOOTSTRAP_SERVERS_CONFIG,
                bootstrap,
                AdminClientConfig.DEFAULT_API_TIMEOUT_MS_CONFIG,
                "30000"))) {

            var existing = admin.listTopics(
                    new org.apache.kafka.clients.admin.ListTopicsOptions()
                            .listInternal(true))
                    .names().get(30, TimeUnit.SECONDS);

            var resources = new HashSet<ConfigResource>();

            for (String topic : Set.of(
                    topics.documents(), topics.workflows())) {

                if (!existing.contains(topic)) {
                    throw new IllegalStateException(
                            "Required source topic missing: " + topic);
                }

                resources.add(resource(topic));
            }

            var changelogs = new HashMap<String, String>();

            for (String store :
                    StreamRetentionPolicy.DURABLE_STORES) {

                String topic = applicationId + "-"
                        + store + "-changelog";

                if (existing.contains(topic)) {
                    resources.add(resource(topic));
                    changelogs.put(store, topic);
                }
            }

            var configurations = admin.describeConfigs(resources)
                    .all().get(30, TimeUnit.SECONDS);

            for (String topic : Set.of(
                    topics.documents(), topics.workflows())) {

                StreamRetentionPolicy.requireSourcePolicy(
                        policy(configurations.get(resource(topic))));
            }

            for (String topic : changelogs.values()) {

                StreamRetentionPolicy.requireChangelogPolicy(
                        policy(configurations.get(resource(topic))));
            }

        } catch (InterruptedException ex) {

            Thread.currentThread().interrupt();

            throw new IllegalStateException(
                    "Retention verification interrupted", ex);

        } catch (Exception ex) {

            throw new IllegalStateException(
                    "Kafka retention verification failed: " + ex, ex);
        }
    }

    private static ConfigResource resource(String topic) {

        return new ConfigResource(
                ConfigResource.Type.TOPIC, topic);
    }

    private static StreamRetentionPolicy.TopicPolicy policy(
            org.apache.kafka.clients.admin.Config config) {

        if (config == null) {
            throw new IllegalStateException(
                    "Missing broker configuration");
        }

        return new StreamRetentionPolicy.TopicPolicy(
                Set.copyOf(Arrays.asList(
                        config.get("cleanup.policy")
                                .value().split(","))),
                Long.parseLong(
                        config.get("retention.ms").value()),
                Long.parseLong(
                        config.get("retention.bytes").value()));
    }
}
