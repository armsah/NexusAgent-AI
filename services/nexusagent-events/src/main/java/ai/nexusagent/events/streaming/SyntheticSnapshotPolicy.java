package ai.nexusagent.events.streaming;

import java.util.Objects;
import java.util.Set;

public final class SyntheticSnapshotPolicy {

    public enum Decision {
        PRESERVE,
        REVIEW_REQUIRED,
        APPROVED_FOR_RECONCILIATION
    }

    private SyntheticSnapshotPolicy() {}

    public static Decision classify(
            String tenant,
            Set<String> verifiedSyntheticTenants,
            Set<String> authoritativeTenants,
            boolean authoritativeInventoryComplete) {

        Objects.requireNonNull(tenant);
        Objects.requireNonNull(verifiedSyntheticTenants);
        Objects.requireNonNull(authoritativeTenants);

        if (tenant.isBlank()) {
            throw new IllegalArgumentException(
                    "Blank tenant identifier");
        }

        if (authoritativeTenants.contains(tenant)) {
            return Decision.PRESERVE;
        }

        if (!verifiedSyntheticTenants.contains(tenant)) {
            return Decision.PRESERVE;
        }

        if (!authoritativeInventoryComplete) {
            return Decision.REVIEW_REQUIRED;
        }

        return Decision.APPROVED_FOR_RECONCILIATION;
    }
}
