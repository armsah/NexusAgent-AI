# Integrated E738-04 through E738-06 acceptance

## Acceptance results

- Initial consolidated run: 67/69 passed.
- Corrective run: 3/3 passed.
- Combined targeted evidence: 69/69 passed.
- Failed, errored or skipped tests in combined reports: zero.
- Cloud expenditure: EUR 0.

The combined evidence consists of retained successful reports
and replacement reports for the two corrected test classes.
It is not a fresh full regression run.

## E738-04

Runtime startup verifies source retention and existing durable
changelog cleanup policies using broker configuration.

Kafka initialization applies source retention protection.

Historical completeness, missing-changelog detection,
configuration drift protection and migration acceptance
remain subject to verification.

## E738-05

The production topology supports explicit topic configuration.

Dedicated topic fixtures isolate dead-letter, transactional
recovery and separate-JVM crash acceptance scenarios.

The crash probe uses earliest offset reset on isolated topics
to recover partitions without established committed offsets.

Fixture cleanup and production end-offset preservation passed.

## E738-06

The bounded read-committed inventory observed 50 snapshots
and identified 20 candidate synthetic tenant identifiers.

The exact candidate manifest is retained separately.

Candidate identifiers do not independently establish synthetic
provenance. Authoritative comparison remains necessary.

No tombstones, production offset resets or destructive
reconciliation operations have been executed.
