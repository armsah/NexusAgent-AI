package ai.nexusagent.events.streaming;

import org.apache.kafka.streams.KafkaStreams;
import org.apache.kafka.streams.StoreQueryParameters;
import org.apache.kafka.streams.errors.InvalidStateStoreException;
import org.apache.kafka.streams.state.QueryableStoreTypes;
import org.apache.kafka.streams.state.ReadOnlyKeyValueStore;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.Objects;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

public final class PendingGapMonitor implements AutoCloseable {

    private static final Logger LOG =
            LoggerFactory.getLogger(PendingGapMonitor.class);

    private static final String STORE =
            "aggregate-pending-v1";

    public record Snapshot(
            long pendingEvents,
            long overdueEvents,
            long oldestAgeMillis,
            long unknownAgeEvents,
            boolean complete,
            Instant observedAt) {}

    private final KafkaStreams streams;
    private final Clock clock;
    private final Duration interval;
    private final Duration threshold;
    private final int batchSize;


    private final AtomicReference<Snapshot> latest;

    private final ScheduledExecutorService executor =
            Executors.newSingleThreadScheduledExecutor(task -> {
                Thread thread = new Thread(
                        task, "nexusagent-pending-gap-monitor");

                thread.setDaemon(true);
                return thread;
            });

    public PendingGapMonitor(
            KafkaStreams streams,
            Clock clock,
            Duration interval,
            Duration threshold,
            int batchSize) {

        this.streams = Objects.requireNonNull(streams);
        this.clock = Objects.requireNonNull(clock);
        this.interval = Objects.requireNonNull(interval);
        this.threshold = Objects.requireNonNull(threshold);

        if (interval.toMillis() < 100
                || threshold.isZero()
                || threshold.isNegative()
                || batchSize < 1) {

            throw new IllegalArgumentException(
                    "Invalid pending-gap monitor configuration");
        }

        this.batchSize = batchSize;

        latest = new AtomicReference<>(
                new Snapshot(
                        0, 0, 0, 0, false, clock.instant()));
    }

    public static PendingGapMonitor fromEnvironment(
            KafkaStreams streams) {

        return new PendingGapMonitor(
                streams,
                Clock.systemUTC(),
                Duration.ofSeconds(readPositive(
                        "NEXUS_GAP_MONITOR_INTERVAL_SECONDS", 30)),
                Duration.ofSeconds(readPositive(
                        "NEXUS_GAP_WARNING_SECONDS", 300)),
                10000);
    }

    private static long readPositive(
            String name, long fallback) {

        long value = Long.parseLong(
                System.getenv().getOrDefault(
                        name, Long.toString(fallback)));

        if (value < 1 || value > 86400) {
            throw new IllegalArgumentException(
                    "Invalid monitoring configuration: " + name);
        }

        return value;
    }

    public void start() {
        executor.scheduleWithFixedDelay(
                this::inspectSafely,
                0,
                interval.toMillis(),
                TimeUnit.MILLISECONDS);
    }

    public Snapshot snapshot() {
        return latest.get();
    }

    private void inspectSafely() {
        try {
            inspect();

        } catch (InvalidStateStoreException ex) {
            resetUnavailable();

        } catch (RuntimeException ex) {
            resetUnavailable();

            LOG.warn(
                    "Pending-gap inspection failed; errorType={}",
                    ex.getClass().getSimpleName());
        }
    }

    private synchronized void resetUnavailable() {

        latest.set(new Snapshot(
                0, 0, 0, 0, false, clock.instant()));
    }

    public synchronized void inspect() {

        ReadOnlyKeyValueStore<String, String> store =
                streams.store(
                        StoreQueryParameters.fromNameAndType(
                                STORE,
                                QueryableStoreTypes
                                        .<String, String>keyValueStore()));

        Instant now = clock.instant();

        long pendingCount = 0;
        long overdueCount = 0;
        long oldestAge = 0;
        long unknownCount = 0;

        try (var iterator = store.all()) {

            while (iterator.hasNext()) {

                if (Thread.currentThread().isInterrupted()) {
                    resetUnavailable();
                    return;
                }

                var entry = iterator.next();

                if (entry.value == null) {
                    continue;
                }

                pendingCount++;

                var observed =
                        PendingGapObservation.observedAt(
                                entry.value);

                if (observed.isEmpty()) {
                    unknownCount++;
                    continue;
                }

                var age = PendingGapAge.evaluate(
                        observed.get(), now, threshold);

                oldestAge = Math.max(
                        oldestAge, age.ageMillis());

                if (age.overdue()) {
                    overdueCount++;
                }

                if (pendingCount % batchSize == 0) {
                    latest.set(new Snapshot(
                            pendingCount, overdueCount,
                            oldestAge, unknownCount,
                            false, now));
                    Thread.yield();
                }
            }
        }

        latest.set(new Snapshot(
                pendingCount, overdueCount,
                oldestAge, unknownCount,
                true, now));

        if (overdueCount > 0 || unknownCount > 0) {
            LOG.warn(
                    "Pending predecessor gaps: pending={}, "
                            + "overdue={}, oldestAgeMillis={}, "
                            + "unknownAge={}",
                    pendingCount, overdueCount,
                    oldestAge, unknownCount);
        }
    }

    @Override
    public void close() {
        executor.shutdownNow();

        try {
            if (!executor.awaitTermination(
                    5, TimeUnit.SECONDS)) {

                LOG.warn(
                        "Pending-gap monitor shutdown timed out");
            }

        } catch (InterruptedException ex) {
            Thread.currentThread().interrupt();
        }
    }
}
