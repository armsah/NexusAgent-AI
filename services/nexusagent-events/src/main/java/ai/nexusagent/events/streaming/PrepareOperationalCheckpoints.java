package ai.nexusagent.events.streaming;

public final class PrepareOperationalCheckpoints {

    private PrepareOperationalCheckpoints() {}

    public static void main(String[] args) throws Exception {

        if (args.length != 2 || !"v2".equals(args[1])) {
            throw new IllegalArgumentException(
                    "Explicit prospective generation required");
        }

        var generation = StreamGeneration.PROSPECTIVE;

        BrokerRetentionGuard.verify(
                args[0],
                generation.applicationId(),
                generation.topics());

        PristineCheckpointInitializer.initializePristineCheckpoints(
                args[0],
                generation.applicationId(),
                generation.topics());

        ProductionRecoveryGuard.verifyStartup(
                args[0],
                generation.applicationId(),
                generation.topics(),
                false,
                true);

        System.out.println(
                "OPERATIONAL_CHECKPOINT_PREPARATION_VERIFIED");
    }
}
