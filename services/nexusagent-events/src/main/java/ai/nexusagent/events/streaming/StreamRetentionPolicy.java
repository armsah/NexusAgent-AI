package ai.nexusagent.events.streaming;

import java.util.Map;
import java.util.Set;
import java.util.Objects;

public final class StreamRetentionPolicy {

    public static final long UNLIMITED = -1L;

    public static final Set<String> DURABLE_STORES = Set.of(
            "accepted-history-v1",
            "accepted-identities-v1",
            "aggregate-latest-v1",
            "aggregate-pending-v1",
            "global-event-identities-v1",
            "tenant-metrics-v1",
            "tenant-metric-identities-v1",
            "aggregate-reconciliation-audit-v1");

    private StreamRetentionPolicy() {}

    public record TopicPolicy(
            Set<String> cleanupPolicies,
            long retentionMillis,
            long retentionBytes) {

        public TopicPolicy {
            cleanupPolicies = Set.copyOf(cleanupPolicies);
        }
    }

    public record RebuildEvidence(
            boolean sourceHistoryComplete,
            boolean authoritativeHistoryVerified,
            boolean isolatedTarget,
            boolean productionOffsetsPreserved,
            boolean productionOutputsPreserved) {}

    public static void requireSourcePolicy(TopicPolicy policy) {

        Objects.requireNonNull(policy);

        if (!policy.cleanupPolicies().equals(Set.of("delete"))
                || policy.retentionMillis() != UNLIMITED
                || policy.retentionBytes() != UNLIMITED) {

            throw new IllegalStateException(
                    "Source history must not expire or compact");
        }
    }

    public static void requireChangelogPolicy(TopicPolicy policy) {

        Objects.requireNonNull(policy);

        if (!policy.cleanupPolicies().equals(Set.of("compact"))) {

            throw new IllegalStateException(
                    "Durable changelog must use compaction only");
        }
    }

    public static void requireDurableStores(
            Map<String, TopicPolicy> policies) {

        Objects.requireNonNull(policies);

        for (String store : DURABLE_STORES) {

            TopicPolicy policy = policies.get(store);

            if (policy == null) {
                throw new IllegalStateException(
                        "Missing durable store policy: " + store);
            }

            requireChangelogPolicy(policy);
        }
    }

    public static void requireSafeRebuild(
            RebuildEvidence evidence) {

        Objects.requireNonNull(evidence);

        if (!evidence.sourceHistoryComplete()
                || !evidence.authoritativeHistoryVerified()
                || !evidence.isolatedTarget()
                || !evidence.productionOffsetsPreserved()
                || !evidence.productionOutputsPreserved()) {

            throw new IllegalStateException(
                    "Rebuild denied: incomplete safety evidence");
        }
    }
}
