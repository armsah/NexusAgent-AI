# E738-04 durable document publication journal

## Implementation

- Immutable wire intent persisted before Kafka transmission.
- Exact serialized payload and partition key retained.
- Acknowledgement persisted separately.
- Conflicting publication identity rejected.
- Acknowledged delivery can bypass retransmission.
- Database triggers protect intent and acknowledgement.

## Verified acceptance

- Release build passed.
- EF database migration applied.
- Database immutability acceptance passed.
- Acceptance transaction rolled back.
- Retained acceptance records: zero.
- Targeted C# regression: 19/19 passed.
- Previous targeted Java regression: 32/32 passed.

## Remaining acceptance

Actual document process interruption and retry recovery,
independent Kafka history comparison, and successful isolated
authoritative rebuild have not yet been verified.

Legacy historical records remain preserved.

The journal migration is intentionally irreversible without
an explicit archival procedure.

E738-04: PARTIAL.

Cloud expenditure: EUR 0.
