package ai.nexusagent.events.streaming;

import org.apache.kafka.common.serialization.Serdes;
import org.apache.kafka.streams.Topology;
import org.apache.kafka.streams.state.Stores;

public final class StreamStateStores {
    public static final String DOCUMENT_IDENTITIES = "document-identities-v1";
    public static final String WORKFLOW_IDENTITIES = "workflow-identities-v1";
    public static final String DOCUMENT_SEQUENCES = "document-sequences-v1";
    public static final String WORKFLOW_SEQUENCES = "workflow-sequences-v1";
    public static final String DOCUMENT_PROJECTIONS = "document-projections-v1";
    public static final String WORKFLOW_PROJECTIONS = "workflow-projections-v1";

    private StreamStateStores() {}

    public static Topology register(
            Topology topology, String processor, String... names) {

        for (String name : names) {
            topology.addStateStore(
                    Stores.keyValueStoreBuilder(
                            Stores.persistentKeyValueStore(name),
                            Serdes.String(),
                            Serdes.String()),
                    processor);
        }

        return topology;
    }
}
