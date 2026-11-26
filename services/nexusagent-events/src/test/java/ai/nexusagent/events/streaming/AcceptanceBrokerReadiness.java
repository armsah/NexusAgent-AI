package ai.nexusagent.events.streaming;

import org.apache.kafka.clients.admin.Admin;
import org.apache.kafka.clients.admin.OffsetSpec;
import org.apache.kafka.common.TopicPartition;
import org.apache.kafka.common.errors.RetriableException;

import java.time.Duration;
import java.util.Collection;
import java.util.HashMap;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;

final class AcceptanceBrokerReadiness {

    private AcceptanceBrokerReadiness() {}

    static void await(
            Admin admin,
            Collection<String> topics,
            Duration timeout) throws Exception {

        long deadline = System.nanoTime() + timeout.toNanos();

        Exception lastFailure = null;

        while (System.nanoTime() < deadline) {

            try {

                var descriptions = admin.describeTopics(topics)
                        .allTopicNames()
                        .get(5, TimeUnit.SECONDS);

                var offsets =
                        new HashMap<TopicPartition, OffsetSpec>();

                boolean ready =
                        descriptions.size() == topics.size();

                for (var entry : descriptions.entrySet()) {

                    var partitions = entry.getValue().partitions();

                    if (partitions.isEmpty()) {
                        ready = false;
                    }

                    for (var partition : partitions) {

                        if (partition.leader() == null ||
                                partition.leader().id() < 0 ||
                                partition.isr().isEmpty()) {

                            ready = false;
                        }

                        offsets.put(
                                new TopicPartition(
                                        entry.getKey(),
                                        partition.partition()),
                                OffsetSpec.latest());
                    }
                }

                if (ready && !offsets.isEmpty()) {

                    var results = admin.listOffsets(offsets)
                            .all()
                            .get(5, TimeUnit.SECONDS);

                    if (results.size() == offsets.size() &&
                            results.values().stream()
                                    .allMatch(value ->
                                            value.offset() >= 0)) {

                        System.out.println(
                                "ACCEPTANCE_BROKER_READY: " +
                                topics.size() + " topics, " +
                                offsets.size() + " partitions");

                        return;
                    }
                }

            } catch (ExecutionException ex) {

                if (!(ex.getCause() instanceof RetriableException)) {
                    throw ex;
                }

                lastFailure = ex;

            } catch (TimeoutException ex) {

                lastFailure = ex;
            }

            Thread.sleep(500);
        }

        throw new IllegalStateException(
                "Acceptance broker readiness deadline exceeded",
                lastFailure);
    }
}
