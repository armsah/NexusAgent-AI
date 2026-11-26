package ai.nexusagent.events.streaming;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

final class ProductionRecoveryInspectionTest {

    @Test
    void inspectsEstablishedProductionRecoveryState() {

        String bootstrap = System.getenv().getOrDefault(
                "NEXUS_TEST_KAFKA_BOOTSTRAP",
                "localhost:19092");

        BrokerRetentionGuard.verify(
                bootstrap,
                ProductionRecoveryGuard.PRODUCTION_APPLICATION,
                StreamTopics.production());

        try {

            ProductionRecoveryGuard.verify(
                    bootstrap,
                    ProductionRecoveryGuard.PRODUCTION_APPLICATION,
                    StreamTopics.production());

            System.out.println(
                    "PRODUCTION_CONTINUATION_CHECKPOINTS_PRESENT");

        } catch (IllegalStateException ex) {

            String message = ex.getMessage();

            assertTrue(
                    (message.equals("Durable state history is incomplete") || message.equals("Explicit initialization approval required")) ||
                    message.equals(
                        "Missing committed source checkpoint") ||
                    message.equals(
                        "Committed checkpoint precedes retained history") ||
                    message.equals(
                        "Committed checkpoint exceeds source history"),
                    "Unexpected verification failure: " + message);

            System.out.println(
                    "PRODUCTION_CONTINUATION_DENIED: " + message);
        }
    }
}
