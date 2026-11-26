package ai.nexusagent.events.streaming;

import org.apache.kafka.clients.producer.ProducerInterceptor;
import org.apache.kafka.clients.producer.ProducerRecord;
import org.apache.kafka.clients.producer.RecordMetadata;
import org.apache.kafka.streams.KafkaStreams;
import org.apache.kafka.streams.StreamsConfig;
import org.apache.kafka.streams.errors.StreamsUncaughtExceptionHandler;

import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.Map;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.atomic.AtomicBoolean;

public final class TransactionCrashProbe {

    private static String targetEvent;
    private static Path marker;

    private TransactionCrashProbe() {}

    public static void main(String[] args) throws Exception {

        String bootstrap = args[0];
        String appId = args[1];
        Path state = Path.of(args[2]);

        targetEvent = args[3];
        marker = Path.of(args[4]);

        boolean crashMode = Boolean.parseBoolean(args[5]);

        var properties = StreamSettings.create(
                bootstrap, appId, state);

        properties.put("auto.offset.reset", "latest");

        properties.put(
                StreamsConfig.COMMIT_INTERVAL_MS_CONFIG, 100);

        properties.put(
                "producer.transaction.timeout.ms", 15000);

        if (crashMode) {
            properties.put(
                    "producer.interceptor.classes",
                    BlockingInterceptor.class.getName());
        }

        KafkaStreams streams = new KafkaStreams(
                EventStreamTopology.build(), properties);

        streams.setUncaughtExceptionHandler(error -> {
            error.printStackTrace();
            return StreamsUncaughtExceptionHandler
                    .StreamThreadExceptionResponse.SHUTDOWN_CLIENT;
        });

        Runtime.getRuntime().addShutdownHook(
                new Thread(() ->
                        streams.close(Duration.ofSeconds(15))));

        streams.start();

        while (streams.state() != KafkaStreams.State.RUNNING) {
            if (streams.state() == KafkaStreams.State.ERROR
                    || streams.state()
                    == KafkaStreams.State.NOT_RUNNING) {
                System.exit(2);
            }

            Thread.sleep(100);
        }

        Files.writeString(
                state.resolve("probe-ready"), "RUNNING");

        new CountDownLatch(1).await();
    }

    public static final class BlockingInterceptor
            implements ProducerInterceptor<byte[], byte[]> {

        private final AtomicBoolean armed = new AtomicBoolean();
        private final AtomicBoolean blocked = new AtomicBoolean();

        @Override
        public ProducerRecord<byte[], byte[]> onSend(
                ProducerRecord<byte[], byte[]> record) {

            if (record.topic().equals(StreamSettings.ANALYTICS)
                    && record.value() != null) {

                String value = new String(
                        record.value(),
                        java.nio.charset.StandardCharsets.UTF_8);

                if (value.contains(targetEvent)) {
                    armed.set(true);
                }
            }

            return record;
        }

        @Override
        public void onAcknowledgement(
                RecordMetadata metadata, Exception exception) {

            if (exception == null
                    && metadata != null
                    && StreamSettings.ANALYTICS.equals(
                            metadata.topic())
                    && armed.get()
                    && blocked.compareAndSet(false, true)) {

                try {
                    Files.writeString(marker,
                            metadata.topic() + ":"
                                    + metadata.partition() + ":"
                                    + metadata.offset());

                    // Block the producer callback before transaction
                    // completion. The parent forcibly kills this JVM.
                    new CountDownLatch(1).await();

                } catch (Exception ex) {
                    Runtime.getRuntime().halt(3);
                }
            }
        }

        @Override
        public void configure(Map<String, ?> configs) {}

        @Override
        public void close() {}
    }
}
