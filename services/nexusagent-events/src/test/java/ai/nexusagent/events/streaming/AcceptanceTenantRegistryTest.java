package ai.nexusagent.events.streaming;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.nio.file.Path;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.*;

class AcceptanceTenantRegistryTest {

    @TempDir
    Path directory;

    @Test
    void preservesExactRegisteredIdentities() throws Exception {

        var registry = directory.resolve("tenants.txt");

        AcceptanceTenantRegistry.register(registry, "tenant-a");
        AcceptanceTenantRegistry.register(registry, "tenant-b");
        AcceptanceTenantRegistry.register(registry, "tenant-a");

        var actual = AcceptanceTenantRegistry.read(registry);

        assertEquals(Set.of("tenant-a", "tenant-b"), actual);
        assertFalse(actual.contains("tenant-a-other"));
    }

    @Test
    void rejectsMissingRegistry() {
        assertThrows(IllegalStateException.class,
                () -> AcceptanceTenantRegistry.read(
                        directory.resolve("missing.txt")));
    }

    @Test
    void rejectsInvalidIdentity() {
        var registry = directory.resolve("tenants.txt");

        assertThrows(IllegalArgumentException.class,
                () -> AcceptanceTenantRegistry.register(
                        registry, "tenant\nother"));

        assertThrows(IllegalArgumentException.class,
                () -> AcceptanceTenantRegistry.register(
                        registry, " tenant"));

        assertThrows(IllegalArgumentException.class,
                () -> AcceptanceTenantRegistry.register(
                        registry, ""));
    }
}
