package ai.nexusagent.events.streaming;

public record HistoricalCoverage(
        long totalPublications,
        long nonStreamPublications,
        long independentlyVerifiedEvents,
        long unresolvedEvents,
        long conflictingEvents,
        boolean sourceInventoryComplete,
        boolean authoritativeInventoryComplete) {

    public HistoricalCoverage {

        if (totalPublications < 0
                || nonStreamPublications < 0
                || independentlyVerifiedEvents < 0
                || unresolvedEvents < 0
                || conflictingEvents < 0) {

            throw new IllegalArgumentException(
                    "Historical coverage counts cannot be negative");
        }

        long accounted = Math.addExact(
                Math.addExact(
                        nonStreamPublications,
                        independentlyVerifiedEvents),
                Math.addExact(
                        unresolvedEvents,
                        conflictingEvents));

        if (accounted != totalPublications) {

            throw new IllegalArgumentException(
                    "Historical publications are not fully accounted for");
        }
    }

    public boolean permitsAuthoritativeRebuild() {

        return sourceInventoryComplete
                && authoritativeInventoryComplete
                && independentlyVerifiedEvents > 0
                && unresolvedEvents == 0
                && conflictingEvents == 0;
    }

    public void requireAuthoritativeRebuild() {

        if (!permitsAuthoritativeRebuild()) {

            throw new IllegalStateException(
                    "Authoritative historical coverage is incomplete");
        }
    }

    public void requireSafeMigration(
            StreamRetentionPolicy.RebuildEvidence evidence) {

        requireAuthoritativeRebuild();

        StreamRetentionPolicy.requireSafeRebuild(evidence);
    }
}
