package ai.nexusagent.events.streaming;

import java.nio.file.Path;
import java.util.Properties;
import org.apache.kafka.common.serialization.Serdes;
import org.apache.kafka.streams.StreamsConfig;

public final class StreamSettings {
    public static final String DOCUMENTS = "nexusagent.documents.v1";
    public static final String WORKFLOWS = "nexusagent.workflows.v1";
    public static final String ANALYTICS = "nexusagent.analytics.v1";
    public static final String DEAD_LETTERS = "nexusagent.events.dlq.v1";

    private StreamSettings() {}

    public static Properties create(
            String bootstrapServers,
            String applicationId,
            Path stateDirectory) {

        if (bootstrapServers == null || bootstrapServers.isBlank()
                || applicationId == null || applicationId.isBlank()
                || stateDirectory == null) {
            throw new IllegalArgumentException(
                    "Bootstrap servers, application ID and state directory are required.");
        }

        var properties = new Properties();

        properties.put(StreamsConfig.BOOTSTRAP_SERVERS_CONFIG,
                bootstrapServers);
        properties.put(StreamsConfig.APPLICATION_ID_CONFIG,
                applicationId);
        properties.put(StreamsConfig.STATE_DIR_CONFIG,
                stateDirectory.toAbsolutePath().toString());

        properties.put(StreamsConfig.PROCESSING_GUARANTEE_CONFIG,
                StreamsConfig.EXACTLY_ONCE_V2);
        properties.put(StreamsConfig.DEFAULT_KEY_SERDE_CLASS_CONFIG,
                Serdes.StringSerde.class.getName());
        properties.put(StreamsConfig.DEFAULT_VALUE_SERDE_CLASS_CONFIG,
                Serdes.StringSerde.class.getName());

        properties.put(StreamsConfig.REPLICATION_FACTOR_CONFIG, 1);
        properties.put(StreamsConfig.NUM_STREAM_THREADS_CONFIG, 1);
        properties.put("auto.offset.reset", "earliest");
        properties.put("allow.auto.create.topics", false);

        return properties;
    }
}
