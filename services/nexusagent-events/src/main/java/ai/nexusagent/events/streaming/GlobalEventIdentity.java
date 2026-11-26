package ai.nexusagent.events.streaming;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;
import java.util.Objects;
import java.util.UUID;

public final class GlobalEventIdentity {

    public enum Decision {
        NEW,
        DUPLICATE,
        CONFLICT
    }

    private GlobalEventIdentity() {}

    public static String registryKey(UUID eventId) {
        Objects.requireNonNull(eventId, "Event ID is required");

        if (eventId.equals(new UUID(0, 0))) {
            throw new IllegalArgumentException("Empty event ID");
        }

        return eventId.toString();
    }

    public static String fingerprint(
            String family,
            String aggregateKey,
            String canonicalPayload) {

        Objects.requireNonNull(family);
        Objects.requireNonNull(aggregateKey);
        Objects.requireNonNull(canonicalPayload);

        if (!family.equals("document") && !family.equals("workflow")) {
            throw new IllegalArgumentException("Unknown event family");
        }

        if (aggregateKey.isBlank() || canonicalPayload.isBlank()) {
            throw new IllegalArgumentException("Empty identity component");
        }

        try {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");

            append(digest, family);
            append(digest, aggregateKey);
            append(digest, canonicalPayload);

            return HexFormat.of().formatHex(digest.digest());

        } catch (NoSuchAlgorithmException ex) {
            throw new IllegalStateException("SHA-256 unavailable", ex);
        }
    }

    private static void append(MessageDigest digest, String value) {
        byte[] bytes = value.getBytes(StandardCharsets.UTF_8);

        digest.update(
                java.nio.ByteBuffer.allocate(Integer.BYTES)
                        .putInt(bytes.length).array());

        digest.update(bytes);
    }

    public static Decision classify(
            String existingFingerprint,
            String incomingFingerprint) {

        Objects.requireNonNull(incomingFingerprint);

        if (!incomingFingerprint.matches("[0-9a-f]{64}")) {
            throw new IllegalArgumentException("Invalid fingerprint");
        }

        if (existingFingerprint == null) {
            return Decision.NEW;
        }

        if (!existingFingerprint.matches("[0-9a-f]{64}")) {
            throw new IllegalStateException("Corrupted identity registry");
        }

        return existingFingerprint.equals(incomingFingerprint)
                ? Decision.DUPLICATE
                : Decision.CONFLICT;
    }
}
