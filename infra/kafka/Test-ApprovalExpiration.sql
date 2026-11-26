BEGIN;

INSERT INTO workflows (
    id, tenant_id, requester_id, request, status,
    created_at_utc, updated_at_utc
)
VALUES (
    'f7000000-0000-0000-0000-000000000030',
    'e7-expiration-test', 'requester', 'expiration acceptance',
    'Created', clock_timestamp() - interval '10 minutes',
    clock_timestamp() - interval '10 minutes'
);

UPDATE workflows
SET status = 'Running'
WHERE id = 'f7000000-0000-0000-0000-000000000030';

UPDATE workflows
SET status = 'WaitingForApproval'
WHERE id = 'f7000000-0000-0000-0000-000000000030';

INSERT INTO agent_approvals (
    "Id", "WorkflowId", "TenantId", "RequesterId",
    "ToolName", "PayloadDigest", "Risk", "Status",
    "CreatedAt", "ExpiresAt"
)
VALUES (
    'f7000000-0000-0000-0000-000000000031',
    'f7000000-0000-0000-0000-000000000030',
    'e7-expiration-test', 'requester', 'sensitive-action',
    repeat('A', 64), 2, 0,
    clock_timestamp() - interval '5 minutes',
    clock_timestamp() - interval '1 minute'
);

SELECT expire_workflow_approvals(1000);

DO $test$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM agent_approvals
        WHERE "Id" = 'f7000000-0000-0000-0000-000000000031'
          AND "Status" = 3
    ) THEN
        RAISE EXCEPTION 'Approval expiration failed';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM workflows
        WHERE id = 'f7000000-0000-0000-0000-000000000030'
          AND status = 'Cancelled'
    ) THEN
        RAISE EXCEPTION 'Workflow cancellation failed';
    END IF;

    IF (
        SELECT array_agg(payload->>'eventType' ORDER BY sequence)
        FROM kafka_workflow_events
        WHERE workflow_id = 'f7000000-0000-0000-0000-000000000030'
    ) IS DISTINCT FROM
        ARRAY['STARTED','APPROVAL_REQUIRED','CANCELLED']
    THEN
        RAISE EXCEPTION 'Unexpected expiration lifecycle';
    END IF;
END;
$test$;

SELECT expire_workflow_approvals(1000);

DO $test$
BEGIN
    IF (
        SELECT count(*) FROM kafka_workflow_events
        WHERE workflow_id = 'f7000000-0000-0000-0000-000000000030'
          AND payload->>'eventType' = 'CANCELLED'
    ) <> 1 THEN
        RAISE EXCEPTION 'Duplicate cancellation';
    END IF;

    RAISE NOTICE 'Expiration and idempotency passed';
END;
$test$;

ROLLBACK;

DO $test$
BEGIN
    IF EXISTS (
        SELECT 1 FROM agent_approvals
        WHERE "Id" = 'f7000000-0000-0000-0000-000000000031'
    ) OR EXISTS (
        SELECT 1 FROM workflows
        WHERE id = 'f7000000-0000-0000-0000-000000000030'
    ) OR EXISTS (
        SELECT 1 FROM kafka_workflow_events
        WHERE workflow_id = 'f7000000-0000-0000-0000-000000000030'
    ) THEN
        RAISE EXCEPTION 'Expiration rollback failed';
    END IF;

    RAISE NOTICE 'Atomic rollback passed';
END;
$test$;
