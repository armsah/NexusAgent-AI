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
    private final java.util.Map<String, String> configuration;

    public StreamRuntime() {
        this(System.getenv());
    }

    StreamRuntime(java.util.Map<String, String> configuration) {
        this.configuration = java.util.Map.copyOf(configuration);
    }

    synchronized KafkaStreams.State runtimeState() {
        return streams == null
                ? KafkaStreams.State.NOT_RUNNING
                : streams.state();
    }

    synchronized String tenantSnapshot(String tenant) {
        if (streams == null) {
            throw new IllegalStateException("Runtime not started");
        }

        return streams.store(
                org.apache.kafka.streams.StoreQueryParameters
                        .fromNameAndType(
                                TenantAnalyticsProcessor.METRICS_STORE,
                                org.apache.kafka.streams.state
                                        .QueryableStoreTypes
                                        .<String, String>keyValueStore()))
                .get(tenant);
    }

    private KafkaStreams streams;
    private PendingGapMonitor gapMonitor;

    @Override
    public boolean isAutoStartup() {
        return "true".equalsIgnoreCase(
                configuration.get("NEXUS_STREAM_ENABLED"));
    }

    @Override
    public synchronized void start() {
        if (running.get()) {
            return;
        }

        String bootstrap = configuration.getOrDefault(
                "NEXUS_KAFKA_BOOTSTRAP_SERVERS", "localhost:19092");

        var generation = StreamGeneration.resolve(
                configuration.getOrDefault(
                        "NEXUS_STREAM_GENERATION", "v1"));

        var topics = generation.topics();

        String applicationId = configuration.getOrDefault(
                "NEXUS_STREAM_APPLICATION_ID",
                generation.applicationId());

        generation.requireBinding(applicationId, topics);

        if (!applicationId.matches("[A-Za-z0-9._-]{1,128}")) {
            throw new IllegalArgumentException("Invalid application ID");
        }

        Path state = Path.of(configuration.getOrDefault(
                "NEXUS_STREAM_STATE_DIR",
                Path.of(System.getProperty("java.io.tmpdir"),
                        "nexusagent-streams").toString()));

        BrokerRetentionGuard.verify(
                bootstrap,
                applicationId,
                topics);

        boolean initializationApproved = "true".equalsIgnoreCase(
                configuration.get("NEXUS_STREAM_INITIALIZE_EMPTY"));

        boolean localStateEmpty = false;

        Path applicationState = state.resolve(applicationId);

        if (java.nio.file.Files.notExists(applicationState)) {
            localStateEmpty = true;
        } else {
            try (var entries =
                    java.nio.file.Files.list(applicationState)) {

                localStateEmpty = entries.findAny().isEmpty();

            } catch (java.io.IOException ex) {

                throw new IllegalStateException(
                        "Unable to verify local stream state", ex);
            }
        }

        var startupMode = ProductionRecoveryGuard.verifyStartup(
                bootstrap,
                applicationId,
                topics,
                initializationApproved,
                localStateEmpty);

        var settings = StreamSettings.create(
                bootstrap, applicationId, state);

        settings.put(
                "auto.offset.reset",
                StreamInitializationPolicy.offsetReset(startupMode));

        streams = new KafkaStreams(
                EventStreamTopology.build(topics),
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
