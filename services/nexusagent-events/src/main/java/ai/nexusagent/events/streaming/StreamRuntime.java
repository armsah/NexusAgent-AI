package ai.nexusagent.events.streaming;

import java.nio.file.Path;
import java.time.Duration;
import java.util.concurrent.atomic.AtomicBoolean;

import org.apache.kafka.streams.KafkaStreams;
import org.apache.kafka.streams.errors.StreamsUncaughtExceptionHandler;

import org.springframework.context.SmartLifecycle;
import org.springframework.stereotype.Component;

@Component
public final class StreamRuntime implements SmartLifecycle {
    private final AtomicBoolean running = new AtomicBoolean();
    private KafkaStreams streams;
    private PendingGapMonitor gapMonitor;

    @Override
    public boolean isAutoStartup() {
        return "true".equalsIgnoreCase(
                System.getenv("NEXUS_STREAM_ENABLED"));
    }

    @Override
    public synchronized void start() {
        if (running.get()) {
            return;
        }

        String bootstrap = System.getenv().getOrDefault(
                "NEXUS_KAFKA_BOOTSTRAP_SERVERS", "localhost:19092");

        String applicationId = System.getenv().getOrDefault(
                "NEXUS_STREAM_APPLICATION_ID", "nexusagent-events-v1");

        if (!applicationId.matches("[A-Za-z0-9._-]{1,128}")) {
            throw new IllegalArgumentException("Invalid application ID");
        }

        Path state = Path.of(System.getenv().getOrDefault(
                "NEXUS_STREAM_STATE_DIR",
                Path.of(System.getProperty("java.io.tmpdir"),
                        "nexusagent-streams").toString()));

        BrokerRetentionGuard.verify(
                bootstrap,
                applicationId,
                StreamTopics.production());

        ProductionRecoveryGuard.verify(
                bootstrap,
                applicationId,
                StreamTopics.production());

        var settings = StreamSettings.create(
                bootstrap, applicationId, state);

        // Never silently replay or skip missing production history.
        settings.put("auto.offset.reset", "none");

        streams = new KafkaStreams(
                EventStreamTopology.build(),
                settings);

        streams.setUncaughtExceptionHandler(exception ->
                StreamsUncaughtExceptionHandler
                        .StreamThreadExceptionResponse.SHUTDOWN_CLIENT);

        streams.setStateListener((next, previous) -> {
            if (next == KafkaStreams.State.ERROR
                    || next == KafkaStreams.State.NOT_RUNNING) {
                running.set(false);
            }
        });

        running.set(true);

        try {
            gapMonitor = PendingGapMonitor.fromEnvironment(streams);
            streams.start();
            gapMonitor.start();
        } catch (RuntimeException ex) {
            if (gapMonitor != null) {
                gapMonitor.close();
                gapMonitor = null;
            }
            running.set(false);
            streams.close(Duration.ofSeconds(10));
            streams = null;
            throw ex;
        }
    }

    @Override
    public synchronized void stop() {
        if (gapMonitor != null) {
            gapMonitor.close();
            gapMonitor = null;
        }
        if (streams != null) {
            streams.close(Duration.ofSeconds(30));
            streams = null;
        }
        running.set(false);
    }

    @Override
    public boolean isRunning() {
        return running.get();
    }
}
