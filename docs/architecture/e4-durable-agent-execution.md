# E4 - Durable Agent Execution, Memory and Recovery

## Implemented capabilities

- PostgreSQL-backed execution state, checkpoints and approvals.
- Versioned four-stage agent execution plan.
- Deterministic invocation identifiers.
- Atomic execution claims and lease-token fencing.
- Three attempts per stage and five-minute execution budget.
- Persistent intermediate agent memory.
- Transactional checkpoint persistence.
- Explicit cancellation and resumable execution.
- Idempotent execution identifiers.
- Evidence revalidation before cached answer disclosure.
- Durable, payload-bound, single-use human approvals.
- Development-only execution, status and cancellation endpoints.

## REST contract

- POST /api/workflows/{id}/agents/execute
- POST /api/workflows/{id}/agents/execute?executionId={guid}
- GET /api/workflows/{id}/agents/executions/{executionId}
- POST /api/workflows/{id}/agents/executions/{executionId}/cancel

The default execution identifier is the workflow identifier.
Independent runs require an explicit execution identifier.
Unfinished executions return HTTP 202 and may be resumed.

## Persistence guarantees

PostgreSQL is authoritative for execution and approval state.
Checkpoint insertion and execution advancement are transactional.
Lease tokens prevent superseded workers from committing results.

Checkpoint persistence is exactly-once per execution stage.
Inference execution itself may be repeated following interruption.
External side effects are not covered by this guarantee.

## Acceptance evidence

- Release build passed.
- Full regression: 337 tests passed, including live local AI.
- Additional evidence-replay acceptance passed.
- Additional durable approval gateway acceptance passed.
- Final targeted hardening regression: 13 tests passed.
- Cumulative distinct test coverage: 339 tests.
- EF Core migration consistency passed.
- Azure expenditure: EUR 0.

## Outstanding verification

- Actual operating-system process termination and recovery.
- Strict wall-clock lease enforcement under database lock contention.
- Separate replay verification budget enforcement.
- Trusted approver identity and tenant authorization.
- Production endpoint authentication.
- Inference timeout and concurrency controls.

These requirements remain tracked in the mandatory completion register.
Database-context restart testing is not equivalent to process-crash testing.