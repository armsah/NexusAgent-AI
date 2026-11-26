# E738-04 historical coverage safety

## Verified database observations

Total document publications: 159.
Non-stream publications: 37.
Missing document versions: 122.
Missing outbox origins: 0.
Metadata available: 0.
Published workflow events: 0.

Legacy publication payloads are not complete Kafka wire
envelopes. Missing version metadata prevents independent
verification of historical content versions.

## Safety implementation

HistoricalCoverage accounts for every publication and rejects
unresolved, conflicting, incomplete or empty historical evidence.

The validator combines historical coverage with the existing
safe rebuild policy.

Targeted tests passed: 28/28.

## Outstanding acceptance

The validator is available for migration callers but is not
yet integrated into a production migration command.

Independent historical verification and live isolated migration
remain outstanding.

E738-04 remains PARTIAL.

No production records or offsets were modified.

Cloud expenditure: EUR 0.
