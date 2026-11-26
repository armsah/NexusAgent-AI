package ai.nexusagent.events.streaming;

import com.fasterxml.jackson.databind.ObjectMapper;

import org.apache.kafka.clients.consumer.KafkaConsumer;
import org.apache.kafka.common.TopicPartition;
import org.apache.kafka.common.serialization.StringDeserializer;

import java.time.Duration;
import java.util.HashMap;
import java.util.Map;
import java.util.Properties;
import java.util.TreeSet;

public final class SyntheticSnapshotInventory {

    private SyntheticSnapshotInventory() {}

    public static void main(String[] args) throws Exception {

        String bootstrap = args.length == 0
                ? "localhost:19092" : args[0];

        var properties = new Properties();

        properties.put("bootstrap.servers", bootstrap);
        properties.put("enable.auto.commit", "false");
        properties.put("isolation.level", "read_committed");
        properties.put("auto.offset.reset", "earliest");

        var json = new ObjectMapper();

        var snapshots =
                new HashMap<String,
                        com.fasterxml.jackson.databind.JsonNode>();

        try (var consumer = new KafkaConsumer<String, String>(
                properties,
                new StringDeserializer(),
                new StringDeserializer())) {

            var partitions = consumer.partitionsFor(
                            StreamSettings.TENANT_ANALYTICS)
                    .stream()
                    .map(info -> new TopicPartition(
                            info.topic(), info.partition()))
                    .toList();

            consumer.assign(partitions);

            var end = consumer.endOffsets(partitions);

            consumer.seekToBeginning(partitions);

            long deadline = System.nanoTime()
                    + java.util.concurrent.TimeUnit
                            .SECONDS.toNanos(60);

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
                            "Snapshot inventory incomplete");
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
                                "Snapshot key missing");
                    }

                    if (record.value() == null) {

                        snapshots.remove(record.key());
                        continue;
                    }

                    var snapshot = json.readTree(record.value());

                    if (!record.key().equals(
                            snapshot.path("tenantId").asText())) {

                        throw new IllegalStateException(
                                "Snapshot identity mismatch");
                    }

                    snapshots.put(record.key(), snapshot);
                }
            }
        }

        var candidates = new TreeSet<String>();

        for (String tenant : snapshots.keySet()) {

            if (tenant.matches(
                    "e73[67]-tenant-[a-f0-9]{32}")
                    || tenant.matches(
                            "e738-crash-[a-f0-9]{32}")) {

                candidates.add(tenant);
            }
        }

        System.out.println(
                "INVENTORY_JSON=" + json.writeValueAsString(
                        Map.of(
                                "observedSnapshots", snapshots.size(),
                                "candidateCount", candidates.size(),
                                "candidates", candidates,
                                "boundedInventoryComplete", true,
                                "authoritativeInventoryVerified", false,
                                "reconciliationAuthorized", false)));
    }
}
