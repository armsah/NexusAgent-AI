package ai.nexusagent.events.streaming;

import org.junit.jupiter.api.Test;

import java.util.HashMap;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.*;

class StreamRetentionPolicyTest {

    private StreamRetentionPolicy.TopicPolicy policy(
            String cleanup,
            long millis,
            long bytes) {

        return new StreamRetentionPolicy.TopicPolicy(
                Set.of(cleanup.split(",")), millis, bytes);
    }

    @Test
    void unlimitedSourceHistoryAccepted() {

        assertDoesNotThrow(() ->
                StreamRetentionPolicy.requireSourcePolicy(
                        policy("delete", -1, -1)));
    }

    @Test
    void finiteTimeRetentionRejected() {

        assertThrows(IllegalStateException.class, () ->
                StreamRetentionPolicy.requireSourcePolicy(
                        policy("delete", 604800000, -1)));
    }

    @Test
    void finiteSizeRetentionRejected() {

        assertThrows(IllegalStateException.class, () ->
                StreamRetentionPolicy.requireSourcePolicy(
                        policy("delete", -1, 1048576)));
    }

    @Test
    void compactedSourceRejected() {

        assertThrows(IllegalStateException.class, () ->
                StreamRetentionPolicy.requireSourcePolicy(
                        policy("compact", -1, -1)));
    }

    @Test
    void compactOnlyChangelogAccepted() {

        assertDoesNotThrow(() ->
                StreamRetentionPolicy.requireChangelogPolicy(
                        policy("compact", -1, -1)));
    }

    @Test
    void expiringChangelogRejected() {

        assertThrows(IllegalStateException.class, () ->
                StreamRetentionPolicy.requireChangelogPolicy(
                        policy("compact,delete", 1000, -1)));
    }

    @Test
    void independentIdentityExpiryRejected() {

        var policies = new HashMap<
                String, StreamRetentionPolicy.TopicPolicy>();

        for (String store :
                StreamRetentionPolicy.DURABLE_STORES) {

            policies.put(store,
                    policy("compact", -1, -1));
        }

        policies.put("accepted-identities-v1",
                policy("delete", 1000, -1));

        assertThrows(IllegalStateException.class, () ->
                StreamRetentionPolicy.requireDurableStores(
                        policies));
    }

    @Test
    void missingStoreRejected() {

        assertThrows(IllegalStateException.class, () ->
                StreamRetentionPolicy.requireDurableStores(
                        java.util.Map.of()));
    }

    @Test
    void verifiedIsolatedRebuildAccepted() {

        assertDoesNotThrow(() ->
                StreamRetentionPolicy.requireSafeRebuild(
                        new StreamRetentionPolicy.RebuildEvidence(
                                true, true, true, true, true)));
    }

    @Test
    void incompleteHistoryRejectsRebuild() {

        assertThrows(IllegalStateException.class, () ->
                StreamRetentionPolicy.requireSafeRebuild(
                        new StreamRetentionPolicy.RebuildEvidence(
                                false, true, true, true, true)));
    }

    @Test
    void productionResetRejectsRebuild() {

        assertThrows(IllegalStateException.class, () ->
                StreamRetentionPolicy.requireSafeRebuild(
                        new StreamRetentionPolicy.RebuildEvidence(
                                true, true, false, false, true)));
    }

    @Test
    void unverifiedAuthoritativeHistoryRejectsRebuild() {

        assertThrows(IllegalStateException.class, () ->
                StreamRetentionPolicy.requireSafeRebuild(
                        new StreamRetentionPolicy.RebuildEvidence(
                                true, false, true, true, true)));
    }
}
