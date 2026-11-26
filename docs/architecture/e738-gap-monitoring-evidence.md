### E738-02 â€” Durable pending-gap monitoring checkpoint

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
- Status: PARTIAL â€” formal acceptance remains open.
- Cloud expenditure: EUR 0.

### E738-02 formal acceptance

Status: CLOSED for the implemented instance-local monitoring scope.

Verified on 2026-11-26:

- Durable pending-event observation timestamps.
- Duplicate timestamp preservation.
- Unknown-age reporting for legacy envelopes.
- Live broker interruption and state restoration.
- Direct overdue detection against restored state.
- Scheduled monitoring execution.
- Resolved-gap detection with zero remaining pending events.
- Targeted topology and observation tests: 50/50 passed.
- Targeted live and observation tests: 17/17 passed.
- Direct monitoring acceptance: 1/1 passed.
- Scheduled and resolved-gap acceptance: 1/1 passed.
- All listed executions: zero failures, errors or skips.

Operational qualifications:

- Monitoring covers locally queryable state, not a cluster-wide atomic snapshot.
- Full traversal uses constant application memory but has no strict duration bound.
- Exhaustive partition-distribution and concurrent rebalance stress testing remain
  final production acceptance obligations.
- Full regression remains mandatory at E7.4.
- Cloud expenditure: EUR 0.
