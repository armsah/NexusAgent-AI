package ai.nexusagent.events.contracts;

import java.util.UUID;

final class EventRules {
    private EventRules() {}

    static void identifier(UUID value, String name) {
        if (value == null || value.equals(new UUID(0, 0))) {
            throw new IllegalArgumentException(name + " is required");
        }
    }

    static void tenant(String value) {
        if (value == null || value.isBlank()
                || value.length() > 256
                || !value.equals(value.strip())
                || value.codePoints().anyMatch(Character::isISOControl)) {
            throw new IllegalArgumentException("Invalid tenant identifier");
        }
    }

    static void version(int value) {
        if (value != 1) {
            throw new IllegalArgumentException("Unsupported schema version");
        }
    }
}