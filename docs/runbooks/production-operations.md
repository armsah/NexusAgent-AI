# NexusAgent AI — Operations, Failure Drills, and Recovery

## Scope and deployment status

This runbook covers the local .NET 10/PostgreSQL 17 + pgvector
portfolio implementation and documents procedures for a future
Azure deployment.

No Azure environment is currently provisioned. Cloud recovery,
load testing, and rollback procedures below are operational plans,
not evidence of completed production drills.

The API deliberately exposes business endpoints only in Development.
Production and Staging expose health endpoints but do not expose
development-identity business operations. Real authentication and
authorization must be implemented before enabling business routes
in a deployed environment.

## Health endpoints

- `GET /health`: liveness; returns HTTP 200 when the API process
  can serve requests. It does not test PostgreSQL.
- `GET /health/ready`: database readiness; returns HTTP 200 when
  PostgreSQL connectivity succeeds and HTTP 503 when connectivity
  fails or the database operation throws.
- A readiness failure must not disclose connection strings,
  credentials, SQL errors, or internal stack traces.
- Client-request cancellation is propagated rather than
  converted into a successful health response.

Do not interpret a successful liveness check as proof that the
retrieval index, embedding service, or model provider is healthy.

## Failure drill 1 — PostgreSQL outage

**Trigger:** PostgreSQL is stopped, unreachable, or rejects connections.

**Expected behavior:** `/health` remains available while
`/health/ready` returns HTTP 503. Database-dependent requests
cannot be considered operational.

**Local procedure:**

1. Start the API with a known working local PostgreSQL instance.
2. Confirm `/health` and `/health/ready` return HTTP 200.
3. Stop PostgreSQL in the isolated test environment.
4. Confirm `/health/ready` returns HTTP 503 without sensitive details.
5. Restore PostgreSQL and confirm readiness returns HTTP 200.
6. Inspect worker/outbox state and replay behavior before declaring
   recovery complete.

**Evidence:** P9 automated integration coverage verifies HTTP 503
against an unreachable database. The full stop/restart drill is
not claimed as executed.

## Failure drill 2 — Model or embedding provider outage

**Trigger:** Azure OpenAI returns an error, times out, or is unreachable.

**Expected behavior:** Requests fail or abstain according to the
existing application contract. Never fabricate an answer or citation.

**Procedure:**

1. In an isolated environment, configure a controlled failing
   provider or HTTP test handler.
2. Verify that timeouts and errors do not expose API keys.
3. Confirm request and failure telemetry increments as expected.
4. Restore provider connectivity and rerun grounded-answer tests.
5. Review retries and rate limits before enabling live traffic.

**Status:** Procedure documented; no live Azure outage drill executed.

## Failure drill 3 — Retrieval/index inconsistency

**Trigger:** Published document content and indexed passages diverge,
or index processing is delayed.

**Procedure:**

1. Identify affected document versions and ingestion/outbox events.
2. Verify tenant and document ACLs before any replay or reindex.
3. Replay the supported ingestion/indexing workflow using existing
   application tooling and a backed-up test database.
4. Re-run cross-tenant denial and grounded-citation regression tests.
5. Verify that deleted or unauthorized passages are not returned.

Never bypass tenant filtering to speed up recovery.

**Status:** Existing regression coverage exercises ingestion and
authorization; an end-to-end production index-drift drill has
not been executed.

## Backup and database recovery

1. Confirm the recovery point objective and retention policy.
2. Take a PostgreSQL backup before destructive maintenance.
3. Restore into a separate environment; never overwrite the only
   recoverable copy during a drill.
4. Apply the repository's supported database migrations.
5. Validate document metadata, ACLs, outbox state, and pgvector
   extension/index availability.
6. Run the full integration suite against the restored environment.
7. Record recovery duration, data-loss window, and operator approval.

**Status:** Recovery procedure documented, not production-validated.

## Load and performance validation

Use a non-production environment with representative authorized
documents and realistic concurrent requests.

Measure:

- HTTP latency percentiles (p50, p95, p99) and error rate.
- PostgreSQL connection saturation and query latency.
- Retrieval candidate counts and evidence-retention ratios.
- Grounded-answer generation latency, timeouts, and abstention rate.
- Worker throughput, outbox backlog, and retry volume.
- CPU, memory, and resource limits.

Test read-only workloads first, then controlled ingestion and
workflow concurrency. Establish baselines and acceptance thresholds
before running a load test. Do not treat the 231-test regression
suite as a throughput benchmark.

**Status:** Load-test plan documented; no measured capacity claim.

## Release validation and rollback

### Pre-release checks

1. Verify the intended Git commit and a clean working directory.
2. Run `dotnet build --configuration Release --nologo`.
3. Run `dotnet test --configuration Release --no-build --nologo`.
4. Review configuration for secrets and development-only values.
5. Validate migrations and backup/restore compatibility.
6. Confirm the production business API remains disabled until
   real authentication and authorization are implemented.
7. Obtain explicit approval before changing a shared environment.

### Rollback procedure

1. Stop further rollout and preserve incident evidence.
2. Identify the last known-good application artifact and commit.
3. Re-deploy the previous artifact through the deployment system.
4. Roll back database schema only when a tested, data-safe
   migration reversal exists; otherwise use a forward repair.
5. Verify liveness, readiness, tenant isolation, and key workflows.
6. Record the incident, impact, recovery time, and corrective action.

A Git revert alone does not restore database state or external
resources. Do not destroy data to make a code rollback succeed.

**Status:** Git-based release procedure documented. No Azure
deployment or rollback drill has been performed.

## Observability limitations

NexusAgent instruments `ActivitySource` traces and .NET meters,
but does not configure an OpenTelemetry exporter or dashboard.
Token counts are estimates and do not represent provider billing.
Evidence-retention ratios are not labeled retrieval-quality metrics.

## Project completion criteria

The local portfolio implementation is complete when the Release
build, regression suite, security checks, README, and Git history
are validated and pushed to GitHub.

Production deployment remains a separate undertaking requiring
identity integration, secret management, observability export,
deployment validation, backup drills, and load testing.