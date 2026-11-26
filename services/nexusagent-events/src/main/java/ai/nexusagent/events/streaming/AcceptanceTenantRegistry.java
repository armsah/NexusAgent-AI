package ai.nexusagent.events.streaming;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardOpenOption;
import java.util.HashSet;
import java.util.Set;

public final class AcceptanceTenantRegistry {

    private AcceptanceTenantRegistry() {}

    public static synchronized void register(
            Path registry, String tenant) throws IOException {

        validate(tenant);

        var absolute = registry.toAbsolutePath();

        Files.createDirectories(absolute.getParent());

        Files.writeString(
                absolute,
                tenant + System.lineSeparator(),
                StandardOpenOption.CREATE,
                StandardOpenOption.APPEND,
                StandardOpenOption.SYNC);
    }

    public static Set<String> read(Path registry)
            throws IOException {

        if (!Files.isRegularFile(registry)) {
            throw new IllegalStateException(
                    "Explicit acceptance disposition registry required");
        }

        var tenants = new HashSet<String>();

        for (String tenant : Files.readAllLines(registry)) {
            validate(tenant);
            tenants.add(tenant);
        }

        return Set.copyOf(tenants);
    }

    private static void validate(String tenant) {

        if (tenant == null
                || tenant.isBlank()
                || tenant.length() > 256
                || !tenant.equals(tenant.strip())
                || tenant.chars().anyMatch(
                        Character::isISOControl)) {

            throw new IllegalArgumentException(
                    "Invalid acceptance tenant identity");
        }
    }
}
