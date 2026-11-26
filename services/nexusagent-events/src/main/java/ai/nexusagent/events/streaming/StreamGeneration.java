package ai.nexusagent.events.streaming;

import java.util.Objects;

public enum StreamGeneration {

    LEGACY(
            "v1",
            "nexusagent-events-v1",
            StreamTopics.production()),

    PROSPECTIVE(
            "v2",
            "nexusagent-events-v2",
            new StreamTopics(
                    "nexusagent.documents.v2",
                    "nexusagent.workflows.v2",
                    "nexusagent.analytics.v2",
                    "nexusagent.events.dlq.v2",
                    "nexusagent.tenant-analytics.v2"));

    private final String version;
    private final String applicationId;
    private final StreamTopics topics;

    StreamGeneration(
            String version,
            String applicationId,
            StreamTopics topics) {

        this.version = version;
        this.applicationId = applicationId;
        this.topics = topics;
    }

    public String version() {
        return version;
    }

    public String applicationId() {
        return applicationId;
    }

    public StreamTopics topics() {
        return topics;
    }

    public static StreamGeneration resolve(String version) {

        Objects.requireNonNull(version);

        for (var generation : values()) {

            if (generation.version.equals(version)) {
                return generation;
            }
        }

        throw new IllegalArgumentException(
                "Unsupported streaming generation");
    }

    public void requireBinding(
            String requestedApplicationId,
            StreamTopics requestedTopics) {

        if (!applicationId.equals(requestedApplicationId) ||
                !topics.equals(requestedTopics)) {

            throw new IllegalStateException(
                    "Cross-generation streaming binding denied");
        }
    }

    public static StreamGeneration requireBindingFor(
            String applicationId,
            StreamTopics topics) {

        for (var generation : values()) {

            if (generation.applicationId.equals(applicationId)) {

                generation.requireBinding(applicationId, topics);
                return generation;
            }
        }

        throw new IllegalStateException(
                "Unverified streaming application migration denied");
    }
}
