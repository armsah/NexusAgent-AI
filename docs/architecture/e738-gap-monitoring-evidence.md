### E738-02 — Durable pending-gap monitoring checkpoint

- Pending-event observation timestamps persist in the existing changelog-backed envelopes.
- Duplicate delivery preserves the original observation timestamp.
- Live transactional recovery verifies timestamp preservation after state restoration.
- Legacy envelopes without timestamps are explicitly reported as unknown-age.
- Monitoring traverses locally queryable partitions using constant application memory.
- Startup exception handling closes the monitoring executor.
- Targeted topology and observation regression: 50/50 passed.
- Subsequent targeted live recovery regression: 17/17 passed.
- Both executions reported zero failures, errors or skipped tests.
- Full regression deferred to E7 stage closure.
- Remaining acceptance: direct monitor lifecycle, partition coverage and resolved-gap observation.
- Monitoring is instance-local; scan duration is not strictly bounded.
- Status: PARTIAL — formal acceptance remains open.
- Cloud expenditure: EUR 0.
