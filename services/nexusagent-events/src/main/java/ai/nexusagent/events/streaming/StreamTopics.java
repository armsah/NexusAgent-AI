package ai.nexusagent.events.streaming;

import java.util.HashSet;
import java.util.List;
import java.util.Objects;
import java.util.UUID;

public record StreamTopics(
        String documents,
        String workflows,
        String analytics,
        String deadLetters,
        String tenantAnalytics) {

    public StreamTopics {

        var names = List.of(
                documents, workflows, analytics,
                deadLetters, tenantAnalytics);

        for (String name : names) {

            Objects.requireNonNull(name);

            if (!name.matches("[A-Za-z0-9._-]{1,249}")
                    || name.equals(".")
                    || name.equals("..")) {

                throw new IllegalArgumentException(
                        "Invalid Kafka topic name");
            }
        }

        if (new HashSet<>(names).size() != names.size()) {

            throw new IllegalArgumentException(
                    "Streaming topics must be distinct");
        }
    }

    public static StreamTopics production() {

        return new StreamTopics(
                StreamSettings.DOCUMENTS,
                StreamSettings.WORKFLOWS,
                StreamSettings.ANALYTICS,
                StreamSettings.DEAD_LETTERS,
                StreamSettings.TENANT_ANALYTICS);
    }

    public static StreamTopics acceptance(UUID runId) {

        String prefix = acceptancePrefix(runId);

        return new StreamTopics(
                prefix + ".documents",
                prefix + ".workflows",
                prefix + ".analytics",
                prefix + ".dlq",
                prefix + ".tenant-analytics");
    }

    public static String acceptancePrefix(UUID runId) {

        Objects.requireNonNull(runId);

        if (runId.equals(new UUID(0, 0))) {

            throw new IllegalArgumentException(
                    "Zero run identifier");
        }

        return "nexus-acceptance-"
                + runId.toString().replace("-", "");
    }

    public List<String> all() {

        return List.of(
                documents, workflows, analytics,
                deadLetters, tenantAnalytics);
    }

    public boolean ownsAcceptanceTopic(
            UUID runId,
            String topic) {

        return equals(acceptance(runId))
                && all().contains(topic);
    }
}
