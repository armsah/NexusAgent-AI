package ai.nexusagent.events.streaming;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import org.apache.kafka.clients.consumer.KafkaConsumer;
import org.apache.kafka.common.TopicPartition;
import org.apache.kafka.common.serialization.StringDeserializer;

import java.time.Duration;
import java.util.HashMap;
import java.util.Map;
import java.util.Properties;

public final class CurrentTenantSnapshotReader {

    private CurrentTenantSnapshotReader() {}

    public static Map<String, JsonNode> read(
            String bootstrap,
            StreamGeneration generation,
            String sourceTopic) throws Exception {

        HistoricalSnapshotDisposition.requireCurrentGeneration(
                generation, sourceTopic);

        var properties = new Properties();
        properties.put("bootstrap.servers", bootstrap);
        properties.put("enable.auto.commit", "false");
        properties.put("isolation.level", "read_committed");
        properties.put("auto.offset.reset", "none");

        var snapshots = new HashMap<String, JsonNode>();
        var json = new ObjectMapper();

        try (var consumer = new KafkaConsumer<String, String>(
                properties,
                new StringDeserializer(),
                new StringDeserializer())) {

            var partitions = consumer.partitionsFor(sourceTopic)
                    .stream()
                    .map(p -> new TopicPartition(
                            p.topic(), p.partition()))
                    .toList();

            if (partitions.isEmpty()) {
                throw new IllegalStateException(
                        "Missing snapshot partitions");
            }

            consumer.assign(partitions);

            var beginning = consumer.beginningOffsets(partitions);
            var end = consumer.endOffsets(partitions);

            for (var partition : partitions) {
                consumer.seek(partition, beginning.get(partition));
            }

            long deadline = System.nanoTime()
                    + Duration.ofSeconds(60).toNanos();

            while (true) {
                boolean complete = true;

                for (var partition : partitions) {
                    if (consumer.position(partition)
                            < end.get(partition)) {
                        complete = false;
                        break;
                    }
                }

                if (complete) {
                    break;
                }

                if (System.nanoTime() >= deadline) {
                    throw new IllegalStateException(
                            "Incomplete snapshot export");
                }

                for (var record :
                        consumer.poll(Duration.ofMillis(500))) {

                    var partition = new TopicPartition(
                            record.topic(), record.partition());

                    if (record.offset() >= end.get(partition)) {
                        continue;
                    }

                    if (record.key() == null) {
                        throw new IllegalStateException(
                                "Missing snapshot identity");
                    }

                    if (record.value() == null) {
                        snapshots.remove(record.key());
                        continue;
                    }

                    var snapshot = json.readTree(record.value());

                    if (!snapshot.isObject()
                            || !record.key().equals(
                            snapshot.path("tenantId").asText())
                            || snapshot.path("schemaVersion").asInt() != 1) {
                        throw new IllegalStateException(
                                "Invalid tenant snapshot");
                    }

                    snapshots.put(record.key(), snapshot);
                }
            }

            if (!beginning.equals(
                    consumer.beginningOffsets(partitions))
                    || !end.equals(
                    consumer.endOffsets(partitions))) {
                throw new IllegalStateException(
                        "Snapshot boundaries changed during export");
            }
        }

        return Map.copyOf(snapshots);
    }

    public static Map<String, JsonNode> readOperational(
            String bootstrap,
            StreamGeneration generation,
            String sourceTopic,
            java.nio.file.Path acceptanceRegistry)
            throws Exception {

        var excluded = AcceptanceTenantRegistry.read(
                acceptanceRegistry);

        var snapshots = new HashMap<>(
                read(bootstrap, generation, sourceTopic));

        excluded.forEach(snapshots::remove);

        return Map.copyOf(snapshots);
    }

    public static void main(String[] args) throws Exception {
        if (args.length != 3) {
            throw new IllegalArgumentException(
                    "Expected bootstrap servers, generation and acceptance registry");
        }

        var generation = StreamGeneration.resolve(args[1]);

        var snapshots = readOperational(
                args[0],
                generation,
                generation.topics().tenantAnalytics(),
                java.nio.file.Path.of(args[2]));

        System.out.println(new ObjectMapper().writeValueAsString(
                Map.of(
                        "generation", args[1],
                        "sourceTopic",
                        generation.topics().tenantAnalytics(),
                        "historicalCoverageComplete", false,
                        "snapshots", snapshots)));
    }
}
