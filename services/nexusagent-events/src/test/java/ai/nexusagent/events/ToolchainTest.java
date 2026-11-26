package ai.nexusagent.events;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.assertEquals;

class ToolchainTest {
    @Test
    void usesJava21() {
        assertEquals(21, Runtime.version().feature());
    }
}